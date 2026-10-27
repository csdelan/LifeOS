using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 / REWARD-8, D17 (xp-domains plan X10, §7.2, §7.3): the shown level. It rises to the
/// target at once, falls 1% of itself a day toward a lower one and never below it, pauses while
/// the last 7 counted days are back on track and on exception days, never falls on a day that
/// hasn't settled, and the best level and rank never fall. Pure: each test folds days it writes.
/// </summary>
public sealed class DomainShownLevelTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DomainRules Health = LifeDomains.Health;

    private static DateOnly D(int index) => Start.AddDays(index);

    private static IEnumerable<DomainDay> Run(int from, int count, long baseXp, long passive = 0)
        => Enumerable.Range(from, count).Select(i => new DomainDay(D(i), baseXp, baseXp, passive));

    /// <summary>100 days of 3,000 XP (days 0–99): level 100, Grandmaster, from day 89.</summary>
    private static readonly IReadOnlyList<DomainDay> Grandmaster = Run(0, 100, 3_000).ToList();

    private static DomainProgress Fold(IEnumerable<DomainDay> days, int today, IEnumerable<int>? exceptions = null, int? settled = null)
        => DomainProgress.Fold(Health, days, D(today), exceptions?.Select(D), settled is { } s ? D(s) : null);

    /// <summary>The first day from <paramref name="from"/> whose standing passes <paramref name="test"/>.</summary>
    private static int FirstDay(DomainProgress progress, int from, Func<DomainStanding, bool> test)
        => progress.Standings.Select((s, i) => (s, i)).First(x => x.i >= from && test(x.s)).i;

    [Fact]
    public void From_level_100_with_nothing_logged_the_level_falls_on_the_plans_timeline()
    {
        var progress = Fold(Grandmaster, today: 360);

        // Raw consistency holds 9 days (90 days × 10% missable): the fall starts on day 10.
        Assert.Equal(100, progress.StandingOn(D(99 + 9)).Level);
        var day10 = progress.StandingOn(D(99 + 10));
        Assert.Equal((97, 99, 9, true), (day10.ConsistencyLevel, day10.Level, day10.Rank, day10.Falling));
        Assert.Equal(990_000, day10.ShownExact);

        // Legend day 10, Champion 21, Master 33, Elite 46, Novice 249 (§7.3).
        Assert.Equal(99 + 10, FirstDay(progress, 100, s => s.Rank <= 9));
        Assert.Equal(99 + 21, FirstDay(progress, 100, s => s.Rank <= 8));
        Assert.Equal(99 + 33, FirstDay(progress, 100, s => s.Rank <= 7));
        Assert.Equal(99 + 46, FirstDay(progress, 100, s => s.Rank <= 6));
        Assert.Equal(99 + 249, FirstDay(progress, 100, s => s.Rank == 0));

        // Each day of the fall takes 1% of the level, in ten-thousandths, rounded down.
        for (var i = 100 + 10; i < 340; i++)
        {
            var (before, after) = (progress.StandingOn(D(i - 1)), progress.StandingOn(D(i)));
            Assert.Equal(Math.Max(after.Target * 10_000L, before.ShownExact * 99 / 100), after.ShownExact);
        }

        // The best never falls.
        Assert.All(progress.Standings.Skip(89), s => Assert.Equal((100, 10), (s.BestLevel, s.BestRank)));
        Assert.Equal((100, 10), (progress.BestLevel, progress.BestRank));
    }

    [Fact]
    public void The_level_never_falls_below_the_target_and_reports_the_fall()
    {
        // Ten days off: consistency drops to 97, and the level falls to it in four days, then holds.
        var progress = Fold([.. Grandmaster, .. Run(110, 30, 3_000)], today: 140);

        Assert.Equal([99, 99, 98, 97, 97], Enumerable.Range(109, 5).Select(i => progress.StandingOn(D(i)).Level));
        Assert.Equal(970_000, progress.StandingOn(D(112)).ShownExact);
        Assert.All(progress.Standings, s => Assert.True(s.Level >= s.Target, $"{s.Day}: level {s.Level} below target {s.Target}"));
        Assert.Equal((97, 97, false, false), (progress.Level, progress.Target, progress.Falling, progress.Paused));
        Assert.Equal(new LevelChange(D(112), 98, 97), progress.LastChange);
        Assert.Null(progress.FallingSince);
        Assert.Equal((100, 10), (progress.BestLevel, progress.BestRank));
    }

    [Fact]
    public void Seven_counted_days_back_on_track_pause_the_fall()
    {
        // 30 days off: consistency drops to 42, far below the level. Back at full effort, the
        // last 7 days pass the level's own tests once 6 of them are active (its level asks 6
        // of 7, with no age test), and the fall pauses there.
        var progress = Fold([.. Grandmaster, .. Run(130, 70, 3_000)], today: 200);

        Assert.All(Enumerable.Range(130, 5), i => Assert.True(progress.StandingOn(D(i)).Falling, $"day {i} should fall"));
        var paused = progress.StandingOn(D(135));
        Assert.True(paused.Paused);
        Assert.False(paused.Falling);
        Assert.Equal(progress.StandingOn(D(134)).ShownExact, paused.ShownExact);
        Assert.Equal(6, LifeDomains.HealthConsistency.ActiveDaysNeeded(paused.Level, 7));
        Assert.Equal(42, paused.Target);
        Assert.All(Enumerable.Range(135, 65), i =>
        {
            var s = progress.StandingOn(D(i));
            Assert.True(s.Paused);
            Assert.Equal(paused.ShownExact, s.ShownExact);
        });

        Assert.True(progress.Paused);
        Assert.False(progress.Falling);
        Assert.Equal(D(109), progress.FallingSince);
        Assert.Equal(100, progress.FallingFrom);
        Assert.Equal(LevelLimit.Consistency, progress.LimitedBy);
    }

    [Fact]
    public void A_marked_lapse_leaves_level_and_rank_unchanged()
    {
        // The same 30 days off, marked as exception days.
        var days = new List<DomainDay>([.. Grandmaster, .. Run(130, 70, 3_000)]);
        var progress = Fold(days, today: 200, exceptions: Enumerable.Range(100, 30));

        Assert.All(progress.Standings.Skip(89), s => Assert.Equal((100, 10, 100), (s.Level, s.Rank, s.ConsistencyLevel)));
        Assert.All(progress.Standings.Skip(100).Take(30), s => Assert.True(s.Exception && !s.Counted && !s.Falling));
        Assert.Equal((100, 100), (progress.Level, progress.Target));

        // The window reaches back past the marked days.
        Assert.Equal(90, progress.Window.Days);
        Assert.Equal(D(80), progress.Window.From);
    }

    [Fact]
    public void An_exception_day_pauses_a_fall_already_under_way()
    {
        // Fifteen days off (the fall starts on day 10 of them), then five marked days, then one more off.
        var progress = Fold(Grandmaster, today: 125, exceptions: Enumerable.Range(115, 5));

        var lastFall = progress.StandingOn(D(114));
        Assert.True(lastFall.Falling);
        Assert.All(Enumerable.Range(115, 5), i =>
        {
            var s = progress.StandingOn(D(i));
            Assert.True(s.Paused && s.Exception);
            Assert.Equal(lastFall.ShownExact, s.ShownExact);
            Assert.Equal(lastFall.ConsistencyLevel, s.ConsistencyLevel);
        });
        Assert.True(progress.StandingOn(D(120)).Falling);
        Assert.True(progress.StandingOn(D(120)).ShownExact < lastFall.ShownExact);

        // Today marked: the fall reads as paused.
        var markedToday = Fold(Grandmaster, today: 125, exceptions: [125]);
        Assert.True(markedToday.TodayException);
        Assert.True(markedToday.Paused);
        Assert.False(markedToday.Falling);
    }

    [Fact]
    public void A_day_that_hasnt_settled_never_lowers_the_level()
    {
        // Day 109 is the tenth day off: the day the fall would start.
        Assert.Equal(100, Fold(Grandmaster, today: 109).Level);
        Assert.Equal(99, Fold(Grandmaster, today: 109, settled: 109).Level);

        // Before 4 a.m. yesterday hasn't settled either.
        var early = Fold(Grandmaster, today: 110, settled: 108);
        Assert.Equal(100, early.Level);
        Assert.False(early.StandingOn(D(109)).Counted);
        Assert.Equal(99, Fold(Grandmaster, today: 110).Level);

        // Unsettled, the level reads as falling toward the target it would reach.
        Assert.True(Fold(Grandmaster, today: 112).Falling);
    }

    [Fact]
    public void The_level_rises_to_the_target_at_once_even_on_an_unsettled_day()
    {
        // 90 days of 60 base XP (50 of it action): the last 90 days support level 61, but
        // lifetime XP only reaches 18.
        var steady = Run(0, 90, 60, passive: 10).ToList();
        var before = Fold(steady, today: 90);
        Assert.Equal((18, 61, 18), (before.LifetimeLevel, before.ConsistencyLevel, before.Level));
        Assert.Equal(LevelLimit.Lifetime, before.LimitedBy);

        // A huge day today lifts lifetime XP past level 70: the level goes straight to 61.
        var after = Fold([.. steady, new DomainDay(D(90), 100_000, 100_000)], today: 90);
        Assert.Equal((70, 61, 61, 61), (after.LifetimeLevel, after.ConsistencyLevel, after.Target, after.Level));
        Assert.Equal(LevelLimit.Consistency, after.LimitedBy);
        Assert.Equal(new LevelChange(D(90), 18, 61), after.LastChange);
        Assert.Equal(D(90), after.BestLevelOn);
        Assert.Equal([1, 2, 3, 4, 5, 6], after.RanksReached.Select(r => r.Rank));
        Assert.All(after.RanksReached.Skip(1), r => Assert.Equal(D(90), r.QualifiedOn));
    }

    [Fact]
    public void Best_level_and_rank_never_fall()
    {
        var random = new Random(11);
        var days = Enumerable.Range(0, 600)
            .Select(i => (i / 60) % 3 == 2 ? new DomainDay(D(i), 0, 0) : new DomainDay(D(i), random.Next(40, 400), random.Next(40, 400), 15))
            .ToList();
        var progress = Fold(days, today: 600);

        for (var i = 1; i < progress.Standings.Count; i++)
        {
            var (before, after) = (progress.Standings[i - 1], progress.Standings[i]);
            Assert.True(after.BestLevel >= before.BestLevel);
            Assert.True(after.BestRank >= before.BestRank);
            Assert.True(after.BestLevel >= after.Level);
            Assert.Equal(Health.RankFor(after.BestLevel), after.BestRank);
        }

        Assert.Contains(progress.Standings, s => s.Level < s.BestLevel);
    }
}
