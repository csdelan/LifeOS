using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Turns a capture into a tracked subject without ever mutating the capture
/// (<c>bsk promote</c>, epic invariant 5). The new subject records
/// <c>origin_event_id = &lt;event&gt;</c>, which <em>is</em> the promotion link
/// (subject → event); the source event is left byte-identical, guaranteed by the
/// append-only trigger. There is deliberately no <c>promoted_from</c> edge — that
/// would be a second, competing representation of the same fact the column already
/// holds. The source subject (e.g. the Problem an <c>idea_session</c> is about)
/// stays reachable through the source event's payload.
/// </summary>
public sealed class PromotionService(SubjectService subjects, IEventReader events)
{
    public async Task<PromotionResult> PromoteAsync(
        Guid eventId, string type, string title, CancellationToken cancellationToken = default)
    {
        var source = await events.FindAsync(eventId, cancellationToken)
            ?? throw new InvalidOperationException($"No event found with id '{eventId}'.");

        // origin_event_id anchors the new subject to its source without touching it.
        var subject = await subjects.CreateAsync(
            type, title, originEventId: source.Id, cancellationToken: cancellationToken);

        return new PromotionResult(subject, source.Id);
    }
}

/// <summary>
/// The outcome of a promotion: the new subject and the source event it was promoted
/// from (recorded as the subject's <c>origin_event_id</c>).
/// </summary>
public sealed record PromotionResult(SubjectRef Subject, Guid OriginEventId);
