using System.Globalization;
using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Home;
using LifeOs.Application.Photos;
using Microsoft.AspNetCore.Mvc;

namespace LifeOs.Api.Http;

/// <summary>
/// The home's spaces, ratings and photos (HOME-1 to HOME-4, environment plan §4.7, §8.3) — the
/// HTTP twin of <c>bsk space</c>, <c>bsk rate</c> and <c>bsk home</c>. A photo is sent as
/// <c>multipart/form-data</c> with a <c>photo</c> file field, as the Food Log's are, and its bytes
/// come back from <c>GET /api/artifacts/{id}/content</c>; photos never move a level, so photo and
/// shot writes answer the space's shots, not <c>progress</c>. Reads go through
/// <see cref="EnvironmentReader"/> on the reader connection; writes call
/// <see cref="SpaceService"/> and <see cref="SpaceRatingService"/>, as the CLI does. A rating
/// write answers what it paid (<c>awards</c>) and each life domain whose level or rank moved
/// (<c>progress</c>). A refusal (a future day, a part outside 1–5 or not in halves, an empty
/// rating, an archived space) is a 400 in words, with nothing written. Archiving a space uses
/// <c>POST /api/archive</c>, as any subject does. Days are home-zone <c>yyyy-MM-dd</c>.
/// </summary>
public static class EnvironmentEndpoints
{
    // Room in a photo upload for the multipart framing and the day and shot fields.
    private const int PhotoRequestOverhead = 64 * 1024;

    public static void MapEnvironmentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/environment")
            .WithTags("Environment")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("", GetHome).WithName("GetHome")
            .Produces<EnvironmentRead>();
        api.MapGet("/days", GetHomeDays).WithName("GetHomeDays")
            .Produces<HomeDaysRead>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/spaces", GetSpaces).WithName("GetSpaces")
            .Produces<IReadOnlyList<SpaceRow>>();
        api.MapPost("/spaces", AddSpace).WithName("AddSpace")
            .Produces<SpaceRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/spaces/{id}", SetSpace).WithName("SetSpace")
            .Produces<SpaceRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/ratings", GetRatings).WithName("GetSpaceRatings")
            .Produces<SpaceRatingsRead>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/ratings", Rate).WithName("RateSpaces")
            .Produces<SpaceRatingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/ratings/{id}", EditRating).WithName("EditSpaceRating")
            .Produces<SpaceRatingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/ratings/{id}/delete", DeleteRating).WithName("DeleteSpaceRating")
            .Produces<SpaceRatingResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);

        // Photos of each space, from a baseline (HOME-4, environment plan §8.3).
        api.MapGet("/spaces/{id}/photos", GetSpacePhotos).WithName("GetSpacePhotos")
            .Produces<SpacePhotosRead>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/spaces/{id}/photos", AddPhoto).WithName("AddSpacePhoto")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoChecks.MaxBytes + PhotoRequestOverhead))
            .Produces<SpacePhotoResult>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/photos/{id}", ChangePhoto).WithName("ChangeSpacePhoto")
            .Produces<SpacePhotoResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/photos/{id}/delete", RemovePhoto).WithName("RemoveSpacePhoto")
            .Produces<SpacePhotoResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/photos/{id}/baseline", SetBaseline).WithName("SetSpacePhotoBaseline")
            .Produces<SpacePhotoResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/spaces/{id}/shots", AddShot).WithName("AddSpaceShot")
            .Produces<SpacePhotoResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/spaces/{id}/shots/{shot}", RenameShot).WithName("RenameSpaceShot")
            .Produces<SpacePhotoResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/spaces/{id}/shots/{shot}/delete", RemoveShot).WithName("RemoveSpaceShot")
            .Produces<SpacePhotoResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    /// <summary>A space's photos, of one shot when given, newest first, each with the space's score on its day.</summary>
    private static async Task<IResult> GetSpacePhotos(
        string id, string? shot, EnvironmentReader environment, CancellationToken cancellationToken)
        => Results.Ok(await environment.GetSpacePhotosAsync(id, shot, cancellationToken));

    /// <summary>
    /// Adds a photo of a space (by id or name): a JPEG, PNG or WebP of at most 5 MB in the
    /// <c>photo</c> field, with an optional <c>day</c> (<c>yyyy-MM-dd</c>; today when left out)
    /// and <c>shot</c> (its id or name; the space's first shot when left out). The same photo
    /// already on that shot comes back unchanged.
    /// </summary>
    private static async Task<IResult> AddPhoto(
        string id, IFormFile? photo, [FromForm] string? day, [FromForm] string? shot,
        SpacePhotoService photos, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        if (photo is null)
        {
            return Results.BadRequest(new ApiError("Send the photo as multipart/form-data, in a field named photo."));
        }

        if (photo.Length > PhotoChecks.MaxBytes)
        {
            return Results.BadRequest(new ApiError(PhotoChecks.TooBig(photo.Length)));
        }

        var on = string.IsNullOrWhiteSpace(day) ? (DateOnly?)null : ParseDay(day);
        using var buffer = new MemoryStream((int)photo.Length);
        await photo.CopyToAsync(buffer, cancellationToken);
        var added = await photos.AddAsync(id, buffer.ToArray(), on, shot, photo.FileName, cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(added.SpaceId, added.Id, cancellationToken));
    }

    /// <summary>Moves a photo to another day or shot.</summary>
    private static async Task<IResult> ChangePhoto(
        string id, SpacePhotoChangeBody body, SpacePhotoService photos, EnvironmentReader environment,
        CancellationToken cancellationToken)
    {
        var day = string.IsNullOrWhiteSpace(body.Day) ? (DateOnly?)null : ParseDay(body.Day);
        var changed = await photos.ChangeAsync(id, day, body.Shot, cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(changed.SpaceId, changed.Id, cancellationToken));
    }

    /// <summary>Removes a photo. Its bytes stay in the record; the shot's first photo left takes over as baseline.</summary>
    private static async Task<IResult> RemovePhoto(
        string id, SpacePhotoService photos, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var removed = await photos.RemoveAsync(id, cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(removed.SpaceId, null, cancellationToken));
    }

    /// <summary>Makes a photo its shot's baseline: the Then its newest photo is compared against.</summary>
    private static async Task<IResult> SetBaseline(
        string id, SpacePhotoService photos, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var photo = await photos.SetBaselineAsync(id, cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(photo.SpaceId, photo.Id, cancellationToken));
    }

    /// <summary>Adds a shot to a space: a spot it's photographed from.</summary>
    private static async Task<IResult> AddShot(
        string id, SpaceShotBody body, SpaceService spaces, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var space = await spaces.AddShotAsync(id, body.Name ?? "", cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(space.Id, null, cancellationToken));
    }

    /// <summary>Renames one of a space's shots (by its id or name).</summary>
    private static async Task<IResult> RenameShot(
        string id, string shot, SpaceShotBody body, SpaceService spaces, EnvironmentReader environment,
        CancellationToken cancellationToken)
    {
        var space = await spaces.RenameShotAsync(id, shot, body.Name ?? "", cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(space.Id, null, cancellationToken));
    }

    /// <summary>Removes one of a space's shots; its photos stay in the record. A space keeps at least one shot.</summary>
    private static async Task<IResult> RemoveShot(
        string id, string shot, SpaceService spaces, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var space = await spaces.RemoveShotAsync(id, shot, cancellationToken);
        return Results.Ok(await environment.PhotoResultAsync(space.Id, null, cancellationToken));
    }

    /// <summary>The home today: the Home Score, each space not archived with its parts, and Fix next.</summary>
    private static async Task<IResult> GetHome(EnvironmentReader environment, CancellationToken cancellationToken)
        => Results.Ok(await environment.GetHomeAsync(cancellationToken));

    /// <summary>The Home Score on each of the last <paramref name="days"/> days (90 by default), oldest first.</summary>
    private static async Task<IResult> GetHomeDays(int? days, EnvironmentReader environment, CancellationToken cancellationToken)
        => Results.Ok(await environment.GetHomeDaysAsync(days ?? SpaceRatingService.DefaultDays, cancellationToken));

    /// <summary>Every space, archived ones included, by name.</summary>
    private static async Task<IResult> GetSpaces(EnvironmentReader environment, CancellationToken cancellationToken)
        => Results.Ok(await environment.GetSpacesAsync(cancellationToken));

    /// <summary>Adds a space; weight and pace are 3 when left out.</summary>
    private static async Task<IResult> AddSpace(
        SpaceBody body, SpaceService spaces, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var space = await spaces.AddAsync(body.Name ?? "", body.Weight, body.Pace, cancellationToken);
        return Results.Ok(await RowAsync(space.Id, environment, cancellationToken));
    }

    /// <summary>Renames, reweights or re-paces a space (by id or name).</summary>
    private static async Task<IResult> SetSpace(
        string id, SpaceBody body, SpaceService spaces, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var space = await spaces.SetAsync(id, body.Name, body.Weight, body.Pace, cancellationToken);
        return Results.Ok(await RowAsync(space.Id, environment, cancellationToken));
    }

    /// <summary>The ratings among the last <paramref name="days"/> (90 by default), of one space when given, newest first.</summary>
    private static async Task<IResult> GetRatings(
        Guid? space, int? days, EnvironmentReader environment, CancellationToken cancellationToken)
        => Results.Ok(await environment.GetRatingsAsync(space, days ?? SpaceRatingService.DefaultDays, cancellationToken));

    /// <summary>Rates one or more spaces on one day (today when none is given): a walk-through or a quick check.</summary>
    private static async Task<IResult> Rate(
        SpaceRatingsBody body, SpaceRatingService ratings, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var day = string.IsNullOrWhiteSpace(body.Day) ? (DateOnly?)null : ParseDay(body.Day);
        var inputs = (body.Ratings ?? []).Select(r => new RatingInput(
                r.Space ?? "", r.Tidiness, r.Cleanliness, r.Aesthetics, r.Note, r.Same ?? false))
            .ToList();
        var result = await ratings.RateAsync(day, inputs, cancellationToken);
        return Results.Ok(EnvironmentRows.From(result, await environment.GetAllSpacesAsync(cancellationToken)));
    }

    /// <summary>Changes a rating's day, parts or note.</summary>
    private static async Task<IResult> EditRating(
        string id, SpaceRatingEditBody body, SpaceRatingService ratings, EnvironmentReader environment,
        CancellationToken cancellationToken)
    {
        var day = string.IsNullOrWhiteSpace(body.Day) ? (DateOnly?)null : ParseDay(body.Day);
        var result = await ratings.EditAsync(
            id, new RatingEdit(day, body.Tidiness, body.Cleanliness, body.Aesthetics, body.Note), cancellationToken);
        return Results.Ok(EnvironmentRows.From(result, await environment.GetAllSpacesAsync(cancellationToken)));
    }

    /// <summary>Deletes a rating.</summary>
    private static async Task<IResult> DeleteRating(
        string id, SpaceRatingService ratings, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        var result = await ratings.DeleteAsync(id, cancellationToken);
        return Results.Ok(EnvironmentRows.From(result, await environment.GetAllSpacesAsync(cancellationToken)));
    }

    private static async Task<SpaceRow> RowAsync(Guid id, EnvironmentReader environment, CancellationToken cancellationToken)
        => (await environment.GetSpacesAsync(cancellationToken)).First(s => s.Id == id);

    private static DateOnly ParseDay(string day)
        => DateOnly.TryParseExact(day.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{day}' isn't a date: give it as yyyy-MM-dd, e.g. 2026-10-27.");
}
