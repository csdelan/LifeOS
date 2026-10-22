using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Activity;

namespace LifeOs.Api.Http;

/// <summary>
/// The Activity page (ACTIVITY-2..4, activity plan §3.6). Reads go through
/// <see cref="ActivityReader"/> on the reader connection; the one write, a workout's note,
/// through <see cref="WorkoutNoteService"/>; syncing through <see cref="OuraSyncService"/>.
/// <list type="bullet">
/// <item><c>GET /days?from&amp;to</c>, <c>GET /workouts?from&amp;to</c>, <c>GET /tags?from&amp;to</c>
/// (the tags Chris adds in the Oura app, activity plan §3.9), <c>GET /day?date=</c> and
/// <c>GET /summary</c> answer what LifeOS holds; they never call Oura.</item>
/// <item><c>POST /workouts/{id}/note</c> sets the note (an empty text clears it) and answers the
/// workout. A note LifeOS refuses is a 400 in words; a workout it doesn't hold, a 404.</item>
/// <item><c>POST /sync</c> is the page's sync: on opening the page it's skipped when LifeOS
/// synced under 5 minutes ago; <c>force</c> (Sync now) always runs it. Either way, activity XP
/// is settled afterwards (<see cref="ActivityRewards"/>), since a day can come due with nothing
/// new from Oura (its 4 a.m. passing). Oura not connected or refusing access is a 409 in words;
/// Oura down or limiting requests, a 502.</item>
/// </list>
/// </summary>
public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/activity")
            .WithTags("Activity")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/days", GetDays).WithName("GetActivityDays")
            .Produces<ActivityDaysRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/workouts", GetWorkouts).WithName("GetActivityWorkouts")
            .Produces<ActivityWorkoutsRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/tags", GetTags).WithName("GetActivityTags")
            .Produces<ActivityTagsRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/workouts/{id}/note", SetNote).WithName("SetWorkoutNote")
            .Produces<WorkoutRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapGet("/day", GetDay).WithName("GetActivityDay")
            .Produces<ActivityDayDetailRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapGet("/summary", GetSummary).WithName("GetActivitySummary")
            .Produces<ActivitySummaryRead>();
        api.MapPost("/sync", Sync).WithName("SyncActivity")
            .Produces<ActivitySyncRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> GetDays(
        DateOnly? from, DateOnly? to, ActivityReader activity, CancellationToken cancellationToken)
        => Results.Ok(await activity.GetDaysAsync(from, to, cancellationToken));

    private static async Task<IResult> GetWorkouts(
        DateOnly? from, DateOnly? to, ActivityReader activity, CancellationToken cancellationToken)
        => Results.Ok(await activity.GetWorkoutsAsync(from, to, cancellationToken));

    private static async Task<IResult> GetTags(
        DateOnly? from, DateOnly? to, ActivityReader activity, CancellationToken cancellationToken)
        => Results.Ok(await activity.GetTagsAsync(from, to, cancellationToken));

    private static async Task<IResult> GetDay(DateOnly? date, ActivityReader activity, CancellationToken cancellationToken)
        => Results.Ok(await activity.GetDayAsync(date, cancellationToken));

    private static async Task<IResult> GetSummary(
        ActivityReader activity, OuraConnectionService oura, CancellationToken cancellationToken)
        => Results.Ok(await activity.GetSummaryAsync(await oura.GetStatusAsync(cancellationToken), cancellationToken));

    private static async Task<IResult> SetNote(
        string id, WorkoutNoteBody? body, WorkoutNoteService notes, ActivityReader activity, CancellationToken cancellationToken)
    {
        if (body?.Text is not { } text)
        {
            return Results.BadRequest(new ApiError("A note takes text; an empty text clears it."));
        }

        // The page names a workout by its whole Oura id; the CLI's short ids aren't taken here.
        if (!await activity.HasWorkoutAsync(id, cancellationToken))
        {
            return Results.NotFound(new ApiError(
                $"LifeOS holds no workout '{id}': it may have been deleted in the Oura app, or not synced yet."));
        }

        return Results.Ok(ActivityRows.From(await notes.SetAsync(id, text, cancellationToken)));
    }

    private static async Task<IResult> Sync(
        ActivitySyncBody? body, OuraSyncService oura, ActivityRewards rewards, CancellationToken cancellationToken)
    {
        var sync = await oura.SyncForPageAsync(body?.Force ?? false, cancellationToken);
        return Results.Ok(ActivityRows.From(sync, await rewards.SettleAsync(cancellationToken)));
    }
}

/// <summary><c>POST /api/activity/workouts/{id}/note</c>: the note, one line of up to 140 characters; empty clears it.</summary>
public sealed class WorkoutNoteBody
{
    public string? Text { get; set; }
}

/// <summary><c>POST /api/activity/sync</c>: <c>force</c> runs the sync even when LifeOS synced moments ago (Sync now).</summary>
public sealed class ActivitySyncBody
{
    public bool? Force { get; set; }
}
