using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// REWARD-7 (xp-domains plan X5, §3.6): an earn's life-domain XP is paid at the multiplier of
/// the day it's credited to, rounding halves up, with what it was before and the multiplier
/// kept beside it. Only configured domains multiply. Pure: each test folds days it writes.
/// </summary>
public sealed class DomainPricingTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    /// <summary>Health with <paramref name="xp"/> on each day from <paramref name="fromAgo"/> down to <paramref name="toAgo"/> days ago.</summary>
    private static IReadOnlyList<DomainProgress> Health(int fromAgo, int toAgo, long xp = 70)
        => [DomainProgress.Fold(
            LifeDomains.Health,
            Enumerable.Range(toAgo, fromAgo - toAgo + 1).Select(ago => new DomainDay(Today.AddDays(-ago), xp, xp)),
            Today)];

    [Theory]
    [InlineData(25, "1.1", 28)]
    [InlineData(25, "1.3", 33)]
    [InlineData(5, "1.1", 6)]
    [InlineData(4, "1.1", 4)]
    [InlineData(10, "1.25", 13)]
    [InlineData(70, "1.0", 70)]
    public void A_multiplied_amount_rounds_to_whole_xp_halves_up(long amount, string multiplier, long paid)
        => Assert.Equal(paid, MultiplierRule.Apply(amount, decimal.Parse(multiplier, CultureInfo.InvariantCulture)));

    [Fact]
    public void Health_xp_pays_at_the_days_multiplier_and_keeps_what_it_was_before()
    {
        // 26 of the 30 days before today succeeded: 87%, ×1.3.
        var domains = Health(30, 5);

        var earn = DomainPricing.Price(new Dictionary<string, long> { [Currencies.HealthXp] = 25 }, Today, domains);

        Assert.Equal(new Dictionary<string, long> { [Currencies.HealthXp] = 33 }, earn.Amounts);
        Assert.Equal(new Dictionary<string, long> { [Currencies.HealthXp] = 25 }, earn.Base);
        Assert.Equal(new Dictionary<string, decimal> { [Currencies.HealthXp] = 1.3m }, earn.Multipliers);
    }

    [Fact]
    public void Only_configured_domains_multiply()
    {
        var price = new Dictionary<string, long>
        {
            [Currencies.HealthXp] = 25,
            [Currencies.SocialXp] = 25,
            [Currencies.Xp] = 10,
            [Currencies.Resolve] = 3,
            [Currencies.Coins] = 1
        };

        var earn = DomainPricing.Price(price, Today, Health(30, 5));

        Assert.Equal(33, earn.Amounts[Currencies.HealthXp]);
        Assert.Equal(price.Where(kv => kv.Key != Currencies.HealthXp), earn.Amounts.Where(kv => kv.Key != Currencies.HealthXp));
        Assert.Equal([Currencies.HealthXp], earn.Base!.Keys);
        Assert.Equal([Currencies.HealthXp], earn.Multipliers!.Keys);
        Assert.True(DomainPricing.CouldMultiply(price));
        Assert.False(DomainPricing.CouldMultiply(price.Where(kv => kv.Key != Currencies.HealthXp).ToDictionary()));
    }

    [Fact]
    public void A_day_is_priced_from_the_30_days_before_it_not_from_today()
    {
        // The last 25 days succeeded: today is 83% (×1.2), but 20 days ago only the 5 days
        // before it had (17%, ×1.0), and 10 days ago 15 of 30 (50%, ×1.1).
        var domains = Health(25, 1);
        var price = new Dictionary<string, long> { [Currencies.HealthXp] = 60 };

        Assert.Equal(72, DomainPricing.Price(price, Today, domains).Amounts[Currencies.HealthXp]);
        Assert.Equal(66, DomainPricing.Price(price, Today.AddDays(-10), domains).Amounts[Currencies.HealthXp]);
        var late = DomainPricing.Price(price, Today.AddDays(-20), domains);
        Assert.Equal(60, late.Amounts[Currencies.HealthXp]);
        Assert.Null(late.Base);
        Assert.Null(late.Multipliers);
    }

    [Fact]
    public void At_one_times_nothing_is_stamped()
    {
        var earn = DomainPricing.Price(new Dictionary<string, long> { [Currencies.HealthXp] = 25 }, Today, Health(30, 20));

        Assert.Equal(new PricedEarn(earn.Amounts), earn);
        Assert.Equal(25, earn.Amounts[Currencies.HealthXp]);
    }
}
