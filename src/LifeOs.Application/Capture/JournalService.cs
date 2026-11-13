using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Capture;

/// <summary>
/// Adds an entry to an item's journal (JOURNAL-1), the one way <c>POST /api/journal</c> and an
/// agent's <c>add_journal_entry</c> do it (code review CR44). The item is found first, so a
/// reference to nothing writes nothing, and the entry's text, its journal event and its
/// <c>concerns</c> edge are written in one transaction, so an entry never lands without its item
/// and a retried request writes nothing (code review CR68).
/// </summary>
public sealed class JournalService(
    SubjectService subjects, IActivityWriter writer, IEventReader events, ISubjectEventRepository edges,
    IClock clock, EventSource eventSource)
{
    /// <param name="subject">The item, as a reference <see cref="SubjectService.ResolveAsync"/> reads.</param>
    /// <param name="text">The entry.</param>
    /// <param name="externalId">
    /// Makes a retried request the same event (invariant 5), for a door that sends one: a retry
    /// answers the first entry, <see cref="JournalEntry.AlreadyWritten"/>, and one whose id named
    /// another kind of write is refused.
    /// </param>
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
        var now = clock.UtcNow;
        var written = await writer.WriteOnceAsync(
            new NewEvent(
                EventKinds.Journal, Provenances.Declared, now, now, eventSource.Id,
                ExternalId: externalId, PayloadJson: payloadJson),
            text,
            [new SubjectEventEdge(item.Id, SubjectEventRelations.Concerns, Provenances.Declared)],
            cancellationToken);
        var artifactId = written.ArtifactId.GetValueOrDefault();
        if (!written.AlreadyWritten)
        {
            return new JournalEntry(written.EventId, artifactId, item, AlreadyWritten: false);
        }

        RequestReusedException.ThrowIfOtherKind(written.Kind, EventKinds.Journal, externalId);
        var earlier = await events.FindWriteAsync(eventSource.Id, externalId!, cancellationToken);
        if (earlier is { Concerns: [var first, ..] })
        {
            return new JournalEntry(written.EventId, artifactId, first, AlreadyWritten: true);
        }

        // An entry written before the entry and its edge were one transaction (code review CR44),
        // whose first call stopped before it reached the item: finish it.
        await edges.CreateEdgeAsync(
            written.EventId, item.Id, SubjectEventRelations.Concerns, Provenances.Declared, cancellationToken);
        return new JournalEntry(written.EventId, artifactId, item, AlreadyWritten: true);
    }
}

/// <summary>
/// What <see cref="JournalService.AppendAsync"/> wrote: the journal event, its text and the item it
/// concerns. A retried request is <paramref name="AlreadyWritten"/>, and these are its first entry's.
/// </summary>
public sealed record JournalEntry(Guid EventId, Guid ArtifactId, SubjectRef Subject, bool AlreadyWritten);
