using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// Captured bytes: artifacts in the database and in object storage.

/// <summary>
/// Stores and retrieves an artifact's <em>binary</em> payload — the swap seam for
/// where the bytes physically live. Without object storage the adapter stores them as
/// Postgres <c>bytea</c> and writes the event + artifact + blob as one transaction, so the
/// bytes commit atomically with their event and the append-only guarantee covers them.
/// With an <see cref="IObjectStorage"/> (Supabase Storage, migration 0035) it uploads the
/// bytes first, then writes the artifact, where the object is and the event as one
/// transaction; nothing above it changes.
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

/// <summary>
/// An object store for artifact bytes (Supabase Storage, binary-artifacts.md). Objects are
/// written once, under content-addressed keys, and never replaced or deleted.
/// </summary>
public interface IObjectStorage
{
    /// <summary>The store's name, recorded with each object (<c>supabase</c>).</summary>
    string Store { get; }

    /// <summary>The bucket new objects go in.</summary>
    string Bucket { get; }

    /// <summary>
    /// Writes <paramref name="bytes"/> at <paramref name="key"/> in <see cref="Bucket"/>, creating
    /// the bucket (private) the first time. An object already at the key is left as it is: keys
    /// are content-addressed, so it holds the same bytes. Throws
    /// <see cref="ObjectStorageException"/> when the store refuses or can't be reached.
    /// </summary>
    Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// The object's bytes, or <c>null</c> when the bucket or the object isn't there. Throws
    /// <see cref="ObjectStorageException"/> when the store refuses or can't be reached.
    /// </summary>
    Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default);
}
