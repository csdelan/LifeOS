using System.Globalization;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 / REWARD-8, D12, D17 (xp-domains plan X10, §7.4.4): the v2 fold's walk — which days
/// count, the consistency level the last 90 counted days support, today's place against the
/// active minimum, and the multiplier for a day (active at the shown level, exception days
/// skipped). Pure: each test folds days it writes itself.
/// </summary>
public sealed class DomainProgressTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DomainRules Health = LifeDomains.Health;

    /// <summary>Credit on day <paramref name="index"/> (0 is <see cref="Start"/>): base, its passive part, and paid (base unless given).</summary>
    private static DomainDay On(int index, long baseXp, long passive = 0, long? paid = null)
        => new(Start.AddDays(index), baseXp, paid ?? baseXp, passive);

    /// <summary>The same credit on <paramref name="count"/> days from day <paramref name="from"/>.</summary>
    private static IEnumerable<DomainDay> Run(int from, int count, long baseXp, long passive = 0)
        => Enumerable.Range(from, count).Select(i => On(i, baseXp, passive));

    private static DateOnly D(int index) => Start.AddDays(index);

    /// <summary>Folds as of day <paramref name="today"/>, settled through the day before unless <paramref name="settled"/> says otherwise.</summary>
    private static DomainProgress Fold(IEnumerable<DomainDay> days, int today, IEnumerable<int>? exceptions = null, int? settled = null)
        => DomainProgress.Fold(Health, days, D(today), exceptions?.Select(D), settled is { } s ? D(s) : null);

    private static decimal Number(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Fact]
    public void Nothing_paid_yet_reads_as_level_1_not_started()
    {
        var progress = Fold([On(0, 0)], today: 4);

        Assert.Null(progress.Start);
        Assert.Empty(progress.Periods);
        Assert.Empty(progress.Standings);
        Assert.Null(progress.TodayPeriod);
        Assert.Equal((1, 1, 1, 1, 0), (progress.Level, progress.LifetimeLevel, progress.ConsistencyLevel, progress.Target, progress.Rank));
        Assert.Equal((1, 0), (progress.BestLevel, progress.BestRank));
        Assert.Equal((0L, 0L, 60L), (progress.TotalXp, progress.XpIntoLevel, progress.XpToNext));
        Assert.Equal(new CountedWindow(0, null, null, 0), progress.Window);
        Assert.Equal((2, 0, 30, 3, 0), (progress.NextConsistency!.Level, progress.NextConsistency.WindowDays, progress.NextConsistency.Average, progress.NextConsistency.AgeNeeded, progress.NextConsistency.AgeNow));
        Assert.False(progress.NextConsistency.Met);
        Assert.Equal((20, false, false), (progress.ActiveMinimum, progress.TodayActive, progress.Falling));
        Assert.Equal((30, 0, 0), (progress.Consistency.Days, progress.Consistency.CoveredDays, progress.Consistency.SuccessfulDays));
        Assert.Equal(1.0m, progress.MultiplierNow);
        Assert.Null(progress.LimitedBy);
        Assert.Null(progress.LastChange);
        Assert.Null(progress.BestLevelOn);
        Assert.Empty(progress.RanksReached);
        Assert.Equal(D(3), progress.SettledThrough);
    }

    [Fact]
    public void The_walk_runs_from_the_first_paid_day_to_today_and_only_settled_non_exception_days_count()
    {
        // A zero day earlier doesn't move the start: only paid XP starts a domain.
        var progress = Fold([On(0, 0), .. Run(2, 6, 50, passive: 15)], today: 7, exceptions: [4]);

        Assert.Equal(D(2), progress.Start);
        Assert.Equal(6, progress.Periods.Count);
        Assert.Equal((D(2), D(7)), (progress.Periods[0].Start, progress.Periods[^1].End));
        Assert.Equal(35, progress.Periods[0].Action);

        // Day 4 is an exception and day 7 (today) hasn't settled.
        Assert.Equal([true, true, false, true, true, false], progress.Standings.Select(s => s.Counted));
        Assert.Equal([false, false, true, false, false, false], progress.Standings.Select(s => s.Exception));
        Assert.Equal(4, progress.Window.Days);
        Assert.Equal((D(2), D(6), 200L, 50m), (progress.Window.From!.Value, progress.Window.To!.Value, progress.Window.Base, progress.Window.Average));

        // XP on the exception day and today still counts toward lifetime XP.
        Assert.Equal(300, progress.TotalXp);
        Assert.Equal((50L, 35L), (progress.TodayBase, progress.TodayAction));
    }

    [Fact]
    public void The_settled_day_defaults_to_yesterday_and_can_never_pass_today()
    {
        var days = Run(0, 5, 50).ToList();

        Assert.Equal(D(3), Fold(days, today: 4).SettledThrough);
        Assert.Equal(D(2), Fold(days, today: 4, settled: 2).SettledThrough);
        Assert.Equal(D(4), Fold(days, today: 4, settled: 9).SettledThrough);
        Assert.Equal(4, Fold(days, today: 4).Window.Days);
        Assert.Equal(5, Fold(days, today: 4, settled: 4).Window.Days);
    }

    [Fact]
    public void Credit_dated_after_today_is_left_out()
    {
        var progress = Fold([On(0, 70), On(1, 500)], today: 0);

        Assert.Equal(70, progress.TotalXp);
        Assert.Equal(D(0), progress.Periods[^1].End);
        Assert.False(progress.ActiveOn(D(1)));
        Assert.Null(Fold([On(2, 90)], today: 0).Start);
        Assert.Equal(progress.StandingOn(D(0)) with { Day = D(9) }, progress.StandingOn(D(9)));
    }

    [Fact]
    public void Consistency_climbs_with_active_days_so_far_because_the_age_test_counts_them()
    {
        // 14 days of 1,000: the age test holds level 13 (14 active days for level 13's 14).
        var binge = Run(0, 14, 1_000).ToList();
        var after = Fold(binge, today: 14);
        Assert.Equal((13, 29, 13), (after.ConsistencyLevel, after.LifetimeLevel, after.Level));
        Assert.Equal(LevelLimit.Consistency, after.LimitedBy);
        var next = after.NextConsistency!;
        Assert.Equal((14, 15, 14), (next.Level, next.AgeNeeded, next.AgeNow));
        Assert.True(next.AverageMet && next.ActiveDaysMet);
        Assert.False(next.AgeMet);

        // Four days of nothing: the window still passes level 17's average and active days, but
        // counting any day as age would let the level climb after the binge stopped. Active days
        // so far are still 14, so it doesn't.
        var stopped = Fold(binge, today: 18);
        Assert.Equal(13, stopped.ConsistencyLevel);
        Assert.Equal(18, stopped.Window.Days);

        // Two weeks of nothing: 14 of 28 days active is level 2's half, and no more.
        Assert.Equal(2, Fold(binge, today: 28).ConsistencyLevel);
    }

    [Fact]
    public void A_level_never_passes_while_one_below_it_fails()
    {
        var random = new Random(7);
        var walker = new DomainWalker(Health);
        for (var i = 0; i < 400; i++)
        {
            var baseXp = random.Next(0, 4) == 0 ? 0 : random.Next(0, 160);
            var passive = Math.Min(baseXp, random.Next(0, 25));
            var exception = random.Next(0, 20) == 0;
            var standing = walker.Step(new DomainPeriod(D(i), D(i), baseXp, baseXp, passive), exception, settled: true);

            var met = Enumerable.Range(2, 99).Select(level => walker.NeedFor(level).Met).ToList();
            var passing = met.TakeWhile(m => m).Count();
            Assert.DoesNotContain(true, met.Skip(passing));
            Assert.Equal(1 + passing, standing.ConsistencyLevel);
        }
    }

    [Fact]
    public void A_full_window_at_the_top_supports_level_100()
    {
        // 100 days of 3,000: lifetime XP passes 255,686 and the last 90 days support 100.
        var progress = Fold(Run(0, 100, 3_000), today: 100);

        Assert.Equal((100, 100, 100, 100, 10), (progress.LifetimeLevel, progress.ConsistencyLevel, progress.Target, progress.Level, progress.Rank));
        Assert.Equal((100, 10, "Grandmaster"), (progress.BestLevel, progress.BestRank, progress.BestRankRule.Title));
        Assert.Null(progress.NextConsistency);
        Assert.Equal((0L, 300_000 - 255_686L), (progress.XpToNext, progress.XpIntoLevel));
        Assert.Equal(90, progress.Window.Days);
        Assert.Null(progress.LimitedBy);

        // Level 100 first qualified on day 89: the 90th active day.
        Assert.Equal(D(89), progress.BestLevelOn);
        Assert.Equal(Enumerable.Range(1, 10), progress.RanksReached.Select(r => r.Rank));
        Assert.Equal(D(89), progress.RanksReached[^1].QualifiedOn);
    }

    [Fact]
    public void A_day_is_active_on_its_action_xp_at_the_level_going_into_it()
    {
        // At level 1 an active day takes 20 action XP: sleep doesn't count toward it.
        Assert.True(Fold([On(0, 40, passive: 20)], today: 0).TodayActive);
        Assert.False(Fold([On(0, 40, passive: 21)], today: 0).TodayActive);

        // At Grandmaster it takes 32: 25 action XP isn't enough, though it is at level 1.
        var top = Run(0, 100, 3_000).ToList();
        var day = Fold([.. top, On(100, 25)], today: 100);
        Assert.Equal((32, false), (day.ActiveMinimum, day.TodayActive));
        Assert.True(Fold([On(0, 25)], today: 0).TodayActive);
        Assert.True(Fold([.. top, On(100, 32)], today: 100).TodayActive);
        Assert.Equal("at least 32 action XP (base XP less sleep)", Health.Success.Describe(day.Level));
    }

    [Theory]
    [InlineData("0", "1.0")]
    [InlineData("0.49", "1.0")]
    [InlineData("0.4999", "1.0")]
    [InlineData("0.50", "1.1")]
    [InlineData("0.69", "1.1")]
    [InlineData("0.70", "1.2")]
    [InlineData("0.84", "1.2")]
    [InlineData("0.85", "1.3")]
    [InlineData("1", "1.3")]
    public void The_multiplier_tiers_start_at_50_70_and_85_percent(string share, string multiplier)
        => Assert.Equal(Number(multiplier), Health.Multiplier.For(Number(share)));

    [Theory]
    [InlineData(14, "1.0")] // 46.7%
    [InlineData(15, "1.1")] // 50%
    [InlineData(20, "1.1")] // 66.7%
    [InlineData(21, "1.2")] // 70%
    [InlineData(25, "1.2")] // 83.3%
    [InlineData(26, "1.3")] // 86.7%
    public void Active_days_in_the_last_30_pick_the_multiplier(int activeDays, string multiplier)
    {
        // 30 days: the first ones active, the rest with no action XP (sleep only).
        var days = Enumerable.Range(0, 30).Select(i => i < activeDays ? On(i, 80) : On(i, 20, passive: 20));

        var progress = Fold(days, today: 30);

        Assert.Equal((30, 30, activeDays), (progress.Consistency.Days, progress.Consistency.CoveredDays, progress.Consistency.SuccessfulDays));
        Assert.Equal(Number(multiplier), progress.MultiplierNow);
        Assert.Equal(progress.MultiplierNow, progress.MultiplierFor(D(30)));
    }

    [Fact]
    public void Days_before_the_start_count_as_not_active()
    {
        var progress = Fold(Run(0, 9, 80), today: 9);

        var window = progress.Consistency;
        Assert.Equal((D(-21), D(8)), (window.From, window.To));
        Assert.Equal((30, 9, 9), (window.Days, window.CoveredDays, window.SuccessfulDays));
        Assert.Equal(1.0m, progress.MultiplierNow);
        Assert.Equal(new MultiplierTier(0.50m, 1.1m), progress.NextTier);
        Assert.Equal(6, window.DaysNeededFor(0.50m));
    }

    [Fact]
    public void A_days_multiplier_reads_the_30_days_before_it_never_the_day_itself()
    {
        // Days 0–14 active (15); then nothing.
        var progress = Fold(Run(0, 15, 80), today: 25);

        Assert.Equal(1.0m, progress.MultiplierFor(D(14)));
        Assert.Equal(1.1m, progress.MultiplierFor(D(15)));
        Assert.Equal(1.0m, progress.MultiplierFor(D(0)));
        var window = progress.WindowBefore(D(15));
        Assert.Equal((D(-15), D(14), 15, 15), (window.From, window.To, window.CoveredDays, window.SuccessfulDays));
    }

    [Fact]
    public void The_multiplier_skips_exception_days_and_reaches_back_one_more_day_for_each()
    {
        // 30 active days, then 10 days with nothing.
        var days = Run(0, 30, 80).ToList();
        var marked = Fold(days, today: 40, exceptions: Enumerable.Range(30, 10));
        var unmarked = Fold(days, today: 40);

        Assert.Equal((D(0), D(39), 30, 30), (marked.Consistency.From, marked.Consistency.To, marked.Consistency.CoveredDays, marked.Consistency.SuccessfulDays));
        Assert.Equal(1.3m, marked.MultiplierNow);
        Assert.Equal(20, unmarked.Consistency.SuccessfulDays);
        Assert.Equal(1.1m, unmarked.MultiplierNow);

        // An exception day is never active, whatever it earned.
        var earned = Fold([.. days, On(30, 500)], today: 31, exceptions: [30]);
        Assert.False(earned.ActiveOn(D(30)));
        Assert.Equal(1.3m, earned.MultiplierNow);
    }

    [Fact]
    public void The_walker_prices_the_next_day_as_the_fold_reads_it()
    {
        var random = new Random(3);
        var walker = new DomainWalker(Health);
        var days = new List<DomainDay>();
        var exceptions = new List<int>();
        var started = false;
        for (var i = 0; i < 120; i++)
        {
            var baseXp = random.Next(0, 5) == 0 ? 0 : random.Next(20, 150);
            var exception = i > 0 && random.Next(0, 12) == 0;
            if (exception)
            {
                exceptions.Add(i);
            }

            // The walk starts on the first paid day, as the fold's does.
            var paid = MultiplierRule.Apply(baseXp, walker.NextMultiplier());
            days.Add(On(i, baseXp, passive: Math.Min(baseXp, 15), paid: paid));
            started |= paid > 0;
            if (started)
            {
                walker.Step(new DomainPeriod(D(i), D(i), baseXp, paid, Math.Min(baseXp, 15)), exception, settled: true);
            }
        }

        var progress = Fold(days, today: 119, exceptions, settled: 119);
        for (var i = 0; i < 120; i++)
        {
            Assert.Equal(MultiplierRule.Apply(days[i].Base, progress.MultiplierFor(D(i))), days[i].Paid);
        }

        Assert.Equal(progress.Level, walker.Level);
    }

    [Fact]
    public void Only_a_daily_cadence_is_built()
    {
        var weekly = Health with { Cadence = (Cadence)7 };

        Assert.Throws<NotSupportedException>(() => DomainProgress.Fold(weekly, [On(0, 70)], D(0)));
    }
}
