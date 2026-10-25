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
        Assert.True(consistency.AverageFrom > 0 && consistency.AverageTo > consistency.AverageFrom, "the average runs upward");
        Assert.True(consistency.ShareFrom > 0 && consistency.ShareTo > consistency.ShareFrom && consistency.ShareTo <= 1, "the active share runs upward");
        Assert.InRange(consistency.ActiveShare, 0.01m, 1m);
        Assert.InRange(consistency.WindowDays, 7, 365);
        Assert.Equal(consistency.AverageFrom, consistency.Average(2));
        Assert.Equal(consistency.AverageTo, consistency.Average(consistency.MaxLevel));
        for (var level = 3; level <= consistency.MaxLevel; level++)
        {
            Assert.True(consistency.Average(level) >= consistency.Average(level - 1), $"level {level} asks a lower average");
            Assert.True(consistency.ActiveMinimum(level) >= consistency.ActiveMinimum(level - 1), $"level {level} asks less of an active day");
            Assert.True(
                consistency.ActiveDaysNeeded(level, consistency.WindowDays) >= consistency.ActiveDaysNeeded(level - 1, consistency.WindowDays),
                $"level {level} asks fewer active days");
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

        Assert.Same(health, Assert.Single(LifeDomains.Configured));
        Assert.Same(health, LifeDomains.For("HEALTH_XP"));
        Assert.Equal(Currencies.HealthXp, health.Currency);
        Assert.Equal(Cadence.Daily, health.Cadence);
        Assert.Equal(new CubicCurve(Linear: 60, Quadratic: 24, Cubic: 0.85m), health.Curve);
        Assert.Equal(new ConsistencyRule(90, 20, 80, 0.4m, 10, 0.50m, 0.90m), health.Consistency);
        Assert.Equal(new FallRule(PercentPerDay: 1, HoldDays: 0, BackOnTrackDays: 7), health.Fall);
        Assert.Equal(new ActiveAtShownLevel(health.Consistency), health.Success);
        Assert.Equal("at least 17 action XP (base XP less sleep)", health.Success.Describe(40));
        Assert.Equal(["sleep"], health.PassiveTriggers);
        Assert.Equal(30, health.Multiplier.WindowDays);
        Assert.Equal(new RankUpGems(1, 5), health.RankUpGems);
        Assert.Equal(
            ["Novice", "Apprentice", "Journeyman", "Adept", "Expert", "Veteran", "Elite", "Master", "Champion", "Legend", "Grandmaster"],
            health.Ranks.Select(r => r.Title));
        Assert.Equal(4, LifeDomains.SettleHour);

        // The other domains collect XP and nothing else until they get rules.
        Assert.Null(LifeDomains.For(Currencies.SocialXp));
        Assert.Null(LifeDomains.For(Currencies.EnvironmentXp));
        Assert.Null(LifeDomains.For(Currencies.FinanceXp));
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
