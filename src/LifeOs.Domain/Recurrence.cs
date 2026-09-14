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

    public static string Interval(int every, string unit)
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

        return new JsonObject { ["freq"] = FreqInterval, ["every"] = every, ["unit"] = u }.ToJsonString();
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
}
