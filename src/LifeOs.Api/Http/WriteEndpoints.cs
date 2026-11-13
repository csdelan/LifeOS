using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Api.Auth;
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
    public static void MapWriteEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Writes")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapPost("/subjects", NewSubject).WithName("NewSubject")
            .Produces<CreatedSubject>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/promote", Promote).WithName("Promote")
            .Produces<CreatedSubject>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/attributes", SetAttributes).WithName("SetAttributes")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/rename", Rename).WithName("Rename")
            .Produces<RenamedSubject>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict);
        api.MapPost("/status", SetStatus).WithName("SetStatus")
            .Produces<EarnResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/recur", Recur).WithName("Recur")
            .Produces<RecurResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/archive", Archive).WithName("Archive")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/restore", Restore).WithName("Restore")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/tag", Tag).WithName("Tag")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/rename-tag", RenameTag).WithName("RenameTag")
            .Produces<RenamedTag>().Produces<ApiError>(StatusCodes.Status400BadRequest);
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
            .Produces<EarnResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/timer", Timer).WithName("Timer")
            .Produces<TimerActionResult>().Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict);
        api.MapPost("/involve", Involve).WithName("Involve")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/journal", AppendJournal).WithName("AppendJournal")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/capture", Capture).WithName("Capture")
            .Produces(StatusCodes.Status200OK).Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> NewSubject(
        NewSubjectBody body,
        SubjectCreationService creation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Type) || string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new ApiError("type and title are required."));
        }

        var canonicalType = SubjectTypes.ParseCreatable(body.Type);
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

        var parent = string.IsNullOrWhiteSpace(body.Parent) ? null : body.Parent;
        var made = await creation.CreateAsync(
            canonicalType, body.Title, attributes.ToJsonString(), parent, body.Relation, cancellationToken);
        return Results.Ok(ToCreated(made.Subject));
    }

    private static async Task<IResult> Promote(
        PromoteBody body,
        PromotionService promotion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Source) || string.IsNullOrWhiteSpace(body.Type)
            || string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new ApiError("source, type, and title are required."));
        }

        // A capture event's id stays as it is; an item's id becomes its URN.
        var promoted = await promotion.PromoteFromInboxAsync(
            body.Source, body.Type, body.Title, cancellationToken);
        return Results.Ok(ToCreated(promoted.Subject));
    }

    private static async Task<IResult> SetAttributes(
        SetAttributesBody body,
        AttributeService attributes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || body.Attrs is null || body.Attrs.Count == 0)
        {
            return Results.BadRequest(new ApiError("subject and at least one attribute are required."));
        }

        var assignments = body.Attrs
            .Select(pair => new AttributeAssignment(pair.Key, pair.Value))
            .ToList();
        await attributes.SetAsync(body.Subject, assignments, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Rename(
        RenameBody body,
        RenameService rename,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Title))
        {
            return Results.BadRequest(new ApiError("subject and title are required."));
        }

        var result = await rename.RenameAsync(body.Subject, body.Title, cancellationToken);
        return Results.Ok(new RenamedSubject
        {
            Id = result.Subject.Id,
            Title = result.Subject.Title,
            PreviousTitle = result.PreviousTitle,
            Changed = result.Changed,
            OccurrencesRenamed = result.OccurrencesRenamed
        });
    }

    private static async Task<IResult> SetStatus(
        StatusBody body,
        StatusService status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Status))
        {
            return Results.BadRequest(new ApiError("subject and status are required."));
        }

        // The reconcile's earns and reversals ride back for REWARD-4's feedback.
        var result = await status.ChangeStatusAsync(
            body.Subject, body.Status, cancellationToken);
        return Results.Ok(RewardRows.Earned(result.Awards, result.Progress));
    }

    /// <summary>
    /// Sets or clears a subject's recurrence (D8), the HTTP twin of <c>bsk recur</c>. The
    /// pattern is built and checked in <see cref="Recurrence"/> and stored as the object
    /// the expander reads; no recurrence clears it. On a Task it makes a repeating Task (D16)
    /// repeating from <c>start</c> (else its due date or today), and answers the repetitions
    /// it made.
    /// </summary>
    private static async Task<IResult> Recur(
        RecurBody body,
        RecurrenceService recurrence,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        var subjectRef = body.Subject;
        if (body.Recurrence is not { } pattern)
        {
            var cleared = await recurrence.ClearAsync(subjectRef, cancellationToken);
            return Results.Ok(new RecurResult { Id = cleared.Id, Urn = cleared.Urn });
        }

        var json = Recurrence.Build(
            pattern.Freq, pattern.On, pattern.Every, pattern.Unit, pattern.Day, pattern.Cue,
            pattern.Period, pattern.From);
        var updated = await recurrence.SetAsync(subjectRef, json, ParseOccurrence(body.Start), cancellationToken);
        return Results.Ok(new RecurResult
        {
            Id = updated.Subject.Id,
            Urn = updated.Subject.Urn,
            Recurrence = updated.Recurrence,
            Made = updated.Made.Select(ToCreated).ToList()
        });
    }

    private static async Task<IResult> Archive(
        SubjectBody body,
        ArchiveService archive,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        await archive.ArchiveAsync(body.Subject, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Restore(
        SubjectBody body,
        ArchiveService archive,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject))
        {
            return Results.BadRequest(new ApiError("subject is required."));
        }

        await archive.RestoreAsync(body.Subject, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Tag(
        TagBody body,
        TagService tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await tags.ApplyAsync(
            body.Item,
            body.Add ?? [],
            body.Remove ?? [],
            cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> RenameTag(
        RenameTagBody body,
        TagService tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.From) || string.IsNullOrWhiteSpace(body.To))
        {
            return Results.BadRequest(new ApiError("from and to are required."));
        }

        var result = await tags.RenameAsync(body.From, body.To, cancellationToken);
        return Results.Ok(new RenamedTag
        {
            From = result.From,
            To = result.To,
            Renamed = result.Renamed,
            Merged = result.Merged
        });
    }

    private static async Task<IResult> Link(
        LinkBody body,
        RelationService relations,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.From) || string.IsNullOrWhiteSpace(body.Relation)
            || string.IsNullOrWhiteSpace(body.To))
        {
            return Results.BadRequest(new ApiError("from, relation, and to are required."));
        }

        var from = body.From;
        var to = body.To;
        if (body.Remove)
        {
            // What an edit form takes away: a Goal's old identity statement, an unticked parent.
            await relations.UnlinkAsync(from, body.Relation, to, cancellationToken);
        }
        else
        {
            await relations.LinkAsync(from, body.Relation, to, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> Relate(
        RelateBody body,
        RelateService relate,
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
            body.EventId, body.Subject, relation, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Flag(
        ItemBody body,
        TriageService triage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await triage.FlagAsync(body.Item, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Dismiss(
        ItemBody body,
        TriageService triage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await triage.DismissAsync(body.Item, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Drop(
        ItemBody body,
        TriageService triage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Item))
        {
            return Results.BadRequest(new ApiError("item is required."));
        }

        await triage.DropAsync(body.Item, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> Adhere(
        AdhereBody body,
        AdherenceService adherence,
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
        var recorded = await adherence.RecordAsync(
            body.Habit, result, occurrence, body.Note, cancellationToken);
        return Results.Ok(RewardRows.Earned(recorded.Awards, recorded.Progress));
    }

    private static async Task<IResult> Timer(
        TimerBody body,
        TimerService timers,
        SubjectReader reader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Task) || string.IsNullOrWhiteSpace(body.Action))
        {
            return Results.BadRequest(new ApiError("task and action are required."));
        }

        var action = body.Action.Trim().ToLowerInvariant();
        var task = body.Task;
        var result = action == "complete"
            ? await timers.CompleteAsync(task, cancellationToken)
            : await timers.ApplyAsync(task, action, cancellationToken);

        // Read back through the reader view so the response has the same shape (and
        // the same session fields) as GET /api/timers.
        var timer = (await reader.GetTimerAsync(result.Task.Id, cancellationToken: cancellationToken)) ?? new TaskTimer
        {
            SubjectId = result.Task.Id,
            SubjectUrn = result.Task.Urn,
            SubjectTitle = result.Task.Title,
            State = TimerStates.Stopped
        };
        return Results.Ok(new TimerActionResult
        {
            Timer = timer,
            Paused = result.Paused.Select(ToCreated).ToList(),
            Completed = result.Completed,
            Awards = result.Awards.Select(RewardRows.From).ToList(),
            Progress = result.Progress.Select(RewardRows.From).ToList()
        });
    }

    private static async Task<IResult> Involve(
        InvolveBody body,
        PeopleService people,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Person)
            || string.IsNullOrWhiteSpace(body.Role))
        {
            return Results.BadRequest(new ApiError("subject, person, and role are required."));
        }

        var subject = body.Subject;
        var person = body.Person;
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
            body.Subject,
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

    /// <summary>The occurrence date asked for, or null for today in the home zone (the service's default).</summary>
    private static DateOnly? ParseOccurrence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
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

public sealed class RenameBody
{
    public string Subject { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class RenameTagBody
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class StatusBody
{
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>
/// A recurrence to set (D8); null clears it. <c>start</c> (<c>yyyy-MM-dd</c>) is the date a
/// repeating Task repeats from (D16); other types take no start here.
/// </summary>
public sealed class RecurBody
{
    public string Subject { get; set; } = "";
    public RecurrenceBody? Recurrence { get; set; }
    public string? Start { get; set; }
}

/// <summary>
/// A recurrence's parts, as <c>bsk recur</c> takes them: <c>freq</c> is daily | weekly |
/// interval | monthly | trigger | each; <c>on</c> holds a weekly one's weekdays (sun..sat) or
/// <c>last</c> for a monthly one on the month's last day; <c>every</c> + <c>unit</c>
/// (days | weeks | months) an interval, with <c>from</c> = completion to count from the last
/// one done (Tasks); <c>day</c> a monthly day (1-31); <c>cue</c> a trigger; <c>period</c>
/// (week) for once each period on any day (Tasks).
/// </summary>
public sealed class RecurrenceBody
{
    public string Freq { get; set; } = "";
    public string[]? On { get; set; }
    public int? Every { get; set; }
    public string? Unit { get; set; }
    public int? Day { get; set; }
    public string? Cue { get; set; }
    public string? Period { get; set; }
    public string? From { get; set; }
}

/// <summary>
/// The subject whose recurrence was set, the stored recurrence (JSON, null when cleared), and
/// for a repeating Task the repetitions it made.
/// </summary>
public sealed class RecurResult
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string? Recurrence { get; set; }
    public IReadOnlyList<CreatedSubject> Made { get; set; } = [];
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
    /// <summary>Removes the edge instead, as <c>bsk unlink</c> does.</summary>
    public bool Remove { get; set; }
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

    /// <summary>The occurrence date (YYYY-MM-DD); omitted, it's today in the home zone (Rewards:HomeTimeZone).</summary>
    public string? On { get; set; }
    public string? Note { get; set; }
}

public sealed class TimerBody
{
    public string Task { get; set; } = "";
    /// <summary>start | pause | resume | stop | complete.</summary>
    public string Action { get; set; } = "";
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
