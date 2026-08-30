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
}

/// <summary>Creates directed subject → subject edges in <c>bsk.subject_relation</c>.</summary>
public interface IRelationRepository
{
    Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
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
