namespace LifeOs.Domain;

/// <summary>
/// The per-type status vocabularies and the terminal set (D7). Statuses are held in
/// the subject's freeform record and moved only by <c>state_change</c> events; this
/// class is the code mirror of the documented map (the SQL side is the
/// <c>bsk.is_terminal_status</c> predicate, migration 0011). It is deliberately
/// data-not-DDL: the single typed-jsonb subject table gains no per-type columns, and
/// validation is app-side — a writer or UI can consult <see cref="For"/> to offer the
/// right choices, but the kernel adds no enum constraint.
///
/// <para>Only status-bearing types appear here. Identity Statement (Value) has no
/// status (it archives instead, D9); Area / Person / Constraint / Season are
/// status-less in the Pilot. Appointment's vocabulary is fixed now though the type is
/// created later; Habit and Review gain theirs when their types land.</para>
/// </summary>
public static class StatusVocabulary
{
    /// <summary>Goal / Project share one shape: developing → Active → done/dead.</summary>
    private static readonly string[] GoalProject = ["developing", "Active", "Completed", "Abandoned"];

    private static readonly Dictionary<string, string[]> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        [SubjectTypes.Goal] = GoalProject,
        [SubjectTypes.Project] = GoalProject,
        [SubjectTypes.Task] = ["Not started", "In progress", "Waiting", "Completed", "Cancelled"],
        [SubjectTypes.Commitment] = ["Open", "Fulfilled", "Missed", "Cancelled"],
        [SubjectTypes.Decision] = ["Open", "Implementing", "Cancelled", "Closed"],
        [SubjectTypes.Problem] = ["Open", "Working", "Resolved"],
        [SubjectTypes.Appointment] = ["Scheduled", "Completed", "Cancelled", "Missed"],
        [SubjectTypes.Idea] = ["New", "Promoted", "Rejected"],
    };

    /// <summary>
    /// The terminal statuses — a subject in one of these is finished or dead and no
    /// longer counts as active. Mirrors the word set in <c>bsk.is_terminal_status</c>
    /// (migration 0011). Includes a few legacy/general words the predicate also carries
    /// (<c>done</c>, <c>dropped</c>, <c>archived</c>, <c>superseded</c>) so the two
    /// mirrors agree word-for-word.
    /// </summary>
    private static readonly HashSet<string> Terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        "done", "completed", "resolved", "closed", "cancelled",
        "abandoned", "dropped", "archived", "superseded",
        "fulfilled", "missed", "promoted", "rejected",
    };

    /// <summary>The subject types that carry a status workflow.</summary>
    public static IReadOnlyCollection<string> StatusBearingTypes => ByType.Keys;

    /// <summary>
    /// The allowed statuses for <paramref name="type"/>, or an empty list for a
    /// status-less type (Value, Area, Person, Constraint, Season).
    /// </summary>
    public static IReadOnlyList<string> For(string type)
        => ByType.TryGetValue(type, out var statuses) ? statuses : [];

    /// <summary>
    /// True when <paramref name="status"/> is a terminal status (finished/dead).
    /// Case- and whitespace-insensitive; a null or blank status is active, not
    /// terminal — matching <c>bsk.is_terminal_status</c>.
    /// </summary>
    public static bool IsTerminal(string? status)
        => !string.IsNullOrWhiteSpace(status) && Terminal.Contains(status.Trim());

    /// <summary>
    /// True when <paramref name="status"/> is in the vocabulary for
    /// <paramref name="type"/>. Case-insensitive. A status-less type accepts nothing.
    /// </summary>
    public static bool IsValid(string type, string status)
        => For(type).Any(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));
}
