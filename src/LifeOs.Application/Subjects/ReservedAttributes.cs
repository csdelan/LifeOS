using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// The attribute keys no generic attribute write may set, each with the verb that does (code
/// review CR49): a column that isn't an attribute, or a checked value with one writer that a
/// page or the reconcile reads. <c>bsk set</c> and the item page's edit (<see cref="AttributeService"/>)
/// and <c>bsk new --attr</c> and <c>POST /api/subjects</c> (<see cref="SubjectCreationService"/>)
/// all refuse them from this one table, so no door stores a key another ignores. A verb's own
/// service writes its key straight through the repository and isn't checked here.
/// </summary>
public static class ReservedAttributes
{
    private sealed record Entry(string? Type, IReadOnlyList<string> Keys, string Refusal);

    private static readonly Entry[] Table =
    [
        // A first-class column, not an attribute: written here it'd be a shadow key never read.
        new(null, ["title"], "Title is not an attribute — use `bsk rename` to change a subject's title."),
        new(null, ["status"], "Status is not an attribute: it moves by a status change (`bsk status`)."),

        // A cover is a checked Unsplash reference (D15).
        new(null, [Cover.AttributeKey], "Cover is set with `bsk cover` (a photo from Unsplash), not `bsk set`."),

        // What an item pays is a checked price the reconcile reads (rewards plan §3.3).
        new(null, [Bounty.AttributeKey], "What an item earns is set with `bsk bounty`, not `bsk set`."),

        // When a habit is ready to establish is a checked target (REWARD-2).
        new(null, [EstablishTarget.AttributeKey], "A habit's establish target is set with `bsk habit target`, not `bsk set`."),

        // A repetition's place in its series (D16).
        new(SubjectTypes.Task, [TaskSeries.SeriesKey, TaskSeries.OccurrenceKey],
            "A Task's '{0}' is set by its repeating task, not `bsk set`."),

        // An appointment occurrence's place in its series (D10, code review CR65). Its day
        // (attributes.date) stays Chris's to move.
        new(SubjectTypes.Appointment, [AppointmentSeries.SeriesKey, AppointmentSeries.OccurrenceKey],
            "An appointment's '{0}' is set when its repeating appointment makes it (`bsk materialize`), not `bsk set`."),

        // The Home Score reads a space's weight and pace (HOME-1) and its shots (HOME-4).
        new(SubjectTypes.Space, [SpaceLimits.WeightKey, SpaceLimits.PaceKey],
            "A space's weight and pace are set with `bsk space set`, not `bsk set`."),
        new(SubjectTypes.Space, [SpaceLimits.ShotsKey],
            "A space's shots are set with `bsk space shot` and `bsk space baseline`, not `bsk set`."),

        // The trend and goal read a metric's settings (MEASURE-1).
        new(SubjectTypes.Metric, MetricLimits.Keys,
            "A metric's unit, decimals, target and which way is better are set with `bsk metric set`, not `bsk set`."),

        // One review per kind and period (migration 0052), set when it's opened.
        new(SubjectTypes.Review, ReviewPeriods.ReservedKeys,
            "A review's kind and period are set when it's opened (`bsk review open`), not with `bsk set`."),

        // What an instant reward costs is a checked price (REWARD-3).
        new(SubjectTypes.Reward, [RewardAttributes.Price],
            "An instant reward's price is set with `bsk rewards price`, not `bsk set`."),
    ];

    /// <summary>Why <paramref name="key"/> can't be written on a <paramref name="type"/>, or null when it can.</summary>
    public static string? RefusalFor(string type, string key)
    {
        var trimmed = key.Trim();
        var entry = Table.FirstOrDefault(e =>
            (e.Type is null || string.Equals(e.Type, type, StringComparison.Ordinal))
            && e.Keys.Any(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase)));
        return entry is null ? null : string.Format(System.Globalization.CultureInfo.InvariantCulture, entry.Refusal, trimmed);
    }

    /// <summary>Refuses, with nothing written, the first of <paramref name="keys"/> a <paramref name="type"/> reserves.</summary>
    public static void Check(string type, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (RefusalFor(type, key) is { } refusal)
            {
                throw new ArgumentException(refusal);
            }
        }
    }
}
