using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Shared formatting for the rewards verbs (<c>wallet</c>, <c>ledger</c>, <c>redeem</c>,
/// <c>bounty</c>, <c>rewards</c>) and the verbs that can earn (<c>status</c>,
/// <c>timer</c>): amounts read "+10 XP, +2 Coins" / "−45 Freedom", money reads "$1,250.00",
/// and every verb's <c>--json</c> carries the wallet, awards and catalog rewards in the
/// same shape.
/// </summary>
internal static class RewardOutput
{
    /// <summary>"+10 XP, +2 Coins" when <paramref name="signed"/>, otherwise "10 XP, 2 Coins".</summary>
    public static string Amounts(IReadOnlyDictionary<string, long> amounts, bool signed = true)
        => string.Join(", ", amounts.Select(kv =>
            $"{(signed && kv.Value > 0 ? "+" : "")}{kv.Value} {Currencies.LabelFor(kv.Key)}"));

    /// <summary>Dollars to the cent: <c>$1,250.00</c>.</summary>
    public static string Usd(decimal value) => "$" + value.ToString("#,0.00", CultureInfo.InvariantCulture);

    /// <summary>The wallet as JSON-ready data.</summary>
    public static object WalletJson(Wallet wallet) => new
    {
        openedAt = wallet.OpenedAt,
        coinValueUsd = wallet.CoinValueUsd,
        balances = wallet.Lines.Select(l => new
        {
            currency = l.Currency.Key,
            label = l.Currency.Label,
            spendable = l.Currency.Spendable,
            earned = l.Earned,
            reversed = l.Reversed,
            spent = l.Spent,
            balance = l.Balance
        })
    };

    /// <summary>The entries a reconcile wrote, as JSON-ready data.</summary>
    public static object AwardsJson(IEnumerable<Award> awards) => awards.Select(a => new
    {
        id = a.EventId,
        entry = a.Entry,
        trigger = a.Trigger,
        awardKey = a.AwardKey,
        subject = a.Subject is { } s ? new { urn = s.Urn, type = s.Type, title = s.Title } : null,
        amounts = a.Amounts,
        note = a.Note,
        @base = a.Base,
        multipliers = a.Multipliers
    });

    /// <summary>
    /// Prints one line per award: "Earned +10 XP, +2 Coins (completed: Write report)." — or, for
    /// a rank-up, "(rank: Health Apprentice)". Domain XP paid at a multiplier says so:
    /// "+28 Health XP (25 × 1.1)".
    /// </summary>
    public static void WriteAwards(IEnumerable<Award> awards)
    {
        foreach (var award in awards)
        {
            var verb = award.Entry == LedgerEntries.Reversal ? "Reversed" : "Earned";
            Console.WriteLine($"{verb} {Amounts(award)} ({award.Trigger}: {award.Subject?.Title ?? award.Note}).");
        }
    }

    /// <summary>An award's amounts, each one paid at a multiplier followed by its working: "+28 Health XP (25 × 1.1)".</summary>
    private static string Amounts(Award award)
        => string.Join(", ", award.Amounts.Select(kv =>
        {
            var amount = Amounts(new Dictionary<string, long> { [kv.Key] = kv.Value });
            return award.Base?.GetValueOrDefault(kv.Key) is { } basis and > 0
                   && award.Multipliers?.GetValueOrDefault(kv.Key) is { } multiplier and > 0
                ? $"{amount} ({basis} × {multiplier.ToString("0.0#", CultureInfo.InvariantCulture)})"
                : amount;
        }));

    /// <summary>Each life domain whose level or rank a write changed, as JSON-ready data.</summary>
    public static object ProgressJson(IEnumerable<DomainChange> progress) => progress.Select(p => new
    {
        domain = p.Domain,
        levelFrom = p.LevelFrom,
        levelTo = p.LevelTo,
        rankFrom = p.RankFrom,
        rankTo = p.RankTo,
        rankTitle = p.RankTitle
    });

    /// <summary>Prints one line per domain that moved: "Health: level 9 → 12, now Apprentice."</summary>
    public static void WriteProgress(IEnumerable<DomainChange> progress)
    {
        foreach (var p in progress)
        {
            var parts = new List<string>();
            if (p.LevelTo != p.LevelFrom)
            {
                parts.Add($"level {p.LevelFrom} → {p.LevelTo}");
            }

            if (p.RankTo != p.RankFrom)
            {
                parts.Add(p.RankTo > p.RankFrom ? $"now {p.RankTitle}" : $"back to {p.RankTitle}");
            }

            Console.WriteLine($"{LifeDomains.Name(p.Domain)}: {string.Join(", ", parts)}.");
        }
    }

    /// <summary>A catalog Reward as JSON-ready data.</summary>
    public static object RewardJson(CatalogReward reward) => new
    {
        id = reward.Reward.Id,
        urn = reward.Reward.Urn,
        name = reward.Reward.Title,
        kind = reward.Kind,
        price = reward.Price,
        targetUsd = reward.TargetUsd,
        redemptions = reward.Redemptions,
        coinsRedeemed = reward.CoinsRedeemed,
        dollarsRedeemed = reward.DollarsRedeemed,
        lastRedeemedAt = reward.LastRedeemedAt,
        percentOfTarget = reward.PercentOfTarget,
        remainingUsd = reward.RemainingUsd,
        description = reward.Description,
        archived = reward.Archived
    };

    /// <summary>
    /// What a Reward costs or how far a savings goal has got: "costs 120 Freedom", or
    /// "$45.00 saved of $500.00 (9%), $455.00 to go".
    /// </summary>
    public static string Describe(CatalogReward reward)
    {
        if (reward.Kind != RewardKinds.Savings)
        {
            return reward.Price.Count == 0 ? "no price yet" : $"costs {Amounts(reward.Price, signed: false)}";
        }

        var saved = $"{Usd(reward.DollarsRedeemed)} saved";
        if (reward is not { TargetUsd: { } target, PercentOfTarget: { } percent })
        {
            return saved;
        }

        var progress = $"{saved} of {Usd(target)} ({percent.ToString("0.#", CultureInfo.InvariantCulture)}%)";
        return reward.RemainingUsd is { } remaining && remaining > 0
            ? $"{progress}, {Usd(remaining)} to go"
            : $"{progress} — target reached";
    }

    /// <summary>A local-time stamp for a ledger line: <c>2026-10-20 14:03</c>.</summary>
    public static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// Parses <c>currency=amount</c> pairs (currency keys are checked by the service, so
    /// the message names a currency the vocabulary knows). <paramref name="usage"/> is
    /// the hint shown for a malformed pair, e.g. "redeem expects currency=amount, e.g. freedom=45".
    /// </summary>
    public static Dictionary<string, long> ParseAmounts(IEnumerable<string> pairs, string usage)
    {
        var amounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0
                || !long.TryParse(pair[(separator + 1)..].Trim(), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var amount))
            {
                throw new ArgumentException($"{usage}; got '{pair}'.");
            }

            var key = pair[..separator].Trim();
            if (!amounts.TryAdd(key, amount))
            {
                throw new ArgumentException($"'{key}' is listed twice.");
            }
        }

        return amounts;
    }
}
