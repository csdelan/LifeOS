using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 / REWARD-8 (xp-domains plan X10, §7.4.4): a configured domain over its last N
/// calendar days, ending at the last settled day so an unfinished day never counts. It counts
/// the days that counted, the exception days and the active days, and gives the level and rank
/// at the start against the end, the best rank, the ranks first reached and every day's
/// standing. Pure: each test folds days it writes itself.
/// </summary>
public sealed class DomainReportTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static DateOnly D(int index) => Start.AddDays(index);

    private static IEnumerable<DomainDay> Run(int from, int count, long baseXp, long passive = 0)
        => Enumerable.Range(from, count).Select(i => new DomainDay(D(i), baseXp, baseXp, passive));

    private static DomainProgress Fold(IEnumerable<DomainDay> days, int today, IEnumerable<int>? exceptions = null)
        => DomainProgress.Fold(LifeDomains.Health, days, D(today), exceptions?.Select(D));

    /// <summary>
    /// 60 days of Health (days 0–59): 80 base XP a day, 20 of it sleep, except days 40–42 with
    /// sleep only; days 50–51 marked as exceptions; 500 XP so far today (day 60).
    /// </summary>
    private static DomainProgress SixtyDays()
        => Fold(
            Run(0, 60, 80, passive: 20).Select(d => d.Day >= D(40) && d.Day <= D(42) ? d with { Base = 20, Paid = 20 } : d)
                .Append(new DomainDay(D(60), 500, 500)),
            today: 60,
            exceptions: [50, 51]);

    [Fact]
    public void A_month_ends_at_the_last_settled_day_and_counts_what_counted()
    {
        var progress = SixtyDays();
        var report = progress.Report(30);

        // Today hasn't settled, so the 30 days end yesterday and its 500 XP isn't in them.
        Assert.Equal((D(30), D(59)), (report.From, report.To));
        Assert.Equal((30, 28, 2), (report.Days, report.DaysCounted, report.ExceptionDays));
        Assert.Equal(25, report.ActiveDays);
        Assert.Equal(25m / 28m, report.ActiveShare);
        Assert.Equal(25 * 80L + 3 * 20L, report.CountedBase);
        Assert.Equal((2_060m / 28m), report.AverageBase);

        // Base and paid count the exception days too: XP on them still counts toward lifetime.
        Assert.Equal((2_220L, 2_220L), (report.Base, report.Paid));

        Assert.Equal(progress.StandingOn(D(29)), report.Before);
        Assert.Equal(progress.StandingOn(D(59)), report.End);
        Assert.Equal(report.End.BestRank, report.BestRank);
        Assert.Equal(30, report.Standings.Count);
        Assert.Equal(Enumerable.Range(30, 30).Select(D), report.Standings.Select(s => s.Day));
        Assert.Equal([true, true], report.Standings.Where(s => s.Exception).Select(s => s.Day >= D(50) && s.Day <= D(51)));
        Assert.All(report.RanksReached, r => Assert.InRange(r.QualifiedOn, report.From, report.To));
        Assert.Equal(
            progress.RanksReached.Where(r => r.QualifiedOn >= D(30) && r.QualifiedOn <= D(59)),
            report.RanksReached);
    }

    [Fact]
    public void Ranks_first_reached_in_the_period_are_listed_and_earlier_ones_are_not()
    {
        var progress = SixtyDays();
        var month = progress.Report(30);
        var all = progress.Report(60);

        Assert.True(progress.RanksReached.Count > 0);
        Assert.Equal(progress.RanksReached, all.RanksReached);
        Assert.True(month.RanksReached.Count < all.RanksReached.Count);
        Assert.Equal((1, 1), (all.Before.Level, all.Before.Rank + 1));
        Assert.Equal(progress.StandingOn(D(59)).Level, all.End.Level);
    }

    [Fact]
    public void A_young_domain_reports_over_the_days_it_has()
    {
        // Nine days old: the 30 days start before it, and those days count for nothing.
        var report = Fold(Run(0, 9, 80), today: 9).Report(30);

        Assert.Equal((D(-21), D(8)), (report.From, report.To));
        Assert.Equal((30, 9, 9, 0), (report.Days, report.DaysCounted, report.ActiveDays, report.ExceptionDays));
        Assert.Equal(80m, report.AverageBase);
        Assert.Equal((0L, 1, 0), (report.Before.TotalXp, report.Before.Level, report.Before.Rank));
        Assert.Equal(720L, report.End.TotalXp);
        Assert.Equal(1, report.Standings[0].Level);
        Assert.False(report.Standings[0].Counted);
    }

    [Fact]
    public void Before_the_start_nothing_counts()
    {
        var progress = Fold([], today: 9);
        var report = progress.Report(7);

        Assert.Equal((D(2), D(8)), (report.From, report.To));
        Assert.Equal((0, 0, 0L), (report.DaysCounted, report.ActiveDays, report.Paid));
        Assert.Equal((0m, 0m), (report.ActiveShare, report.AverageBase));
        Assert.Equal((1, 1), (report.Before.Level, report.End.Level));
        Assert.Equal(7, report.Standings.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => progress.Report(0));
    }
}
