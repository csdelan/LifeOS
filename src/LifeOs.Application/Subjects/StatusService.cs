using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Moves a subject's status by appending a <c>state_change</c> event
/// (<c>bsk status</c>). Status is never edited directly: this writes an event and
/// nothing else — the <c>subject_current</c> projection reflects it only after a
/// rebuild folds the event in (epic invariant 8).
/// <para>
/// Every status change then reconciles the subject's rewards (REWARD-2, rewards plan
/// §3.3): completing a Task, Project or Goal earns its bounty, and moving it off
/// Completed gives that back. This covers <c>bsk status</c>, the web's Change status and
/// the timer's Complete, which all come through here.
/// </para>
/// </summary>
public sealed class StatusService(
    SubjectService subjects, IEventStore events, RewardService rewards, IClock clock, string sourceId)
{
    public async Task<StatusResult> ChangeStatusAsync(
        string reference, string newStatus, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newStatus))
        {
            throw new ArgumentException("A status is required.", nameof(newStatus));
        }

        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        var status = newStatus.Trim();

        var payload = JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), status });

        var now = clock.UtcNow;
        var newEvent = new NewEvent(
            Kind: EventKinds.StateChange,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);

        var eventId = await events.AppendAsync(newEvent, cancellationToken);
        var awards = await rewards.ReconcileAsync(subject, eventId, cancellationToken);
        return new StatusResult(eventId, subject, status, awards);
    }
}

/// <summary>
/// The outcome of a status change: the state_change event, the subject, and any ledger
/// entries it earned or reversed.
/// </summary>
public sealed record StatusResult(Guid EventId, SubjectRef Subject, string Status, IReadOnlyList<Award> Awards);
