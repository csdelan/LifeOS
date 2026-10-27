using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7, D17 (xp-domains plan X10, §7.2): Health's lifetime curve, the rank table, and the
/// consistency formulas each level is held to — the numbers the plan's tables print, worked in
/// whole numbers so C# and the web agree to the digit. Pure.
/// </summary>
public sealed class DomainLevelTests
{
    private static readonly DomainRules Health = LifeDomains.Health;
    private static readonly ILevelCurve Curve = Health.Curve;
    private static readonly ConsistencyRule Consistency = Health.Consistency;

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 60)]
    [InlineData(3, 144)]
    [InlineData(10, 1_476)]
    [InlineData(20, 6_068)]
    [InlineData(30, 14_590)]
    [InlineData(40, 27_893)]
    [InlineData(45, 36_602)]
    [InlineData(50, 46_825)]
    [InlineData(60, 72_237)]
    [InlineData(70, 104_979)]
    [InlineData(80, 145_902)]
    [InlineData(90, 195_854)]
    [InlineData(100, 255_686)]
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
    public void The_curve_is_the_whole_number_form_of_the_cubic_and_each_level_costs_at_least_the_last()
    {
        // XP(L) = 60n + 12n(n − 1) + ⌈17n(n − 1)(n − 2) / 120⌉, n = L − 1 (§7.2).
        for (var level = 1; level <= 100; level++)
        {
            long n = level - 1;
            var expected = 60 * n + 12 * n * (n - 1) + (17 * n * (n - 1) * (n - 2) + 119) / 120;
            Assert.Equal(expected, Curve.TotalFor(level));
        }

        Assert.Equal(84, Curve.CostToNext(2));
        Assert.Equal(306, Curve.CostToNext(10));
        Assert.Equal(661, Curve.CostToNext(20));
        Assert.Equal(1_101, Curve.CostToNext(30));
        Assert.Equal(1_920, Curve.CostToNext(45));
        Assert.Equal(2_235, Curve.CostToNext(50));
        Assert.Equal(3_710, Curve.CostToNext(70));
        Assert.Equal(5_524, Curve.CostToNext(90));
        Assert.Equal(0, Curve.CostToNext(100));
        for (var level = 2; level < 100; level++)
        {
            Assert.True(Curve.CostToNext(level) >= Curve.CostToNext(level - 1));
        }

        Assert.Equal(100, Curve.MaxLevel);
        Assert.Equal(100, Curve.LevelFor(10_000_000));
        Assert.Equal(1, Curve.LevelFor(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.TotalFor(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.TotalFor(101));
    }

    [Fact]
    public void The_old_ramp_curve_stays_as_a_shape_a_domain_can_pick()
    {
        var ramp = new RampCurve(Base: 300, PerLevel: 25, RampFrom: 45, RampPerLevel: 70);

        Assert.Equal((300L, 3_600L, 254_925L), (ramp.TotalFor(2), ramp.TotalFor(10), ramp.TotalFor(100)));
        Assert.Equal(1_495, ramp.CostToNext(46));
        Assert.Equal(12, ramp.LevelFor(5_000));
    }

    [Theory]
    [InlineData(10, "Apprentice", 34, 20, 48, 1_476)]
    [InlineData(20, "Journeyman", 39, 20, 52, 6_068)]
    [InlineData(30, "Adept", 44, 20, 56, 14_590)]
    [InlineData(40, "Expert", 49, 20, 59, 27_893)]
    [InlineData(50, "Veteran", 54, 22, 63, 46_825)]
    [InlineData(60, "Elite", 60, 24, 67, 72_237)]
    [InlineData(70, "Master", 65, 26, 70, 104_979)]
    [InlineData(80, "Champion", 70, 28, 74, 145_902)]
    [InlineData(90, "Legend", 75, 30, 78, 195_854)]
    [InlineData(100, "Grandmaster", 80, 32, 81, 255_686)]
    public void Each_rank_holds_its_first_level_at_the_plans_figures(
        int level, string title, int average, int activeMinimum, int activeDaysOf90, long total)
    {
        var rank = Health.RankFor(level);

        Assert.Equal((title, level), (Health.Ranks[rank].Title, Health.Ranks[rank].StartLevel));
        Assert.Equal(average, Consistency.Average(level));
        Assert.Equal(activeMinimum, Consistency.ActiveMinimum(level));
        Assert.Equal(activeDaysOf90, Consistency.ActiveDaysNeeded(level, 90));
        Assert.Equal(total, Curve.TotalFor(level));
        Assert.Equal(Health.RankFor(level - 1) + 1, rank);
    }

    [Fact]
    public void The_formulas_run_30_to_80_20_to_32_and_half_to_nine_tenths_with_no_rounding_halves()
    {
        Assert.Equal((30, 80), (Consistency.Average(2), Consistency.Average(100)));
        Assert.Equal((20, 32), (Consistency.ActiveMinimum(2), Consistency.ActiveMinimum(100)));
        Assert.Equal((20, 21), (Consistency.ActiveMinimum(44), Consistency.ActiveMinimum(45)));
        Assert.Equal((45, 81), (Consistency.ActiveDaysNeeded(2, 90), Consistency.ActiveDaysNeeded(100, 90)));
        Assert.Equal((4, 7), (Consistency.ActiveDaysNeeded(2, 7), Consistency.ActiveDaysNeeded(100, 7)));
        Assert.Equal((3, 90, 90), (Consistency.AgeNeeded(2), Consistency.AgeNeeded(89), Consistency.AgeNeeded(100)));

        for (var level = 2; level <= 100; level++)
        {
            // A(L) = 30 + round(50(L − 2) / 98): never a half, so the rounding rule can't matter.
            Assert.NotEqual(49, 50 * (level - 2) % 98);
            Assert.Equal(30 + (int)Math.Round(50.0 * (level - 2) / 98), Consistency.Average(level));
            // M(L) = max(20, round(0.4·A)): 4A is even, so 4A / 10 never ends in a half either.
            Assert.NotEqual(5, 4 * Consistency.Average(level) % 10);

            // ⌈P(L)·N⌉ = ⌈N(490 + 4(L − 2)) / 980⌉ in integers.
            foreach (var days in new[] { 1, 7, 30, 45, 89, 90 })
            {
                var numerator = days * (490 + 4 * (level - 2));
                Assert.Equal((numerator + 979) / 980, Consistency.ActiveDaysNeeded(level, days));
            }
        }
    }

    [Fact]
    public void A_level_qualifies_only_when_all_three_tests_pass()
    {
        // Level 40 over 90 days: 49 a day on average (4,410), 59 active days, 41 active so far.
        Assert.True(Consistency.Qualifies(40, activeSoFar: 41, days: 90, windowBase: 4_410, windowActive: 59));
        Assert.False(Consistency.Qualifies(40, activeSoFar: 40, days: 90, windowBase: 4_410, windowActive: 59));
        Assert.False(Consistency.Qualifies(40, activeSoFar: 41, days: 90, windowBase: 4_409, windowActive: 59));
        Assert.False(Consistency.Qualifies(40, activeSoFar: 41, days: 90, windowBase: 4_410, windowActive: 58));
        Assert.False(Consistency.Qualifies(2, activeSoFar: 10, days: 0, windowBase: 0, windowActive: 0));
    }

    [Fact]
    public void Rank_is_the_shown_level_in_tens_and_each_rank_pays_its_gems_once_under_its_own_key()
    {
        Assert.Equal([0, 0, 1, 1, 9, 9, 10], new[] { 1, 9, 10, 19, 90, 99, 100 }.Select(Health.RankFor));
        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 1, 5], Enumerable.Range(1, 10).Select(Health.GemsFor));
        Assert.Equal("rank:health_xp:10", Health.RankAwardKey(1));
        Assert.Equal("rank:health_xp:100", Health.RankAwardKey(10));
        Assert.Equal("Health", LifeDomains.Name(Currencies.HealthXp));
    }

    [Fact]
    public void The_fall_works_in_ten_thousandths_of_a_level_and_shows_rounded_up()
    {
        var fall = Health.Fall;

        Assert.Equal(990_000, fall.Next(1_000_000));
        Assert.Equal(980_100, fall.Next(990_000));
        Assert.Equal(9_900, fall.Next(10_000));
        Assert.Equal(100, FallRule.LevelOf(1_000_000));
        Assert.Equal(100, FallRule.LevelOf(990_001));
        Assert.Equal(99, FallRule.LevelOf(990_000));
        Assert.Equal(1, FallRule.LevelOf(9_900));
    }
}
