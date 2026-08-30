namespace LifeOs.Domain;

/// <summary>
/// An event to append to the source stream. Provenance, timestamps and source
/// are always set; payload is a JSON object; derived events carry their sources.
/// </summary>
public sealed record NewEvent(
    string Kind,
    string Provenance,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string SourceId,
    string? ExternalId = null,
    string PayloadJson = "{}",
    IReadOnlyList<Guid>? DerivedFrom = null,
    Guid? ArtifactId = null);

/// <summary>A subject to create. Attributes are a JSON object.</summary>
public sealed record NewSubject(
    string Urn,
    string Type,
    string Title,
    string AttributesJson = "{}",
    Guid? OriginEventId = null);

/// <summary>A lightweight read view of a subject, used for resolve-or-create.</summary>
public sealed record SubjectRef(Guid Id, string Urn, string Type, string Title);
