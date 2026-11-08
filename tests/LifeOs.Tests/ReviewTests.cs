using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The review rules without a database (REVIEW-1, REVIEW-2, D8, reviews plan §2): Monday-to-Sunday
/// weeks named by their Sunday, what a review looks back on and plans, and when it reads as missed.
/// </summary>
public sealed class ReviewPeriodTests
{
    private static readonly DateOnly SunOct25 = new(2026, 10, 25);

    [Theory]
    [InlineData("2026-10-19", "2026-10-25")] // Monday
    [InlineData("2026-10-22", "2026-10-25")] // Thursday
    [InlineData("2026-10-25", "2026-10-25")] // the Sunday itself
    [InlineData("2026-10-26", "2026-11-01")] // the next Monday starts the next week
    public void A_day_belongs_to_the_week_ending_on_the_next_Sunday(string day, string sunday)
        => Assert.Equal(DateOnly.Parse(sunday), ReviewPeriods.WeekSunday(DateOnly.Parse(day)));

    [Fact]
    public void A_weekly_review_looks_back_on_its_week_and_plans_the_next_as_Chris_described()
    {
        // "A weekly review on 10/25/26 would review my accomplishments from 10/19/26 to 10/25/26,
        // but then plan priorities for 10/26/26 through 11/1/2026."
        Assert.Equal((new DateOnly(2026, 10, 19), SunOct25), ReviewPeriods.LooksBack(ReviewKinds.Weekly, SunOct25));
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1)), ReviewPeriods.Plans(ReviewKinds.Weekly, SunOct25));
        Assert.Equal("Weekly review, Sun Oct 25", ReviewPeriods.Title(ReviewKinds.Weekly, SunOct25));

        var thursday = new DateOnly(2026, 10, 22);
        Assert.Equal((thursday, thursday), ReviewPeriods.LooksBack(ReviewKinds.Daily, thursday));
        Assert.Equal((thursday.AddDays(1), thursday.AddDays(1)), ReviewPeriods.Plans(ReviewKinds.Daily, thursday));
        Assert.Equal("Daily review, Thu Oct 22", ReviewPeriods.Title(ReviewKinds.Daily, thursday));
        Assert.Equal(SunOct25, ReviewPeriods.PeriodFor(ReviewKinds.Weekly, thursday));
        Assert.Equal(thursday, ReviewPeriods.PeriodFor(ReviewKinds.Daily, thursday));
    }

    [Fact]
    public void Neither_kind_opens_ahead_of_its_day_and_a_past_one_opens_late()
    {
        var thursday = new DateOnly(2026, 10, 22);
        Assert.Contains("opens that Sunday", ReviewPeriods.WhyNotOpen(ReviewKinds.Weekly, SunOct25, thursday));
        Assert.Contains("opens that day", ReviewPeriods.WhyNotOpen(ReviewKinds.Daily, thursday.AddDays(1), thursday));
        Assert.Null(ReviewPeriods.WhyNotOpen(ReviewKinds.Daily, thursday, thursday));
        Assert.Null(ReviewPeriods.WhyNotOpen(ReviewKinds.Weekly, SunOct25, SunOct25));
        Assert.Null(ReviewPeriods.WhyNotOpen(ReviewKinds.Weekly, SunOct25, SunOct25.AddDays(30)));
    }

    [Fact]
    public void An_open_review_is_missed_once_its_day_is_over_and_a_late_completion_keeps_the_miss()
    {
        var day = new DateOnly(2026, 10, 22);
        var open = Review(day, status: null, firstCompletedOn: null);
        Assert.Equal((ReviewStatuses.Open, false), (open.StatusAsOf(day), open.MissedAsOf(day)));
        Assert.Equal((ReviewStatuses.Missed, true), (open.StatusAsOf(day.AddDays(1)), open.MissedAsOf(day.AddDays(1))));

        var onTime = Review(day, ReviewStatuses.Completed, firstCompletedOn: day);
        Assert.Equal((ReviewStatuses.Completed, false, false),
            (onTime.StatusAsOf(day.AddDays(9)), onTime.MissedAsOf(day.AddDays(9)), onTime.CompletedLate));

        // Backfilled the next day (REVIEW-2): completed, and still missed.
        var late = Review(day, ReviewStatuses.Completed, firstCompletedOn: day.AddDays(1));
        Assert.Equal((ReviewStatuses.Completed, true, true),
            (late.StatusAsOf(day.AddDays(1)), late.MissedAsOf(day.AddDays(1)), late.CompletedLate));

        // Set Missed by hand on its own day: missed already.
        var dropped = Review(day, ReviewStatuses.Missed, firstCompletedOn: null);
        Assert.Equal((ReviewStatuses.Missed, true), (dropped.StatusAsOf(day), dropped.MissedAsOf(day)));

        // Completed on time, then reopened: not missed, and Open rather than Missed past its day.
        var reopened = Review(day, ReviewStatuses.Open, firstCompletedOn: day);
        Assert.Equal((ReviewStatuses.Open, false), (reopened.StatusAsOf(day.AddDays(3)), reopened.MissedAsOf(day.AddDays(3))));
    }

    private static Review Review(DateOnly period, string? status, DateOnly? firstCompletedOn)
        => new(Guid.NewGuid(), "urn:bsk:review:x", "x", ReviewKinds.Daily, period, status, KeepFocus: true,
            CompletedAt: status == ReviewStatuses.Completed ? DateTimeOffset.UtcNow : null,
            FirstCompletedAt: firstCompletedOn is null ? null : DateTimeOffset.UtcNow,
            FirstCompletedOn: firstCompletedOn, Archived: false, CreatedAt: DateTimeOffset.UtcNow);
}

/// <summary>
/// Reviews in the kernel (REVIEW-1, REVIEW-2, D3, D8, migration 0052, reviews plan R1): opening is
/// open-or-create, one per kind and period; neither kind opens ahead of its day; completing is a
/// status change; an Open review past its day reads as missed, and completing it late keeps that.
/// Each test has a database of its own (home zone UTC) and a clock set by hand.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A Wednesday, and the Sunday ending its week.
    private static readonly DateOnly Wednesday = new(2026, 11, 4);
    private static readonly DateOnly Sunday = new(2026, 11, 8);

    private sealed record Kernel(ServiceProvider Provider, DayClock Clock, ReviewService Reviews) : IAsyncDisposable
    {
        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private static Kernel Build(DomainTestDatabase db, DateOnly on)
    {
        var clock = new DayClock();
        clock.On(on);
        var provider = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone)
            .AddSingleton<IClock>(clock)
            .BuildServiceProvider();
        return new Kernel(provider, clock, provider.GetRequiredService<ReviewService>());
    }

    private static async Task<long> CountAsync(DomainTestDatabase db, string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, args, cancellationToken: Ct));
    }

    [Fact]
    public async Task Opening_a_review_twice_or_at_once_from_many_places_makes_one()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var k = Build(db, Wednesday);

        var first = await k.Reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct);
        Assert.True(first.Created);
        Assert.Equal((ReviewKinds.Daily, Wednesday, "Daily review, Wed Nov 4"),
            (first.Review.Kind, first.Review.Period, first.Review.Title));
        Assert.StartsWith("urn:bsk:review:", first.Review.Urn);

        var again = await k.Reviews.OpenAsync("Daily", Wednesday, Ct);
        Assert.False(again.Created);
        Assert.Equal(first.Review.Id, again.Review.Id);

        // Yesterday's, five times at once (two tabs, a phone): one subject.
        var opens = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => k.Reviews.OpenAsync(ReviewKinds.Daily, Wednesday.AddDays(-1), Ct)));
        Assert.Single(opens.Select(o => o.Review.Id).Distinct());
        Assert.Single(opens, o => o.Created);

        Assert.Equal(2, await CountAsync(db, "SELECT count(*) FROM bsk.subject WHERE type = 'Review'"));
        Assert.Equal(ReviewStatuses.Open, first.Review.StatusAsOf(Wednesday));
        Assert.True(first.Review.KeepFocus);
    }

    [Fact]
    public async Task A_weekly_review_is_its_weeks_Sunday_and_opens_that_day_not_before()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var k = Build(db, Wednesday);

        var early = await Assert.ThrowsAsync<ArgumentException>(() => k.Reviews.OpenAsync(ReviewKinds.Weekly, cancellationToken: Ct));
        Assert.Contains("opens that Sunday", early.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => k.Reviews.OpenAsync(ReviewKinds.Daily, Wednesday.AddDays(1), Ct));
        Assert.Contains("daily or weekly", (await Assert.ThrowsAsync<ArgumentException>(
            () => k.Reviews.OpenAsync("monthly", cancellationToken: Ct))).Message);

        // Last week's is open to backfill from any day in it.
        var lastWeek = await k.Reviews.OpenAsync(ReviewKinds.Weekly, Wednesday.AddDays(-5), Ct);
        Assert.Equal(new DateOnly(2026, 11, 1), lastWeek.Review.Period);

        k.Clock.On(Sunday);
        var opened = await k.Reviews.OpenAsync(ReviewKinds.Weekly, cancellationToken: Ct);
        Assert.Equal((Sunday, "Weekly review, Sun Nov 8"), (opened.Review.Period, opened.Review.Title));
        Assert.Equal((new DateOnly(2026, 11, 2), Sunday), opened.Review.LooksBack);
        Assert.Equal(opened.Review.Id, (await k.Reviews.OpenAsync(ReviewKinds.Weekly, Wednesday, Ct)).Review.Id);

        // The daily and the weekly review for the Sunday are two reviews.
        var daily = await k.Reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct);
        Assert.NotEqual(opened.Review.Id, daily.Review.Id);
        var listed = await k.Get<IReviewRepository>().ListAsync(from: Sunday, to: Sunday, cancellationToken: Ct);
        Assert.Equal([ReviewKinds.Daily, ReviewKinds.Weekly], listed.Select(r => r.Kind));
    }

    [Fact]
    public async Task Completing_is_a_status_change_written_once()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var k = Build(db, Wednesday);
        var review = (await k.Reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct)).Review;

        var completed = await k.Reviews.CompleteAsync(review.Id, Ct);
        Assert.True(completed.IsCompleted);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(Wednesday, completed.FirstCompletedOn);
        Assert.Equal((ReviewStatuses.Completed, false, false),
            (completed.StatusAsOf(Wednesday.AddDays(3)), completed.MissedAsOf(Wednesday.AddDays(3)), completed.CompletedLate));

        await k.Reviews.CompleteAsync(review.Id, Ct);
        Assert.Equal(1, await CountAsync(db,
            "SELECT count(*) FROM bsk.event WHERE kind = 'state_change' AND payload->>'subject_id' = @id",
            new { id = review.Id.ToString() }));

        // Still a subject like any other: `bsk status` reopens it, and that keeps its first completion.
        await k.Get<StatusService>().ChangeStatusAsync(review.Urn, "open", Ct);
        var reopened = await k.Reviews.MustGetAsync(review.Id, Ct);
        Assert.Equal((ReviewStatuses.Open, Wednesday), (reopened.Status, reopened.FirstCompletedOn));
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task An_uncompleted_review_is_missed_after_its_day_and_completing_it_late_keeps_the_miss()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var k = Build(db, Wednesday);
        var review = (await k.Reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct)).Review;

        k.Clock.On(Wednesday.AddDays(1));
        var thursday = await k.Reviews.TodayAsync(Ct);
        var missed = await k.Reviews.MustGetAsync(review.Id, Ct);
        Assert.Equal((ReviewStatuses.Missed, true), (missed.StatusAsOf(thursday), missed.MissedAsOf(thursday)));
        Assert.Null(missed.Status); // nothing was written: missed is read, not stored

        var late = await k.Reviews.CompleteAsync(ReviewKinds.Daily, Wednesday, Ct);
        Assert.Equal(review.Id, late.Id);
        Assert.Equal((ReviewStatuses.Completed, true, true),
            (late.StatusAsOf(thursday), late.MissedAsOf(thursday), late.CompletedLate));

        // A review never opened can be completed late too (a backfill), and it's missed.
        var never = await k.Reviews.CompleteAsync(ReviewKinds.Daily, Wednesday.AddDays(-2), Ct);
        Assert.True(never.MissedAsOf(thursday));
        Assert.True(never.CompletedLate);
    }

    [Fact]
    public async Task Keep_focus_is_the_reviews_and_its_kind_and_period_are_the_review_services()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var k = Build(db, Wednesday);
        var review = (await k.Reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct)).Review;

        Assert.False((await k.Reviews.SetKeepFocusAsync(review.Id, false, Ct)).KeepFocus);
        Assert.True((await k.Reviews.SetKeepFocusAsync(review.Id, true, Ct)).KeepFocus);

        var attributes = k.Get<AttributeService>();
        foreach (var key in new[] { "kind", "period" })
        {
            var refused = await Assert.ThrowsAsync<ArgumentException>(() => attributes.SetAsync(
                review.Urn, [new AttributeAssignment(key, "weekly")], Ct));
            Assert.Contains("bsk review open", refused.Message);
        }

        // `bsk set keep_focus=false` writes text; it reads the same.
        await attributes.SetAsync(review.Urn, [new AttributeAssignment("keep_focus", "false")], Ct);
        Assert.False((await k.Reviews.MustGetAsync(review.Id, Ct)).KeepFocus);

        // Missed by hand ("not doing this one") reads as missed on its own day.
        await k.Get<StatusService>().ChangeStatusAsync(review.Urn, "Missed", Ct);
        var dropped = await k.Reviews.MustGetAsync(review.Id, Ct);
        Assert.Equal((ReviewStatuses.Missed, true), (dropped.StatusAsOf(Wednesday), dropped.MissedAsOf(Wednesday)));
    }
}
