using System.Text.Json.Nodes;

namespace LifeOs.Domain;

/// <summary>
/// Builds and validates the structured recurrence stored in
/// <c>attributes.recurrence</c> (D8). The canonical shapes are the ones
/// <c>bsk.recurrence_occurrences</c> (migration 0016) expands; this is the write-side
/// gate that produces them, so a malformed recurrence never reaches the store.
/// Recurrence is additive to the free-text <c>expected_cadence</c> the neglect
/// diagnostic reads.
/// </summary>
public static class Recurrence
{
    public const string FreqDaily = "daily";
    public const string FreqWeekly = "weekly";
    public const string FreqInterval = "interval";
    public const string FreqMonthly = "monthly";
    public const string FreqTrigger = "trigger";

    /// <summary>Once each period, on any day in it (TASKS-3): <c>{"freq":"each","period":"week"}</c>.</summary>
    public const string FreqEach = "each";

    /// <summary>The periods an <see cref="FreqEach"/> repeat takes. Weeks run Monday to Sunday.</summary>
    public static readonly IReadOnlyList<string> Periods = ["week"];

    /// <summary>
    /// An every-N repeat counted from the day the last one closed rather than from the start
    /// (TASKS-3: the fridge, every 30 days): <c>"from":"completion"</c>.
    /// </summary>
    public const string FromCompletion = "completion";

    /// <summary>Weekday tokens used by a weekly recurrence, Sunday first.</summary>
    public static readonly IReadOnlyList<string> Weekdays = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>Interval units.</summary>
    public static readonly IReadOnlyList<string> Units = ["days", "weeks", "months"];

    public static string Daily() => new JsonObject { ["freq"] = FreqDaily }.ToJsonString();

    public static string Weekly(IReadOnlyList<string> on)
    {
        var normalized = (on ?? [])
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();
        if (normalized.Count == 0)
        {
            throw new ArgumentException("A weekly recurrence needs at least one weekday.", nameof(on));
        }

        var invalid = normalized.Where(d => !Weekdays.Contains(d)).ToList();
        if (invalid.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown weekday(s): {string.Join(", ", invalid)}. Expected: {string.Join(", ", Weekdays)}.",
                nameof(on));
        }

        // Store in canonical Sunday-first order for a stable representation.
        var ordered = Weekdays.Where(normalized.Contains);
        return new JsonObject
        {
            ["freq"] = FreqWeekly,
            ["on"] = new JsonArray(ordered.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray())
        }.ToJsonString();
    }

    public static string Interval(int every, string unit, string? from = null)
    {
        if (every < 1)
        {
            throw new ArgumentException("An interval recurrence needs a positive 'every'.", nameof(every));
        }

        var u = (unit ?? "").Trim().ToLowerInvariant();
        if (!Units.Contains(u))
        {
            throw new ArgumentException(
                $"Unknown unit '{unit}'. Expected: {string.Join(", ", Units)}.", nameof(unit));
        }

        var interval = new JsonObject { ["freq"] = FreqInterval, ["every"] = every, ["unit"] = u };
        var f = from?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(f))
        {
            if (f != FromCompletion)
            {
                throw new ArgumentException(
                    $"An every-N repeat counts from the start, or from '{FromCompletion}'; got '{from}'.", nameof(from));
            }

            interval["from"] = FromCompletion;
        }

        return interval.ToJsonString();
    }

    /// <summary>Once each <paramref name="period"/> on any day in it: <c>{"freq":"each","period":"week"}</c>.</summary>
    public static string Each(string period)
    {
        var p = (period ?? "").Trim().ToLowerInvariant();
        if (!Periods.Contains(p))
        {
            throw new ArgumentException(
                $"Once each what? Expected: {string.Join(", ", Periods)}; got '{period}'.", nameof(period));
        }

        return new JsonObject { ["freq"] = FreqEach, ["period"] = p }.ToJsonString();
    }

    public static string MonthlyLast()
        => new JsonObject { ["freq"] = FreqMonthly, ["on"] = "last" }.ToJsonString();

    public static string MonthlyDay(int day)
    {
        if (day is < 1 or > 31)
        {
            throw new ArgumentException("A monthly day must be between 1 and 31.", nameof(day));
        }

        return new JsonObject { ["freq"] = FreqMonthly, ["day"] = day }.ToJsonString();
    }

    public static string Trigger(string cue)
    {
        if (string.IsNullOrWhiteSpace(cue))
        {
            throw new ArgumentException("A trigger recurrence needs a cue.", nameof(cue));
        }

        return new JsonObject { ["freq"] = FreqTrigger, ["cue"] = cue.Trim() }.ToJsonString();
    }

    /// <summary>The attribute that holds a subject's recurrence.</summary>
    public const string AttributeKey = "recurrence";

    /// <summary>
    /// Builds a recurrence from its parts, the way <c>bsk recur</c> and <c>POST /api/recur</c>
    /// name them: <paramref name="on"/> holds a weekly recurrence's weekdays, or <c>last</c> for
    /// a monthly one on the month's last day; <paramref name="period"/> is an <c>each</c>
    /// repeat's period; <paramref name="from"/> is <c>completion</c> for an every-N repeat
    /// counted from the last one closed.
    /// </summary>
    public static string Build(
        string? freq, IReadOnlyList<string>? on = null, int? every = null, string? unit = null,
        int? day = null, string? cue = null, string? period = null, string? from = null)
    {
        var f = (freq ?? "").Trim().ToLowerInvariant();
        var days = on ?? [];
        if (!string.IsNullOrWhiteSpace(from) && f != FreqInterval)
        {
            throw new ArgumentException("Only an every-N repeat can count from the last completion.", nameof(from));
        }

        return f switch
        {
            FreqDaily => Daily(),
            FreqWeekly => Weekly(days),
            FreqInterval => Interval(
                every ?? throw new ArgumentException("An interval recurrence needs 'every'."), unit ?? "", from),
            FreqMonthly => day is { } d
                ? MonthlyDay(d)
                : days.Contains("last", StringComparer.OrdinalIgnoreCase)
                    ? MonthlyLast()
                    : throw new ArgumentException("A monthly recurrence needs a day (1-31) or 'last'."),
            FreqTrigger => Trigger(cue ?? ""),
            FreqEach => Each(period ?? ""),
            "" => throw new ArgumentException("A recurrence needs a freq."),
            _ => throw new ArgumentException(
                $"Unknown freq '{freq}'. Expected: daily, weekly, interval, monthly, trigger, each.")
        };
    }

    /// <summary>
    /// Checks a recurrence given as JSON and returns its canonical form: the object
    /// <c>bsk.recurrence_occurrences</c> reads. It takes the object itself, or text holding
    /// one, which is how a recurrence arrives through a plain string attribute. Every door that
    /// writes a recurrence comes through here, so a pattern is never stored as text the
    /// expander can't read. Throws <see cref="ArgumentException"/> for anything else.
    /// </summary>
    public static string Canonicalize(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            node = ParseText(text);
        }

        if (node is not JsonObject recurrence)
        {
            throw new ArgumentException(
                """A recurrence is a JSON object such as {"freq":"weekly","on":["wed"]}.""");
        }

        return Build(
            Text(recurrence, "freq"),
            On(recurrence["on"]),
            Int(recurrence, "every"),
            Text(recurrence, "unit"),
            Int(recurrence, "day"),
            Text(recurrence, "cue"),
            Text(recurrence, "period"),
            Text(recurrence, "from"));
    }

    /// <inheritdoc cref="Canonicalize(JsonNode?)"/>
    public static string Canonicalize(string json) => Canonicalize(ParseText(json));

    /// <summary>
    /// <paramref name="attributesJson"/> with its <c>recurrence</c>, if it has one, in canonical
    /// form (<see cref="Canonicalize(JsonNode?)"/>), so creating a subject with a recurrence
    /// written as text stores the object. Anything without one comes back unchanged.
    /// </summary>
    public static string NormalizeAttributes(string attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson)
            || JsonNode.Parse(attributesJson) is not JsonObject attributes
            || !attributes.ContainsKey(AttributeKey))
        {
            return attributesJson;
        }

        var recurrence = attributes[AttributeKey];
        if (recurrence is null)
        {
            attributes.Remove(AttributeKey);
        }
        else
        {
            attributes[AttributeKey] = JsonNode.Parse(Canonicalize(recurrence));
        }

        return attributes.ToJsonString();
    }

    private static JsonNode? ParseText(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ArgumentException(
                $$"""'{{text}}' is not a recurrence. A recurrence is a JSON object such as {"freq":"weekly","on":["wed"]}.""");
        }
    }

    private static string? Text(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject o, string key)
        => o[key] switch
        {
            JsonValue v when v.TryGetValue<int>(out var n) => n,
            JsonValue v when v.TryGetValue<string>(out var s) && int.TryParse(s, out var n) => n,
            null => null,
            _ => throw new ArgumentException($"A recurrence's '{key}' must be a whole number.")
        };

    private static List<string> On(JsonNode? node)
        => node switch
        {
            null => [],
            JsonArray a => a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s)
                    ? s
                    : throw new ArgumentException("A recurrence's 'on' holds weekday names such as \"wed\"."))
                .ToList(),
            JsonValue v when v.TryGetValue<string>(out var s) => [s],
            _ => throw new ArgumentException("A recurrence's 'on' holds weekday names such as \"wed\".")
        };
}
