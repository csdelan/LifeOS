using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

/// <summary>A clock, so services can be tested with a fixed time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Stores raw captured content. Stage 1 is text-only; the store returns the id
/// events reference. Content is immutable once written.
/// </summary>
public interface IArtifactStore
{
    Task<Guid> AddAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>Appends events to the source stream. The only write path for events.</summary>
public interface IEventStore
{
    Task<Guid> AppendAsync(NewEvent newEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores and retrieves an artifact's <em>binary</em> payload — the swap seam for
/// where the bytes physically live. The default adapter stores them as Postgres
/// <c>bytea</c> and writes the event + artifact + blob as one transaction, so the
/// bytes commit atomically with their event and the append-only guarantee covers
/// them. A future object-storage adapter (Supabase Storage / S3) implements this
/// same interface — writing bytes to the object store and the artifact / blob-metadata
/// / event rows to Postgres — with no change to anything above it and no schema churn.
/// </summary>
public interface IArtifactBlobStore
{
    /// <summary>
    /// Writes an event, its artifact (text sidecar), and the binary payload as one
    /// atomic unit, deduplicated by content hash: if an event already exists for
    /// (<see cref="NewBinaryCapture.SourceId"/>, <see cref="NewBinaryCapture.Sha256"/>),
    /// nothing is inserted and the pre-existing ids are returned with
    /// <see cref="BinaryCaptureResult.Deduplicated"/> set.
    /// </summary>
    Task<BinaryCaptureResult> PutAsync(NewBinaryCapture capture, CancellationToken cancellationToken = default);

    /// <summary>True when the artifact carries a binary payload.</summary>
    Task<bool> ExistsAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an artifact's bytes and metadata for export, or <c>null</c> when the
    /// artifact has no binary payload. The supported way to get bytes back out
    /// byte-for-byte (behind <c>bsk artifact get</c> and, later, the web API).
    /// </summary>
    Task<ArtifactBlob?> GetAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>Reads an artifact's binary metadata without the bytes, or <c>null</c> when it has none.</summary>
    Task<ArtifactBlobMetadata?> GetMetadataAsync(Guid artifactId, CancellationToken cancellationToken = default);
}

/// <summary>Reads source events. Read-only; the source stream is never mutated.</summary>
public interface IEventReader
{
    Task<SourceEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Reads and creates subjects. Used by resolve-or-create flows.</summary>
public interface ISubjectRepository
{
    Task<SubjectRef?> FindByUrnAsync(string urn, CancellationToken cancellationToken = default);

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
}

/// <summary>
/// Reads which occurrence dates of a recurring appointment series still need to be
/// materialized (CAL-1 / D10). Expands the series' recurrence in the database and
/// subtracts the dates already materialized as occurrence subjects.
/// </summary>
public interface IAppointmentRepository
{
    Task<IReadOnlyList<DateOnly>> PendingOccurrenceDatesAsync(
        Guid seriesId, string seriesUrn, DateOnly through, CancellationToken cancellationToken = default);
}

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
}
