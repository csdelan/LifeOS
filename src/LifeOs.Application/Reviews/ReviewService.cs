using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Reviews;

/// <summary>
/// Opens, completes and lists reviews (REVIEW-1, REVIEW-2, D3, D8, reviews plan §2): the one
/// writer of a Review's kind and period, shared by <c>bsk review</c> and <c>/api/reviews</c>.
/// <list type="bullet">
/// <item>Opening is open-or-create: one review per kind and period, so opening it again, or from
/// two tabs at once, returns the same one (migration 0052's unique index settles a race). It's
/// created lazily, the first time it's opened, since nothing runs on a schedule.</item>
/// <item>Neither kind opens ahead of its day; a weekly review opens on its Sunday. A past one
/// opens late, to backfill it, and still reads as missed.</item>
/// <item>Completing is a <c>state_change</c> to Completed through <see cref="StatusService"/>, as
/// <c>bsk status</c> would write it. Completing a completed review writes nothing.</item>
/// </list>
/// Days are home-zone days (<see cref="IHomeCalendar"/>).
/// </summary>
public sealed class ReviewService(
    SubjectService subjects,
    ISubjectRepository subjectRepository,
    IReviewRepository reviews,
    StatusService status,
    IHomeCalendar calendar,
    IClock clock)
{
    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    /// <summary>
    /// The <paramref name="kind"/> review holding <paramref name="day"/> (today when null), made
    /// now if it isn't open yet. A weekly review is the one for the Monday-to-Sunday week holding
    /// the day, named by its Sunday.
    /// </summary>
    public async Task<ReviewOpened> OpenAsync(
        string kind, DateOnly? day = null, CancellationToken cancellationToken = default)
    {
        var canonical = KindOf(kind);
        var today = await TodayAsync(cancellationToken);
        var period = ReviewPeriods.PeriodFor(canonical, day ?? today);
        if (ReviewPeriods.WhyNotOpen(canonical, period, today) is { } why)
        {
            throw new ArgumentException(why);
        }

        if (await reviews.FindAsync(canonical, period, cancellationToken) is { } existing)
        {
            return new ReviewOpened(existing, Created: false);
        }

        var attributes = new JsonObject
        {
            [ReviewPeriods.KindKey] = canonical,
            [ReviewPeriods.PeriodKey] = period.ToString("yyyy-MM-dd")
        };
        try
        {
            var created = await subjects.CreateAsync(
                SubjectTypes.Review, ReviewPeriods.Title(canonical, period), attributes.ToJsonString(),
                cancellationToken: cancellationToken);
            return new ReviewOpened(await MustGetAsync(created.Id, cancellationToken), Created: true);
        }
        catch (DuplicateSubjectException)
        {
            // Another open of the same review won the race; it's the one.
            return new ReviewOpened(
                await reviews.FindAsync(canonical, period, cancellationToken)
                    ?? throw new InvalidOperationException($"The {canonical} review for {period:yyyy-MM-dd} couldn't be opened."),
                Created: false);
        }
    }

    /// <summary>
    /// Completes the review. Late is fine (that's a backfill); it stays missed. Already completed:
    /// nothing is written.
    /// </summary>
    public async Task<Review> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await MustGetAsync(id, cancellationToken);
        if (review.IsCompleted)
        {
            return review;
        }

        await status.ChangeStatusAsync(review.Urn, ReviewStatuses.Completed, cancellationToken);
        return await MustGetAsync(id, cancellationToken);
    }

    /// <summary>Opens (if need be) and completes the <paramref name="kind"/> review holding <paramref name="day"/>.</summary>
    public async Task<Review> CompleteAsync(
        string kind, DateOnly? day = null, CancellationToken cancellationToken = default)
    {
        var opened = await OpenAsync(kind, day, cancellationToken);
        return await CompleteAsync(opened.Review.Id, cancellationToken);
    }

    /// <summary>Sets whether the review keeps the Primary focus (REVIEW-1, REVIEW-2: an explicit choice).</summary>
    public async Task<Review> SetKeepFocusAsync(Guid id, bool keepFocus, CancellationToken cancellationToken = default)
    {
        var review = await MustGetAsync(id, cancellationToken);
        if (review.KeepFocus == keepFocus)
        {
            return review;
        }

        var patch = new JsonObject { [ReviewPeriods.KeepFocusKey] = keepFocus };
        await subjectRepository.UpdateAttributesAsync(id, patch.ToJsonString(), [], cancellationToken);
        return await MustGetAsync(id, cancellationToken);
    }

    /// <summary>The review with that id.</summary>
    public async Task<Review> MustGetAsync(Guid id, CancellationToken cancellationToken = default)
        => await reviews.GetAsync(id, cancellationToken) ?? throw new SubjectNotFoundException(id.ToString());

    /// <summary>The kind's own spelling, or a refusal in words.</summary>
    public static string KindOf(string? kind)
        => ReviewKinds.Canonical(kind)
            ?? throw new ArgumentException($"A review is daily or weekly; got '{kind?.Trim()}'.");
}

/// <summary>The review opened, and whether opening it made it.</summary>
public sealed record ReviewOpened(Review Review, bool Created);
