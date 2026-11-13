using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// The lowest-ceremony write path: turn a body of text into one immutable event
/// whose content is stored as an artifact, the two in one transaction. Transport-neutral — it
/// knows nothing of the CLI. A capture may concern nothing, forever; nothing is required of it
/// beyond its text.
/// </summary>
public sealed class CaptureService(
    IEventStore events, IActivityWriter writer, IClock clock, EventSource eventSource)
{
    /// <summary>Captures a free-text note as one <c>note</c> event.</summary>
    public Task<CaptureResult> CaptureNoteAsync(string text, CancellationToken cancellationToken = default)
        => CaptureBodyAsync(EventKinds.Note, text, cancellationToken);

    /// <summary>Captures a longer body as one <c>journal</c> event.</summary>
    public Task<CaptureResult> CaptureJournalAsync(string text, CancellationToken cancellationToken = default)
        => CaptureBodyAsync(EventKinds.Journal, text, cancellationToken);

    /// <summary>
    /// Captures a voice note whose audio isn't kept (CAP-2's transcript-only default) as one
    /// <c>voice</c> event holding just the transcript. A voice note that keeps its audio goes
    /// through <see cref="AttachmentService.CaptureVoiceAsync"/> instead.
    /// </summary>
    public Task<CaptureResult> CaptureVoiceTranscriptAsync(string transcript, CancellationToken cancellationToken = default)
        => CaptureBodyAsync(EventKinds.Voice, transcript, cancellationToken);

    /// <summary>
    /// Captures a note or a journal entry for a door that says more about where it came from
    /// (an agent, agent-access plan §5.3): <paramref name="externalId"/> makes a retried request
    /// the same event, since the store keeps one event per source and external id (invariant 5),
    /// and <paramref name="payloadJson"/> is what the door records with it. A retry writes
    /// nothing and answers the first write, <see cref="CaptureResult.AlreadyWritten"/>; one whose
    /// id named another kind of write is refused (code review CR68).
    /// </summary>
    public Task<CaptureResult> CaptureFromAsync(
        string kind, string text, string? externalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (kind != EventKinds.Note && kind != EventKinds.Journal)
        {
            throw new ArgumentException($"A capture from elsewhere is a note or a journal entry, not '{kind}'.", nameof(kind));
        }

        return CaptureBodyAsync(kind, text, cancellationToken, externalId, payloadJson);
    }

    /// <summary>
    /// Records an idea session — a problem statement and all its ideas — as ONE
    /// immutable <c>idea_session</c> event that references the Problem subject.
    /// The ideas are stored verbatim; no rating or judgement happens here, and no
    /// subjects are created for the individual ideas.
    /// </summary>
    public async Task<IdeaSessionResult> CaptureIdeaSessionAsync(
        Guid problemId, string problemStatement, IReadOnlyList<string> ideas,
        CancellationToken cancellationToken = default)
    {
        if (ideas.Count == 0)
        {
            throw new ArgumentException("An idea session needs at least one idea.", nameof(ideas));
        }

        var payload = JsonSerializer.Serialize(new
        {
            subject_id = problemId,
            problem = problemStatement,
            ideas
        });

        var now = clock.UtcNow;
        var newEvent = new NewEvent(
            Kind: EventKinds.IdeaSession,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: eventSource.Id,
            PayloadJson: payload);

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        return new IdeaSessionResult(eventId, problemId, ideas.Count);
    }

    private async Task<CaptureResult> CaptureBodyAsync(
        string kind, string text, CancellationToken cancellationToken,
        string? externalId = null, string payloadJson = "{}")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Capture text must not be empty.", nameof(text));
        }

        // occurred_at and recorded_at are the same for a direct capture: it
        // happened when we recorded it.
        var now = clock.UtcNow;
        var newEvent = new NewEvent(
            Kind: kind,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: eventSource.Id,
            ExternalId: externalId,
            PayloadJson: payloadJson);

        // The text and its event in one transaction: a retry that races its first call writes
        // neither, rather than text nothing names (code review CR68).
        var written = await writer.WriteOnceAsync(newEvent, text, [], cancellationToken);
        RequestReusedException.ThrowIfOtherKind(written.Kind, kind, externalId);
        return new CaptureResult(written.EventId, written.ArtifactId.GetValueOrDefault(), written.AlreadyWritten);
    }
}

/// <summary>
/// The outcome of a capture: the event written and the artifact it references. A retried request
/// is <paramref name="AlreadyWritten"/>, and these are its first write's.
/// </summary>
public sealed record CaptureResult(Guid EventId, Guid ArtifactId, bool AlreadyWritten = false);

/// <summary>The outcome of an idea session: the one event written and its idea count.</summary>
public sealed record IdeaSessionResult(Guid EventId, Guid ProblemId, int IdeaCount);
