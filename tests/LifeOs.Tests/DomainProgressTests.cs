using System.Globalization;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7, D12 (xp-domains plan X3, §3.5): the fold's daily part — Health's
/// periods from its start to today, which succeeded, consistency, and the multiplier for a
/// day. Pure: each test folds days it writes itself.
/// </summary>
public sealed class DomainProgressTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    /// <summary>One day's credit, <paramref name="daysAgo"/> before <see cref="Today"/>.</summary>
    private static DomainDay Day(int daysAgo, long baseXp, long? paid = null)
        => new(Today.AddDays(-daysAgo), baseXp, paid ?? baseXp);

    /// <summary>A successful day (70 base XP).</summary>
    private static DomainDay Good(int daysAgo) => Day(daysAgo, 70);

    private static DomainProgress Fold(IEnumerable<DomainDay> days) => DomainProgress.Fold(LifeDomains.Health, days, Today);

    private static DomainProgress Fold(params DomainDay[] days) => Fold(days.AsEnumerable());

    private static decimal Number(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Fact]
    public void A_day_succeeds_on_its_base_xp_not_what_was_paid()
    {
        var progress = Fold(
            Day(3, 60, paid: 78),   // 60 base at ×1.3: still short
            Day(2, 70, paid: 70),
            Day(1, 40), Day(1, 30), // two awards on one day add up
            Day(0, 69, paid: 89));

        Assert.Equal([false, true, true, false], progress.Periods.Select(p => p.Succeeded));
        Assert.Equal((70L, 70L), (progress.Periods[2].Base, progress.Periods[2].Paid));
        Assert.Equal(78 + 70 + 70 + 89, progress.TotalXp);
        Assert.False(new MinimumBaseXp(70).Succeeded(new PeriodFacts(Today, Today, BaseXp: 69, PaidXp: 1000)));
        Assert.True(new MinimumBaseXp(70).Succeeded(new PeriodFacts(Today, Today, BaseXp: 70, PaidXp: 0)));
    }

    [Fact]
    public void Days_before_the_start_count_as_unsuccessful_and_uncovered()
    {
        // A zero day earlier on doesn't move the start: only paid XP starts a domain.
        var progress = Fold(new[] { Day(20, 0) }.Concat(Enumerable.Range(1, 9).Select(Good)));

        Assert.Equal(Today.AddDays(-9), progress.Start);
        Assert.Equal(10, progress.Periods.Count);
        Assert.Equal((Today.AddDays(-9), Today), (progress.Periods[0].Start, progress.Periods[^1].End));
        Assert.False(progress.SucceededOn(Today.AddDays(-15)));

        // The multiplier's window is the 30 complete days before today: 9 since the start.
        var window = progress.Consistency;
        Assert.Equal((Today.AddDays(-30), Today.AddDays(-1)), (window.From, window.To));
        Assert.Equal((30, 9, 9), (window.Days, window.CoveredDays, window.SuccessfulDays));
        Assert.Equal(0.3m, window.Share);
        Assert.Equal(1.0m, progress.MultiplierNow);
        Assert.Equal(new MultiplierTier(0.50m, 1.1m), progress.NextTier);
        Assert.Equal(6, window.DaysNeededFor(0.50m));
    }

    [Fact]
    public void Nothing_paid_yet_reads_as_not_started()
    {
        var progress = Fold(Day(4, 0));

        Assert.Null(progress.Start);
        Assert.Empty(progress.Periods);
        Assert.Null(progress.TodayPeriod);
        Assert.Equal((0L, 0L, false), (progress.TotalXp, progress.TodayBase, progress.TodaySucceeded));
        Assert.Equal((30, 0, 0), (progress.Consistency.Days, progress.Consistency.CoveredDays, progress.Consistency.SuccessfulDays));
        Assert.Equal(1.0m, progress.MultiplierNow);
        Assert.Equal(15, progress.Consistency.DaysNeededFor(progress.NextTier!.MinShare));
    }

    [Fact]
    public void Today_counts_toward_a_window_ending_today_once_it_succeeds()
    {
        var thirteenGoodDays = Enumerable.Range(1, 13).Select(Good).ToList();
        var notYet = Fold(thirteenGoodDays.Append(Day(0, 40)));
        var done = Fold(thirteenGoodDays.Append(Day(0, 30)).Append(Day(0, 40)));

        // Short of 70, today is left out: the window ends yesterday and today can only help.
        Assert.False(notYet.TodaySucceeded);
        Assert.Equal(40, notYet.TodayBase);
        var before = notYet.WindowEndingOn(Today, 14);
        Assert.Equal((Today.AddDays(-14), Today.AddDays(-1), 13), (before.From, before.To, before.SuccessfulDays));

        Assert.True(done.TodaySucceeded);
        var after = done.WindowEndingOn(Today, 14);
        Assert.Equal((Today.AddDays(-13), Today, 14), (after.From, after.To, after.SuccessfulDays));
        Assert.Equal(1m, after.Share);

        // The multiplier reads complete days only, so today's success doesn't move it.
        Assert.Equal(notYet.Consistency, done.Consistency);
        Assert.Equal(notYet.MultiplierNow, done.MultiplierNow);

        // A window can't end after today, and one ending earlier is taken as asked.
        Assert.Equal(Today, done.WindowEndingOn(Today.AddDays(5), 14).To);
        var earlier = done.WindowEndingOn(Today.AddDays(-3), 7);
        Assert.Equal((Today.AddDays(-9), Today.AddDays(-3), 7), (earlier.From, earlier.To, earlier.SuccessfulDays));
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
        => Assert.Equal(Number(multiplier), LifeDomains.Health.Multiplier.For(Number(share)));

    [Theory]
    [InlineData(14, "1.0")] // 46.7%
    [InlineData(15, "1.1")] // 50%
    [InlineData(20, "1.1")] // 66.7%
    [InlineData(21, "1.2")] // 70%
    [InlineData(25, "1.2")] // 83.3%
    [InlineData(26, "1.3")] // 86.7%
    public void Successful_days_in_the_last_30_pick_the_multiplier(int successfulDays, string multiplier)
    {
        // Started 30 days ago: the oldest days succeed, the rest earn a little short of 70.
        var days = Enumerable.Range(1, 30).Select(ago => ago > 30 - successfulDays ? Good(ago) : Day(ago, 10));

        var progress = Fold(days);

        Assert.Equal((30, 30, successfulDays), (progress.Consistency.Days, progress.Consistency.CoveredDays, progress.Consistency.SuccessfulDays));
        Assert.Equal(Number(multiplier), progress.MultiplierNow);
        Assert.Equal(progress.MultiplierNow, progress.MultiplierFor(Today));
    }

    [Fact]
    public void A_days_multiplier_reads_the_30_days_before_it_never_the_day_itself()
    {
        // Days -20 to -6 succeed (15); then nothing.
        var progress = Fold(Enumerable.Range(6, 15).Select(Good));

        // Day -6 is the 15th success, but its own multiplier sees only the 14 before it.
        Assert.Equal(1.0m, progress.MultiplierFor(Today.AddDays(-6)));
        Assert.Equal(1.1m, progress.MultiplierFor(Today.AddDays(-5)));
        Assert.Equal(1.0m, progress.MultiplierFor(Today.AddDays(-20)));
        var window = progress.WindowBefore(Today.AddDays(-5), 30);
        Assert.Equal((Today.AddDays(-35), Today.AddDays(-6), 15, 15), (window.From, window.To, window.CoveredDays, window.SuccessfulDays));
    }

    [Fact]
    public void Credit_dated_after_today_is_left_out()
    {
        var progress = Fold(Good(0), Day(-1, 500));

        Assert.Equal(70, progress.TotalXp);
        Assert.Equal(Today, progress.Periods[^1].End);
        Assert.False(progress.SucceededOn(Today.AddDays(1)));
        Assert.Null(Fold(Day(-2, 90)).Start);
    }

    [Fact]
    public void Only_a_daily_cadence_is_built()
    {
        var weekly = LifeDomains.Health with { Cadence = (Cadence)7 };

        Assert.Throws<NotSupportedException>(() => DomainProgress.Fold(weekly, [Good(0)], Today));
    }
}
