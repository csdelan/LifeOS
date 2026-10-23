using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Covers;
using LifeOs.Domain;

namespace LifeOs.Api.Http;

/// <summary>
/// Cover images (COVER-1..3, D15; cover-images plan §3.4) — the HTTP twin of <c>bsk cover</c>.
/// The picker searches Unsplash through the server, so the Access Key never reaches the
/// browser, and a cover is set by photo id only: the server looks the photo up and stores
/// what Unsplash says. A subject's cover comes back on the ordinary subject reads.
/// </summary>
public static class CoverEndpoints
{
    public static void MapCoverEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Covers")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/covers/search", Search).WithName("SearchCovers")
            .Produces<CoverSearchRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/cover", SetCover).WithName("SetCover")
            .Produces<CoverResult>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status409Conflict)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/cover/position", MoveCover).WithName("MoveCover")
            .Produces<CoverResult>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/cover/clear", ClearCover).WithName("ClearCover")
            .Produces<CoverClearedRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// A page of landscape photos for the picker. Without an Access Key on the server this
    /// answers <c>configured: false</c> (no photos) rather than an error, so the picker can say
    /// covers aren't set up yet; the words are still checked.
    /// </summary>
    private static async Task<IResult> Search(
        CoverService covers, string? q, CancellationToken cancellationToken, int page = 1)
    {
        var words = CoverService.CheckQuery(q);
        if (page is < 1 or > CoverService.MaxPage)
        {
            return Results.BadRequest(new ApiError($"Pages run from 1 to {CoverService.MaxPage}; got {page}."));
        }

        var appName = covers.AppName;
        var read = new CoverSearchRead
        {
            Configured = covers.IsSetUp,
            Query = words,
            Page = page,
            UnsplashLink = Cover.UnsplashLink(appName),
        };
        if (!covers.IsSetUp)
        {
            return Results.Ok(read);
        }

        var found = await covers.SearchAsync(words, page, CoverService.DefaultPerPage, cancellationToken);
        read.Total = found.Total;
        read.TotalPages = found.TotalPages;
        read.Photos = found.Photos.Select(photo => CoverPhotoRead.From(photo, appName)).ToList();
        return Results.Ok(read);
    }

    private static async Task<IResult> SetCover(
        CoverBody body, CoverService covers, SubjectReader reader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.PhotoId))
        {
            return Results.BadRequest(new ApiError("subject and photoId are required."));
        }

        var change = await covers.SetFromUnsplashAsync(
            WriteEndpoints.ResolveWriteRef(body.Subject, reader), body.PhotoId, body.PositionY, cancellationToken);
        return Results.Ok(CoverResult.From(change, covers.AppName));
    }

    private static async Task<IResult> MoveCover(
        CoverPositionBody body, CoverService covers, SubjectReader reader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || body.PositionY is not { } position)
        {
            return Results.BadRequest(new ApiError("subject and positionY are required."));
        }

        var change = await covers.MoveAsync(
            WriteEndpoints.ResolveWriteRef(body.Subject, reader), position, cancellationToken);
        return Results.Ok(CoverResult.From(change, covers.AppName));
    }

    private static async Task<IResult> ClearCover(
        CoverClearBody body, CoverService covers, SubjectReader reader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        var cleared = await covers.ClearAsync(WriteEndpoints.ResolveWriteRef(body.Subject, reader), cancellationToken);
        return Results.Ok(CoverClearedRead.From(cleared));
    }
}

/// <summary><c>POST /api/cover</c> (<c>bsk cover set</c>).</summary>
public sealed class CoverBody
{
    /// <summary>The identity statement (Value), Goal or Project (id, urn or title).</summary>
    public string Subject { get; set; } = "";

    /// <summary>The Unsplash photo's id, as the search gave it.</summary>
    public string PhotoId { get; set; } = "";

    /// <summary>Where the banner is centred vertically, 0 (top) to 100 (bottom); the middle when omitted.</summary>
    public int? PositionY { get; set; }
}

/// <summary><c>POST /api/cover/position</c> (<c>bsk cover move</c>).</summary>
public sealed class CoverPositionBody
{
    public string Subject { get; set; } = "";

    /// <summary>0 (top) to 100 (bottom).</summary>
    public int? PositionY { get; set; }
}

/// <summary><c>POST /api/cover/clear</c> (<c>bsk cover clear</c>).</summary>
public sealed class CoverClearBody
{
    public string Subject { get; set; } = "";
}
