using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7 (xp-domains plan X7, §3.13): a configured domain over its last N days.
/// The days end today once today has succeeded, and yesterday until then. Consistency and the
/// average are over the days the domain has history for, and the report says how many those
/// are. Levels and ranks are where the domain stood the day before the period against now.
/// Pure: each test folds days it writes itself.
/// </summary>
public sealed class DomainReportTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static DomainDay Day(int daysAgo, long baseXp) => new(Today.AddDays(-daysAgo), baseXp, baseXp);

    /// <summary>A successful day (70 base XP) on each of <paramref name="daysAgo"/>.</summary>
    private static IEnumerable<DomainDay> Good(IEnumerable<int> daysAgo) => daysAgo.Select(ago => Day(ago, 70));

    private static DomainProgress Fold(IEnumerable<DomainDay> days) => DomainProgress.Fold(LifeDomains.Health, days, Today);

    /// <summary>
    /// 60 days of Health ending yesterday, every one successful but 20–22 days ago, and 30 base
    /// XP so far today. Apprentice qualified 6 days ago: the day the XP reached level 10, with
    /// the 14 days before it all successful.
    /// </summary>
    private static DomainProgress SixtyDays(long today = 30)
        => Fold(Good(Enumerable.Range(1, 60).Where(ago => ago is < 20 or > 22)).Append(Day(0, today)));

    [Fact]
    public void A_month_counts_the_days_the_rank_gates_read_and_what_moved_in_them()
    {
        var progress = SixtyDays();
        var report = progress.Report(30);

        // Today hasn't succeeded, so the 30 days end yesterday and its 30 XP isn't in them.
        Assert.Equal((Today.AddDays(-30), Today.AddDays(-1)), (report.From, report.To));
        Assert.Equal((30, 30, 27), (report.Days, report.DaysCounted, report.SuccessfulDays));
        Assert.True(report.FullHistory);
        Assert.Equal(0.9m, report.Consistency);
        Assert.Equal((27 * 70L, 27 * 70L), (report.Base, report.Paid));
        Assert.Equal(63m, report.AverageBase);

        // 30 good days before it: 2,100 XP is level 6. Now 4,020 is level 10, Apprentice.
        Assert.Equal((2_100L, 6, 0), (report.Before.TotalXp, report.Before.Level, report.Before.Rank));
        Assert.Equal((4_020L, 10, 1), (report.Now.TotalXp, report.Now.Level, report.Now.Rank));
        var reached = Assert.Single(report.RanksReached);
        Assert.Equal(("Apprentice", Today.AddDays(-6)), (reached.Rule.Title, reached.QualifiedOn));
        Assert.Equal((progress.Level, progress.Rank), (report.Now.Level, report.Now.Rank));
    }

    [Fact]
    public void A_week_starts_from_the_standing_the_day_before_it()
    {
        var report = SixtyDays().Report(7);

        Assert.Equal((Today.AddDays(-7), Today.AddDays(-1)), (report.From, report.To));
        Assert.Equal((7, 7, 1m, 70m), (report.DaysCounted, report.SuccessfulDays, report.Consistency, report.AverageBase));
        Assert.Equal(490, report.Paid);
        // 3,500 XP at the end of 8 days ago: level 9, still Novice.
        Assert.Equal((3_500L, 9, 0), (report.Before.TotalXp, report.Before.Level, report.Before.Rank));
        Assert.Equal((10, 1), (report.Now.Level, report.Now.Rank));
        Assert.Equal(["Apprentice"], report.RanksReached.Select(r => r.Rule.Title));
    }

    [Fact]
    public void Today_counts_once_it_has_succeeded()
    {
        var report = SixtyDays(today: 70).Report(7);

        Assert.Equal((Today.AddDays(-6), Today), (report.From, report.To));
        Assert.Equal((7, 7), (report.DaysCounted, report.SuccessfulDays));
        Assert.Equal(490, report.Base);
    }

    [Fact]
    public void A_young_domain_reports_over_the_days_it_has_and_says_how_many()
    {
        // Nine days old, with 4 days ago missed: 8 of its 9 days, not 8 of 30.
        var report = Fold(Good(Enumerable.Range(1, 9).Where(ago => ago != 4))).Report(30);

        Assert.Equal((30, 9, 8), (report.Days, report.DaysCounted, report.SuccessfulDays));
        Assert.False(report.FullHistory);
        Assert.Equal(8m / 9m, report.Consistency);
        Assert.Equal(560m / 9m, report.AverageBase);
        Assert.Equal((0L, 1, 0), (report.Before.TotalXp, report.Before.Level, report.Before.Rank));
        Assert.Equal((560L, 2), (report.Now.TotalXp, report.Now.Level));
        Assert.Empty(report.RanksReached);
    }

    [Fact]
    public void Before_the_start_nothing_counts()
    {
        var report = Fold([]).Report(7);

        Assert.Equal((Today.AddDays(-7), Today.AddDays(-1)), (report.From, report.To));
        Assert.Equal((0, 0, 0L), (report.DaysCounted, report.SuccessfulDays, report.Paid));
        Assert.Equal((0m, 0m), (report.Consistency, report.AverageBase));
        Assert.Equal((1, 1), (report.Before.Level, report.Now.Level));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fold([]).Report(0));
    }
}
