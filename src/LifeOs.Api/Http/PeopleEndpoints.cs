using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Photos;
using LifeOs.Application.Subjects;
using Microsoft.AspNetCore.Mvc;

namespace LifeOs.Api.Http;

/// <summary>
/// A Person's photo (GEN-15), human or AI agent: the HTTP twin of <c>bsk person photo</c>. The
/// photo is sent as <c>multipart/form-data</c> with a <c>photo</c> file field and stored through
/// <see cref="PersonPhotoService"/>; it shows on the People page and the Person's own page through
/// <c>photoArtifactId</c> on <c>GET /api/people</c> and the subject read, and its bytes come back
/// from <c>GET /api/artifacts/{id}/content</c>. Uploads use the bearer token, not cookies, so
/// they need no antiforgery token.
/// </summary>
public static class PeopleEndpoints
{
    // Room in a photo upload for the multipart framing around the file.
    private const int PhotoRequestOverhead = 64 * 1024;

    public static void MapPeopleEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/people")
            .WithTags("People")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapPost("/{id}/photo", SetPhoto).WithName("SetPersonPhoto")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoChecks.MaxBytes + PhotoRequestOverhead))
            .Produces<PersonPhotoRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/{id}/photo/delete", RemovePhoto).WithName("RemovePersonPhoto")
            .Produces<PersonPhotoRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Sets a Person's photo, in place of any it had: a JPEG, PNG or WebP of at most 5 MB in the
    /// <c>photo</c> field. The web app sends a 1,200-pixel JPEG.
    /// </summary>
    private static async Task<IResult> SetPhoto(
        string id, IFormFile? photo, PersonPhotoService photos, CancellationToken cancellationToken)
    {
        if (photo is null)
        {
            return Results.BadRequest(new ApiError("Send the photo as multipart/form-data, in a field named photo."));
        }

        if (photo.Length > PhotoChecks.MaxBytes)
        {
            return Results.BadRequest(new ApiError(PhotoChecks.TooBig(photo.Length)));
        }

        using var buffer = new MemoryStream((int)photo.Length);
        await photo.CopyToAsync(buffer, cancellationToken);
        var set = await photos.SetAsync(id, buffer.ToArray(), photo.FileName, cancellationToken);
        return Results.Ok(PersonPhotoRead.From(set));
    }

    /// <summary>Removes a Person's photo. Its bytes stay in the record; the Person just stops showing them.</summary>
    private static async Task<IResult> RemovePhoto(string id, PersonPhotoService photos, CancellationToken cancellationToken)
        => Results.Ok(PersonPhotoRead.From(await photos.RemoveAsync(id, cancellationToken)));
}
