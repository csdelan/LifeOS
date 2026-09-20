using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Api.Http;

/// <summary>
/// Thin HTTP adapters over <c>LifeOs.Application</c> write services — the CLI's
/// twin. Each action validates/deserializes, calls the same service the matching
/// <c>bsk</c> command calls, and serializes. No business logic lives here.
/// </summary>
public static class WriteEndpoints
{
    private static readonly string[] SubjectTypesList =
    [
        SubjectTypes.Value, SubjectTypes.Goal, SubjectTypes.Problem, SubjectTypes.Project,
        SubjectTypes.Task, SubjectTypes.Commitment, SubjectTypes.Decision, SubjectTypes.Idea,
        SubjectTypes.Person, SubjectTypes.Constraint, SubjectTypes.Season, SubjectTypes.Area,
        SubjectTypes.Habit, SubjectTypes.Appointment
    ];

    public static void MapWriteEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Writes");

        api.MapPost("/subjects", NewSubject).WithName("NewSubject")
            .Produces<CreatedSubject>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/promote", Promote).WithName("Promote")
            .Produces<CreatedSubject>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/attributes", SetAttributes).WithName("SetAttributes")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/status", SetStatus).WithName("SetStatus")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/archive", Archive).WithName("Archive")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/restore", Restore).WithName("Restore")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/tag", Tag).WithName("Tag")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/link", Link).WithName("Link")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/relate", Relate).WithName("Relate")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/flag", Flag).WithName("Flag")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/dismiss", Dismiss).WithName("Dismiss")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/drop", Drop).WithName("Drop")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/adhere", Adhere).WithName("Adhere")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/involve", Involve).WithName("Involve")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/journal", AppendJournal).WithName("AppendJournal")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/capture", Capture).WithName("Capture")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> NewSubject(
        NewSubjectBody body,
        SubjectService subjects,
        ChildCreationService children,
        TriageService triage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Type) || string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new ApiError("type and title are required."));
        }

        var canonicalType = ResolveType(body.Type);
        var attributes = BuildCreateAttributes(body);

        if (canonicalType == SubjectTypes.Value
            && (attributes["statement"] is not JsonValue statement || string.IsNullOrWhiteSpace(statement.ToString())))
        {
            return Results.BadRequest(new ApiError(
                "A Value needs statement: the title is the short handle, the statement is who you've chosen to be."));
        }

        if (string.IsNullOrWhiteSpace(body.Parent) && !string.IsNullOrWhiteSpace(body.Relation))
        {
            return Results.BadRequest(new ApiError("relation only applies with parent."));
        }

        SubjectRef created;
        if (!string.IsNullOrWhiteSpace(body.Parent))
        {
            var childLink = await children.CreateChildAsync(
                canonicalType, body.Title, body.Parent, body.Relation,
                attributes.ToJsonString(), cancellationToken);
            created = childLink.Child;
        }
        else if (canonicalType == SubjectTypes.Problem)
        {
            var resolved = await subjects.ResolveOrCreateAsync(
                canonicalType, body.Title, attributes.ToJsonString(), cancellationToken);
            created = resolved.Subject;
        }
        else
        {
            created = await subjects.CreateAsync(
                canonicalType, body.Title, attributes.ToJsonString(), cancellationToken: cancellationToken);
        }

        if (canonicalType is SubjectTypes.Problem or SubjectTypes.Idea)
        {
            await triage.FlagItemAsync(isEvent: false, created.Id, cancellationToken);
        }

        return Results.Ok(ToCreated(created));
    }

    private static async Task<IResult> Promote(
        PromoteBody body,
        PromotionService promotion,
        TriageService triage,
        IEventReader events,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Source) || string.IsNullOrWhiteSpace(body.Type)
            || string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new ApiError("source, type, and title are required."));
        }

        Guid? eventId = null;
        if (Guid.TryParse(body.Source, out var gid)
            && await events.FindAsync(gid, cancellationToken) is not null)
        {
            eventId = gid;
        }

        if (eventId is { } id)
        {
            var result = await promotion.PromoteAsync(id, body.Type, body.Title, cancellationToken);
            await triage.SetStateAsync(isEvent: true, result.OriginEventId, TriageStates.Promoted, cancellationToken);
            return Results.Ok(ToCreated(result.Subject));
        }

        var sourceRef = ResolveWriteRef(body.Source, reader);
        var subjectResult = await promotion.PromoteSubjectAsync(
            sourceRef, body.Type, body.Title, cancellationToken);
        await triage.SetStateAsync(isEvent: false, subjectResult.Source.Id, TriageStates.Promoted, cancellationToken);
        return Results.Ok(ToCreated(subjectResult.Target));
    }

    private static async Task<IResult> SetAttributes(
        SetAttributesBody body,
        AttributeService attributes,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || body.Attrs is null || body.Attrs.Count == 0)
        {
            return Results.BadRequest(new ApiError("subject and at least one attribute are required."));
        }

        var assignments = body.Attrs
            .Select(pair => new AttributeAssignment(pair.Key, pair.Value))
            .ToList();
        await attributes.SetAsync(ResolveWriteRef(body.Subject, reader), assignments, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> SetStatus(
        StatusBody body,
        StatusService status,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Status))
        {
            return Results.BadRequest(new ApiError("subject and status are required."));
        }

        await status.ChangeStatusAsync(
            ResolveWriteRef(body.Subject, reader), body.Status, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Archive(
        SubjectBody body,
        ArchiveService archive,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        await archive.ArchiveAsync(ResolveWriteRef(body.Subject, reader), cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Restore(
        SubjectBody body,
        ArchiveService archive,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        await archive.RestoreAsync(ResolveWriteRef(body.Subject, reader), cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Tag(
        TagBody body,
        TagService tags,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await tags.ApplyAsync(
            ResolveWriteRef(body.Item, reader),
            body.Add ?? [],
            body.Remove ?? [],
            cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Link(
        LinkBody body,
        RelationService relations,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.From) || string.IsNullOrWhiteSpace(body.Relation)
            || string.IsNullOrWhiteSpace(body.To))
        {
            return Results.BadRequest(new ApiError("from, relation, and to are required."));
        }

        await relations.LinkAsync(
            ResolveWriteRef(body.From, reader),
            body.Relation,
            ResolveWriteRef(body.To, reader),
            cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Relate(
        RelateBody body,
        RelateService relate,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (body.EventId == Guid.Empty || string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("eventId and subject are required."));
        }

        var relation = string.IsNullOrWhiteSpace(body.As)
            ? SubjectEventRelations.Concerns
            : body.As;
        await relate.RelateAsync(
            body.EventId, ResolveWriteRef(body.Subject, reader), relation, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Flag(
        ItemBody body,
        TriageService triage,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await triage.FlagAsync(ResolveWriteRef(body.Item, reader), cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Dismiss(
        ItemBody body,
        TriageService triage,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await triage.DismissAsync(ResolveWriteRef(body.Item, reader), cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Drop(
        ItemBody body,
        TriageService triage,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await triage.DropAsync(ResolveWriteRef(body.Item, reader), cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Adhere(
        AdhereBody body,
        AdherenceService adherence,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Habit) || string.IsNullOrWhiteSpace(body.State))
        {
            return Results.BadRequest(new ApiError("habit and state are required."));
        }

        var result = body.State.Trim().ToLowerInvariant() switch
        {
            "not_followed" => AdherenceResults.Missed,
            var other => other
        };
        var occurrence = ParseOccurrence(body.On);
        await adherence.RecordAsync(
            ResolveWriteRef(body.Habit, reader), result, occurrence, body.Note, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Involve(
        InvolveBody body,
        PeopleService people,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Person)
            || string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.BadRequest(new ApiError("subject, person, and role are required."));
        }

        var subject = ResolveWriteRef(body.Subject, reader);
        var person = ResolveWriteRef(body.Person, reader);
        if (body.Remove)
        {
            await people.RemoveAsync(subject, person, body.Role, cancellationToken);
        }
        else
        {
            await people.AddAsync(subject, person, body.Role, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> AppendJournal(
        JournalBody body,
        CaptureService capture,
        RelateService relate,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Text))
        {
            return Results.BadRequest(new ApiError("subject and text are required."));
        }

        // Same composition as the Pilot's BskCli.AppendJournal: capture a journal
        // event, then relate it to the subject. Both services already exist.
        var captured = await capture.CaptureJournalAsync(body.Text, cancellationToken);
        await relate.RelateAsync(
            captured.EventId,
            ResolveWriteRef(body.Subject, reader),
            SubjectEventRelations.Concerns,
            cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Capture(
        CaptureBody body,
        CaptureService capture,
        TriageService triage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Text))
        {
            return Results.BadRequest(new ApiError("text is required."));
        }

        var result = await capture.CaptureNoteAsync(body.Text, cancellationToken);
        await triage.FlagItemAsync(isEvent: true, result.EventId, cancellationToken);
        return Results.Ok();
    }

    private static string ResolveType(string type)
        => SubjectTypesList.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException(
               $"Unknown subject type '{type}'. Expected one of: {string.Join(", ", SubjectTypesList)}.");

    private static JsonObject BuildCreateAttributes(NewSubjectBody body)
    {
        var attributes = new JsonObject();
        if (!string.IsNullOrWhiteSpace(body.Area))
        {
            attributes["area"] = body.Area.Trim();
        }

        if (body.Attrs is null)
        {
            return attributes;
        }

        foreach (var (key, value) in body.Attrs)
        {
            // Status moves only by state_change; title is a first-class column.
            if (key is "status" or "title" || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            attributes[key.Trim()] = value.Trim();
        }

        return attributes;
    }

    /// <summary>
    /// HTTP clients name subjects by id; Application services resolve urn / short id /
    /// title. When the reference is a uuid that names a subject, substitute its urn.
    /// Event ids (promote / tag / triage of a capture) are left as the guid string.
    /// </summary>
    private static string ResolveWriteRef(string reference, SubjectReader reader)
    {
        if (!Guid.TryParse(reference.Trim(), out var id))
        {
            return reference;
        }

        var subject = reader.GetSubject(id);
        return subject?.Urn ?? reference;
    }

    private static DateOnly ParseOccurrence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date))
        {
            throw new ArgumentException($"'{text}' is not a valid date (expected YYYY-MM-DD).");
        }

        return date;
    }

    private static CreatedSubject ToCreated(SubjectRef created) => new()
    {
        Id = created.Id,
        Urn = created.Urn,
        Type = created.Type,
        Title = created.Title
    };
}

public sealed class NewSubjectBody
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Area { get; set; }
    public string? Parent { get; set; }
    public string? Relation { get; set; }
    public Dictionary<string, string>? Attrs { get; set; }
}

public sealed class PromoteBody
{
    public string Source { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class SetAttributesBody
{
    public string Subject { get; set; } = "";
    public Dictionary<string, string> Attrs { get; set; } = new();
}

public sealed class StatusBody
{
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class SubjectBody
{
    public string Subject { get; set; } = "";
}

public sealed class ItemBody
{
    public string Item { get; set; } = "";
}

public sealed class TagBody
{
    public string Item { get; set; } = "";
    public string[]? Add { get; set; }
    public string[]? Remove { get; set; }
}

public sealed class LinkBody
{
    public string From { get; set; } = "";
    public string Relation { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class RelateBody
{
    public Guid EventId { get; set; }
    public string Subject { get; set; } = "";
    public string? As { get; set; }
}

public sealed class AdhereBody
{
    public string Habit { get; set; } = "";
    public string State { get; set; } = "";
    public string? On { get; set; }
    public string? Note { get; set; }
}

public sealed class InvolveBody
{
    public string Subject { get; set; } = "";
    public string Person { get; set; } = "";
    public string Role { get; set; } = "";
    public bool Remove { get; set; }
}

public sealed class JournalBody
{
    public string Subject { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class CaptureBody
{
    public string Text { get; set; } = "";
}

public sealed record ApiError(string Error, object? Candidates = null);
