using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Subjects;

namespace LifeOs.Api.Http;

/// <summary>
/// Repeating Tasks (TASKS-3, D16), the HTTP twin of <c>bsk tasks</c>: the series to manage,
/// one series' history, and the settle the Tasks and Focus pages call when they open.
/// </summary>
public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/tasks")
            .WithTags("Tasks")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/series", ListSeries).WithName("ListTaskSeries")
            .Produces<IReadOnlyList<TaskSeriesRow>>();
        api.MapGet("/series/{id:guid}", GetSeries).WithName("GetTaskSeries")
            .Produces<TaskSeriesHistory>().Produces(StatusCodes.Status404NotFound);
        api.MapPost("/settle", Settle).WithName("SettleTasks")
            .Produces<TaskSettleRead>();
    }

    /// <summary>Every repeating Task, by title; archived ones only when asked.</summary>
    private static IResult ListSeries(SubjectReader reader, bool includeArchived = false)
        => Results.Ok(reader.GetTaskSeries(includeArchived));

    /// <summary>A repeating Task (by the series' id) and its repetitions, latest first.</summary>
    private static IResult GetSeries(Guid id, SubjectReader reader)
        => reader.GetTaskSeriesHistory(id) is { } history ? Results.Ok(history) : Results.NotFound();

    /// <summary>
    /// Makes the repetitions every repeating Task is owed by today: a calendar date that passed
    /// while the app was closed gets its next. Changes no status. Safe to repeat.
    /// </summary>
    private static async Task<IResult> Settle(TaskSeriesService series, CancellationToken cancellationToken)
    {
        var result = await series.SettleAsync(cancellationToken);
        return Results.Ok(new TaskSettleRead
        {
            Series = result.Series,
            Made = result.Made.Select(made => new CreatedSubject
            {
                Id = made.Id, Urn = made.Urn, Type = made.Type, Title = made.Title
            }).ToList()
        });
    }
}
