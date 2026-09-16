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
    SubjectService subjects, IEventReader events, IEventStore store, IClock clock, string sourceId,
    StatusService status)
{
    /// <summary>Flags the referenced item into the inbox.</summary>
    public Task<TriageResult> FlagAsync(string reference, CancellationToken cancellationToken = default)
        => ResolveThenWriteAsync(reference, TriageStates.Flagged, cancellationToken);

    /// <summary>
    /// Drops the item out of the inbox as "it's nothing." A <em>subject</em> records the
    /// resolution where it belongs — its Status — by a <c>state_change</c> to that type's
    /// terminal dismiss status (Problem → Cancelled, Idea → Rejected, …); the inbox clears
    /// via the v_inbox terminal-status rule (0021), and the result reports StatusChanged.
    /// An event capture has no status, so Drop degrades to a plain Dismiss (attention-only
    /// marker). See docs/pilot/inbox-attention-vs-status.md.
    /// </summary>
    public async Task<TriageResult> DropAsync(string reference, CancellationToken cancellationToken = default)
    {
        var item = await ResolveItemAsync(reference, cancellationToken);

        if (!item.IsEvent)
        {
            var dismiss = StatusVocabulary.DismissStatusFor(item.SubjectType);
            if (!string.IsNullOrEmpty(dismiss))
            {
                // item.Label is the subject's urn; ChangeStatusAsync re-resolves it.
                var statusResult = await status.ChangeStatusAsync(item.Label, dismiss, cancellationToken);
                return new TriageResult(statusResult.EventId, item, dismiss, StatusChanged: true);
            }
        }

        // Event (or status-less subject): no status to set — clear it attention-only.
        var eventId = await AppendAsync(item.IsEvent, item.Id, TriageStates.Dismissed, cancellationToken);
        return new TriageResult(eventId, item, TriageStates.Dismissed);
    }

    /// <summary>
    /// Dismisses the item out of the inbox attention-only — "clarified, nothing to record"
    /// — with no status change. The single non-status resolution (it replaced the former
    /// file/drop triage states, 0021).
    /// </summary>
    public Task<TriageResult> DismissAsync(string reference, CancellationToken cancellationToken = default)
        => ResolveThenWriteAsync(reference, TriageStates.Dismissed, cancellationToken);

    /// <summary>
    /// Flags an item whose id is already known — used by flag-on-capture, where the
    /// caller just created the note event or Problem/Idea subject. Returns the marker
    /// event id.
    /// </summary>
    public Task<Guid> FlagItemAsync(
        bool isEvent, Guid itemId, CancellationToken cancellationToken = default)
        => SetStateAsync(isEvent, itemId, TriageStates.Flagged, cancellationToken);

    /// <summary>
    /// Writes a triage state for an item whose id is already known — used by the
    /// promote path to resolve an item out of the inbox (<see cref="TriageStates.Promoted"/>)
    /// without re-resolving the reference. Returns the marker event id.
    /// </summary>
    public Task<Guid> SetStateAsync(
        bool isEvent, Guid itemId, string state, CancellationToken cancellationToken = default)
        => AppendAsync(isEvent, itemId, state, cancellationToken);

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
        return new TriageItem(IsEvent: false, subject.Id, subject.Urn, subject.Type);
    }
}

/// <summary>
/// The item a triage marker points at — a subject or an event — with a label (the urn
/// for a subject) and, for a subject, its type (empty for an event).
/// </summary>
public sealed record TriageItem(bool IsEvent, Guid Id, string Label, string SubjectType = "");

/// <summary>
/// The outcome of a triage action: the written event, the item, and the resulting state.
/// <paramref name="StatusChanged"/> is true when a Drop rerouted to a subject Status change
/// — then <paramref name="EventId"/> is the <c>state_change</c> event and
/// <paramref name="State"/> is the new Status, not a triage-marker state.
/// </summary>
public sealed record TriageResult(Guid EventId, TriageItem Item, string State, bool StatusChanged = false);
