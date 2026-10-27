using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Food;
using LifeOs.Application.Photos;
using LifeOs.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LifeOs.Api.Http;

/// <summary>
/// The Food Log (FOOD-1..4, food-log plan §3.6) — the HTTP twin of <c>bsk food</c>. Reads go
/// through <see cref="FoodReader"/> on the reader connection; writes call
/// <see cref="FoodLogService"/>, as the CLI does, and answer what settling the food days paid
/// (<c>awards</c>) and each life domain whose level or rank moved (<c>progress</c>). A refusal
/// (a bad score, a future time, an overlapping fast) is a 400 in words, with nothing written.
/// Days are home-zone <c>yyyy-MM-dd</c>; times are ISO instants with an offset.
/// <para>
/// A photo (FOOD-5) is sent as <c>multipart/form-data</c> with a <c>photo</c> file field and
/// stored through <see cref="FoodPhotoService"/>; its bytes come back from
/// <c>GET /api/artifacts/{id}/content</c>. Uploads use the bearer token, not cookies, so they
/// need no antiforgery token.
/// </para>
/// </summary>
public static class FoodEndpoints
{
    private const int DefaultRecent = 20;

    // Room in a photo upload for the multipart framing around the file.
    private const int PhotoRequestOverhead = 64 * 1024;

    public static void MapFoodEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/food")
            .WithTags("Food")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/day", GetDay).WithName("GetFoodDay")
            .Produces<FoodDayRead>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/days", GetDays).WithName("GetFoodDays")
            .Produces<FoodDaysRead>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/recent", GetRecent).WithName("GetRecentFood")
            .Produces<IReadOnlyList<MealRow>>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/drinks", GetDrinks).WithName("GetFoodDrinks")
            .Produces<FoodDrinksRead>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/meals", LogMeal).WithName("LogFood")
            .Produces<FoodWriteResultRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/meals/{id}", EditMeal).WithName("EditFood")
            .Produces<FoodWriteResultRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/meals/{id}/delete", DeleteMeal).WithName("DeleteFood")
            .Produces<FoodWriteResultRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/meals/{id}/photo", SetPhoto).WithName("SetFoodPhoto")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoChecks.MaxBytes + PhotoRequestOverhead))
            .Produces<FoodWriteResultRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/meals/{id}/photo/delete", RemovePhoto).WithName("RemoveFoodPhoto")
            .Produces<FoodWriteResultRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/fasts", Fast).WithName("FastAction")
            .Produces<FoodWriteResultRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/close", Close).WithName("CloseFoodDay")
            .Produces<FoodWriteResultRow>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/settle", Settle).WithName("SettleFood")
            .Produces<EarnResult>();
    }

    /// <summary>One day of the Food Log, today by default.</summary>
    private static async Task<IResult> GetDay(DateOnly? date, FoodReader food, CancellationToken cancellationToken)
        => Results.Ok(await food.GetDayAsync(date, cancellationToken));

    /// <summary>
    /// Every day from <paramref name="from"/> to <paramref name="to"/> (inclusive, at most 62):
    /// <paramref name="to"/> defaults to today and <paramref name="from"/> to that week's Monday.
    /// </summary>
    private static async Task<IResult> GetDays(DateOnly? from, DateOnly? to, FoodReader food, CancellationToken cancellationToken)
        => Results.Ok(await food.GetDaysAsync(from, to, cancellationToken));

    /// <summary>
    /// The alcoholic drinks drunk between <paramref name="from"/> and <paramref name="to"/>
    /// (FOOD-6): each a home-zone day (<c>yyyy-MM-dd</c>, both included) or an ISO instant
    /// (<paramref name="to"/> excluded). The last 30 days with today by default; any range.
    /// </summary>
    private static async Task<IResult> GetDrinks(string? from, string? to, FoodReader food, CancellationToken cancellationToken)
        => Results.Ok(await food.GetDrinksAsync(from, to, cancellationToken));

    /// <summary>The latest entry of each name, newest first (20 by default, 1 to 100): "Log again".</summary>
    private static async Task<IResult> GetRecent(int? limit, FoodReader food, CancellationToken cancellationToken)
        => Results.Ok(await food.GetRecentAsync(limit ?? DefaultRecent, cancellationToken));

    /// <summary>
    /// Logs a food entry; with <c>endFast</c>, the running fast ends at the entry's time in the
    /// same write, and with <c>photoArtifactId</c> it shows that stored photo (Log again).
    /// </summary>
    private static async Task<IResult> LogMeal(FoodEntryBody body, FoodLogService food, CancellationToken cancellationToken)
    {
        if (Missing(body) is { } missing)
        {
            return missing;
        }

        var result = await food.LogAsync(
            Input(body, body.EatenAt, body.MealType), body.EndFast, body.PhotoArtifactId, cancellationToken);
        return Results.Ok(FoodRows.From(result));
    }

    /// <summary>
    /// Replaces a food entry (its id, or the start of it) with the one sent: the whole entry, as
    /// the Log food sheet sends it, so a nutrient left out is cleared. Its time, meal type and
    /// alcohol flag stay as they were when left out.
    /// </summary>
    private static async Task<IResult> EditMeal(
        string id, FoodEntryBody body, FoodLogService food, CancellationToken cancellationToken)
    {
        if (Missing(body) is { } missing)
        {
            return missing;
        }

        var meal = await food.ResolveMealAsync(id, cancellationToken);
        var result = await food.EditAsync(
            meal.Id.ToString(), Input(body, body.EatenAt ?? meal.EatenAt, body.MealType ?? meal.MealType), cancellationToken);
        return Results.Ok(FoodRows.From(result));
    }

    /// <summary>Deletes a food entry: a voided version, so the record keeps what it was.</summary>
    private static async Task<IResult> DeleteMeal(string id, FoodLogService food, CancellationToken cancellationToken)
        => Results.Ok(FoodRows.From(await food.DeleteAsync(id, cancellationToken)));

    /// <summary>
    /// Sets an entry's photo (replacing any it had): a JPEG, PNG or WebP of at most 5 MB in the
    /// <c>photo</c> field. The web app sends a 1,200-pixel JPEG.
    /// </summary>
    private static async Task<IResult> SetPhoto(
        string id, IFormFile? photo, FoodPhotoService photos, CancellationToken cancellationToken)
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
        var result = await photos.AttachAsync(id, buffer.ToArray(), photo.FileName, cancellationToken);
        return Results.Ok(FoodRows.From(result));
    }

    /// <summary>Removes an entry's photo. Its bytes stay in the record; the entry just stops showing them.</summary>
    private static async Task<IResult> RemovePhoto(string id, FoodPhotoService photos, CancellationToken cancellationToken)
        => Results.Ok(FoodRows.From(await photos.RemoveAsync(id, cancellationToken)));

    /// <summary>
    /// A fast action: <c>begin</c> or <c>end</c> (at <c>at</c>, default now), <c>record</c> a
    /// past fast (<c>from</c>, <c>to</c>), <c>edit</c> one (<c>fast</c>, <c>from</c>, and
    /// <c>to</c> unless it's still running) or <c>delete</c> one (<c>fast</c>).
    /// </summary>
    private static async Task<IResult> Fast(FoodFastBody body, FoodLogService food, CancellationToken cancellationToken)
    {
        var action = (body.Action ?? "").Trim().ToLowerInvariant();
        FoodWriteResult result;
        switch (action)
        {
            case "begin":
                result = await food.BeginFastAsync(body.At, cancellationToken);
                break;
            case "end":
                result = await food.EndFastAsync(body.At, cancellationToken);
                break;
            case "record":
                if (body.From is not { } from || body.To is not { } to)
                {
                    return Results.BadRequest(new ApiError("Recording a fast takes from and to."));
                }

                result = await food.RecordFastAsync(from, to, cancellationToken);
                break;
            case "edit":
                if (string.IsNullOrWhiteSpace(body.Fast) || body.From is not { } start)
                {
                    return Results.BadRequest(new ApiError("Editing a fast takes fast and from (and to, once it has ended)."));
                }

                result = await food.EditFastAsync(body.Fast, start, body.To, cancellationToken);
                break;
            case "delete":
                if (string.IsNullOrWhiteSpace(body.Fast))
                {
                    return Results.BadRequest(new ApiError("Deleting a fast takes fast."));
                }

                result = await food.DeleteFastAsync(body.Fast, cancellationToken);
                break;
            default:
                return Results.BadRequest(new ApiError(
                    $"Unknown fast action '{body.Action}'. Use begin, end, record, edit or delete."));
        }

        return Results.Ok(FoodRows.From(result));
    }

    /// <summary>
    /// Closes a day (today by default) so its food pays. A day already closed, by hand or
    /// because it's over, writes nothing (<c>already_closed</c>) but still settles.
    /// </summary>
    private static async Task<IResult> Close(FoodCloseBody? body, FoodLogService food, CancellationToken cancellationToken)
        => Results.Ok(FoodRows.From(await food.CloseDayAsync(body?.Date, cancellationToken)));

    /// <summary>
    /// Settles every food day with the ledger (plan §3.4): what the Food Log page calls when it
    /// opens, so a day that closed itself pays then. Never opens the wallet; safe to repeat.
    /// </summary>
    private static async Task<IResult> Settle(FoodLogService food, CancellationToken cancellationToken)
    {
        var earned = await food.SettleAsync(cancellationToken);
        return Results.Ok(RewardRows.Earned(earned.Awards, earned.Progress));
    }

    private static IResult? Missing(FoodEntryBody body)
        => string.IsNullOrWhiteSpace(body.Name) || (body.Score is null && body.Alcoholic != true) || body.Calories is null
            ? Results.BadRequest(new ApiError("A food entry takes name, score and calories (a drink needs no score)."))
            : null;

    private static FoodEntryInput Input(FoodEntryBody body, DateTimeOffset? eatenAt, string? mealType)
        => new(
            body.Name!, body.Score ?? FoodEntries.DrinkScore, body.Calories!.Value, eatenAt, mealType,
            body.ProteinG, body.CarbsG, body.FatG, body.Alcoholic);
}

/// <summary>
/// A food entry (FOOD-1) for <c>POST /api/food/meals</c> and <c>/meals/{id}</c>. Name, score
/// (1–5 in half steps) and calories (0–5,000) are required; the rest is optional.
/// </summary>
public sealed class FoodEntryBody
{
    /// <summary>1–80 characters.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// 1 (junk food) to 5 (very healthy) in whole or half points (3.5); always 1 for an
    /// alcoholic drink, which needs none.
    /// </summary>
    public decimal? Score { get; set; }

    /// <summary>A rough estimate, 0 to 5,000.</summary>
    public int? Calories { get; set; }

    /// <summary>When it was eaten; now by default (when logging).</summary>
    public DateTimeOffset? EatenAt { get; set; }

    /// <summary>
    /// <c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c> or <c>drink</c>; guessed from
    /// the home-zone time when logging without one.
    /// </summary>
    public string? MealType { get; set; }

    public int? ProteinG { get; set; }
    public int? CarbsG { get; set; }
    public int? FatG { get; set; }

    /// <summary>
    /// One alcoholic drink (FOOD-6): its score is always 1 and its meal type defaults to a drink.
    /// Left out, a new entry isn't a drink and an edit keeps what the entry was.
    /// </summary>
    public bool? Alcoholic { get; set; }

    /// <summary>Logging only: end the running fast at the entry's time ("End your fast?").</summary>
    public bool EndFast { get; set; }

    /// <summary>
    /// Logging only: a photo already stored (another entry's <c>photoArtifactId</c>) for the new
    /// entry to show. Log again and Have another carry the last one's photo this way, so it
    /// isn't uploaded or stored again. Refused in words when it isn't a stored photo.
    /// </summary>
    public Guid? PhotoArtifactId { get; set; }
}

/// <summary><c>POST /api/food/fasts</c>: one fast action and what it takes.</summary>
public sealed class FoodFastBody
{
    /// <summary><c>begin</c>, <c>end</c>, <c>record</c>, <c>edit</c> or <c>delete</c>.</summary>
    public string? Action { get; set; }

    /// <summary>The fast to edit or delete: its id, or the start of it.</summary>
    public string? Fast { get; set; }

    /// <summary>When a fast begins or ends; now by default.</summary>
    public DateTimeOffset? At { get; set; }

    /// <summary>A recorded or edited fast's start.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>A recorded or edited fast's end; left out of an edit, a running fast keeps running.</summary>
    public DateTimeOffset? To { get; set; }
}

/// <summary><c>POST /api/food/close</c>: the day to close, today by default.</summary>
public sealed class FoodCloseBody
{
    public DateOnly? Date { get; set; }
}
