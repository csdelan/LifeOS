using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Turns a capture into a tracked subject without ever mutating the capture
/// (<c>bsk promote</c>, epic invariant 5). The new subject records
/// <c>origin_event_id = &lt;event&gt;</c>; the source event is left byte-identical
/// (guaranteed by the append-only trigger). When the source event names a subject
/// — e.g. the Problem an <c>idea_session</c> is about — a <c>promoted_from</c>
/// relation links the new subject back to it, so the derived-from trail is explicit.
/// </summary>
public sealed class PromotionService(
    SubjectService subjects, IEventReader events, IRelationRepository relations)
{
    public async Task<PromotionResult> PromoteAsync(
        Guid eventId, string type, string title, CancellationToken cancellationToken = default)
    {
        var source = await events.FindAsync(eventId, cancellationToken)
            ?? throw new InvalidOperationException($"No event found with id '{eventId}'.");

        // origin_event_id anchors the new subject to its source without touching it.
        var subject = await subjects.CreateAsync(
            type, title, originEventId: source.Id, cancellationToken: cancellationToken);

        Guid? promotedFrom = null;
        if (source.SubjectId is { } sourceSubjectId)
        {
            await relations.CreateAsync(
                subject.Id, RelationKinds.PromotedFrom, sourceSubjectId, Provenances.Derived, cancellationToken);
            promotedFrom = sourceSubjectId;
        }

        return new PromotionResult(subject, source.Id, promotedFrom);
    }
}

/// <summary>
/// The outcome of a promotion: the new subject, the source event it was promoted
/// from, and the source subject the <c>promoted_from</c> relation points to (if any).
/// </summary>
public sealed record PromotionResult(SubjectRef Subject, Guid OriginEventId, Guid? PromotedFromSubjectId);
