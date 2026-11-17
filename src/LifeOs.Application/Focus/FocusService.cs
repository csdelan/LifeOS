using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Focus;

/// <summary>
/// Chooses the Primary focus (TODAY-1, D6, focus plan G1), behind <c>bsk focus</c> and
/// <c>POST /api/focus/primary</c>. The focus is an Active, unarchived Goal, or none. Each change is
/// a <c>declared</c> <c>note</c> event, <c>{focus: {level, goal_id, review_id}}</c>, that
/// <c>concerns</c> the Goal and the review it was chosen in; the newest wins, so the log keeps every
/// one. Choosing the focus already held writes nothing. Chosen in a review, it also turns that
/// review's Keep focus off, so the review records that it changed the focus.
/// </summary>
public sealed class FocusService(
    IFocusRepository focus,
    SubjectService subjects,
    ReviewService reviews,
    IActivityWriter writer,
    IClock clock,
    EventSource eventSource)
{
    /// <summary>The focus as last chosen, or null when none ever was.</summary>
    public Task<ChosenFocus?> GetAsync(CancellationToken cancellationToken = default)
        => focus.GetAsync(PrimaryFocus.Weekly, cancellationToken);

    /// <summary>
    /// Makes the Goal <paramref name="goalReference"/> names (an id, URN, short id or title) the
    /// Primary focus, or clears it when it's blank (No focus). <paramref name="reviewId"/> is the
    /// review it's chosen in, if any. Refused in words for anything but an Active, unarchived Goal,
    /// and for a review that isn't there.
    /// </summary>
    public async Task<FocusChange> SetAsync(
        string? goalReference, Guid? reviewId = null, CancellationToken cancellationToken = default)
    {
        var review = reviewId is { } id ? await reviews.MustGetAsync(id, cancellationToken) : null;
        var goal = string.IsNullOrWhiteSpace(goalReference) ? null : await GoalAsync(goalReference, cancellationToken);

        var current = await GetAsync(cancellationToken);
        if (current?.GoalId == goal?.Id && (current is not null || goal is null))
        {
            return new FocusChange(current, Written: false);
        }

        var choice = new JsonObject
        {
            ["level"] = PrimaryFocus.Weekly,
            ["goal_id"] = goal?.Id.ToString(),
            ["review_id"] = review?.Id.ToString(),
        };
        var edges = new List<SubjectEventEdge>();
        if (goal is not null)
        {
            edges.Add(new SubjectEventEdge(goal.Id, SubjectEventRelations.Concerns, Provenances.Declared));
        }

        if (review is not null)
        {
            edges.Add(new SubjectEventEdge(review.Id, SubjectEventRelations.Concerns, Provenances.Declared));
        }

        var now = clock.UtcNow;
        await writer.WriteAsync(
            new NewEvent(
                Kind: EventKinds.Note,
                Provenance: Provenances.Declared,
                OccurredAt: now,
                RecordedAt: now,
                SourceId: eventSource.Id,
                PayloadJson: new JsonObject { [PrimaryFocus.PayloadKey] = choice }.ToJsonString()),
            edges,
            cancellationToken);

        if (review is not null)
        {
            await reviews.SetKeepFocusAsync(review.Id, keepFocus: false, cancellationToken);
        }

        return new FocusChange(await GetAsync(cancellationToken), Written: true);
    }

    /// <summary>The Goal the reference names, refused in words when it can't be the focus.</summary>
    private async Task<SubjectRef> GoalAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference.Trim(), cancellationToken);
        var (status, archived) = await focus.GetStateAsync(subject.Id, cancellationToken);
        return PrimaryFocus.WhyNot(subject.Title, subject.Type, status, archived) is { } why
            ? throw new ArgumentException(why)
            : subject;
    }
}

/// <summary>What choosing did: the focus as it now reads (null when none was ever chosen), and whether anything was written.</summary>
public sealed record FocusChange(ChosenFocus? Focus, bool Written);
