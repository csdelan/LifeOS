using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-9, D18 (environment plan §2.3, §2.4, §4.5): Environment's level, held by the home.
/// Kept-up days are settled, non-exception days whose Home Score reaches a level's bar; the
/// condition level is the highest level whose bar enough of the last 90 such days reached, and
/// the shown level follows the lower of it and the lifetime level, as Health's does. Pure: each
/// test folds days and Home Scores it writes.
/// </summary>
public sealed class EnvironmentLevelTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DomainRules Environment = LifeDomains.Environment;

    private static DateOnly D(int index) => Start.AddDays(index);

    private static IEnumerable<DomainDay> Run(int from, int count, long xp)
        => Enumerable.Range(from, count).Select(i => new DomainDay(D(i), xp, xp));

    /// <summary>A Home Score of <paramref name="score"/> (ten-thousandths) on each day from <paramref name="from"/>.</summary>
    private static IEnumerable<KeyValuePair<DateOnly, long>> Scores(int from, int count, long score)
        => Enumerable.Range(from, count).Select(i => KeyValuePair.Create(D(i), score));

    private static DomainProgress Fold(
        IEnumerable<DomainDay> days, IEnumerable<KeyValuePair<DateOnly, long>> scores, int today, IEnumerable<int>? exceptions = null)
        => DomainProgress.Fold(Environment, days, D(today), exceptions?.Select(D), homeScores: new Dictionary<DateOnly, long>(scores));

    /// <summary>Level 51's bar, 57.5: <c>⌈(30 · 98 + 55 · 49) · 10,000 / 98⌉</c>.</summary>
    private const long Bar51 = 575_000;

    [Fact]
    public void Before_the_first_rating_XP_alone_holds_level_1()
    {
        var progress = Fold(Run(0, 30, 200), [], today: 30);

        Assert.Equal(37, progress.LifetimeLevel);
        Assert.Equal((1, 1, 0), (progress.ConsistencyLevel, progress.Level, progress.Rank));
        Assert.Equal(LevelLimit.Consistency, progress.LimitedBy);
        Assert.All(progress.Standings, s => Assert.False(s.Counted));
        Assert.All(progress.Standings, s => Assert.False(s.Active));
    }

    [Fact]
    public void Kept_up_days_lift_the_condition_level_to_the_highest_bar_they_clear()
    {
        // 100 days at 57.5 with 200 XP a day: lifetime 62, but the home holds 51.
        var progress = Fold(Run(0, 100, 200), Scores(0, 100, Bar51), today: 100);

        // Level L asks L + 1 kept-up days so far: ten days hold level 9.
        Assert.Equal(9, progress.StandingOn(D(9)).ConsistencyLevel);
        Assert.Equal(9, progress.StandingOn(D(9)).Level);
        Assert.Equal((62, 51, 51, 5), (progress.LifetimeLevel, progress.ConsistencyLevel, progress.Level, progress.Rank));
        Assert.Equal(LevelLimit.Consistency, progress.LimitedBy);
        Assert.Equal(90, progress.Window.Days);
        Assert.Equal(57.5m, progress.Window.AverageHomeScore);

        // Every settled day cleared the bar of the level going into it: all successful for the multiplier.
        Assert.All(progress.Standings.Where(s => s.Day < D(100)), s => Assert.True(s.Active, $"{s.Day} wasn't kept up"));

        // One ten-thousandth short of 57.5 holds level 50 (bar 56.9388).
        var short1 = Fold(Run(0, 100, 200), Scores(0, 100, Bar51 - 1), today: 100);
        Assert.Equal(50, short1.ConsistencyLevel);
    }

    [Theory]
    [InlineData(37, 51)] // 63 of the last 90 days at 57.5: level 51 asks 63
    [InlineData(38, 48)] // 62: level 49 asks 63, level 48 asks 62
    public void The_share_of_kept_up_days_sets_the_level(int lowDays, int level)
    {
        var scores = Scores(0, lowDays, 300_000).Concat(Scores(lowDays, 100 - lowDays, Bar51));

        var progress = Fold(Run(0, 100, 200), scores, today: 100);

        Assert.Equal(level, progress.ConsistencyLevel);
        Assert.Equal(level, progress.Level);
    }

    [Fact]
    public void Exception_days_pause_the_level_and_unmarked_low_days_let_it_fall()
    {
        // 100 kept-up days, then 40 days at the floor's 30.
        var days = Run(0, 140, 200).ToList();
        var scores = Scores(0, 100, Bar51).Concat(Scores(100, 40, 300_000)).ToList();

        var unmarked = Fold(days, scores, today: 140);
        Assert.Equal(15, unmarked.ConsistencyLevel);
        Assert.True(unmarked.Level < 51);
        Assert.Contains(unmarked.Standings, s => s.Falling);
        Assert.False(unmarked.StandingOn(D(120)).Active);

        var marked = Fold(days, scores, today: 140, exceptions: Enumerable.Range(100, 40));
        Assert.Equal((51, 51), (marked.ConsistencyLevel, marked.Level));
        Assert.All(Enumerable.Range(100, 40), i =>
        {
            var standing = marked.StandingOn(D(i));
            Assert.Equal((51, false, true), (standing.Level, standing.Counted, standing.Exception));
        });
    }

    [Fact]
    public void The_walk_starts_at_the_first_Home_Score_or_paid_XP_whichever_comes_first()
    {
        // A walk-through before any XP starts the walk: the condition climbs while lifetime waits.
        var ratedFirst = Fold(Run(5, 25, 200), Scores(0, 30, Bar51), today: 30);
        Assert.Equal(D(0), ratedFirst.Start);
        Assert.Equal((1, 4), (ratedFirst.StandingOn(D(4)).LifetimeLevel, ratedFirst.StandingOn(D(4)).ConsistencyLevel));
        Assert.Equal(1, ratedFirst.StandingOn(D(4)).Level);

        // XP before any rating: the walk starts with the XP, and the level waits for the home.
        var paidFirst = Fold(Run(0, 30, 200), Scores(10, 20, Bar51), today: 30);
        Assert.Equal(D(0), paidFirst.Start);
        Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(1, paidFirst.StandingOn(D(i)).Level));
        Assert.Equal(19, paidFirst.ConsistencyLevel);
    }
}
