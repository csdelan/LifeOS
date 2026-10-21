using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-6 / REWARD-7 (xp-domains plan X3, §3.2): the drift test for <see cref="LifeDomains"/>.
/// Rules live in code, so these checks are what keep a retune honest: every configured domain
/// reads a tracked life-domain currency, ranks climb from level 1 to 100 with gates that never
/// loosen, and a better share never earns a smaller multiplier.
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
    public void Ranks_climb_from_level_1_to_100_and_their_gates_never_loosen(string currency)
    {
        var ranks = LifeDomains.For(currency)!.Ranks;

        Assert.Equal(1, ranks[0].StartLevel);
        Assert.Equal(100, ranks[^1].StartLevel);
        Assert.Equal((0m, 0), (ranks[0].MinConsistency, ranks[0].WindowDays));
        Assert.Equal(ranks.Count, ranks.Select(r => r.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (var i = 1; i < ranks.Count; i++)
        {
            var (lower, higher) = (ranks[i - 1], ranks[i]);
            Assert.True(higher.StartLevel > lower.StartLevel, $"{higher.Title} starts no higher than {lower.Title}");
            Assert.True(higher.MinConsistency >= lower.MinConsistency, $"{higher.Title} asks less than {lower.Title}");
            Assert.True(higher.WindowDays >= lower.WindowDays, $"{higher.Title} looks back less than {lower.Title}");
            Assert.InRange(higher.MinConsistency, 0.01m, 1m);
            Assert.InRange(higher.WindowDays, 1, 365);
        }

        // Milestone gates are designed, not built (§2.2): no pilot rank asks for one.
        Assert.All(ranks, r => Assert.Empty(r.Milestones));
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

    [Fact]
    public void Health_is_the_pilot_domain_judged_daily_at_70_base_xp()
    {
        var health = LifeDomains.Health;

        Assert.Same(health, Assert.Single(LifeDomains.Configured));
        Assert.Same(health, LifeDomains.For("HEALTH_XP"));
        Assert.Equal(Currencies.HealthXp, health.Currency);
        Assert.Equal(Cadence.Daily, health.Cadence);
        Assert.Equal(new RampCurve(Base: 300, PerLevel: 25, RampFrom: 45, RampPerLevel: 70), health.Curve);
        Assert.Equal(new MinimumBaseXp(70), health.Success);
        Assert.Equal("at least 70 base XP", health.Success.Describe());
        Assert.Equal(30, health.Multiplier.WindowDays);
        Assert.Equal(new RankUpGems(1, 5), health.RankUpGems);
        Assert.Equal(
            ["Novice", "Apprentice", "Journeyman", "Adept", "Expert", "Veteran", "Elite", "Master", "Champion", "Legend", "Grandmaster"],
            health.Ranks.Select(r => r.Title));

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
