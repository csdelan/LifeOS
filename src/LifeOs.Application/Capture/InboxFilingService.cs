using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// Puts a capture in the Inbox (D4), the same way from <c>bsk capture</c>, <c>bsk attach</c>,
/// <c>bsk voice</c>, the web's typed, document and voice captures and an agent's <c>capture</c>
/// (agent-access plan §5.2, code review CR7, CR44 and CR63). A Note flags the capture event itself;
/// a file captured again is the same capture, flagged only if it never reached the Inbox (one
/// that was dealt with stays dealt with). An Idea or a Problem is made now with the event
/// concerning it; the item is what's flagged. Once the capture is stored, filing it runs to the
/// end even if the caller has gone: a capture that's never flagged never reaches the Inbox.
/// </summary>
public sealed class InboxFilingService(
    CaptureService capture, AttachmentService attachments, SubjectService subjects, RelateService relate,
    TriageService triage, IHomeCalendar calendar)
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

    /// <summary>
    /// Stores a document (CAP-4) and files it as <paramref name="type"/>, titled by
    /// <paramref name="title"/>, else the description's first line, else the filename. Only the
    /// store heeds <paramref name="cancellationToken"/>: once the file is kept, filing it and
    /// relating it to <paramref name="concerning"/> (an item's URN) run to the end.
    /// </summary>
    public async Task<FiledCapture> CaptureDocumentAsync(
        CaptureFile file, string? description = null, string? type = null, string? title = null,
        string? concerning = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var kind = ParseKind(type);
        var text = Clean(description);
        var stored = await attachments.CaptureAttachmentAsync(
            file.Bytes, file.Filename, file.ContentType, text, cancellationToken);
        var item = await FinishAsync(
            kind, stored.EventId, stored.Deduplicated,
            Clean(title) ?? FirstLine(text) ?? file.Filename ?? "Document", text ?? file.Filename ?? "Document",
            concerning);
        return new FiledCapture(stored.EventId, stored.Kind, kind, stored, item, NeedsTranscription: false);
    }

    /// <summary>
    /// A voice note (CAP-2): by default only its <paramref name="transcript"/> is kept; the
    /// recording is stored when <paramref name="keepAudio"/> is true or there's no transcript to
    /// keep, and then it's required, and a note with no transcript is marked as needing one.
    /// Filed as <see cref="CaptureDocumentAsync"/> files, and as surely.
    /// </summary>
    public async Task<FiledCapture> CaptureVoiceAsync(
        CaptureFile? audio, string? transcript, bool keepAudio = false, string? type = null,
        string? title = null, string? concerning = null, CancellationToken cancellationToken = default)
    {
        var kind = ParseKind(type);
        var text = Clean(transcript);
        var needsTranscription = text is null;
        var heading = Clean(title) ?? FirstLine(text) ?? "Needs transcription";
        var body = text ?? "Needs transcription.";

        if (!keepAudio && !needsTranscription)
        {
            var spoken = await capture.CaptureVoiceTranscriptAsync(text!, cancellationToken);
            var made = await FinishAsync(kind, spoken.EventId, deduplicated: false, heading, body, concerning);
            return new FiledCapture(spoken.EventId, EventKinds.Voice, kind, Stored: null, made, NeedsTranscription: false);
        }

        if (audio is null)
        {
            throw new ArgumentException(needsTranscription
                ? "A voice note with no transcript needs its recording, in a field named audio."
                : "Keep audio needs the recording, in a field named audio.");
        }

        var stored = await attachments.CaptureVoiceAsync(
            audio.Bytes, audio.Filename, audio.ContentType, text, cancellationToken);
        var item = await FinishAsync(kind, stored.EventId, stored.Deduplicated, heading, body, concerning);
        return new FiledCapture(stored.EventId, stored.Kind, kind, stored, item, needsTranscription);
    }

    // Everything after the store, run to the end whatever the caller does (CR63): the phone that
    // uploaded twenty minutes of dictation may have dropped by now.
    private async Task<SubjectRef?> FinishAsync(
        string kind, Guid eventId, bool deduplicated, string title, string description, string? concerning)
    {
        var item = await FileAsync(kind, eventId, deduplicated, title, description, CancellationToken.None);
        if (concerning is not null)
        {
            await relate.RelateAsync(eventId, concerning, SubjectEventRelations.Concerns, CancellationToken.None);
        }

        return item;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
    /// it made (an existing Problem of that title is reused), or null for a Note. A
    /// <paramref name="deduplicated"/> Note, a file captured again, is flagged only if it was never
    /// triaged: the first capture may have stopped before its flag (CR63), and one Chris has dealt
    /// with stays dealt with.
    /// </summary>
    public async Task<SubjectRef?> FileAsync(
        string kind, Guid eventId, bool deduplicated, string title, string description,
        CancellationToken cancellationToken = default)
    {
        if (kind == Note)
        {
            if (deduplicated)
            {
                await triage.FlagIfNeverTriagedAsync(isEvent: true, eventId, cancellationToken);
            }
            else
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

/// <summary>A file a capture carries: its bytes, its name and its type (guessed from the name when null).</summary>
public sealed record CaptureFile(byte[] Bytes, string? Filename = null, string? ContentType = null);

/// <summary>
/// What a document or voice capture wrote: the event and its kind (<c>note</c> or <c>voice</c>),
/// the kind it was filed as (Note, Idea or Problem), the stored file (none for a voice note that
/// keeps only its transcript), the Idea or Problem made from it, and whether it needs a transcript.
/// </summary>
public sealed record FiledCapture(
    Guid EventId, string EventKind, string Kind, AttachmentResult? Stored, SubjectRef? Item, bool NeedsTranscription);
