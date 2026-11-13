using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// Subjects and what hangs off them: relations, edges to events, tags, appointments, adherence and timers.

/// <summary>Reads and creates subjects. Used by resolve-or-create flows.</summary>
public interface ISubjectRepository
{
    Task<SubjectRef?> FindByUrnAsync(string urn, CancellationToken cancellationToken = default);

    Task<SubjectRef?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SubjectRef?> FindByTypeAndTitleAsync(
        string type, string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds subjects whose URN carries the given short id (the minted hex tail).
    /// Returns every match so the caller can disambiguate; short ids are unique by
    /// construction, so this is normally zero or one.
    /// </summary>
    Task<IReadOnlyList<SubjectRef>> FindByShortIdAsync(
        string shortId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds subjects whose title contains <paramref name="fragment"/>
    /// (case-insensitive), for fuzzy resolution. An exact case-insensitive title
    /// match, when present, sorts first so the resolver can prefer it.
    /// </summary>
    Task<IReadOnlyList<SubjectRef>> FindByTitleContainsAsync(
        string fragment, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(NewSubject newSubject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subject and a parent edge (child <paramref name="relation"/> parent,
    /// child = the edge's <c>from</c>) in one transaction, so parent-first creation is
    /// all-or-nothing: if the edge cannot be written, the child is not left orphaned
    /// (GEN-7). Returns the new child id and edge id.
    /// </summary>
    Task<(Guid ChildId, Guid EdgeId)> CreateWithParentEdgeAsync(
        NewSubject child, string relation, Guid parentId, string provenance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subject and an edge pointing <em>into</em> it (existing
    /// <paramref name="fromId"/> <paramref name="relation"/> new subject) in one
    /// transaction — the shape promotion needs: an Idea <c>results_in</c> the new work
    /// it produced. All-or-nothing, so a failed edge leaves no orphan. Returns the new
    /// subject id and edge id.
    /// </summary>
    Task<(Guid NewId, Guid EdgeId)> CreateWithIncomingEdgeAsync(
        NewSubject newSubject, string relation, Guid fromId, string provenance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a jsonb patch into a subject's <c>attributes</c> and removes the named
    /// keys, in one update. Returns whether a row was affected (<c>false</c> = no such
    /// subject). Only the attributes bag is touched — type, title, and status are
    /// unaffected; status still moves solely by a <c>state_change</c> event.
    /// </summary>
    Task<bool> UpdateAttributesAsync(
        Guid id, string patchJson, IReadOnlyList<string> removeKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a subject's <c>title</c> in place. Only the title column is touched:
    /// the URN is an immutable handle (its slug is a birth-time convenience), and
    /// edges/tags reference the subject by <c>id</c>, so a rename never breaks a link.
    /// Returns whether a row was affected (<c>false</c> = no such subject). Renaming a
    /// reuse-by-title subject (e.g. a Problem) onto a title that already exists trips
    /// the store's uniqueness guarantee, surfaced as <see cref="DuplicateSubjectException"/>.
    /// </summary>
    Task<bool> RenameAsync(
        Guid id, string newTitle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single top-level attribute value as text (<c>attributes-&gt;&gt;key</c>),
    /// or <c>null</c> when the subject or the key is absent.
    /// </summary>
    Task<string?> GetAttributeValueAsync(
        Guid id, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// The subject's current status, folded from its <c>state_change</c> events (the newest
    /// wins), or <c>null</c> when it has none.
    /// </summary>
    Task<string?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Repeating appointments in the store (CAL-1 / D10).</summary>
public interface IAppointmentRepository
{
    /// <summary>
    /// Makes the occurrences of <paramref name="series"/> through <paramref name="through"/> that
    /// aren't made yet, in one transaction under an advisory lock per series, so two runs at once
    /// make each date once (code review CR65). A date is made when an occurrence stands for it
    /// (<see cref="AppointmentSeries.OccurrenceKey"/>, or an older occurrence's date). When more
    /// than <paramref name="max"/> are due it makes none, and says how many were.
    /// </summary>
    Task<OccurrencesMade> MaterializeAsync(
        SubjectRef series, DateOnly through, int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an Appointment and, in the same transaction, the occurrences it has
    /// materialized that are dated on or after <paramref name="fromDate"/> and still
    /// carry <paramref name="previousTitle"/> (each occurrence copied the series title
    /// when it was created). Past occurrences and ones renamed individually keep their
    /// titles. For a one-time appointment or an occurrence nothing else matches.
    /// Returns whether the appointment existed and how many occurrences followed it.
    /// </summary>
    Task<(bool Updated, int OccurrencesRenamed)> RenameWithUpcomingOccurrencesAsync(
        Guid id, string urn, string previousTitle, string newTitle, DateOnly fromDate,
        CancellationToken cancellationToken = default);
}

/// <summary>What one Materialize made, and how many occurrences were due (none made when that's over the limit).</summary>
public sealed record OccurrencesMade(IReadOnlyList<SubjectRef> Created, int Due);

/// <summary>Creates directed subject → subject edges in <c>bsk.subject_relation</c>.</summary>
public interface IRelationRepository
{
    Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a directed edge (from → relation → to). An alignment edge is a
    /// structural fact, not a lifecycle event, so removal is a hard delete rather
    /// than a tombstone (unlike status / archive, whose history matters). Returns the
    /// number of edges removed — 0 when none matched.
    /// </summary>
    Task<int> DeleteAsync(
        Guid fromSubject, string relation, Guid toSubject,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes an event and its <c>event → subject</c> edges as one atomic unit, so an
/// activity and the commitments it evidences/violates are never half-recorded. The
/// event stream is append-only, so a partial write cannot be corrected after the
/// fact — atomicity here is a correctness requirement, not an optimisation.
/// </summary>
public interface IActivityWriter
{
    Task<Guid> WriteAsync(
        NewEvent activityEvent, IReadOnlyList<SubjectEventEdge> edges,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="text"/> as the event's artifact (when given), the event and its edges
    /// as one transaction (code review CR68). When the event's source already wrote its external id
    /// (invariant 5), nothing is written, not even the text, and the answer is the earlier event,
    /// <see cref="EventWrite.AlreadyWritten"/>. A retry that races its first call waits for it.
    /// </summary>
    Task<EventWrite> WriteOnceAsync(
        NewEvent newEvent, string? text, IReadOnlyList<SubjectEventEdge> edges,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What <see cref="IActivityWriter.WriteOnceAsync"/> did: the event and its text, and its kind.
/// When <paramref name="AlreadyWritten"/> they're the earlier event's, whose kind may differ.
/// </summary>
public sealed record EventWrite(Guid EventId, Guid? ArtifactId, string Kind, bool AlreadyWritten);

/// <summary>
/// Reads and appends Task timer actions (TASKS-2). Nothing about a timer is stored
/// except its append-only actions; state and recorded time are folded by
/// <c>bsk.v_task_timer</c>.
/// </summary>
public interface ITimerRepository
{
    /// <summary>The task's timer, or <c>null</c> when it has never been started.</summary>
    Task<TimerSnapshot?> GetAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>Every timer that is running right now — normally none or one.</summary>
    Task<IReadOnlyList<TimerSnapshot>> GetRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>The task's status now, folded from its events; null when it has none.</summary>
    Task<string?> GetTaskStatusAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends timer actions, each an event plus a <c>concerns</c> edge to its task, as
    /// one transaction serialized against every other timer write. Each task's state is
    /// re-read inside that transaction; if any no longer matches its
    /// <see cref="TimerWrite.ExpectedState"/>, nothing is written and
    /// <see cref="TimerConflictException"/> is thrown.
    /// </summary>
    Task AppendAsync(IReadOnlyList<TimerWrite> writes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates <c>event → subject</c> edges (concerns/evidences/violates) for events that
/// already exist — the write path behind <c>bsk relate</c>. Distinct from
/// <see cref="IActivityWriter"/>, which writes a new event and its edges together.
/// </summary>
public interface ISubjectEventRepository
{
    /// <summary>
    /// Records one edge, idempotently: an edge that already exists is left untouched.
    /// Returns whether a new edge was created.
    /// </summary>
    Task<bool> CreateEdgeAsync(
        Guid eventId, Guid subjectId, string relation, string provenance,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adds and removes tags on an item (a subject or an event) in <c>bsk.item_tag</c>
/// (GEN-1). Tags arrive already normalized (see <see cref="LifeOs.Domain.Tags"/>).
/// Adding is idempotent; removing a tag that is absent is a no-op.
/// </summary>
public interface ITagRepository
{
    /// <summary>Adds tags to the item; returns how many were newly added.</summary>
    Task<int> AddAsync(
        bool isEvent, Guid itemId, IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>Removes tags from the item; returns how many were removed.</summary>
    Task<int> RemoveAsync(
        bool isEvent, Guid itemId, IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies every tag on the source item onto a subject (promotion carries tags to the
    /// new subject); returns how many were newly added. The source keeps its tags.
    /// </summary>
    Task<int> CopyToSubjectAsync(
        bool fromIsEvent, Guid fromItemId, Guid toSubjectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag everywhere it is used, in one transaction. An item that already
    /// carries <paramref name="newTag"/> keeps that row and drops the old one, so
    /// renaming onto an existing tag merges the two. Returns how many rows were
    /// relabelled and how many were folded into an existing tag.
    /// </summary>
    Task<(int Renamed, int Merged)> RenameAsync(
        string oldTag, string newTag, CancellationToken cancellationToken = default);
}
