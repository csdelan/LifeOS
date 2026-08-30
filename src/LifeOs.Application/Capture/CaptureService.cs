using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// The lowest-ceremony write path: turn a body of text into one immutable event
/// whose content is stored as an artifact. Transport-neutral — it knows nothing
/// of the CLI. A capture may concern nothing, forever; nothing is required of it
/// beyond its text.
/// </summary>
public sealed class CaptureService(
    IEventStore events, IArtifactStore artifacts, IClock clock, string sourceId)
{
    /// <summary>Captures a free-text note as one <c>note</c> event.</summary>
    public Task<CaptureResult> CaptureNoteAsync(string text, CancellationToken cancellationToken = default)
        => CaptureBodyAsync(EventKinds.Note, text, cancellationToken);

    /// <summary>Captures a longer body as one <c>journal</c> event.</summary>
    public Task<CaptureResult> CaptureJournalAsync(string text, CancellationToken cancellationToken = default)
        => CaptureBodyAsync(EventKinds.Journal, text, cancellationToken);

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
            SourceId: sourceId,
            PayloadJson: payload);

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        return new IdeaSessionResult(eventId, problemId, ideas.Count);
    }

    private async Task<CaptureResult> CaptureBodyAsync(
        string kind, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Capture text must not be empty.", nameof(text));
        }

        var artifactId = await artifacts.AddAsync(text, cancellationToken);

        // occurred_at and recorded_at are the same for a direct capture: it
        // happened when we recorded it.
        var now = clock.UtcNow;
        var newEvent = new NewEvent(
            Kind: kind,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            ArtifactId: artifactId);

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        return new CaptureResult(eventId, artifactId);
    }
}

/// <summary>The outcome of a capture: the event written and the artifact it references.</summary>
public sealed record CaptureResult(Guid EventId, Guid ArtifactId);

/// <summary>The outcome of an idea session: the one event written and its idea count.</summary>
public sealed record IdeaSessionResult(Guid EventId, Guid ProblemId, int IdeaCount);
