namespace LifeOs.Domain;

/// <summary>
/// The per-type status vocabularies and the terminal set (D7). Statuses are held in
/// the subject's freeform record and moved only by <c>state_change</c> events; this
/// class is the code mirror of the documented map (the SQL side is the
/// <c>bsk.is_terminal_status</c> predicate, migration 0011). It is deliberately
/// data-not-DDL: the single typed-jsonb subject table gains no per-type columns, and
/// validation is app-side — a UI consults <see cref="For"/> to offer the right
/// choices, and <c>StatusService</c> refuses any word outside it (storing the
/// vocabulary's casing), but the kernel adds no enum constraint.
///
/// <para>Only status-bearing types appear here. Identity Statement (Value) has no
/// status (it archives instead, D9); Area / Person / Constraint / Season / Reward are
/// status-less. Habit's lifecycle (Building → Established | Abandoned)
/// arrived with habit earning (migration 0027): a terminal habit graduates and leaves
/// daily tracking. Review gains its vocabulary when its type lands.</para>
/// </summary>
public static class StatusVocabulary
{
    /// <summary>Goal / Project share one shape: New → Active → done/dead.</summary>
    private static readonly string[] GoalProject = ["New", "Active", "Completed", "Abandoned"];

    private static readonly Dictionary<string, string[]> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        [SubjectTypes.Goal] = GoalProject,
        [SubjectTypes.Project] = GoalProject,
        [SubjectTypes.Task] = ["Not started", "In progress", "Waiting", "Completed", "Cancelled"],
        [SubjectTypes.Commitment] = ["Open", "Fulfilled", "Missed", "Cancelled"],
        [SubjectTypes.Decision] = ["Open", "Implementing", "Cancelled", "Closed"],
        [SubjectTypes.Problem] = ["Open", "Working", "Resolved", "Cancelled"],
        [SubjectTypes.Appointment] = ["Scheduled", "Completed", "Cancelled", "Missed"],
        [SubjectTypes.Idea] = ["New", "Promoted", "Rejected"],
        [SubjectTypes.Habit] = ["Building", "Established", "Abandoned"],
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
        "established",
    };

    /// <summary>
    /// The terminal status a <c>Drop</c> assigns per type — "resolved out as nothing /
    /// not pursued" (inbox attention-vs-status, migration 0021 /
    /// docs/pilot/inbox-attention-vs-status.md). Each is that type's own give-up
    /// terminal: Goal/Project <c>Abandoned</c>, Idea <c>Rejected</c>, the rest
    /// <c>Cancelled</c> (and never <c>Resolved</c>/<c>Completed</c>, which mean success).
    /// A status-less type has none — its inbox items resolve by the attention marker.
    /// </summary>
    private static readonly Dictionary<string, string> DismissStatusByType = new(StringComparer.OrdinalIgnoreCase)
    {
        [SubjectTypes.Goal] = "Abandoned",
        [SubjectTypes.Project] = "Abandoned",
        [SubjectTypes.Task] = "Cancelled",
        [SubjectTypes.Commitment] = "Cancelled",
        [SubjectTypes.Decision] = "Cancelled",
        [SubjectTypes.Problem] = "Cancelled",
        [SubjectTypes.Appointment] = "Cancelled",
        [SubjectTypes.Idea] = "Rejected",
        [SubjectTypes.Habit] = "Abandoned",
    };

    /// <summary>The subject types that carry a status workflow.</summary>
    public static IReadOnlyCollection<string> StatusBearingTypes => ByType.Keys;

    /// <summary>
    /// The allowed statuses for <paramref name="type"/>, or an empty list for a
    /// status-less type (Value, Area, Person, Constraint, Season, Reward).
    /// </summary>
    public static IReadOnlyList<string> For(string type)
        => ByType.TryGetValue(type, out var statuses) ? statuses : [];

    /// <summary>
    /// The terminal status a <c>Drop</c> assigns to a subject of <paramref name="type"/>
    /// ("it's nothing / not pursued"), or "" for a status-less type. Always a valid,
    /// terminal member of the type's vocabulary.
    /// </summary>
    public static string DismissStatusFor(string type)
        => DismissStatusByType.TryGetValue(type, out var status) ? status : "";

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
        => Canonical(type, status) is not null;

    /// <summary>
    /// The vocabulary's own spelling of <paramref name="status"/> for
    /// <paramref name="type"/> (<c>in progress</c> → <c>In progress</c>), or null when
    /// the word is not in that type's vocabulary. Case- and whitespace-insensitive; a
    /// status-less type has no canonical status.
    /// </summary>
    public static string? Canonical(string type, string? status)
        => For(type).FirstOrDefault(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));
}
