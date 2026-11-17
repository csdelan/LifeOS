using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Contracts;

/// <summary>A subject type and how many subjects have it — the left-hand tree.</summary>
public sealed class TypeCount
{
    public string Type { get; set; } = "";
    public long N { get; set; }
}

/// <summary>One row in a subject list (Map, Browse, Goals, Projects, Tasks, …).</summary>
public sealed class SubjectListItem
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Status { get; set; }
    public string? Due { get; set; }
    public string? Scheduled { get; set; }
    public string? TargetDate { get; set; }
    public string? Area { get; set; }
    public string? AreaName { get; set; }
    public string? Tags { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ExpectedCadence { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public string? PersonKind { get; set; }
    public int? VisionOrder { get; set; }

    /// <summary>
    /// A repeating Task's series (D16). Series stay out of the lists unless asked for
    /// (<c>includeSeries</c>): their repetitions are the Tasks to do.
    /// </summary>
    public bool IsSeries { get; set; }

    /// <summary>On a repetition: the id of the repeating Task it belongs to.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>On a repetition: the date it stands for (<c>yyyy-MM-dd</c>).</summary>
    public string? Occurrence { get; set; }

    /// <summary>On a series or a repetition: the series' pattern, the JSON object D8 stores.</summary>
    public string? Repeat { get; set; }

    /// <summary>
    /// What completing it (a Habit: following it for a day) pays, currency → amount — its
    /// own bounty or the type default; a repetition without its own pays its series'. Null for
    /// a type that doesn't earn (REWARD-2).
    /// </summary>
    public IReadOnlyDictionary<string, long>? Bounty { get; set; }

    /// <summary>Its cover image (COVER-1), or null — always null for a type that can't have one.</summary>
    public CoverRead? Cover { get; set; }
}

/// <summary>The detail-pane header fields for one subject, plus the raw attributes jsonb.</summary>
public sealed class SubjectDetail
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Status { get; set; }
    public string? Due { get; set; }
    public string? TargetDate { get; set; }
    public string? ExpectedCadence { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public string? Scope { get; set; }
    public string? Statement { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Archived { get; set; }
    public string? Area { get; set; }
    public string? AreaName { get; set; }
    public string? Attributes { get; set; }

    /// <summary>What each trigger pays (REWARD-2); null for a type that doesn't earn.</summary>
    public IReadOnlyList<BountyLineRow>? Bounty { get; set; }

    /// <summary>
    /// True on a repetition of a repeating Task that has no price of its own: <see cref="Bounty"/>
    /// is its series' price (D16). Setting a bounty on the repetition prices that one only.
    /// </summary>
    public bool BountyFromSeries { get; set; }

    /// <summary>On a repeating Task's series or one of its repetitions: the series and its counts (D16).</summary>
    public TaskRepeatRead? Repeating { get; set; }

    /// <summary>Its cover image (COVER-1), or null — always null for a type that can't have one.</summary>
    public CoverRead? Cover { get; set; }

    /// <summary>
    /// A Person's photo (GEN-15): the artifact whose bytes <c>/api/artifacts/{id}/content</c>
    /// answers, or null. Always null for anything but a Person.
    /// </summary>
    public Guid? PhotoArtifactId { get; set; }
}

/// <summary>One alignment-graph edge, in whichever direction it was queried.</summary>
public sealed class RelationEdge
{
    public string Relation { get; set; } = "";
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string? Title { get; set; }

    /// <summary>The related item's status, or null when it has none (a Goal's sub-goals show it, focus plan G2).</summary>
    public string? Status { get; set; }

    /// <summary>The related item's target date (<c>yyyy-MM-dd</c>), when it has one.</summary>
    public string? TargetDate { get; set; }
}

/// <summary>A raw alignment edge used to compose the Map forest client-side.</summary>
public sealed class AlignmentEdge
{
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public string Relation { get; set; } = "";
}

/// <summary>
/// What the Map draws (<c>GET /api/alignment</c>, code review CR120): the alignment types and any
/// item an edge links, a repeating Task once as its series, and the edges among them. The
/// <c>alignment</c> tool folds the same read.
/// </summary>
public sealed class AlignmentRead
{
    public IReadOnlyList<SubjectListItem> Subjects { get; set; } = [];
    public IReadOnlyList<AlignmentEdge> Edges { get; set; } = [];
}

/// <summary>
/// One page of Browse (BROWSE-2, <c>GET /api/browse</c>, code review CR120): the type list's
/// counts, the type's filter options and columns, how many items it holds and how many the
/// filters keep, and the first of those, sorted.
/// </summary>
public sealed class BrowseRead
{
    /// <summary>Each type Browse lists that holds an item, in its order, with how many.</summary>
    public IReadOnlyList<BrowseTypeCount> Types { get; set; } = [];

    /// <summary>The type's items (every type's for <c>all</c>), archived ones only when included.</summary>
    public int Total { get; set; }

    /// <summary>How many of them the filters keep.</summary>
    public int Matching { get; set; }

    public BrowseOptionsRead Options { get; set; } = new();

    /// <summary>The list's columns, from the type's items before the filters: type, status, due, area, tags, entered.</summary>
    public IReadOnlyList<string> Columns { get; set; } = [];

    /// <summary>The first <c>limit</c> of the items the filters keep, sorted.</summary>
    public IReadOnlyList<SubjectListItem> Rows { get; set; } = [];
}

public sealed class BrowseTypeCount
{
    public string Type { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>What Browse's filters offer for a type: each value with how many of its items have it.</summary>
public sealed class BrowseOptionsRead
{
    public IReadOnlyList<BrowseFilterOption> Statuses { get; set; } = [];
    public IReadOnlyList<BrowseFilterOption> Areas { get; set; } = [];
    public IReadOnlyList<BrowseFilterOption> Tags { get; set; } = [];
}

public sealed class BrowseFilterOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>An item an edge from <see cref="FromId"/> points at.</summary>
public sealed class AlignmentTarget
{
    public Guid FromId { get; set; }
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class SubjectRelationsResponse
{
    public IReadOnlyList<RelationEdge> Parents { get; set; } = [];
    public IReadOnlyList<RelationEdge> Children { get; set; } = [];
}

/// <summary>An event that <c>concerns</c> the selected subject.</summary>
public sealed class ConcerningEvent
{
    public string Kind { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public Guid EventId { get; set; }
    public string? Content { get; set; }
}

/// <summary>One recorded <c>state_change</c> in the subject's status history.</summary>
public sealed class StatusHistoryEntry
{
    public string Status { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public Guid Id { get; set; }
}

/// <summary>The command palette's search: the best matches, and how many match in all (code review CR76).</summary>
public sealed class SubjectSearchResult
{
    public IReadOnlyList<SubjectListItem> Matches { get; set; } = [];
    public int Total { get; set; }
}

/// <summary>How many items wait in the Inbox.</summary>
public sealed class InboxCount
{
    public int Count { get; set; }
}

/// <summary>One flagged item awaiting triage, from <c>bsk.v_inbox</c>.</summary>
public sealed class InboxItem
{
    public Guid ItemId { get; set; }
    public string ItemKind { get; set; } = "";
    public DateTime TriagedAt { get; set; }
    public string? SubjectUrn { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectTitle { get; set; }
    public string? EventKind { get; set; }
    public string? EventContent { get; set; }
    /// <summary>The item's own tags — on the event for a raw capture, on the subject otherwise.</summary>
    public string[] Tags { get; set; } = [];
    /// <summary>
    /// The file or audio the capture carries (CAP-2 / CAP-4): the event's own for a raw capture,
    /// the capture it was made from for an Idea or Problem. Null when there's none.
    /// </summary>
    public ArtifactRecord? Attachment { get; set; }
    /// <summary>A voice note with no transcript: its audio is kept for later processing (CAP-2).</summary>
    public bool NeedsTranscription { get; set; }
}

/// <summary>
/// An item that was triaged and has left the inbox, from <c>bsk.v_inbox_resolved</c>.
/// <see cref="Resolution"/> is the newest triage state (<c>dismissed</c>, <c>promoted</c>,
/// legacy <c>filed</c>/<c>dropped</c>) or <c>status</c> when a terminal Status cleared it.
/// </summary>
public sealed class ResolvedInboxItem
{
    public Guid ItemId { get; set; }
    public string ItemKind { get; set; } = "";
    public DateTime TriagedAt { get; set; }
    public string Resolution { get; set; } = "";
    public DateTime ResolvedAt { get; set; }
    public string? SubjectStatus { get; set; }
    /// <summary>True when the subject must be re-opened before a re-flag can surface it.</summary>
    public bool SubjectStatusTerminal { get; set; }
    public string? SubjectUrn { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectTitle { get; set; }
    public string? EventKind { get; set; }
    public string? EventContent { get; set; }
    public string[] Tags { get; set; } = [];
    public ArtifactRecord? Attachment { get; set; }
    public bool NeedsTranscription { get; set; }
}

/// <summary>
/// A stored file's metadata, from <c>bsk.v_artifact</c> (CAP-2 / CAP-4): never its bytes, which
/// come from <c>GET /api/artifacts/{id}/content</c>.
/// </summary>
public sealed class ArtifactRecord
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    /// <summary>The event that carries it: <c>note</c> for a document, <c>voice</c> for audio.</summary>
    public string EventKind { get; set; } = "";
    public string? Filename { get; set; }
    public string ContentType { get; set; } = "";
    public long ByteSize { get; set; }
    public string? Sha256 { get; set; }
    public bool HasBytes { get; set; }
}

public sealed class AreaRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TagUniverseItem
{
    public string Tag { get; set; } = "";
    public long ItemCount { get; set; }
}

public sealed class HabitRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cue { get; set; }
    public string? Routine { get; set; }
    public string? Reward { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public bool AllowsPartial { get; set; }
    public string? Recurrence { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CurrentStreak { get; set; }
    public string? LastState { get; set; }

    /// <summary>
    /// Planned (not tracked yet), Building, Established (graduated) or Abandoned. New habits
    /// start Planned; null, on habits made before Planned existed, reads as Building.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>What a followed day pays, currency → amount (REWARD-2).</summary>
    public IReadOnlyDictionary<string, long>? Bounty { get; set; }

    /// <summary>Progress toward the establish target, while the habit is Building and unarchived.</summary>
    public HabitEstablishment? Establishment { get; set; }
}

public sealed class HabitOccurrenceRow
{
    public Guid HabitId { get; set; }
    public string HabitUrn { get; set; } = "";
    public string HabitName { get; set; } = "";
    public DateOnly OccurrenceDate { get; set; }
    public string State { get; set; } = "";
    public bool AllowsPartial { get; set; }

    /// <summary>The note recorded with the day's newest check-off, if it had one.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// A tracked habit's day as the app offers it (code review CR105): the kernel's occurrence when it
/// lists one, which can then be missed, or an unrecorded day to log a session done anyway on.
/// </summary>
public sealed class HabitDay
{
    public Guid HabitId { get; set; }
    public string HabitUrn { get; set; } = "";
    public string HabitName { get; set; } = "";
    public DateOnly OccurrenceDate { get; set; }
    public string State { get; set; } = "";
    public bool AllowsPartial { get; set; }
    public string? Note { get; set; }

    /// <summary>The kernel lists the day (its pattern schedules it, or it's recorded), so it can be missed.</summary>
    public bool Listed { get; set; }

    /// <summary>The kernel's <paramref name="row"/> for <paramref name="habit"/> on <paramref name="day"/>, or an unrecorded one.</summary>
    public static HabitDay Of(HabitRow habit, DateOnly day, HabitOccurrenceRow? row) => row is null
        ? new()
        {
            HabitId = habit.Id, HabitUrn = habit.Urn, HabitName = habit.Name, OccurrenceDate = day,
            State = "unrecorded", AllowsPartial = habit.AllowsPartial,
        }
        : new()
        {
            HabitId = row.HabitId, HabitUrn = row.HabitUrn, HabitName = row.HabitName, OccurrenceDate = row.OccurrenceDate,
            State = row.State, AllowsPartial = row.AllowsPartial, Note = row.Note, Listed = true,
        };
}

/// <summary>The Focus page (<c>GET /api/focus</c>), worked out by <c>FocusReader</c>.</summary>
public sealed class DashboardRead
{
    /// <summary>
    /// The Goal Chris chose in a review (focus plan G1), while it's Active; otherwise the Active goal
    /// with the nearest target date, as a suggestion; or a stand-in with no subject when there is none.
    /// </summary>
    public required FocusGoal PrimaryFocus { get; set; }

    /// <summary>
    /// Up to three Active projects that result in the focus or in any of its Active sub-goals,
    /// nearest target date first, or any Active ones when none do (focus plan G2).
    /// </summary>
    public IReadOnlyList<FocusObjective> Objectives { get; set; } = [];

    /// <summary>The focus Goal's Active sub-goals, its milestones, nearest target date first (focus plan G2).</summary>
    public IReadOnlyList<FocusMilestone> Milestones { get; set; } = [];

    /// <summary>Open Tasks due today or late, or whose do-date has come, soonest first (TASKS-1).</summary>
    public IReadOnlyList<SubjectListItem> DueAndOverdue { get; set; } = [];

    /// <summary>Up to four open Tasks In progress that aren't due.</summary>
    public IReadOnlyList<SubjectListItem> NextActions { get; set; } = [];

    public int InboxCount { get; set; }

    /// <summary>Each Building habit's day today.</summary>
    public IReadOnlyList<HabitDay> TodayHabits { get; set; } = [];

    /// <summary>Each Established habit's day today (REWARD-2 maintenance): never scheduled, so never missed.</summary>
    public IReadOnlyList<HabitDay> MaintainingHabits { get; set; } = [];

    public IReadOnlyList<SubjectListItem> ActiveGoals { get; set; } = [];
    public IReadOnlyList<SubjectListItem> ActiveProjects { get; set; } = [];
}

/// <summary>
/// What Focus is on: a goal, or a stand-in titled so with an empty subject id. Its members are
/// <c>required</c> because the web document marks a response's fields present only on a type
/// reached at the top or in a list, and this one is reached only as <see cref="DashboardRead.PrimaryFocus"/>.
/// </summary>
public sealed class FocusGoal
{
    public required string Title { get; set; }
    public required string SubjectId { get; set; }
    public required string Kind { get; set; }
    public CoverRead? Cover { get; set; }

    /// <summary>Chris chose it (TODAY-1: told apart from a suggestion); false for the nearest-target-date suggestion or the stand-in.</summary>
    public required bool Chosen { get; set; }

    /// <summary>When he chose it; null unless <see cref="Chosen"/>.</summary>
    public required DateTimeOffset? ChosenAt { get; set; }

    /// <summary>The review he chose it in ("Weekly review, Sun Nov 22"); null unless chosen in one.</summary>
    public required string? ChosenIn { get; set; }

    /// <summary>A Goal he chose that is no longer Active or was archived, so isn't the focus now; null otherwise.</summary>
    public required string? LapsedTitle { get; set; }

    /// <summary>What <see cref="LapsedTitle"/> reads as now: its status (Completed, Abandoned, ...) or Archived.</summary>
    public required string? LapsedAs { get; set; }
}

public sealed class FocusObjective
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public Guid SubjectId { get; set; }
}

/// <summary>One of the focus Goal's Active sub-goals (focus plan G2).</summary>
public sealed class FocusMilestone
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";

    /// <summary>Its target date (<c>yyyy-MM-dd</c>), when it has one.</summary>
    public string? TargetDate { get; set; }
}

public sealed class AppointmentRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Date { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? AllDay { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }
    public string? Area { get; set; }
    public string? SeriesUrn { get; set; }
    public string? Recurrence { get; set; }
    public string Status { get; set; } = "";
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PersonAssociationRow
{
    public Guid SubjectId { get; set; }
    public string SubjectUrn { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string SubjectTitle { get; set; } = "";
    public string Role { get; set; } = "";
    public Guid PersonId { get; set; }
    public string PersonUrn { get; set; } = "";
    public string PersonName { get; set; } = "";
}

public sealed class PersonRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PersonKind { get; set; }
    public string? Role { get; set; }
    public bool Archived { get; set; }

    /// <summary>Its photo (GEN-15): the artifact whose bytes <c>/api/artifacts/{id}/content</c> answers, or null.</summary>
    public Guid? PhotoArtifactId { get; set; }
}

public sealed class JournalEntry
{
    public Guid EventId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Content { get; set; } = "";
}

public sealed class CreatedSubject
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>
/// The subject whose recurrence was set, the stored recurrence (JSON, null when cleared), and
/// for a repeating Task the repetitions it made.
/// </summary>
public sealed class RecurResult
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string? Recurrence { get; set; }
    public IReadOnlyList<CreatedSubject> Made { get; set; } = [];
}

/// <summary>What Materialize made: the series and each new occurrence (none when all were made already).</summary>
public sealed class MaterializeRow
{
    public CreatedSubject Series { get; set; } = new();
    public IReadOnlyList<CreatedSubject> Created { get; set; } = [];
}

public sealed class RenamedSubject
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string PreviousTitle { get; set; } = "";
    public bool Changed { get; set; }
    /// <summary>For an Appointment series: upcoming occurrences that took the new title.</summary>
    public int OccurrencesRenamed { get; set; }
}

public sealed class RenamedTag
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int Renamed { get; set; }
    public int Merged { get; set; }
}

/// <summary>
/// A Task's timer, from <c>bsk.v_task_timer</c> (TASKS-2). The seconds count closed
/// segments only: while <see cref="State"/> is <c>running</c>, add now − <see cref="RunningSince"/>.
/// </summary>
public sealed class TaskTimer
{
    public Guid SubjectId { get; set; }
    public string SubjectUrn { get; set; } = "";
    public string SubjectTitle { get; set; } = "";
    /// <summary><c>running</c>, <c>paused</c>, or <c>stopped</c>.</summary>
    public string State { get; set; } = "";
    public DateTime? RunningSince { get; set; }
    public long TotalSeconds { get; set; }
    /// <summary>Closed time in the newest session (start → stop, across pauses).</summary>
    public long SessionSeconds { get; set; }
    public DateTime? SessionStartedAt { get; set; }
    public long Sessions { get; set; }
    public DateTime LastChangedAt { get; set; }
}

/// <summary>The outcome of a timer action: the task's timer afterwards and anything it paused.</summary>
public sealed class TimerActionResult
{
    public TaskTimer Timer { get; set; } = new();
    /// <summary>Other tasks whose running timer this action paused (one timer runs at a time).</summary>
    public IReadOnlyList<CreatedSubject> Paused { get; set; } = [];
    /// <summary>True when the action was <c>complete</c> and the Task moved to Completed.</summary>
    public bool Completed { get; set; }
    /// <summary>What completing earned (REWARD-4); empty for every other action.</summary>
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
    /// <summary>Each life domain whose level or rank completing changed (xp-domains plan §3.8).</summary>
    public IReadOnlyList<DomainChangeRow> Progress { get; set; } = [];
}

/// <summary>
/// Maps the kernel's subject writes onto the API's shapes, which <c>bsk --json</c> prints for the
/// same write (code review CR89).
/// </summary>
public static class SubjectRows
{
    public static CreatedSubject From(SubjectRef subject) => new()
    {
        Id = subject.Id,
        Urn = subject.Urn,
        Type = subject.Type,
        Title = subject.Title
    };

    public static RenamedSubject From(RenameResult result) => new()
    {
        Id = result.Subject.Id,
        Title = result.Subject.Title,
        PreviousTitle = result.PreviousTitle,
        Changed = result.Changed,
        OccurrencesRenamed = result.OccurrencesRenamed
    };

    public static RenamedTag From(TagRenameResult result) => new()
    {
        From = result.From,
        To = result.To,
        Renamed = result.Renamed,
        Merged = result.Merged
    };

    public static RecurResult From(RecurrenceResult result) => new()
    {
        Id = result.Subject.Id,
        Urn = result.Subject.Urn,
        Recurrence = result.Recurrence,
        Made = result.Made.Select(From).ToList()
    };

    /// <summary>A recurrence cleared: the subject, and no recurrence or repetitions.</summary>
    public static RecurResult Cleared(SubjectRef subject) => new() { Id = subject.Id, Urn = subject.Urn };

    public static TaskSettleRead From(TaskSettleResult result) => new()
    {
        Series = result.Series,
        Made = result.Made.Select(made => From(made)).ToList()
    };

    public static MaterializeRow From(MaterializeResult result) => new()
    {
        Series = From(result.Series),
        Created = result.Created.Select(From).ToList()
    };
}
