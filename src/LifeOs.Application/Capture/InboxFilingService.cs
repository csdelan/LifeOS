using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// Puts a capture in the Inbox (D4), the same way from <c>bsk capture</c>, <c>bsk attach</c>,
/// <c>bsk voice</c>, the web's typed, document and voice captures and an agent's <c>capture</c>
/// (agent-access plan §5.2, code review CR7, CR44, CR63 and CR114). A Note flags the capture event
/// itself; a file captured again is the same capture, flagged only if it never reached the Inbox
/// (one that was dealt with stays dealt with). An Idea or a Problem is made now with the event
/// concerning it, and the item is what's flagged, in one write; a capture sent again finds it and
/// makes no second one. Once the capture is stored, filing it runs to the end even if the caller
/// has gone: a capture that's never flagged never reaches the Inbox.
/// </summary>
public sealed class InboxFilingService(
    CaptureService capture, AttachmentService attachments, SubjectService subjects, RelateService relate,
    TriageService triage, IHomeCalendar calendar, IEventReader events, EventSource eventSource)
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
    /// (Note when none is given; an Idea or a Problem is titled by its first line), concerning
    /// <paramref name="concerning"/> (an item's reference) when given, which is found before
    /// anything is written. Once the note is written, filing it runs to the end even if the caller
    /// has gone: a capture that's never flagged never reaches the Inbox. <paramref name="externalId"/>
    /// and <paramref name="payloadJson"/> are for a door that says more about where the capture
    /// came from (<see cref="CaptureService.CaptureFromAsync"/>): a retried request finishes what
    /// its first call stopped before and answers as that call would have (code review CR68).
    /// </summary>
    public async Task<InboxCapture> CaptureAsync(
        string text, string? type = null, string? externalId = null, string payloadJson = "{}",
        string? concerning = null, CancellationToken cancellationToken = default)
    {
        var kind = ParseKind(type);
        var body = text?.Trim() ?? "";
        var title = FirstLine(body) ?? "Capture";
        var about = await AboutAsync(concerning, cancellationToken);
        var captured = await capture.CaptureFromAsync(EventKinds.Note, body, externalId, payloadJson, cancellationToken);

        // A retried request is its first call's note: what that call filed is among what it concerns.
        var earlier = captured.AlreadyWritten
            ? (await events.FindWriteAsync(eventSource.Id, externalId!, CancellationToken.None)
                ?? throw new InvalidOperationException($"The capture for request_id '{externalId}' is gone.")).Concerns
            : null;
        var (item, concerns) = await FinishAsync(kind, captured.EventId, earlier, title, body, about);
        return new InboxCapture(captured.EventId, captured.ArtifactId, kind, item, concerns, captured.AlreadyWritten);
    }

    /// <summary>
    /// Stores a document (CAP-4) and files it as <paramref name="type"/>, titled by
    /// <paramref name="title"/>, else the description's first line, else the filename,
    /// concerning <paramref name="concerning"/> (an item's reference), which is found before the
    /// file is stored. Only the store heeds <paramref name="cancellationToken"/>: once the file is
    /// kept, filing it and relating it run to the end. The same file sent again is the same
    /// capture, and an Idea or Problem it was filed as is that capture's, not a second one.
    /// </summary>
    public async Task<FiledCapture> CaptureDocumentAsync(
        CaptureFile file, string? description = null, string? type = null, string? title = null,
        string? concerning = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var kind = ParseKind(type);
        var text = Clean(description);
        var about = await AboutAsync(concerning, cancellationToken);
        var stored = await attachments.CaptureAttachmentAsync(
            file.Bytes, file.Filename, file.ContentType, text, cancellationToken);
        var (item, _) = await FinishAsync(
            kind, stored.EventId, await EarlierAsync(stored),
            Clean(title) ?? FirstLine(text) ?? file.Filename ?? "Document", text ?? file.Filename ?? "Document",
            about);
        return new FiledCapture(stored.EventId, stored.Kind, kind, stored, item, NeedsTranscription: false);
    }

    /// <summary>
    /// A voice note (CAP-2): by default only its <paramref name="transcript"/> is kept; the
    /// recording is stored when <paramref name="keepAudio"/> is true or there's no transcript to
    /// keep, and then it's required, and a note with no transcript is marked as needing one.
    /// Filed as <see cref="CaptureDocumentAsync"/> files, and as surely; an item named by
    /// <paramref name="concerning"/> that isn't there is refused before anything is stored.
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
        if ((keepAudio || needsTranscription) && audio is null)
        {
            throw new ArgumentException(needsTranscription
                ? "A voice note with no transcript needs its recording, in a field named audio."
                : "Keep audio needs the recording, in a field named audio.");
        }

        var about = await AboutAsync(concerning, cancellationToken);
        if (!keepAudio && !needsTranscription)
        {
            var spoken = await capture.CaptureVoiceTranscriptAsync(text!, cancellationToken);
            var (made, _) = await FinishAsync(kind, spoken.EventId, earlier: null, heading, body, about);
            return new FiledCapture(spoken.EventId, EventKinds.Voice, kind, Stored: null, made, NeedsTranscription: false);
        }

        var stored = await attachments.CaptureVoiceAsync(
            audio!.Bytes, audio.Filename, audio.ContentType, text, cancellationToken);
        var (item, _) = await FinishAsync(kind, stored.EventId, await EarlierAsync(stored), heading, body, about);
        return new FiledCapture(stored.EventId, stored.Kind, kind, stored, item, needsTranscription);
    }

    // The item a capture is about, found before anything is stored, so a bad reference leaves no
    // capture behind, from every door.
    private async Task<SubjectRef?> AboutAsync(string? concerning, CancellationToken cancellationToken)
        => Clean(concerning) is { } reference ? await subjects.ResolveAsync(reference, cancellationToken) : null;

    // A file stored before is the same capture: what it was filed as is among what it concerns.
    private async Task<IReadOnlyList<SubjectRef>?> EarlierAsync(AttachmentResult stored)
        => stored.Deduplicated ? await events.ConcernsAsync(stored.EventId, CancellationToken.None) : null;

    /// <summary>
    /// Files the capture <paramref name="eventId"/> as <paramref name="kind"/> and relates it to
    /// <paramref name="about"/>, run to the end whatever the caller does (CR63): the phone that
    /// uploaded twenty minutes of dictation may have dropped by now. A capture stored before (a
    /// retried request, a file sent again) comes with <paramref name="earlier"/>, the items it
    /// concerns: its Note is flagged only if it never reached the Inbox, so one Chris dealt with
    /// stays dealt with, and the Idea or Problem it was filed as is found there and not made again
    /// (CR68, CR114). Answers the Idea or Problem, and the other items the capture concerns.
    /// </summary>
    private async Task<(SubjectRef? Item, IReadOnlyList<SubjectRef> Concerns)> FinishAsync(
        string kind, Guid eventId, IReadOnlyList<SubjectRef>? earlier, string title, string description,
        SubjectRef? about)
    {
        var none = CancellationToken.None;
        var concerns = earlier?.ToList() ?? [];
        SubjectRef? item = null;
        if (kind == Note)
        {
            if (earlier is null)
            {
                await triage.FlagItemAsync(isEvent: true, eventId, none);
            }
            else
            {
                await triage.FlagIfNeverTriagedAsync(isEvent: true, eventId, none);
            }
        }
        else if (concerns.FirstOrDefault(c => c.Type == kind && c.Id != about?.Id) is { } filed)
        {
            item = filed;
            await triage.FlagIfNeverTriagedAsync(isEvent: false, filed.Id, none);
        }
        else
        {
            item = await FileAsync(kind, eventId, title, description, none);
        }

        if (about is not null && concerns.All(c => c.Id != about.Id))
        {
            await relate.RelateAsync(eventId, about.Urn, SubjectEventRelations.Concerns, none);
            concerns.Add(about);
        }

        return (item, concerns.Where(c => c.Id != item?.Id).ToList());
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
    /// Makes the Idea or Problem the capture <paramref name="eventId"/> is filed as, with the
    /// capture's edge to it and its flag, in one write (code review CR114). A Problem of that title
    /// already there is filed instead (D4).
    /// </summary>
    private async Task<SubjectRef> FileAsync(
        string kind, Guid eventId, string title, string description, CancellationToken cancellationToken)
    {
        var attributes = new JsonObject { ["description"] = description };
        var problem = kind == SubjectTypes.Problem;
        if (problem)
        {
            // Today where Chris lives, as the web's todayIso() gives a typed Problem.
            var today = await calendar.TodayAsync(cancellationToken);
            attributes["date_identified"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var filed = await subjects.CreateFromCaptureAsync(
            kind, title, attributes.ToJsonString(), eventId, reuseByTitle: problem, cancellationToken);
        return filed.Subject;
    }
}

/// <summary>
/// What <see cref="InboxFilingService.CaptureAsync"/> wrote: the note event and its text, the kind
/// it was filed as (Note, Idea or Problem), the Idea or Problem made from it, and the other items
/// it concerns. A retried request is <paramref name="AlreadyWritten"/>, and these are its first
/// capture's.
/// </summary>
public sealed record InboxCapture(
    Guid EventId, Guid ArtifactId, string Kind, SubjectRef? Item, IReadOnlyList<SubjectRef> Concerns, bool AlreadyWritten);

/// <summary>A file a capture carries: its bytes, its name and its type (guessed from the name when null).</summary>
public sealed record CaptureFile(byte[] Bytes, string? Filename = null, string? ContentType = null);

/// <summary>
/// What a document or voice capture wrote: the event and its kind (<c>note</c> or <c>voice</c>),
/// the kind it was filed as (Note, Idea or Problem), the stored file (none for a voice note that
/// keeps only its transcript), the Idea or Problem made from it, and whether it needs a transcript.
/// </summary>
public sealed record FiledCapture(
    Guid EventId, string EventKind, string Kind, AttachmentResult? Stored, SubjectRef? Item, bool NeedsTranscription);
