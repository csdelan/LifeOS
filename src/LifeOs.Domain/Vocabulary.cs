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
}

/// <summary>
/// Triage marker states (INBOX-1). The newest marker per item wins: <c>flagged</c>
/// means the item is in the inbox awaiting a decision; <c>dropped</c> ("nothing to
/// do") and <c>filed</c> (kept as reference) both resolve it out.
/// </summary>
public static class TriageStates
{
    public const string Flagged = "flagged";
    public const string Dropped = "dropped";
    public const string Filed = "filed";

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

/// <summary>Identifiers for where an event originated (the event's <c>source_id</c>).</summary>
public static class KernelSources
{
    /// <summary>Events written by the <c>bsk</c> command-line interface.</summary>
    public const string Cli = "cli";
}
