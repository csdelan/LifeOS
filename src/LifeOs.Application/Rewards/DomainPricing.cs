using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// An earn's amounts after the life-domain multiplier (xp-domains plan §3.6).
/// <see cref="Amounts"/> is what it pays. When a multiplier above 1.0 applied,
/// <see cref="Base"/> holds those currencies' amounts before it and <see cref="Multipliers"/>
/// the multiplier each was paid at; both are null otherwise.
/// </summary>
public sealed record PricedEarn(
    IReadOnlyDictionary<string, long> Amounts,
    IReadOnlyDictionary<string, long>? Base = null,
    IReadOnlyDictionary<string, decimal>? Multipliers = null);

/// <summary>
/// Prices an earn's life-domain XP at the multiplier of the day it's credited to (xp-domains
/// plan §3.6): <c>paid = round(base × multiplierFor(creditedOn))</c>, halves up. Only
/// configured domains multiply; unconfigured domains, Life XP, Resolve and the spendables pay
/// their price as it is.
/// </summary>
public static class DomainPricing
{
    /// <summary>Whether <paramref name="price"/> pays XP in a configured life domain, so a multiplier could apply.</summary>
    public static bool CouldMultiply(IReadOnlyDictionary<string, long> price)
        => price.Any(kv => kv.Value > 0 && LifeDomains.For(kv.Key) is not null);

    /// <summary>
    /// <paramref name="price"/> paid on <paramref name="creditedOn"/>, each configured domain's
    /// XP at that day's multiplier from <paramref name="domains"/> — the record as it stands
    /// at payment.
    /// </summary>
    public static PricedEarn Price(
        IReadOnlyDictionary<string, long> price, DateOnly creditedOn, IReadOnlyList<DomainProgress> domains)
    {
        var amounts = new Dictionary<string, long>(price);
        var basis = new Dictionary<string, long>();
        var multipliers = new Dictionary<string, decimal>();
        foreach (var (currency, amount) in price)
        {
            var domain = domains.FirstOrDefault(d =>
                string.Equals(d.Rules.Currency, currency, StringComparison.OrdinalIgnoreCase));
            if (domain is null || amount <= 0)
            {
                continue;
            }

            var multiplier = domain.MultiplierFor(creditedOn);
            if (multiplier <= 1.0m)
            {
                continue;
            }

            amounts[currency] = MultiplierRule.Apply(amount, multiplier);
            basis[currency] = amount;
            multipliers[currency] = multiplier;
        }

        return multipliers.Count == 0
            ? new PricedEarn(amounts)
            : new PricedEarn(amounts, basis, multipliers);
    }
}
