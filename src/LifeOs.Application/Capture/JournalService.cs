using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// Adds an entry to an item's journal (JOURNAL-1), the one way <c>POST /api/journal</c> and an
/// agent's <c>add_journal_entry</c> do it (code review CR44). The item is found first, so a
/// reference to nothing writes nothing, and the journal event and its <c>concerns</c> edge are
/// written in one transaction, so an entry never lands without its item.
/// </summary>
public sealed class JournalService(
    SubjectService subjects, IArtifactStore artifacts, IActivityWriter writer, IClock clock, EventSource eventSource)
{
    /// <param name="subject">The item, as a reference <see cref="SubjectService.ResolveAsync"/> reads.</param>
    /// <param name="text">The entry.</param>
    /// <param name="externalId">Makes a retried request the same event (invariant 5), for a door that sends one.</param>
    /// <param name="payloadJson">What the door records with the entry, such as the agent's Person.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    public async Task<JournalEntry> AppendAsync(
        string subject, string text, string? externalId = null, string payloadJson = "{}",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("A journal entry needs the item it's about.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A journal entry needs its text.");
        }

        var item = await subjects.ResolveAsync(subject, cancellationToken);
        var artifactId = await artifacts.AddAsync(text, cancellationToken);
        var now = clock.UtcNow;
        var eventId = await writer.WriteAsync(
            new NewEvent(
                EventKinds.Journal, Provenances.Declared, now, now, eventSource.Id,
                ExternalId: externalId, PayloadJson: payloadJson, ArtifactId: artifactId),
            [new SubjectEventEdge(item.Id, SubjectEventRelations.Concerns, Provenances.Declared)],
            cancellationToken);
        return new JournalEntry(eventId, artifactId, item);
    }
}

/// <summary>What <see cref="JournalService.AppendAsync"/> wrote: the journal event, its text and the item it concerns.</summary>
public sealed record JournalEntry(Guid EventId, Guid ArtifactId, SubjectRef Subject);
