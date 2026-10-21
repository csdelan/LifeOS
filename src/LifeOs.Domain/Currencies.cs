namespace LifeOs.Domain;

/// <summary>
/// One rewards currency (REWARD-1): its ledger key, its display label, and whether it
/// can be spent. A tracked currency (Life XP, Resolve, the life-domain XPs) accrues and is
/// reported but can never be redeemed. <see cref="LifeDomain"/> marks the XP of a life
/// domain (REWARD-6): Health, Social, Environment, Finance.
/// </summary>
public sealed record Currency(string Key, string Label, bool Spendable, bool LifeDomain = false);

/// <summary>
/// The rewards currencies (REWARD-1 / D11). This is vocabulary <em>data</em>, validated
/// app-side as <see cref="StatusVocabulary"/> is: the ledger stores amounts keyed by
/// <see cref="Currency.Key"/> and no database enum names them, so adding a currency
/// (REWARD-6's life domains, say) is one line here, not a migration.
/// <see cref="All"/> is also the display order. A life domain pays 0 until an item is
/// priced in it: no bounty default names one (xp-domains plan §3.1).
/// </summary>
public static class Currencies
{
    /// <summary>Redeemed as real money moved into a savings goal, at a set rate.</summary>
    public const string Coins = "coins";

    /// <summary>Redeemed for entertainment minutes: 1 Freedom = 1 minute.</summary>
    public const string Freedom = "freedom";

    /// <summary>Earned for significant accomplishments; redeemed for significant events.</summary>
    public const string Gems = "gems";

    /// <summary>Earned for good deeds.</summary>
    public const string Karma = "karma";

    /// <summary>General experience from everything ("Life XP"). Tracked, not spent.</summary>
    public const string Xp = "xp";

    /// <summary>Overcoming fear, procrastination and resistance. Tracked, not spent.</summary>
    public const string Resolve = "resolve";

    /// <summary>Health: exercise, sleep, nutrition, recovery. A life domain; tracked.</summary>
    public const string HealthXp = "health_xp";

    /// <summary>Social: family, friends, outreach. A life domain; tracked.</summary>
    public const string SocialXp = "social_xp";

    /// <summary>Environment: cleaning, tidying, home maintenance, decluttering. A life domain; tracked.</summary>
    public const string EnvironmentXp = "environment_xp";

    /// <summary>
    /// Finance: income and expense, budgeting, net worth, savings, financial discipline.
    /// A life domain; tracked.
    /// </summary>
    public const string FinanceXp = "finance_xp";

    public static readonly IReadOnlyList<Currency> All =
    [
        new(Coins, "Coins", Spendable: true),
        new(Freedom, "Freedom", Spendable: true),
        new(Gems, "Gems", Spendable: true),
        new(Karma, "Karma", Spendable: true),
        new(Xp, "Life XP", Spendable: false),
        new(Resolve, "Resolve", Spendable: false),
        new(HealthXp, "Health XP", Spendable: false, LifeDomain: true),
        new(SocialXp, "Social XP", Spendable: false, LifeDomain: true),
        new(EnvironmentXp, "Environment XP", Spendable: false, LifeDomain: true),
        new(FinanceXp, "Finance XP", Spendable: false, LifeDomain: true),
    ];

    /// <summary>The life-domain currencies (REWARD-6), in display order.</summary>
    public static readonly IReadOnlyList<Currency> LifeDomains = All.Where(c => c.LifeDomain).ToList();

    /// <summary>The currency with this key (case- and whitespace-insensitive), or <c>null</c>.</summary>
    public static Currency? Find(string? key)
    {
        var normalized = key?.Trim();
        return All.FirstOrDefault(c => string.Equals(c.Key, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The display label for <paramref name="key"/>, or the key itself when it is unknown.</summary>
    public static string LabelFor(string key) => Find(key)?.Label ?? key;
}

/// <summary>
/// What a <c>ledger</c> event records (D11), carried on its payload's <c>entry</c>.
/// </summary>
public static class LedgerEntries
{
    /// <summary>The wallet's start line: nothing that happened before it pays. Carries no amounts.</summary>
    public const string Open = "open";

    /// <summary>An award the record says is due. Derived; cites what caused it. Positive amounts.</summary>
    public const string Earn = "earn";

    /// <summary>An award the record no longer supports, given back. Derived. Negative amounts.</summary>
    public const string Reversal = "reversal";

    /// <summary>Currency spent on a reward. Declared, with a note. Negative amounts.</summary>
    public const string Redeem = "redeem";
}
