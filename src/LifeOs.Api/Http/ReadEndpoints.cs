using LifeOs.Api.Auth;
using LifeOs.Api.Read;

namespace LifeOs.Api.Http;

/// <summary>
/// Read endpoints over <c>bsk.v_*</c> via the <c>bsk_reader</c> connection.
/// Shapes match <c>web/src/lib/production-ui-types.ts</c>. Composed reads
/// (alignment forest, dashboard) stay in <c>web/src/lib/derive.ts</c> — this
/// surface returns the primitives those functions consume.
/// </summary>
public static class ReadEndpoints
{
    public static void MapReadEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Reads")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/subjects", ListSubjects).WithName("ListSubjects")
            .Produces<IReadOnlyList<SubjectListItem>>();
        api.MapGet("/subjects/{id:guid}", GetSubject).WithName("GetSubject")
            .Produces<SubjectDetail>().Produces(StatusCodes.Status404NotFound);
        api.MapGet("/subjects/{id:guid}/list-item", GetListItem).WithName("GetListItem")
            .Produces<SubjectListItem>().Produces(StatusCodes.Status404NotFound);
        api.MapGet("/subjects/{id:guid}/relations", GetRelations).WithName("GetRelations")
            .Produces<SubjectRelationsResponse>();
        api.MapGet("/subjects/{id:guid}/tags", GetTags).WithName("GetSubjectTags")
            .Produces<IReadOnlyList<string>>();
        api.MapGet("/subjects/{id:guid}/journal", GetJournal).WithName("GetJournal")
            .Produces<IReadOnlyList<JournalEntry>>();
        api.MapGet("/subjects/{id:guid}/history", GetHistory).WithName("GetHistory")
            .Produces<IReadOnlyList<StatusHistoryEntry>>();
        api.MapGet("/subjects/{id:guid}/files", GetFiles).WithName("GetSubjectFiles")
            .Produces<IReadOnlyList<ArtifactRecord>>();
        api.MapGet("/tags", GetTagUniverse).WithName("GetTagUniverse")
            .Produces<IReadOnlyList<TagUniverseItem>>();
        api.MapGet("/inbox", GetInbox).WithName("GetInbox")
            .Produces<IReadOnlyList<InboxItem>>();
        api.MapGet("/inbox/resolved", GetResolvedInbox).WithName("GetResolvedInbox")
            .Produces<IReadOnlyList<ResolvedInboxItem>>();
        api.MapGet("/areas", GetAreas).WithName("GetAreas")
            .Produces<IReadOnlyList<AreaRow>>();
        api.MapGet("/people", GetPeople).WithName("GetPeople")
            .Produces<IReadOnlyList<PersonRow>>();
        api.MapGet("/habits", GetHabits).WithName("GetHabits")
            .Produces<IReadOnlyList<HabitRow>>();
        api.MapGet("/habits/occurrences", GetOccurrences).WithName("GetHabitOccurrences")
            .Produces<IReadOnlyList<HabitOccurrenceRow>>();
        api.MapGet("/edges", GetEdges).WithName("GetAlignmentEdges")
            .Produces<IReadOnlyList<AlignmentEdge>>();
        api.MapGet("/timers", GetTimers).WithName("GetTimers")
            .Produces<IReadOnlyList<TaskTimer>>();
    }

    private static async Task<IResult> GetTimers(SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetTimersAsync(cancellationToken: cancellationToken)));

    /// <summary>
    /// Subjects of a type, or every type. A repeating Task's series is left out unless
    /// <paramref name="includeSeries"/>: its repetitions are the Tasks to do (D16).
    /// </summary>
    private static async Task<IResult> ListSubjects(
        SubjectReader reader, CancellationToken cancellationToken, string? type, bool includeArchived = false, bool includeSeries = false)
        => Results.Ok((await reader.QuerySubjectsAsync(type, includeArchived, includeSeries, cancellationToken: cancellationToken)));

    private static async Task<IResult> GetSubject(Guid id, SubjectReader reader, CancellationToken cancellationToken)
    {
        var subject = await reader.GetSubjectAsync(id, cancellationToken: cancellationToken);
        return subject is null ? Results.NotFound() : Results.Ok(subject);
    }

    private static async Task<IResult> GetListItem(Guid id, SubjectReader reader, CancellationToken cancellationToken)
    {
        var item = await reader.GetListItemAsync(id, cancellationToken: cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> GetRelations(Guid id, SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok(new SubjectRelationsResponse
        {
            Parents = (await reader.GetServesAsync(id, cancellationToken: cancellationToken)),
            Children = (await reader.GetServedByAsync(id, cancellationToken: cancellationToken))
        });

    private static async Task<IResult> GetTags(Guid id, SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetTagsAsync(id, cancellationToken: cancellationToken)));

    private static async Task<IResult> GetJournal(Guid id, SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetJournalAsync(id, cancellationToken: cancellationToken)));

    private static async Task<IResult> GetHistory(Guid id, SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetStatusHistoryAsync(id, cancellationToken: cancellationToken)));

    private static async Task<IResult> GetFiles(Guid id, SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetSubjectFilesAsync(id, cancellationToken: cancellationToken)));

    private static async Task<IResult> GetTagUniverse(SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetTagUniverseAsync(cancellationToken: cancellationToken)));

    private static async Task<IResult> GetInbox(SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetInboxAsync(cancellationToken: cancellationToken)));

    private static async Task<IResult> GetResolvedInbox(SubjectReader reader, CancellationToken cancellationToken, int limit = 50)
        => Results.Ok((await reader.GetResolvedInboxAsync(Math.Clamp(limit, 1, 200), cancellationToken: cancellationToken)));

    private static async Task<IResult> GetAreas(SubjectReader reader, CancellationToken cancellationToken)
        => Results.Ok((await reader.GetAreasAsync(cancellationToken: cancellationToken)));

    private static async Task<IResult> GetPeople(SubjectReader reader, CancellationToken cancellationToken, bool includeArchived = false, string? kind = null)
        => Results.Ok((await reader.GetPeopleAsync(includeArchived, kind, cancellationToken: cancellationToken)));

    private static async Task<IResult> GetHabits(SubjectReader reader, CancellationToken cancellationToken, bool includeArchived = false)
        => Results.Ok((await reader.GetHabitsAsync(includeArchived, cancellationToken: cancellationToken)));

    /// <summary>
    /// Habit days: one habit's or every habit's, on one day (<paramref name="on"/>) or from
    /// <paramref name="from"/> to <paramref name="to"/> (each end optional; a review's days,
    /// REVIEW-5). A day that doesn't parse is ignored.
    /// </summary>
    private static async Task<IResult> GetOccurrences(
        SubjectReader reader, CancellationToken cancellationToken, Guid? habitId = null, string? on = null, string? from = null, string? to = null)
        => Results.Ok((await reader.GetHabitOccurrencesAsync(habitId, Day(on), Day(from), Day(to), cancellationToken: cancellationToken)));

    private static DateOnly? Day(string? text)
        => !string.IsNullOrWhiteSpace(text) && DateOnly.TryParse(text, out var parsed) ? parsed : null;

    private static async Task<IResult> GetEdges(SubjectReader reader, CancellationToken cancellationToken, bool includeArchived = false)
        => Results.Ok((await reader.GetAlignmentEdgesAsync(includeArchived, cancellationToken: cancellationToken)));
}
