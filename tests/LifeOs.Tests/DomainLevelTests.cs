using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7, D12 (xp-domains plan X4, §2.1, §3.5): Health's level curve, the cap
/// at the top of the rank held with the XP beyond it banked, and the rank walk — chained ranks,
/// the age check, no regression from inactivity, and a retune re-reading the same days. Pure:
/// each test folds days it writes itself.
/// </summary>
public sealed class DomainLevelTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly ILevelCurve Curve = LifeDomains.Health.Curve;

    /// <summary>One day's credit (base and paid alike), <paramref name="daysAgo"/> before <see cref="Today"/>.</summary>
    private static DomainDay Day(int daysAgo, long xp) => new(Today.AddDays(-daysAgo), xp, xp);

    /// <summary>The same credit on each day from <paramref name="fromAgo"/> down to <paramref name="toAgo"/> days ago.</summary>
    private static IEnumerable<DomainDay> Days(int fromAgo, int toAgo, long xp)
        => Enumerable.Range(toAgo, fromAgo - toAgo + 1).Select(ago => Day(ago, xp));

    private static DomainProgress Fold(IEnumerable<DomainDay> days, DomainRules? rules = null)
        => DomainProgress.Fold(rules ?? LifeDomains.Health, days, Today);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 300)]
    [InlineData(10, 3_600)]
    [InlineData(20, 9_975)]
    [InlineData(30, 18_850)]
    [InlineData(45, 36_850)]
    [InlineData(46, 38_250)]
    [InlineData(50, 44_800)]
    [InlineData(70, 100_350)]
    [InlineData(90, 193_900)]
    [InlineData(100, 254_925)]
    public void Healths_curve_reaches_each_level_at_the_plans_totals(int level, long total)
    {
        Assert.Equal(total, Curve.TotalFor(level));
        Assert.Equal(level, Curve.LevelFor(total));
        if (level > 1)
        {
            Assert.Equal(level - 1, Curve.LevelFor(total - 1));
        }
    }

    [Fact]
    public void Each_level_costs_300_plus_25_a_level_then_70_more_a_level_after_45()
    {
        Assert.Equal(300, Curve.CostToNext(1));
        Assert.Equal(325, Curve.CostToNext(2));
        Assert.Equal(1_400, Curve.CostToNext(45));
        Assert.Equal(1_495, Curve.CostToNext(46)); // the ramp starts
        Assert.Equal(6_530, Curve.CostToNext(99));
        Assert.Equal(0, Curve.CostToNext(100));
        for (var level = 1; level < 100; level++)
        {
            Assert.Equal(Curve.TotalFor(level + 1) - Curve.TotalFor(level), Curve.CostToNext(level));
        }

        Assert.Equal(100, Curve.MaxLevel);
        Assert.Equal(100, Curve.LevelFor(10_000_000));
        Assert.Equal(1, Curve.LevelFor(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.TotalFor(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.TotalFor(101));
    }

    [Fact]
    public void Before_any_xp_a_domain_is_a_level_1_novice()
    {
        var progress = Fold([]);

        Assert.Equal((1, 1, 0, 9), (progress.Level, progress.RawLevel, progress.Rank, progress.TopLevel));
        Assert.Equal("Novice", progress.RankRule.Title);
        Assert.Equal((0L, 300L, 0L), (progress.XpIntoLevel, progress.XpToNext, progress.BankedXp));
        Assert.False(progress.Capped);
        Assert.Null(progress.RankQualifiedOn);
        Assert.Empty(progress.RanksReached);
        Assert.Equal(new DomainStanding(Today, 0, 1, 1, 0), progress.StandingOn(Today));
        var next = progress.Next!;
        Assert.Equal(("Apprentice", 0, false, false), (next.Rule.Title, next.DaysOld, next.LevelReached, next.OldEnough));
        Assert.Equal((7, 7), (next.Required, next.DaysNeeded));
    }

    [Fact]
    public void Xp_ahead_of_the_next_rank_stops_the_level_at_the_ranks_top_and_banks_the_rest()
    {
        // 5,000 XP on day one: raw level 12, but Apprentice needs a domain 14 days old.
        var progress = Fold([Day(0, 5_000)]);

        Assert.Equal((12, 9, 0), (progress.RawLevel, progress.Level, progress.Rank));
        Assert.True(progress.Capped);
        Assert.Equal(Curve.CostToNext(9), progress.XpIntoLevel); // the level's bar is full
        Assert.Equal(0, progress.XpToNext);
        Assert.Equal(5_000 - 3_600, progress.BankedXp);          // beyond what level 10 takes
        var next = progress.Next!;
        Assert.True(next.LevelReached);
        Assert.False(next.OldEnough);
        Assert.Equal(1, next.DaysOld);
        Assert.Equal((1, 6), (next.Window.SuccessfulDays, next.DaysNeeded));
    }

    [Fact]
    public void A_rank_waits_for_a_domain_as_old_as_its_window_then_the_level_jumps()
    {
        // Level and share are met from the first day; the 14-day window first fits on day 14.
        var progress = Fold(new[] { Day(20, 5_000) }.Concat(Days(19, 0, 70)));

        var apprentice = Assert.Single(progress.RanksReached);
        Assert.Equal((1, Today.AddDays(-7)), (apprentice.Rank, apprentice.QualifiedOn));
        Assert.Equal(9, progress.StandingOn(Today.AddDays(-8)).Level);
        Assert.Equal(new DomainStanding(Today.AddDays(-7), 5_000 + 13 * 70, 14, 14, 1), progress.StandingOn(Today.AddDays(-7)));
        Assert.Equal((14, 14, 1, false), (progress.Level, progress.RawLevel, progress.Rank, progress.Capped));
        Assert.Equal(Today.AddDays(-7), progress.RankQualifiedOn);
        Assert.Equal(19, progress.TopLevel);
    }

    [Fact]
    public void Several_ranks_can_be_entered_on_one_day()
    {
        // 29 successful days, then a day that lifts the level past 20: Apprentice's and
        // Journeyman's windows are both already full, so both open that day.
        var progress = Fold(Days(29, 1, 70).Append(Day(0, 10_000)));

        Assert.Equal(
            [(1, Today), (2, Today)],
            progress.RanksReached.Select(r => (r.Rank, r.QualifiedOn)));
        Assert.Equal((22, 22, "Journeyman"), (progress.RawLevel, progress.Level, progress.RankRule.Title));
        Assert.Equal("Adept", progress.Next!.Rule.Title);
        Assert.False(progress.Next.LevelReached);
    }

    [Fact]
    public void A_year_at_the_top_reaches_grandmaster_and_level_100_with_nothing_banked()
    {
        // 700 a day: level 90 arrives on day 277, but Legend's 365-day window first fits on
        // day 365 — the day level 100 arrives too.
        var progress = Fold(Days(399, 0, 700));

        Assert.Equal(Enumerable.Range(1, 10), progress.RanksReached.Select(r => r.Rank));
        var legend = progress.RanksReached[^2];
        var grandmaster = progress.RanksReached[^1];
        Assert.Equal(Today.AddDays(-399 + 364), legend.QualifiedOn);
        Assert.Equal(legend.QualifiedOn, grandmaster.QualifiedOn);
        Assert.Equal((100, 100, 10, false), (progress.Level, progress.RawLevel, progress.Rank, progress.Capped));
        Assert.Equal(400 * 700 - 254_925, progress.XpIntoLevel);
        Assert.Equal((0L, 0L), (progress.XpToNext, progress.BankedXp));
        Assert.Null(progress.Next);
    }

    [Fact]
    public void Inactivity_never_lowers_a_rank_or_a_level()
    {
        // Apprentice on day 14, then 60 days of nothing.
        var progress = Fold(new[] { Day(79, 5_000) }.Concat(Days(78, 60, 70)));

        Assert.Equal(1, progress.Rank);
        Assert.Equal(Today.AddDays(-66), progress.RankQualifiedOn);
        Assert.Equal(progress.StandingOn(Today.AddDays(-60)) with { Day = Today }, progress.StandingOn(Today));
        Assert.Equal(0, progress.Consistency.SuccessfulDays);
        Assert.Equal(1.0m, progress.MultiplierNow);
        Assert.False(progress.Next!.BarMet);
    }

    [Fact]
    public void A_retuned_rule_re_reads_the_same_days()
    {
        // Half the last 14 days succeed: enough for Apprentice's 50%, not for a 60% bar.
        var days = Days(13, 0, 70).Select((d, i) => i % 2 == 0 ? d : d with { Base = 10, Paid = 10 })
            .Prepend(Day(14, 4_000))
            .ToList();
        var stricter = LifeDomains.Health with
        {
            Ranks = [.. LifeDomains.Health.Ranks.Select((r, i) => i == 1 ? r with { MinConsistency = 0.6m } : r)]
        };
        var cheaper = LifeDomains.Health with { Curve = new RampCurve(100, 0, 100, 0) };

        Assert.Equal(1, Fold(days).Rank);
        Assert.Equal(0, Fold(days, stricter).Rank);
        Assert.Equal(9, Fold(days, stricter).Level);
        Assert.Equal(Curve.LevelFor(Fold(days).TotalXp), Fold(days).RawLevel);
        Assert.Equal(Fold(days).TotalXp / 100 + 1, Fold(days, cheaper).RawLevel);
    }

    [Fact]
    public void Today_opens_a_rank_gate_only_once_it_has_succeeded()
    {
        // 13 days old before today, with the level and the share in hand.
        var history = new[] { Day(13, 4_000) }.Concat(Days(12, 1, 70)).ToList();

        var notYet = Fold(history.Append(Day(0, 50)));
        var done = Fold(history.Append(Day(0, 70)));

        Assert.Equal(0, notYet.Rank);
        Assert.Equal((13, false), (notYet.Next!.DaysOld, notYet.Next.OldEnough));
        Assert.Equal(1, done.Rank);
        Assert.Equal(Today, done.RankQualifiedOn);
    }

    [Fact]
    public void A_rank_that_lists_a_milestone_waits_and_so_does_every_rank_above_it()
    {
        var gated = LifeDomains.Health with
        {
            Ranks = [.. LifeDomains.Health.Ranks.Select((r, i) => i == 1 ? r with { Milestones = ["Run a 10K"] } : r)]
        };

        var progress = Fold(Days(29, 1, 70).Append(Day(0, 10_000)), gated);

        Assert.Equal((0, 9), (progress.Rank, progress.Level));
        Assert.Empty(progress.RanksReached);
    }

    [Fact]
    public void Each_rank_tops_out_below_the_next_and_pays_its_gems_once_under_its_own_key()
    {
        var rules = LifeDomains.Health;

        Assert.Equal([9, 19, 29, 39, 49, 59, 69, 79, 89, 99, 100], Enumerable.Range(0, 11).Select(rules.TopLevelOf));
        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 1, 5], Enumerable.Range(1, 10).Select(rules.GemsFor));
        Assert.Equal("rank:health_xp:10", rules.RankAwardKey(1));
        Assert.Equal("rank:health_xp:100", rules.RankAwardKey(10));
        Assert.Equal("Health", LifeDomains.Name(Currencies.HealthXp));
    }
}
