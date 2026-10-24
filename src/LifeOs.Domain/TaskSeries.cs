using System.Text.Json.Nodes;

namespace LifeOs.Domain;

/// <summary>How a repeating Task's next repetition is dated (TASKS-3, D16).</summary>
public enum RepeatKind
{
    /// <summary>No repetitions: no pattern, or a cue (<c>trigger</c>) with no dates.</summary>
    None,

    /// <summary>On calendar dates (every Wednesday, rent on the 1st): due on each date.</summary>
    Calendar,

    /// <summary>Once each week on any day: do-date Monday, due Sunday.</summary>
    EachWeek,

    /// <summary>Every N days, weeks or months after the last one closed (the fridge).</summary>
    AfterCompletion
}

/// <summary>
/// The shape of a repeating Task (D16). A <b>series</b> is a Task carrying
/// <c>attributes.recurrence</c>: it holds the pattern, the start date and the price, and
/// never shows in the Task lists. Each <b>repetition</b> is an ordinary Task carrying
/// <c>attributes.series</c> (the series' urn) and <c>attributes.occurrence</c> (the date it
/// stands for), due on that date, or for once each week scheduled on the Monday and due the
/// Sunday. A repetition copies the fields in <see cref="CopiedKeys"/> when it's made, never
/// the bounty: the price comes from the series.
/// </summary>
public static class TaskSeries
{
    /// <summary>On a repetition: the urn of the series it belongs to.</summary>
    public const string SeriesKey = "series";

    /// <summary>On a repetition: the date it stands for (<c>yyyy-MM-dd</c>).</summary>
    public const string OccurrenceKey = "occurrence";

    /// <summary>On a series: the first date it repeats from (the anchor of an every-N repeat).</summary>
    public const string StartKey = "start";

    public const string DueKey = "due";
    public const string ScheduledKey = "scheduled";

    /// <summary>The attributes a repetition copies from its series, and that a series edit follows to.</summary>
    public static readonly IReadOnlyList<string> CopiedKeys = ["description", "area", "priority", "estimated_duration"];

    /// <summary>The statuses a series itself takes: repeating, or stopped.</summary>
    public static readonly IReadOnlyList<string> SeriesStatuses = ["Not started", "Cancelled"];

    /// <summary>
    /// Refuses a pattern a Task can't repeat on: a cue (<c>trigger</c>) has no dates, so the
    /// Task would be a series that never makes a repetition.
    /// </summary>
    public static void CheckTaskPattern(string recurrenceJson)
    {
        if (KindOf(recurrenceJson) == RepeatKind.None)
        {
            throw new ArgumentException(
                "A repeating Task needs dates: on days of the week or month, every N, once each week, or after the last completion. A cue is for Habits.");
        }
    }

    /// <summary>How a stored pattern dates its repetitions.</summary>
    public static RepeatKind KindOf(string? recurrenceJson)
    {
        if (string.IsNullOrWhiteSpace(recurrenceJson))
        {
            return RepeatKind.None;
        }

        JsonObject? recurrence;
        try
        {
            recurrence = JsonNode.Parse(recurrenceJson) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return RepeatKind.None;
        }

        var freq = Text(recurrence, "freq");
        return freq switch
        {
            Recurrence.FreqEach => RepeatKind.EachWeek,
            Recurrence.FreqInterval when Text(recurrence, "from") == Recurrence.FromCompletion => RepeatKind.AfterCompletion,
            Recurrence.FreqDaily or Recurrence.FreqWeekly or Recurrence.FreqInterval or Recurrence.FreqMonthly
                => RepeatKind.Calendar,
            _ => RepeatKind.None
        };
    }

    /// <summary>
    /// The date an after-completion repetition is due: <paramref name="closedOn"/> plus the
    /// pattern's every-N, months landing on the month's last day when it's shorter.
    /// </summary>
    public static DateOnly AfterCompletion(string recurrenceJson, DateOnly closedOn)
    {
        var recurrence = JsonNode.Parse(recurrenceJson) as JsonObject
            ?? throw new ArgumentException("A recurrence is a JSON object.");
        var every = recurrence["every"]?.GetValue<int>() ?? 1;
        return Text(recurrence, "unit") switch
        {
            "weeks" => closedOn.AddDays(7 * every),
            "months" => closedOn.AddMonths(every),
            _ => closedOn.AddDays(every)
        };
    }

    /// <summary>The Monday of the week holding <paramref name="date"/> (weeks run Monday to Sunday).</summary>
    public static DateOnly WeekOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// When a repetition standing for <paramref name="occurrence"/> is due, and its do-date:
    /// once each week is scheduled on the Monday and due the Sunday; every other kind is due
    /// on the date itself.
    /// </summary>
    public static (DateOnly Due, DateOnly? Scheduled) DatesFor(RepeatKind kind, DateOnly occurrence)
        => kind == RepeatKind.EachWeek ? (occurrence.AddDays(6), occurrence) : (occurrence, null);

    private static string? Text(JsonObject? o, string key)
        => o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
