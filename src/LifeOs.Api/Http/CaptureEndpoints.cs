using System.Text.Json.Nodes;
using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LifeOs.Api.Http;

/// <summary>
/// Captures that carry a file: a document (CAP-4, the twin of <c>bsk attach</c>) and a voice note
/// (CAP-2, the twin of <c>bsk voice</c>). Each is <c>multipart/form-data</c> and lands in the Inbox
/// the way a typed capture does (D4): a Note is the capture event itself, flagged for triage; an
/// Idea or a Problem is a subject made at once and flagged, with the capture event concerning it so
/// the file shows in its Files section. Uploads use the bearer token, not cookies, so they need no
/// antiforgery token. The bytes come back from <c>GET /api/artifacts/{id}/content</c>.
/// </summary>
public static class CaptureEndpoints
{
    // Room in an upload for the multipart framing and the text fields around the file.
    private const int RequestOverhead = 256 * 1024;

    public static void MapCaptureEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/capture")
            .WithTags("Writes")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapPost("/document", CaptureDocument).WithName("CaptureDocument")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(ArtifactLimits.DefaultMaxBytes + RequestOverhead))
            .Produces<BinaryCaptureRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
        api.MapPost("/voice", CaptureVoice).WithName("CaptureVoice")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(ArtifactLimits.DefaultMaxBytes + RequestOverhead))
            .Produces<BinaryCaptureRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// A document: the <c>file</c> field, with an optional <c>description</c> of why it matters, a
    /// <c>type</c> (Note, the default; Idea or Problem) and, for an Idea or Problem, a <c>title</c>
    /// (the description's first line, else the filename, when left out). The same file captured
    /// again is the same capture, not a second one.
    /// </summary>
    private static async Task<IResult> CaptureDocument(
        IFormFile? file,
        [FromForm] string? description,
        [FromForm] string? type,
        [FromForm] string? title,
        AttachmentService attachments,
        InboxFilingService filing,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Results.BadRequest(new ApiError("Send the document as multipart/form-data, in a field named file."));
        }

        if (!InboxFilingService.TryKind(type, out var kind))
        {
            return Results.BadRequest(new ApiError(BadKind(type)));
        }

        var bytes = await ReadAsync(file, cancellationToken);
        var text = Clean(description);
        var filename = string.IsNullOrWhiteSpace(file.FileName) ? null : Path.GetFileName(file.FileName);
        var result = await attachments.CaptureAttachmentAsync(
            bytes, filename, file.ContentType, text, cancellationToken);

        var subject = await FileAsync(
            kind, result.EventId, result.Deduplicated,
            Clean(title) ?? InboxFilingService.FirstLine(text) ?? filename ?? "Document",
            text ?? filename ?? "Document", filing, cancellationToken);

        return Results.Ok(new BinaryCaptureRow
        {
            EventId = result.EventId,
            ArtifactId = result.ArtifactId,
            Kind = result.Kind,
            Filename = filename,
            ContentType = result.ContentType,
            ByteSize = result.ByteSize,
            Sha256 = result.Sha256,
            Deduplicated = result.Deduplicated,
            Subject = subject
        });
    }

    /// <summary>
    /// A voice note: the <c>transcript</c>, the <c>audio</c> recording and <c>keepAudio</c>, plus
    /// <c>type</c> and <c>title</c> as for a document. CAP-2's default keeps only the transcript, so
    /// the audio is stored only when <c>keepAudio</c> is true or there's no transcript to keep:
    /// then the audio is required, and a note with no transcript is marked as needing one.
    /// <c>concerning</c>, an item's id or URN, makes the capture concern it: the Reviews page's
    /// Dictate reflection sends its review, so the agent that files it knows which review it
    /// answers (reviews plan §4.1).
    /// </summary>
    private static async Task<IResult> CaptureVoice(
        IFormFile? audio,
        [FromForm] string? transcript,
        [FromForm] bool? keepAudio,
        [FromForm] string? type,
        [FromForm] string? title,
        [FromForm] string? concerning,
        AttachmentService attachments,
        CaptureService capture,
        SubjectReader reader,
        InboxFilingService filing,
        RelateService relate,
        CancellationToken cancellationToken)
    {
        if (!InboxFilingService.TryKind(type, out var kind))
        {
            return Results.BadRequest(new ApiError(BadKind(type)));
        }

        // Found before anything is written, so a bad id leaves no capture behind.
        string? about = null;
        if (Clean(concerning) is { } reference)
        {
            var detail = await reader.FindByIdOrUrnAsync(reference, cancellationToken);
            if (detail is null)
            {
                return Results.BadRequest(new ApiError($"concerning names no item: '{reference}'."));
            }

            about = detail.Urn;
        }

        var result = await CaptureVoiceAsync(audio, transcript, keepAudio, kind, title, attachments, capture, filing, cancellationToken);
        if (about is not null && result.Row is { } row)
        {
            await relate.RelateAsync(row.EventId, about, SubjectEventRelations.Concerns, cancellationToken);
        }

        return result.Response;
    }

    private static async Task<(IResult Response, BinaryCaptureRow? Row)> CaptureVoiceAsync(
        IFormFile? audio, string? transcript, bool? keepAudio, string kind, string? title,
        AttachmentService attachments, CaptureService capture, InboxFilingService filing,
        CancellationToken cancellationToken)
    {
        var text = Clean(transcript);
        var needsTranscription = text is null;
        var keep = keepAudio == true || needsTranscription;
        var heading = Clean(title) ?? InboxFilingService.FirstLine(text) ?? "Needs transcription";
        var body = text ?? "Needs transcription.";

        if (!keep)
        {
            var spoken = await capture.CaptureVoiceTranscriptAsync(text!, cancellationToken);
            var made = await FileAsync(kind, spoken.EventId, deduplicated: false, heading, body, filing, cancellationToken);
            var said = new BinaryCaptureRow { EventId = spoken.EventId, Kind = EventKinds.Voice, Subject = made };
            return (Results.Ok(said), said);
        }

        if (audio is null)
        {
            return (Results.BadRequest(new ApiError(needsTranscription
                ? "A voice note with no transcript needs its recording, in a field named audio."
                : "Keep audio needs the recording, in a field named audio.")), null);
        }

        var bytes = await ReadAsync(audio, cancellationToken);
        var filename = string.IsNullOrWhiteSpace(audio.FileName) ? null : Path.GetFileName(audio.FileName);
        var result = await attachments.CaptureVoiceAsync(
            bytes, filename, audio.ContentType, text, cancellationToken);
        var subject = await FileAsync(kind, result.EventId, result.Deduplicated, heading, body, filing, cancellationToken);

        var row = new BinaryCaptureRow
        {
            EventId = result.EventId,
            ArtifactId = result.ArtifactId,
            Kind = result.Kind,
            Filename = filename,
            ContentType = result.ContentType,
            ByteSize = result.ByteSize,
            Sha256 = result.Sha256,
            Deduplicated = result.Deduplicated,
            Subject = subject,
            NeedsTranscription = needsTranscription
        };
        return (Results.Ok(row), row);
    }

    /// <summary>Files a capture in the Inbox (<see cref="InboxFilingService"/>), as the API reads what it made.</summary>
    private static async Task<CreatedSubject?> FileAsync(
        string kind, Guid eventId, bool deduplicated, string title, string description,
        InboxFilingService filing, CancellationToken cancellationToken)
        => await filing.FileAsync(kind, eventId, deduplicated, title, description, cancellationToken) is { } made
            ? new CreatedSubject { Id = made.Id, Urn = made.Urn, Type = made.Type, Title = made.Title }
            : null;

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string BadKind(string? type) => $"A capture is a Note, an Idea or a Problem, not '{type}'.";

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

}

/// <summary>
/// What a document or voice capture wrote: the event, the stored file (none for a voice note
/// that keeps only its transcript), and the Idea or Problem made from it.
/// </summary>
public sealed class BinaryCaptureRow
{
    public Guid EventId { get; set; }
    public Guid? ArtifactId { get; set; }
    public string Kind { get; set; } = "";
    public string? Filename { get; set; }
    public string? ContentType { get; set; }
    public long? ByteSize { get; set; }
    public string? Sha256 { get; set; }
    /// <summary>The same file was captured before: this is that capture, and nothing new was written.</summary>
    public bool Deduplicated { get; set; }
    public CreatedSubject? Subject { get; set; }
    public bool NeedsTranscription { get; set; }
}
