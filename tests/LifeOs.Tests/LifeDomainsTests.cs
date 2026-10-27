using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7 / REWARD-8 (xp-domains plan X3, §3.2, §7.4.1): the drift test for
/// <see cref="LifeDomains"/>. Rules live in code, so these checks are what keep a retune honest:
/// every configured domain reads a tracked life-domain currency, ranks start every 10 levels from
/// 1 to 100, what consistency asks only rises with the level, the curve never gets cheaper, a
/// better share never earns a smaller multiplier, and passive XP comes from known award triggers.
/// </summary>
public sealed class LifeDomainsTests
{
    public static TheoryData<string> ConfiguredCurrencies()
        => new(LifeDomains.Configured.Select(r => r.Currency));

    [Fact]
    public void Every_configured_domain_reads_a_life_domain_currency_and_none_is_configured_twice()
    {
        Assert.NotEmpty(LifeDomains.Configured);
        Assert.All(LifeDomains.Configured, rules =>
        {
            var currency = Currencies.Find(rules.Currency);
            Assert.NotNull(currency);
            Assert.True(currency.LifeDomain, $"{rules.Currency} isn't a life domain");
            Assert.False(currency.Spendable);
        });
        Assert.Equal(
            LifeDomains.Configured.Count,
            LifeDomains.Configured.Select(r => r.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(ConfiguredCurrencies))]
    public void Ranks_start_every_10_levels_from_1_to_100(string currency)
    {
        var rules = LifeDomains.For(currency)!;
        var ranks = rules.Ranks;

        Assert.Equal([1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100], ranks.Select(r => r.StartLevel));
        Assert.Equal(ranks.Count, ranks.Select(r => r.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (var level = 1; level <= rules.Curve.MaxLevel; level++)
        {
            Assert.Equal(Math.Min(10, level / 10), rules.RankFor(level));
        }
    }

    [Theory]
    [MemberData(nameof(ConfiguredCurrencies))]
    public void What_consistency_asks_only_rises_with_the_level(string currency)
    {
        var rules = LifeDomains.For(currency)!;
        var consistency = rules.Consistency;

        Assert.Equal(rules.Curve.MaxLevel, consistency.MaxLevel);
        Assert.InRange(consistency.WindowDays, 7, 365);
        switch (consistency)
        {
            case ConsistencyRule xp:
                Assert.Equal("xp", xp.Kind);
                Assert.True(xp.AverageFrom > 0 && xp.AverageTo > xp.AverageFrom, "the average runs upward");
                Assert.True(xp.ShareFrom > 0 && xp.ShareTo > xp.ShareFrom && xp.ShareTo <= 1, "the active share runs upward");
                Assert.InRange(xp.ActiveShare, 0.01m, 1m);
                Assert.Equal(xp.AverageFrom, xp.Average(2));
                Assert.Equal(xp.AverageTo, xp.Average(xp.MaxLevel));
                break;
            case HomeConditionRule home:
                Assert.Equal("home", home.Kind);
                Assert.True(home.BarFrom > 0 && home.BarTo > home.BarFrom && home.BarTo <= 100, "the bar runs upward within 0–100");
                Assert.True(home.ShareFrom > 0 && home.ShareTo > home.ShareFrom && home.ShareTo <= 1, "the kept-up share runs upward");
                Assert.Equal(home.BarFrom, home.Bar(2));
                Assert.Equal(home.BarTo, home.Bar(home.MaxLevel));
                Assert.Equal(0, home.BaseNeeded(home.MaxLevel, home.WindowDays));
                break;
            default:
                Assert.Fail($"{currency} has a consistency rule the drift test doesn't know");
                break;
        }

        for (var level = 3; level <= consistency.MaxLevel; level++)
        {
            var window = consistency.WindowDays;
            Assert.True(consistency.Threshold(level) >= consistency.Threshold(level - 1), $"level {level} asks less of a day");
            Assert.True(consistency.DayMinimum(level) >= consistency.DayMinimum(level - 1), $"level {level} shows a lower day minimum");
            Assert.True(
                consistency.BaseNeeded(level, window) >= consistency.BaseNeeded(level - 1, window),
                $"level {level} asks a lower average");
            Assert.True(
                consistency.ReachedDaysNeeded(level, window) >= consistency.ReachedDaysNeeded(level - 1, window),
                $"level {level} asks fewer days at its threshold");
            Assert.True(consistency.AgeNeeded(level) >= consistency.AgeNeeded(level - 1), $"level {level} asks a younger domain");
        }

        Assert.True(rules.Fall.PercentPerDay is > 0 and < 100, "the fall is a share of the level");
        Assert.True(rules.Fall.HoldDays >= 0 && rules.Fall.BackOnTrackDays > 0);
    }

    [Theory]
    [MemberData(nameof(ConfiguredCurrencies))]
    public void A_better_share_never_earns_a_smaller_multiplier(string currency)
    {
        var rules = LifeDomains.For(currency)!;
        var multiplier = rules.Multiplier;

        Assert.InRange(multiplier.WindowDays, 1, 365);
        Assert.All(multiplier.Tiers, t =>
        {
            Assert.InRange(t.MinShare, 0.01m, 1m);
            Assert.True(t.Multiplier > 1m, "a tier that pays 1.0 or less isn't a reward");
        });
        Assert.Equal(multiplier.Tiers.Count, multiplier.Tiers.Select(t => t.MinShare).Distinct().Count());

        var previous = 0m;
        for (var percent = 0; percent <= 100; percent++)
        {
            var now = multiplier.For(percent / 100m);
            Assert.True(now >= previous, $"{percent}% pays less than {percent - 1}%");
            previous = now;
        }

        Assert.Equal(1.0m, multiplier.For(0m));
        Assert.True(rules.RankUpGems.PerRank > 0 && rules.RankUpGems.Top >= rules.RankUpGems.PerRank);
    }

    [Theory]
    [MemberData(nameof(ConfiguredCurrencies))]
    public void The_level_curve_runs_to_the_last_rank_and_never_gets_cheaper(string currency)
    {
        var rules = LifeDomains.For(currency)!;
        var curve = rules.Curve;

        Assert.Equal(rules.Ranks[^1].StartLevel, curve.MaxLevel);
        Assert.Equal(0, curve.TotalFor(1));
        for (var level = 1; level < curve.MaxLevel; level++)
        {
            Assert.True(curve.CostToNext(level) > 0, $"level {level} costs nothing");
            Assert.Equal(level, curve.LevelFor(curve.TotalFor(level)));
            if (level > 1)
            {
                Assert.True(curve.CostToNext(level) >= curve.CostToNext(level - 1), $"level {level} costs less than {level - 1}");
            }
        }

        // Every rank pays its Gems under a key of its own.
        var keys = Enumerable.Range(1, rules.Ranks.Count - 1).Select(rules.RankAwardKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, k => Assert.Equal(LifeDomains.RankTrigger, AwardKeys.TriggerOf(k)));
    }

    [Theory]
    [MemberData(nameof(ConfiguredCurrencies))]
    public void Passive_xp_comes_from_known_day_award_triggers(string currency)
    {
        var rules = LifeDomains.For(currency)!;

        // Passive XP is split out by the award key's trigger (§7.4.2), so it must be a day award's
        // trigger, never an item's.
        Assert.All(rules.PassiveTriggers, trigger =>
        {
            Assert.Contains(trigger, ActivityAwards.Triggers.Concat(FoodAwards.Triggers));
            Assert.DoesNotContain(
                trigger,
                new[] { BountyTriggers.Completed, BountyTriggers.Timed, BountyTriggers.Followed, BountyTriggers.Partial, BountyTriggers.Established });
        });
        Assert.All(rules.PassiveTriggers, t => Assert.Contains(t, LifeDomains.PassiveTriggers));
    }

    [Fact]
    public void Health_is_the_pilot_domain_with_the_levels_v2_rules()
    {
        var health = LifeDomains.Health;

        Assert.Equal([health, LifeDomains.Environment], LifeDomains.Configured);
        Assert.Same(health, LifeDomains.For("HEALTH_XP"));
        Assert.Equal(Currencies.HealthXp, health.Currency);
        Assert.Equal(Cadence.Daily, health.Cadence);
        Assert.Equal(new CubicCurve(Linear: 60, Quadratic: 24, Cubic: 0.85m), health.Curve);
        Assert.Equal(new ConsistencyRule(90, 30, 80, 0.4m, 20, 0.50m, 0.90m), health.Consistency);
        Assert.Same(LifeDomains.HealthConsistency, health.Consistency);
        Assert.Equal(new FallRule(PercentPerDay: 1, HoldDays: 0, BackOnTrackDays: 7), health.Fall);
        Assert.Equal(new ActiveAtShownLevel(LifeDomains.HealthConsistency), health.Success);
        Assert.Equal("at least 24 action XP (base XP less sleep)", health.Success.Describe(60));
        Assert.Equal(["sleep"], health.PassiveTriggers);
        Assert.Equal(30, health.Multiplier.WindowDays);
        Assert.Equal(new RankUpGems(1, 5), health.RankUpGems);
        Assert.Equal(
            ["Novice", "Apprentice", "Journeyman", "Adept", "Expert", "Veteran", "Elite", "Master", "Champion", "Legend", "Grandmaster"],
            health.Ranks.Select(r => r.Title));
        Assert.Equal(4, LifeDomains.SettleHour);

        // Social and Finance collect XP and nothing else until they get rules.
        Assert.Null(LifeDomains.For(Currencies.SocialXp));
        Assert.Null(LifeDomains.For(Currencies.FinanceXp));
    }

    [Fact]
    public void Environment_is_held_by_the_home_with_Healths_shown_level_ranks_and_multiplier()
    {
        // Environment plan §4.5 (REWARD-9, D18): the curve at a quarter of Health's price, the
        // Home Score's bar for consistency, and the rest Health's.
        var environment = LifeDomains.Environment;
        var health = LifeDomains.Health;

        Assert.Same(environment, LifeDomains.For(Currencies.EnvironmentXp));
        Assert.Equal(Cadence.Daily, environment.Cadence);
        Assert.Equal(new CubicCurve(Linear: 15, Quadratic: 6, Cubic: 0.2125m), environment.Curve);
        Assert.Equal(new HomeConditionRule(90, 30, 85, 0.50m, 0.90m), environment.Consistency);
        Assert.Same(LifeDomains.EnvironmentCondition, environment.Consistency);
        Assert.Equal(new KeptUpAtShownLevel(LifeDomains.EnvironmentCondition), environment.Success);
        Assert.Equal("a Home Score of at least 68.2", environment.Success.Describe(70));
        Assert.Equal(health.Fall, environment.Fall);
        Assert.Equal(health.Ranks, environment.Ranks);
        Assert.Equal(health.Multiplier, environment.Multiplier);
        Assert.Equal(health.RankUpGems, environment.RankUpGems);
        Assert.Empty(environment.PassiveTriggers);

        // A quarter of Health's price, level by level.
        for (var level = 2; level <= 100; level++)
        {
            Assert.Equal((health.Curve.TotalFor(level) + 3) / 4, environment.Curve.TotalFor(level));
        }
    }

    [Theory]
    [InlineData(1, 300_000, 45)]
    [InlineData(2, 300_000, 45)]
    [InlineData(10, 344_898, 48)]
    [InlineData(51, 575_000, 63)]
    [InlineData(70, 681_633, 70)]
    [InlineData(100, 850_000, 81)]
    public void The_home_bar_and_kept_up_days_rise_from_30_on_half_the_days_to_85_on_nine_in_ten(
        int level, long threshold, int keptUpDays)
    {
        // Q(L) = ⌈(30 · 98 + 55 · (L − 2)) · 10,000 / 98⌉; days = ⌈90 · (0.5 · 98 + 0.4 · (L − 2)) / 98⌉ (environment plan §2.3).
        var condition = LifeDomains.EnvironmentCondition;

        Assert.Equal(threshold, condition.Threshold(level));
        Assert.Equal(keptUpDays, condition.ReachedDaysNeeded(level, 90));
        Assert.Equal(Math.Min(90, level + 1), condition.AgeNeeded(level));
        Assert.Equal(0, condition.BaseNeeded(level, 90));
        Assert.Equal((int)Math.Ceiling(threshold / 10_000m), condition.DayMinimum(level));
    }

    [Fact]
    public void A_bar_shows_rounded_up_to_a_tenth()
    {
        var condition = LifeDomains.EnvironmentCondition;

        Assert.Equal("34.5", HomeConditionRule.Text(condition.Bar(10)));
        Assert.Equal("68.2", HomeConditionRule.Text(condition.Bar(70)));
        Assert.Equal("57.5", HomeConditionRule.Text(condition.Bar(51)));
        Assert.Equal("85.0", HomeConditionRule.Text(condition.Bar(100)));
    }

    [Theory]
    [InlineData("health", Currencies.HealthXp)]
    [InlineData(" Health XP ", Currencies.HealthXp)]
    [InlineData("HEALTH_XP", Currencies.HealthXp)]
    [InlineData("social", Currencies.SocialXp)]
    [InlineData("Environment", Currencies.EnvironmentXp)]
    [InlineData("finance_xp", Currencies.FinanceXp)]
    public void A_domain_is_named_by_its_key_label_or_first_word(string reference, string expected)
        => Assert.Equal(expected, DomainService.Resolve(reference).Key);

    [Theory]
    [InlineData("xp")]
    [InlineData("life")]
    [InlineData("coins")]
    [InlineData("wealth")]
    public void Something_that_isnt_a_life_domain_is_refused(string reference)
    {
        var refused = Assert.Throws<ArgumentException>(() => DomainService.Resolve(reference));
        Assert.Contains("isn't a life domain", refused.Message);
        Assert.Contains(Currencies.HealthXp, refused.Message);
    }
}
