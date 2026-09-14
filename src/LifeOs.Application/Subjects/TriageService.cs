using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Flags an item into the inbox and resolves it out — the write path behind
/// <c>bsk flag</c> / <c>bsk drop</c> / <c>bsk file</c> (INBOX-1). Inbox membership is
/// asserted, not inferred: a <c>triage</c> event records {item, state}, the newest
/// per item wins, and <c>v_inbox</c> shows the ones whose newest state is
/// <c>flagged</c>. An item is a subject (urn / short id / title) or an event (a bare
/// uuid naming an existing event — how a raw capture is flagged before promotion).
/// The marker is append-only, mirroring status and archive.
/// </summary>
public sealed class TriageService(
    SubjectService subjects, IEventReader events, IEventStore store, IClock clock, string sourceId)
{
    /// <summary>Flags the referenced item into the inbox.</summary>
    public Task<TriageResult> FlagAsync(string reference, CancellationToken cancellationToken = default)
        => ResolveThenWriteAsync(reference, TriageStates.Flagged, cancellationToken);

    /// <summary>Resolves the item out of the inbox as dropped ("nothing to do").</summary>
    public Task<TriageResult> DropAsync(string reference, CancellationToken cancellationToken = default)
        => ResolveThenWriteAsync(reference, TriageStates.Dropped, cancellationToken);

    /// <summary>Resolves the item out of the inbox as filed (kept as reference).</summary>
    public Task<TriageResult> FileAsync(string reference, CancellationToken cancellationToken = default)
        => ResolveThenWriteAsync(reference, TriageStates.Filed, cancellationToken);

    /// <summary>
    /// Flags an item whose id is already known — used by flag-on-capture, where the
    /// caller just created the note event or Problem/Idea subject. Returns the marker
    /// event id.
    /// </summary>
    public Task<Guid> FlagItemAsync(
        bool isEvent, Guid itemId, CancellationToken cancellationToken = default)
        => AppendAsync(isEvent, itemId, TriageStates.Flagged, cancellationToken);

    private async Task<TriageResult> ResolveThenWriteAsync(
        string reference, string state, CancellationToken cancellationToken)
    {
        var item = await ResolveItemAsync(reference, cancellationToken);
        var eventId = await AppendAsync(item.IsEvent, item.Id, state, cancellationToken);
        return new TriageResult(eventId, item, state);
    }

    private async Task<Guid> AppendAsync(
        bool isEvent, Guid itemId, string state, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            item_kind = isEvent ? "event" : "subject",
            item_id = itemId.ToString(),
            state
        });

        var now = clock.UtcNow;
        return await store.AppendAsync(
            new NewEvent(
                Kind: EventKinds.Triage,
                Provenance: Provenances.Declared,
                OccurredAt: now,
                RecordedAt: now,
                SourceId: sourceId,
                PayloadJson: payload),
            cancellationToken);
    }

    private async Task<TriageItem> ResolveItemAsync(string reference, CancellationToken cancellationToken)
    {
        // A bare uuid naming a real event is that event (a raw capture flagged before
        // it is ever a subject); anything else resolves as a subject.
        if (Guid.TryParse(reference?.Trim(), out var id))
        {
            var sourceEvent = await events.FindAsync(id, cancellationToken);
            if (sourceEvent is not null)
            {
                return new TriageItem(IsEvent: true, sourceEvent.Id, $"event {sourceEvent.Id}");
            }
        }

        var subject = await subjects.ResolveAsync(reference!, cancellationToken);
        return new TriageItem(IsEvent: false, subject.Id, subject.Urn);
    }
}

/// <summary>The item a triage marker points at — a subject or an event — with a label.</summary>
public sealed record TriageItem(bool IsEvent, Guid Id, string Label);

/// <summary>The outcome of a triage action: the marker event, the item, and the new state.</summary>
public sealed record TriageResult(Guid EventId, TriageItem Item, string State);
