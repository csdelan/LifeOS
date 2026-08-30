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
