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
/// stays reachable through the source event's payload. The source's tags are copied
/// onto the new subject, so classification done during triage follows the item.
/// </summary>
public sealed class PromotionService(
    SubjectService subjects, IEventReader events, StatusService status, ITagRepository tags, TriageService triage)
{
    /// <summary>
    /// Promotes what the Inbox holds, as every door does (<c>bsk promote</c>,
    /// <c>POST /api/promote</c>, code review CR7): a uuid naming a capture event promotes the
    /// event; anything else names an item (canonically an Idea) to promote into new work. Either
    /// way the source leaves the Inbox, marked promoted.
    /// </summary>
    public async Task<InboxPromotion> PromoteFromInboxAsync(
        string source, string type, string title, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(source.Trim(), out var id) && await events.FindAsync(id, cancellationToken) is not null)
        {
            var promoted = await PromoteAsync(id, type, title, cancellationToken);
            await triage.SetStateAsync(isEvent: true, promoted.OriginEventId, TriageStates.Promoted, cancellationToken);
            return new InboxPromotion(promoted.Subject, promoted.OriginEventId, null);
        }

        var result = await PromoteSubjectAsync(source, type, title, cancellationToken);
        await triage.SetStateAsync(isEvent: false, result.Source.Id, TriageStates.Promoted, cancellationToken);
        return new InboxPromotion(result.Target, null, result);
    }

    public async Task<PromotionResult> PromoteAsync(
        Guid eventId, string type, string title, CancellationToken cancellationToken = default)
    {
        type = SubjectTypes.ParseCreatable(type);
        var source = await events.FindAsync(eventId, cancellationToken)
            ?? throw new InvalidOperationException($"No event found with id '{eventId}'.");

        // origin_event_id anchors the new subject to its source without touching it.
        var subject = await subjects.CreateAsync(
            type, title, originEventId: source.Id, cancellationToken: cancellationToken);
        await tags.CopyToSubjectAsync(fromIsEvent: true, source.Id, subject.Id, cancellationToken);

        return new PromotionResult(subject, source.Id);
    }

    /// <summary>
    /// The subject → subject half of "promote" (D4 / CAP-6): an existing subject — the
    /// canonical case is an Idea — promotes into new work by creating the target and a
    /// <c>results_in</c> edge (source → target) atomically. When the source is an Idea
    /// its status advances to <c>Promoted</c>; other source types keep their status
    /// (e.g. a Problem is not resolved just because it produced work, GEN-14). It leaves the
    /// source's Inbox triage alone; <see cref="PromoteFromInboxAsync"/> marks it.
    /// </summary>
    public async Task<SubjectPromotionResult> PromoteSubjectAsync(
        string sourceReference, string targetType, string targetTitle,
        CancellationToken cancellationToken = default)
    {
        targetType = SubjectTypes.ParseCreatable(targetType);
        var source = await subjects.ResolveAsync(sourceReference, cancellationToken);

        var (target, edgeId) = await subjects.CreateWithIncomingEdgeAsync(
            targetType, targetTitle, "{}", SubjectRelations.ResultsIn, source.Id, cancellationToken);
        await tags.CopyToSubjectAsync(fromIsEvent: false, source.Id, target.Id, cancellationToken);

        // An Idea that has been acted on is Promoted (CAP-6); leave other types alone.
        if (source.Type == SubjectTypes.Idea)
        {
            await status.ChangeStatusAsync(source.Urn, "Promoted", cancellationToken);
        }

        return new SubjectPromotionResult(source, target, edgeId);
    }
}

/// <summary>
/// The outcome of a promotion: the new subject and the source event it was promoted
/// from (recorded as the subject's <c>origin_event_id</c>).
/// </summary>
public sealed record PromotionResult(SubjectRef Subject, Guid OriginEventId);

/// <summary>The outcome of a subject → subject promotion: the source, the new target, and the edge.</summary>
public sealed record SubjectPromotionResult(SubjectRef Source, SubjectRef Target, Guid EdgeId);

/// <summary>
/// What <see cref="PromotionService.PromoteFromInboxAsync"/> made: the new item, and either the
/// capture event it came from or the item promotion (source, target and edge).
/// </summary>
public sealed record InboxPromotion(SubjectRef Subject, Guid? OriginEventId, SubjectPromotionResult? FromSubject);
