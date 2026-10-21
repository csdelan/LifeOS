using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using Microsoft.Net.Http.Headers;

namespace LifeOs.Api.Http;

/// <summary>
/// An artifact's bytes (a food photo, FOOD-5), for the owner only. The web app fetches them
/// with its token and shows them through an object URL, since an <c>img</c> can't carry the
/// token. Bytes never change, so the answer is cached for a year in the browser
/// (<c>private, immutable</c>), its ETag is the content hash, and a matching
/// <c>If-None-Match</c> answers 304 without fetching the bytes.
/// </summary>
public static class ArtifactEndpoints
{
    private const string Immutable = "private, max-age=31536000, immutable";

    public static void MapArtifactEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/artifacts")
            .WithTags("Artifacts")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/{id:guid}/content", GetContent).WithName("GetArtifactContent")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "image/jpeg", "image/png", "image/webp", "application/octet-stream")
            .Produces(StatusCodes.Status304NotModified)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// The artifact's bytes in their own content type. Images show inline; anything else
    /// downloads, sandboxed, so a stored page can never run as this site.
    /// </summary>
    private static async Task<IResult> GetContent(
        Guid id, ArtifactReader artifacts, HttpContext context, CancellationToken cancellationToken)
    {
        var metadata = await artifacts.GetMetadataAsync(id, cancellationToken);
        if (metadata is null)
        {
            return Results.NotFound(new ApiError($"No artifact with bytes has the id {id}."));
        }

        var etag = new EntityTagHeaderValue($"\"{metadata.Sha256}\"");
        var headers = context.Response.Headers;
        headers.CacheControl = Immutable;
        headers.ETag = etag.ToString();
        var image = IsImage(metadata.ContentType);
        if (!image)
        {
            headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        }

        var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false)))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        var blob = await artifacts.GetAsync(id, cancellationToken);
        if (blob is null)
        {
            return Results.NotFound(new ApiError($"No artifact with bytes has the id {id}."));
        }

        return Results.Bytes(
            blob.Bytes, blob.ContentType,
            fileDownloadName: image ? null : blob.Filename ?? $"artifact-{id:N}",
            entityTag: etag);
    }

    private static bool IsImage(string contentType)
        => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
           && !contentType.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase);
}
