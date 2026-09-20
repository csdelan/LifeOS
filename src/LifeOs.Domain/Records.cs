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

/// <summary>
/// A lightweight read view of a source event, used by promotion. <c>SubjectId</c>
/// is the subject the event's payload references (e.g. the Problem an
/// <c>idea_session</c> is about), or <c>null</c> when the event names no subject.
/// </summary>
public sealed record SourceEvent(Guid Id, string Kind, Guid? SubjectId);

/// <summary>
/// An <c>event → subject</c> edge to write alongside its event: which subject, with
/// which relation (concerns/evidences/violates) and provenance. The event id is
/// supplied by the writer that creates the event in the same transaction.
/// </summary>
public sealed record SubjectEventEdge(Guid SubjectId, string Relation, string Provenance);

/// <summary>
/// A binary capture to write as one atomic unit: an event, its artifact (whose text
/// <c>content</c> is the transcript/description sidecar — empty when there is none),
/// and the binary payload with its metadata. <see cref="Sha256"/> is the lowercase-hex
/// content hash; it doubles as the event's <c>external_id</c>, so re-capturing an
/// identical file from the same source collapses to one event (idempotency, inv. 7).
/// </summary>
public sealed record NewBinaryCapture(
    string Kind,
    string Provenance,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string SourceId,
    string Sha256,
    byte[] Bytes,
    string ContentType,
    long ByteSize,
    string? Filename = null,
    string TextContent = "",
    string PayloadJson = "{}");

/// <summary>
/// The outcome of a binary capture: the event and artifact written (or, on a dedup
/// hit, the pre-existing ones). <see cref="Deduplicated"/> is <c>true</c> when an
/// event for this (source, hash) already existed and nothing new was inserted.
/// </summary>
public sealed record BinaryCaptureResult(Guid EventId, Guid ArtifactId, bool Deduplicated);

/// <summary>An artifact's binary payload plus its metadata — the export read model.</summary>
public sealed record ArtifactBlob(
    Guid ArtifactId, byte[] Bytes, string ContentType, long ByteSize, string Sha256, string? Filename);

/// <summary>An artifact's binary metadata without the bytes — for list/metadata reads.</summary>
public sealed record ArtifactBlobMetadata(
    Guid ArtifactId, string ContentType, long ByteSize, string Sha256, string? Filename);
