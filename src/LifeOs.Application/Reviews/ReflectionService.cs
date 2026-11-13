using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Reviews;

/// <summary>
/// Links a review to the reflection that answers it (reviews plan §4.1, notes plan N8): a
/// <c>declared</c> <c>observation</c> event, <c>{"reflection": {path, vault, summary}}</c>, with a
/// <c>concerns</c> edge to the Review. The newest link wins, so linking again writes a new version
/// and the log keeps every one; linking the same note with the same summary writes nothing.
/// Linking never completes the review. The connector's <c>link_reflection</c> builds one of these
/// for each agent, writing as <paramref name="eventSource"/>.
/// </summary>
public sealed class ReflectionService(
    IEventStore events,
    ISubjectEventRepository edges,
    IReviewRepository reviews,
    IClock clock,
    EventSource eventSource)
{
    /// <summary>
    /// Links the note at <paramref name="path"/> in <paramref name="vault"/> to the review.
    /// <paramref name="externalId"/> makes a retry land on the first write (unique per source);
    /// <paramref name="extra"/> adds fields to the payload, such as the agent's Person. Refused in
    /// words for a path, vault or summary <see cref="ReviewReflections.Check"/> refuses.
    /// </summary>
    public async Task<ReflectionLinked> LinkAsync(
        Guid reviewId, string? path, string? vault, string? summary, string? externalId = null,
        JsonObject? extra = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var link = ReviewReflections.Check(path, vault, summary, now);
        var review = await MustGetAsync(reviewId, cancellationToken);
        if (review.Reflection is { } current && current.Same(link))
        {
            return new ReflectionLinked(review, EventId: null, Written: false);
        }

        var payload = extra?.DeepClone().AsObject() ?? [];
        payload[ReviewReflections.PayloadKey] = new JsonObject
        {
            ["path"] = link.Path,
            ["vault"] = link.Vault,
            ["summary"] = link.Summary,
        };

        var eventId = await events.AppendAsync(new NewEvent(
            Kind: EventKinds.Observation,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: eventSource.Id,
            ExternalId: externalId,
            PayloadJson: payload.ToJsonString()), cancellationToken);
        await FinishAsync(eventId, review.Id, cancellationToken);
        return new ReflectionLinked(await MustGetAsync(review.Id, cancellationToken), eventId, Written: true);
    }

    /// <summary>
    /// Ties a link event to its review: the step a link takes after writing its event, and the
    /// one a retry finishes when the first call stopped between the two.
    /// </summary>
    public Task FinishAsync(Guid eventId, Guid reviewId, CancellationToken cancellationToken = default)
        => edges.CreateEdgeAsync(eventId, reviewId, SubjectEventRelations.Concerns, Provenances.Declared, cancellationToken);

    /// <summary>The review with that id.</summary>
    public async Task<Review> MustGetAsync(Guid id, CancellationToken cancellationToken = default)
        => await reviews.GetAsync(id, cancellationToken) ?? throw new SubjectNotFoundException(id.ToString());
}

/// <summary>
/// What linking did: the review as it reads now, the link event (null when nothing was written)
/// and whether a link was written.
/// </summary>
public sealed record ReflectionLinked(Review Review, Guid? EventId, bool Written);
