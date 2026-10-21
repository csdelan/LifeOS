using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// Reads artifact bytes for <c>GET /api/artifacts/{id}/content</c> through a blob store on the
/// reader connection, so serving a photo never needs the owner's rights: the bytes come from
/// the database or the object store, wherever they were put.
/// </summary>
public sealed class ArtifactReader(IArtifactBlobStore store)
{
    public Task<ArtifactBlobMetadata?> GetMetadataAsync(Guid id, CancellationToken cancellationToken)
        => store.GetMetadataAsync(id, cancellationToken);

    public Task<ArtifactBlob?> GetAsync(Guid id, CancellationToken cancellationToken)
        => store.GetAsync(id, cancellationToken);
}
