using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// Puts a capture in the Inbox (D4), the same way from <c>bsk capture</c>, the web's typed,
/// document and voice captures and an agent's <c>capture</c> (agent-access plan §5.2, code review
/// CR7 and CR44). A Note flags the capture event itself, unless it's a file already captured (that
/// one is already there, or was dealt with). An Idea or a Problem is made now with the event
/// concerning it; the item is what's flagged.
/// </summary>
public sealed class InboxFilingService(
    CaptureService capture, SubjectService subjects, RelateService relate, TriageService triage, IHomeCalendar calendar)
{
    /// <summary>The capture kind that stays an event (D4); Idea and Problem are subject types.</summary>
    public const string Note = "Note";

    /// <summary>The kind a capture is, as written: Note when none is given.</summary>
    public static bool TryKind(string? type, out string kind)
    {
        var wanted = string.IsNullOrWhiteSpace(type) ? Note : type.Trim();
        kind = new[] { Note, SubjectTypes.Idea, SubjectTypes.Problem }
            .FirstOrDefault(k => string.Equals(k, wanted, StringComparison.OrdinalIgnoreCase)) ?? "";
        return kind.Length > 0;
    }

    /// <summary>The kind a capture is, as written (Note when none is given), or a refusal that says what it may be.</summary>
    public static string ParseKind(string? type)
        => TryKind(type, out var kind)
            ? kind
            : throw new ArgumentException($"A capture is a Note, an Idea or a Problem, not '{type}'.");

    /// <summary>
    /// Captures <paramref name="text"/> as a note event and files it as <paramref name="type"/>
    /// (Note when none is given; an Idea or a Problem is titled by its first line). Once the note is
    /// written, filing it runs to the end even if the caller has gone: a capture that's never
    /// flagged never reaches the Inbox. <paramref name="externalId"/> and
    /// <paramref name="payloadJson"/> are for a door that says more about where the capture came
    /// from (<see cref="CaptureService.CaptureFromAsync"/>).
    /// </summary>
    public async Task<InboxCapture> CaptureAsync(
        string text, string? type = null, string? externalId = null, string payloadJson = "{}",
        CancellationToken cancellationToken = default)
    {
        var kind = ParseKind(type);
        var body = text?.Trim() ?? "";
        var captured = await capture.CaptureFromAsync(EventKinds.Note, body, externalId, payloadJson, cancellationToken);
        var item = await FileAsync(
            kind, captured.EventId, deduplicated: false, FirstLine(body) ?? "Capture", body, CancellationToken.None);
        return new InboxCapture(captured.EventId, captured.ArtifactId, kind, item);
    }

    /// <summary>The first line, cut to 80 characters with an ellipsis, as the web's deriveTitle does.</summary>
    public static string? FirstLine(string? text)
    {
        var line = text?.Split('\n')[0].Trim();
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        return line.Length <= 80 ? line : $"{line[..79].TrimEnd()}…";
    }

    /// <summary>
    /// Files the capture <paramref name="eventId"/> as <paramref name="kind"/>: the Idea or Problem
    /// it made (an existing Problem of that title is reused), or null for a Note.
    /// </summary>
    public async Task<SubjectRef?> FileAsync(
        string kind, Guid eventId, bool deduplicated, string title, string description,
        CancellationToken cancellationToken = default)
    {
        if (kind == Note)
        {
            if (!deduplicated)
            {
                await triage.FlagItemAsync(isEvent: true, eventId, cancellationToken);
            }

            return null;
        }

        var attributes = new JsonObject { ["description"] = description };
        SubjectRef created;
        if (kind == SubjectTypes.Problem)
        {
            // Today where Chris lives, as the web's todayIso() gives a typed Problem.
            var today = await calendar.TodayAsync(cancellationToken);
            attributes["date_identified"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            created = (await subjects.ResolveOrCreateAsync(kind, title, attributes.ToJsonString(), cancellationToken)).Subject;
        }
        else
        {
            created = await subjects.CreateAsync(kind, title, attributes.ToJsonString(), cancellationToken: cancellationToken);
        }

        await relate.RelateAsync(eventId, created.Urn, SubjectEventRelations.Concerns, cancellationToken);
        await triage.FlagItemAsync(isEvent: false, created.Id, cancellationToken);
        return created;
    }
}

/// <summary>
/// What <see cref="InboxFilingService.CaptureAsync"/> wrote: the note event and its text, the kind
/// it was filed as (Note, Idea or Problem), and the Idea or Problem made from it.
/// </summary>
public sealed record InboxCapture(Guid EventId, Guid ArtifactId, string Kind, SubjectRef? Item);
