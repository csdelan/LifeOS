using System.Globalization;
using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Home;

namespace LifeOs.Api.Http;

/// <summary>
/// The home's spaces and ratings (HOME-1 to HOME-3, environment plan §4.7) — the HTTP twin of
/// <c>bsk space</c>, <c>bsk rate</c> and <c>bsk home</c>. Reads go through
/// <see cref="EnvironmentReader"/> on the reader connection; writes call
/// <see cref="SpaceService"/> and <see cref="SpaceRatingService"/>, as the CLI does. A rating
/// write answers what it paid (<c>awards</c>) and each life domain whose level or rank moved
/// (<c>progress</c>). A refusal (a future day, a part outside 1–5 or not in halves, an empty
/// rating, an archived space) is a 400 in words, with nothing written. Archiving a space uses
/// <c>POST /api/archive</c>, as any subject does. Days are home-zone <c>yyyy-MM-dd</c>.
/// </summary>
public static class EnvironmentEndpoints
{
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
