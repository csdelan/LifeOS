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
/// for each agent, writing as <paramref name="eventSource"/>. The event and its edge are one
/// transaction.
/// </summary>
public sealed class ReflectionService(
    IActivityWriter writer,
    IEventReader events,
    ISubjectEventRepository edges,
    IReviewRepository reviews,
    IClock clock,
    EventSource eventSource)
{
    /// <summary>
    /// Links the note at <paramref name="path"/> in <paramref name="vault"/> to the review.
    /// <paramref name="externalId"/> makes a retry land on the first write (unique per source), and
    /// answer it <see cref="ReflectionLinked.AlreadyWritten"/>; one whose id linked another review,
    /// or named another kind of write, is refused (code review CR68). <paramref name="extra"/> adds
    /// fields to the payload, such as the agent's Person. Refused in words for a path, vault or
    /// summary <see cref="ReviewReflections.Check"/> refuses.
    /// </summary>
    public async Task<ReflectionLinked> LinkAsync(
        Guid reviewId, string? path, string? vault, string? summary, string? externalId = null,
        JsonObject? extra = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var link = ReviewReflections.Check(path, vault, summary, now);
        var review = await MustGetAsync(reviewId, cancellationToken);
        if (externalId is not null && await events.FindWriteAsync(eventSource.Id, externalId, cancellationToken) is { } earlier)
        {
            return await RetriedAsync(earlier, review, externalId, cancellationToken);
        }

        if (review.Reflection is { } current && current.Same(link))
        {
            return new ReflectionLinked(review, EventId: null, Written: false, AlreadyWritten: false);
        }

        var payload = extra?.DeepClone().AsObject() ?? [];
        payload[ReviewReflections.PayloadKey] = new JsonObject
        {
            ["path"] = link.Path,
            ["vault"] = link.Vault,
            ["summary"] = link.Summary,
        };

        var written = await writer.WriteOnceAsync(
            new NewEvent(
                Kind: EventKinds.Observation,
                Provenance: Provenances.Declared,
                OccurredAt: now,
                RecordedAt: now,
                SourceId: eventSource.Id,
                ExternalId: externalId,
                PayloadJson: payload.ToJsonString()),
            text: null,
            [new SubjectEventEdge(review.Id, SubjectEventRelations.Concerns, Provenances.Declared)],
            cancellationToken);
        if (written.AlreadyWritten)
        {
            // The same request, retried while the first was still writing: it has finished now.
            var first = await events.FindWriteAsync(eventSource.Id, externalId!, cancellationToken)
                ?? throw new InvalidOperationException($"The event for request_id '{externalId}' is gone.");
            return await RetriedAsync(first, review, externalId!, cancellationToken);
        }

        return new ReflectionLinked(await MustGetAsync(review.Id, cancellationToken), written.EventId, Written: true, AlreadyWritten: false);
    }

    /// <summary>
    /// A retried link: the review as it reads now. A link written before the event and its edge
    /// were one transaction, whose first call stopped between the two, is tied to its review now.
    /// </summary>
    private async Task<ReflectionLinked> RetriedAsync(
        EarlierWrite earlier, Review review, string externalId, CancellationToken cancellationToken)
    {
        RequestReusedException.ThrowIfOtherKind(earlier.Kind, EventKinds.Observation, externalId);
        if (earlier.Concerns.Count == 0)
        {
            await edges.CreateEdgeAsync(
                earlier.EventId, review.Id, SubjectEventRelations.Concerns, Provenances.Declared, cancellationToken);
        }
        else if (earlier.Concerns[0].Id != review.Id)
        {
            throw new RequestReusedException(
                $"request_id '{externalId}' already linked a reflection to {earlier.Concerns[0].Title}. Send a new UUID for each write.");
        }

        return new ReflectionLinked(
            await MustGetAsync(review.Id, cancellationToken), earlier.EventId, Written: false, AlreadyWritten: true);
    }

    /// <summary>The review with that id.</summary>
    public async Task<Review> MustGetAsync(Guid id, CancellationToken cancellationToken = default)
        => await reviews.GetAsync(id, cancellationToken) ?? throw new SubjectNotFoundException(id.ToString());
}

/// <summary>
/// What linking did: the review as it reads now, the link event (null when nothing was written
/// and no request was retried), whether a link was written, and whether this request had written
/// it before.
/// </summary>
public sealed record ReflectionLinked(Review Review, Guid? EventId, bool Written, bool AlreadyWritten);
