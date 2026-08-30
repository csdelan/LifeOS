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

/// <summary>Reads and creates subjects. Used by resolve-or-create flows.</summary>
public interface ISubjectRepository
{
    Task<SubjectRef?> FindByUrnAsync(string urn, CancellationToken cancellationToken = default);

    Task<SubjectRef?> FindByTypeAndTitleAsync(
        string type, string title, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(NewSubject newSubject, CancellationToken cancellationToken = default);
}
