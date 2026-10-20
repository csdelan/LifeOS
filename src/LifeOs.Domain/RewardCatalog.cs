namespace LifeOs.Domain;

/// <summary>
/// The two kinds of Reward (REWARD-3), held in a Reward's <c>attributes.kind</c>.
/// <c>bsk.v_reward</c> (migration 0028) reads anything that isn't <see cref="Savings"/>
/// as <see cref="Instant"/>.
/// </summary>
public static class RewardKinds
{
    /// <summary>A treat with a catalog price (<c>attributes.price</c>, currency → amount), e.g. Movie night.</summary>
    public const string Instant = "instant";

    /// <summary>
    /// A savings goal that Coins are deposited into, with an optional
    /// <c>attributes.target_usd</c>, e.g. Bike fund.
    /// </summary>
    public const string Savings = "savings";

    public static readonly IReadOnlyList<string> All = [Instant, Savings];

    /// <summary>The kind <paramref name="value"/> names (case- and whitespace-insensitive), or <c>null</c>.</summary>
    public static string? Find(string? value)
        => All.FirstOrDefault(k => string.Equals(k, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The attributes a Reward keeps (REWARD-3, migration 0028).</summary>
public static class RewardAttributes
{
    public const string Kind = "kind";

    /// <summary>An instant reward's price: currency → a whole number above zero.</summary>
    public const string Price = "price";

    /// <summary>A savings goal's target, in US dollars.</summary>
    public const string TargetUsd = "target_usd";
}

/// <summary>
/// What a Coin is worth in real money (rewards plan §1). Every redemption that spends
/// Coins records the dollars it moved and the rate in force, so changing the rate never
/// rewrites a past deposit. Configured as <c>Rewards:CoinValueUsd</c>.
/// </summary>
public static class CoinRates
{
    /// <summary>The configuration key for the rate.</summary>
    public const string ConfigurationKey = "Rewards:CoinValueUsd";

    /// <summary>The rate when none is configured: $0.50 a Coin.</summary>
    public const decimal DefaultUsd = 0.50m;

    /// <summary>The dollars <paramref name="coins"/> are worth at <paramref name="rate"/>, to the cent.</summary>
    public static decimal Dollars(long coins, decimal rate)
        => Math.Round(coins * rate, 2, MidpointRounding.AwayFromZero);
}
