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
        var api = app.MapGroup("/api").WithTags("Reads");

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
        api.MapGet("/tags", GetTagUniverse).WithName("GetTagUniverse")
            .Produces<IReadOnlyList<TagUniverseItem>>();
        api.MapGet("/inbox", GetInbox).WithName("GetInbox")
            .Produces<IReadOnlyList<InboxItem>>();
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
        api.MapGet("/health", () => Results.Ok(new { ok = true })).WithName("Health").ExcludeFromDescription();
    }

    private static IResult ListSubjects(SubjectReader reader, string? type, bool includeArchived = false)
        => Results.Ok(reader.QuerySubjects(type, includeArchived));

    private static IResult GetSubject(Guid id, SubjectReader reader)
    {
        var subject = reader.GetSubject(id);
        return subject is null ? Results.NotFound() : Results.Ok(subject);
    }

    private static IResult GetListItem(Guid id, SubjectReader reader)
    {
        var item = reader.GetListItem(id);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static IResult GetRelations(Guid id, SubjectReader reader)
        => Results.Ok(new SubjectRelationsResponse
        {
            Parents = reader.GetServes(id),
            Children = reader.GetServedBy(id)
        });

    private static IResult GetTags(Guid id, SubjectReader reader)
        => Results.Ok(reader.GetTags(id));

    private static IResult GetJournal(Guid id, SubjectReader reader)
        => Results.Ok(reader.GetJournal(id));

    private static IResult GetHistory(Guid id, SubjectReader reader)
        => Results.Ok(reader.GetStatusHistory(id));

    private static IResult GetTagUniverse(SubjectReader reader)
        => Results.Ok(reader.GetTagUniverse());

    private static IResult GetInbox(SubjectReader reader)
        => Results.Ok(reader.GetInbox());

    private static IResult GetAreas(SubjectReader reader)
        => Results.Ok(reader.GetAreas());

    private static IResult GetPeople(SubjectReader reader, bool includeArchived = false, string? kind = null)
        => Results.Ok(reader.GetPeople(includeArchived, kind));

    private static IResult GetHabits(SubjectReader reader, bool includeArchived = false)
        => Results.Ok(reader.GetHabits(includeArchived));

    private static IResult GetOccurrences(SubjectReader reader, Guid? habitId = null, string? on = null)
    {
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(on) && DateOnly.TryParse(on, out var parsed))
        {
            date = parsed;
        }

        return Results.Ok(reader.GetHabitOccurrences(habitId, date));
    }

    private static IResult GetEdges(SubjectReader reader, bool includeArchived = false)
        => Results.Ok(reader.GetAlignmentEdges(includeArchived));
}
