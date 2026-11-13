namespace LifeOs.Domain;

/// <summary>Event kinds, matching the CHECK constraint on <c>bsk.event.kind</c>.</summary>
public static class EventKinds
{
    public const string Journal = "journal";
    public const string Note = "note";
    public const string Voice = "voice";
    public const string IdeaSession = "idea_session";
    public const string Observation = "observation";
    public const string Activity = "activity";
    public const string Measurement = "measurement";
    public const string Interaction = "interaction";
    public const string StateChange = "state_change";

    /// <summary>Archive / restore, folded by <c>bsk.is_archived</c> (D9, migration 0012).</summary>
    public const string ArchiveChange = "archive_change";

    /// <summary>Inbox triage marker, folded by <c>v_inbox</c> (INBOX-1, migration 0015).</summary>
    public const string Triage = "triage";

    /// <summary>
    /// A rewards ledger entry — an earn, reversal or redemption of currencies, folded by
    /// <c>v_ledger</c> / <c>v_wallet</c> (REWARD-1 / D11, migration 0025).
    /// </summary>
    public const string Ledger = "ledger";

    /// <summary>
    /// A subject's title or attributes changed, from what to what, appended in the same transaction
    /// as the change (code review CR24, migration 0057). The views still read the subject row.
    /// </summary>
    public const string AttributeChange = "attribute_change";

    /// <summary>Every kind, as the CHECK on <c>bsk.event.kind</c> allows them; a test keeps the two equal.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Journal, Note, Voice, IdeaSession, Observation, Activity, Measurement, Interaction, StateChange,
        ArchiveChange, Triage, Ledger, AttributeChange,
    ];
}

/// <summary>
/// Triage marker states (INBOX-1). The newest marker per item wins, and it encodes
/// <em>attention only</em> — the item's resolution lives in its Status, not here (see
/// docs/pilot/inbox-attention-vs-status.md). <c>flagged</c> means the item is in the
/// inbox awaiting a decision; <c>dismissed</c> resolves it out with no status change
/// ("clarified, nothing to record"). Dropping a <em>subject</em> is a Status change,
/// not a triage state; <c>dismissed</c> replaced the former <c>dropped</c>/<c>filed</c>
/// states (0021) — historical markers of those names still fold correctly, since
/// <c>v_inbox</c> keeps only <c>flagged</c>.
/// </summary>
public static class TriageStates
{
    public const string Flagged = "flagged";
    public const string Dismissed = "dismissed";

    /// <summary>Resolved by being promoted into a subject / into new work (CAP-6 / D4).</summary>
    public const string Promoted = "promoted";
}

/// <summary>Provenance values, matching the CHECK on <c>provenance</c> columns.</summary>
public static class Provenances
{
    public const string Declared = "declared";
    public const string Observed = "observed";
    public const string Derived = "derived";
}

/// <summary>Subject types, matching the CHECK on <c>bsk.subject.type</c>.</summary>
public static class SubjectTypes
{
    public const string Value = "Value";
    public const string Goal = "Goal";
    public const string Problem = "Problem";
    public const string Project = "Project";
    public const string Task = "Task";
    public const string Commitment = "Commitment";
    public const string Decision = "Decision";
    public const string Idea = "Idea";
    public const string Person = "Person";
    public const string Constraint = "Constraint";
    public const string Season = "Season";

    /// <summary>Area of Focus — a durable life domain items point to (GEN-2 / D1, migration 0014).</summary>
    public const string Area = "Area";

    /// <summary>A habit — its own type composing recurrence + adherence (GEN-3 / D2, migration 0017).</summary>
    public const string Habit = "Habit";

    /// <summary>A calendar appointment; occurrences are materialized subjects (CAL-1 / D10, migration 0020).</summary>
    public const string Appointment = "Appointment";

    /// <summary>
    /// A reward catalog entry — a priced treat or a savings goal currency is redeemed
    /// against. Status-less; archive it to retire it (REWARD-3, migration 0028).
    /// </summary>
    public const string Reward = "Reward";

    /// <summary>
    /// A space in the home (the office, the kitchen…), rated on Tidiness, Cleanliness and
    /// Aesthetics for the Home Score. Status-less; archive it to retire it (HOME-1, D18,
    /// migration 0044).
    /// </summary>
    public const string Space = "Space";

    /// <summary>
    /// Something measured about Chris (Weight, Waist, HbA1c), with a unit, a goal and which way
    /// is better; its readings are versioned <c>measurement</c> events. Status-less; archive it
    /// to retire it (MEASURE-1, D20, migration 0050).
    /// </summary>
    public const string Metric = "Metric";

    /// <summary>
    /// A daily or weekly review (REVIEW-0..2, D3, D8): one per day or per Sunday-named week,
    /// opened on its day, due at the end of it and missed if not completed by then. Its kind and
    /// period are attributes the review service writes (migration 0052).
    /// </summary>
    public const string Review = "Review";

    /// <summary>
    /// Every type, as the CHECK on <c>bsk.subject.type</c> allows them (a test keeps the two
    /// equal), in the order a list of them reads best: the alignment ladder first.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        Value, Area, Goal, Project, Task, Habit, Commitment, Appointment, Decision, Problem, Idea,
        Person, Constraint, Season, Reward, Space, Metric, Review,
    ];

    /// <summary>
    /// The types any door can make as a plain item (<c>bsk new</c>, <c>POST /api/subjects</c>).
    /// A Space, Metric or Review is made only by its own service, which writes what it needs
    /// with it (a Space's rating scale, a Metric's unit, a Review's period).
    /// </summary>
    public static readonly IReadOnlyList<string> Creatable = [.. All.Where(t => t is not (Space or Metric or Review))];

    /// <summary>The type <paramref name="type"/> names, ignoring case, in the form the schema stores; null when it names none.</summary>
    public static string? Canonical(string? type)
        => All.FirstOrDefault(t => string.Equals(t, type?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The creatable type <paramref name="type"/> names, ignoring case. Throws
    /// <see cref="ArgumentException"/> for a name that isn't a type, or one made only by its own service.
    /// </summary>
    public static string ParseCreatable(string? type)
    {
        var canonical = Canonical(type)
            ?? throw new ArgumentException($"Unknown subject type '{type}'. Expected one of: {string.Join(", ", Creatable)}.");
        return Creatable.Contains(canonical)
            ? canonical
            : throw new ArgumentException(
                $"A {canonical} isn't made as a plain item: it has its own page and command, which write what it needs with it.");
    }
}

/// <summary>
/// How an expected habit occurrence turned out (GEN-3). Recorded on the adherence
/// event's payload; the edge is <c>evidences</c> for followed/partial and
/// <c>violates</c> for a miss. Partial is fixed half-credit and is only permitted
/// when the habit's <c>allows_partial</c> flag is set (D2).
/// </summary>
public static class AdherenceResults
{
    public const string Followed = "followed";
    public const string Partial = "partial";
    public const string Missed = "missed";

    public static readonly IReadOnlyList<string> All = [Followed, Partial, Missed];

    /// <summary>The habit attribute that allows <see cref="Partial"/>.</summary>
    public const string AllowsPartialKey = "allows_partial";

    /// <summary>
    /// Whether a habit's <see cref="AllowsPartialKey"/> (as text, <c>attributes->>key</c>) allows
    /// partial credit: "true" in any case, spaces trimmed; anything else, or none, doesn't.
    /// <c>bsk.v_habit</c> reads it the same way (migration 0065, code review CR70).
    /// </summary>
    public static bool PartialAllowed(string? flag) => string.Equals(flag?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What can be done to a Task's timer (TASKS-2). Each action is recorded as an
/// append-only activity event carrying <c>{subject_id, timer: action}</c>; state and
/// recorded time are folded by <c>bsk.v_task_timer</c> (migration 0024). Complete is
/// not a timer action of its own: it stops the timer and then moves the Task's Status.
/// </summary>
public static class TimerActions
{
    public const string Start = "start";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Stop = "stop";

    public static readonly IReadOnlyList<string> All = [Start, Pause, Resume, Stop];
}

/// <summary>The folded state of a Task's timer, as <c>bsk.v_task_timer.state</c> reports it.</summary>
public static class TimerStates
{
    public const string Running = "running";
    public const string Paused = "paused";

    /// <summary>Stopped, or never started (a task with no timer events has no row).</summary>
    public const string Stopped = "stopped";
}

/// <summary>
/// Subject → subject edge kinds, matching the CHECK on <c>bsk.subject_relation.relation</c>.
/// These are the edges both of whose ends are subjects. Event → subject edges live
/// separately in <see cref="SubjectEventRelations"/>; <c>promoted_from</c> is not an
/// edge at all — it is the <c>subject.origin_event_id</c> column.
/// </summary>
public static class SubjectRelations
{
    public const string Serves = "serves";
    public const string ResultsIn = "results_in";
    public const string Supersedes = "supersedes";

    public static readonly IReadOnlyList<string> All = [Serves, ResultsIn, Supersedes];
}

/// <summary>
/// Event → subject edge kinds, matching the CHECK on <c>bsk.subject_event.relation</c>.
/// An event <c>concerns</c> any subject; an event <c>evidences</c> or <c>violates</c>
/// a Commitment. These are recorded through capture / <c>bsk log</c>, never through
/// <c>bsk link</c> (which is subject → subject only).
/// </summary>
public static class SubjectEventRelations
{
    public const string Concerns = "concerns";
    public const string Evidences = "evidences";
    public const string Violates = "violates";

    public static readonly IReadOnlyList<string> All = [Concerns, Evidences, Violates];
}

/// <summary>
/// Roles for a People-association (<c>attributes.people</c>): how a Person is involved
/// in an item. A property, not an alignment edge — see migration 0019.
/// </summary>
public static class PersonRoles
{
    public const string Attendee = "attendee";
    public const string Owner = "owner";
    public const string Assignee = "assignee";
    public const string WaitingFor = "waiting_for";
    public const string Involves = "involves";

    public static readonly IReadOnlyList<string> All = [Attendee, Owner, Assignee, WaitingFor, Involves];
}

/// <summary>
/// Guard rails for managed binary artifacts (CAP-2 / CAP-4). The kernel refuses a
/// payload larger than the configured maximum before any bytes are written — a
/// blunt safety valve, not a policy. The default suits personal audio notes and
/// PDFs; a host can raise or lower it (see the composition root).
/// </summary>
public static class ArtifactLimits
{
    /// <summary>Default maximum binary payload size: 25 MiB.</summary>
    public const long DefaultMaxBytes = 25L * 1024 * 1024;
}

/// <summary>Identifiers for where an event originated (the event's <c>source_id</c>).</summary>
public static class KernelSources
{
    /// <summary>Events written by the <c>bsk</c> command-line interface.</summary>
    public const string Cli = "cli";

    /// <summary>Events written by the ASP.NET Core API (the web door into Application).</summary>
    public const string Api = "api";

    /// <summary>
    /// Records read off the Oura Ring (D14, activity plan §3.2), whichever door ran the sync, so
    /// the CLI and the API dedupe against each other.
    /// </summary>
    public const string Oura = "oura";

    /// <summary>
    /// The prefix for events an agent wrote through <c>/mcp</c> (agent-access plan §5.3). Each
    /// agent writes as <see cref="Agent"/>, so a retried request id dedupes within its own writes.
    /// </summary>
    public const string Mcp = "mcp";

    /// <summary>An agent's source: <c>mcp:&lt;client_id&gt;</c>.</summary>
    public static string Agent(string clientId) => $"{Mcp}:{clientId}";
}
