using System.Globalization;
using LifeOs.Api.Auth;
using LifeOs.Api.Read;
using LifeOs.Application.Reviews;

namespace LifeOs.Api.Http;

/// <summary>
/// Daily and weekly reviews (REVIEW-1, REVIEW-2, REVIEW-5, reviews plan §3), the HTTP twin of
/// <c>bsk review</c>. Reads go through <see cref="ReviewReader"/> on the reader connection; writes
/// call <see cref="ReviewService"/>, as the CLI does. Days are home-zone <c>yyyy-MM-dd</c>.
/// <list type="bullet">
/// <item><c>GET /api/reviews?kind=&amp;from=&amp;to=</c>: the reviews whose period is in the range
/// (the last four weeks by default), each with what it reads as today.</item>
/// <item><c>POST /api/reviews/open</c>: open-or-create the review holding a day, so opening it
/// twice can't make two. A review ahead of its day is a 400 in words.</item>
/// <item><c>POST /api/reviews/{id}/complete</c>: complete it; again is a no-op.</item>
/// <item><c>POST /api/reviews/{id}</c>: save what the review holds (keep focus).</item>
/// <item><c>GET /api/reviews/recap?from=&amp;to=&amp;asOf=</c>: what was recorded over the days.</item>
/// </list>
/// </summary>
public static class ReviewEndpoints
{
    /// <summary>The longest list read, in days.</summary>
    public const int MaxListDays = 400;

    public static void MapReviewEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/reviews")
            .WithTags("Reviews")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("", List).WithName("GetReviews")
            .Produces<IReadOnlyList<ReviewRow>>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/open", Open).WithName("OpenReview")
            .Produces<ReviewRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/{id:guid}/complete", Complete).WithName("CompleteReview")
            .Produces<ReviewRow>()
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPost("/{id:guid}", Save).WithName("SaveReview")
            .Produces<ReviewRow>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapGet("/recap", Recap).WithName("GetReviewRecap")
            .Produces<IReadOnlyList<RecapSectionRow>>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    /// <summary>The reviews from <paramref name="from"/> to <paramref name="to"/>, of <paramref name="kind"/> when given.</summary>
    private static async Task<IResult> List(
        string? kind, string? from, string? to, ReviewReader reader, CancellationToken cancellationToken)
    {
        var today = await reader.TodayAsync(cancellationToken);
        if (!TryDay(to, today, out var last) || !TryDay(from, last.AddDays(-27), out var first))
        {
            return Results.BadRequest(new ApiError("from and to are days. Send them as yyyy-MM-dd."));
        }

        if (first > last || last.DayNumber - first.DayNumber >= MaxListDays)
        {
            return Results.BadRequest(new ApiError($"from must be on or before to, at most {MaxListDays} days apart."));
        }

        var canonical = string.IsNullOrWhiteSpace(kind) ? null : ReviewService.KindOf(kind);
        return Results.Ok(await reader.ListAsync(canonical, first, last, cancellationToken));
    }

    /// <summary>
    /// The review of <c>kind</c> holding <c>date</c> (today by default), made now if it isn't open
    /// yet. A weekly review is the Monday-to-Sunday week's, named by its Sunday.
    /// </summary>
    private static async Task<IResult> Open(
        OpenReviewBody body, ReviewService reviews, ReviewReader reader, CancellationToken cancellationToken)
    {
        DateOnly? day = null;
        if (!string.IsNullOrWhiteSpace(body.Date))
        {
            if (!DateOnly.TryParseExact(body.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Results.BadRequest(new ApiError($"'{body.Date}' isn't a day. Send it as yyyy-MM-dd."));
            }

            day = parsed;
        }

        var opened = await reviews.OpenAsync(body.Kind ?? "", day, cancellationToken);
        return Results.Ok(await reader.RowAsync(opened.Review, cancellationToken));
    }

    /// <summary>Completes the review. Late is fine: it stays missed. Already completed: nothing is written.</summary>
    private static async Task<IResult> Complete(
        Guid id, ReviewService reviews, ReviewReader reader, CancellationToken cancellationToken)
        => Results.Ok(await reader.RowAsync(await reviews.CompleteAsync(id, cancellationToken), cancellationToken));

    /// <summary>Saves what the review holds. Only what's sent changes.</summary>
    private static async Task<IResult> Save(
        Guid id, SaveReviewBody body, ReviewService reviews, ReviewReader reader, CancellationToken cancellationToken)
    {
        var review = await reviews.MustGetAsync(id, cancellationToken);
        if (body.KeepFocus is { } keepFocus)
        {
            review = await reviews.SetKeepFocusAsync(id, keepFocus, cancellationToken);
        }

        return Results.Ok(await reader.RowAsync(review, cancellationToken));
    }

    /// <summary>
    /// What was recorded from <paramref name="from"/> to <paramref name="to"/>, with Tasks overdue
    /// as of <paramref name="asOf"/> (the earlier of today and <paramref name="to"/> by default).
    /// </summary>
    private static async Task<IResult> Recap(
        string? from, string? to, string? asOf, ReviewReader reader, CancellationToken cancellationToken)
    {
        var today = await reader.TodayAsync(cancellationToken);
        if (!TryDay(from, null, out var first) || !TryDay(to, null, out var last))
        {
            return Results.BadRequest(new ApiError("from and to are required. Send them as yyyy-MM-dd."));
        }

        if (first > last || last.DayNumber - first.DayNumber >= ReviewReader.MaxRecapDays)
        {
            return Results.BadRequest(new ApiError($"from must be on or before to, at most {ReviewReader.MaxRecapDays} days apart."));
        }

        if (!TryDay(asOf, last < today ? last : today, out var asOfDay))
        {
            return Results.BadRequest(new ApiError("asOf is a day. Send it as yyyy-MM-dd."));
        }

        return Results.Ok(await reader.RecapAsync(first, last, asOfDay, cancellationToken));
    }

    /// <summary>A <c>yyyy-MM-dd</c> day, or <paramref name="fallback"/> when none is sent (false when there's no fallback).</summary>
    private static bool TryDay(string? text, DateOnly? fallback, out DateOnly day)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            day = fallback ?? default;
            return fallback is not null;
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }
}

public sealed class OpenReviewBody
{
    /// <summary><c>daily</c> or <c>weekly</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>A day in the review's period, <c>yyyy-MM-dd</c>; today when left out.</summary>
    public string? Date { get; set; }
}

public sealed class SaveReviewBody
{
    /// <summary>Whether the review keeps the Primary focus; unchanged when left out.</summary>
    public bool? KeepFocus { get; set; }
}
