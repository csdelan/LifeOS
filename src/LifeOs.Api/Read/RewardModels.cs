using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// <c>GET /api/wallet</c> (REWARD-1 / REWARD-3): every currency's balance in display order,
/// when the wallet opened (null before the first completion that pays), and what a Coin
/// is worth in dollars.
/// </summary>
public sealed class WalletSummary
{
    public DateTimeOffset? OpenedAt { get; set; }
    public decimal CoinValueUsd { get; set; }
    public IReadOnlyList<WalletCurrencyLine> Balances { get; set; } = [];
}

/// <summary>One currency in the wallet. XP and Resolve are tracked: never spendable.</summary>
public sealed class WalletCurrencyLine
{
    public string Currency { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Spendable { get; set; }
    public long Earned { get; set; }
    public long Reversed { get; set; }
    public long Spent { get; set; }
    public long Balance { get; set; }
}

/// <summary>
/// One ledger entry (REWARD-5), newest first in <c>GET /api/ledger</c>: an earn or
/// reversal for an item, or a redemption — against a catalog Reward or with a note.
/// Amounts are signed; <see cref="Dollars"/> and <see cref="CoinRate"/> are set when it
/// spent Coins.
/// </summary>
public sealed class LedgerRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    /// <summary><c>open</c>, <c>earn</c>, <c>reversal</c> or <c>redeem</c>.</summary>
    public string Entry { get; set; } = "";
    public string Provenance { get; set; } = "";
    public IReadOnlyDictionary<string, long> Amounts { get; set; } = new Dictionary<string, long>();
    public string? Trigger { get; set; }
    public string? AwardKey { get; set; }
    public CreatedSubject? Subject { get; set; }
    public string? Note { get; set; }
    public decimal? Dollars { get; set; }
    public decimal? CoinRate { get; set; }
}

/// <summary>
/// One catalog Reward (REWARD-3), from <c>bsk.v_reward</c>: an instant reward with a price,
/// or a savings goal with an optional dollar target, and what has been redeemed against it.
/// <see cref="PercentOfTarget"/> and <see cref="RemainingUsd"/> are set only for a savings
/// goal with a target.
/// </summary>
public sealed class RewardRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary><c>instant</c> or <c>savings</c>.</summary>
    public string Kind { get; set; } = "";
    public IReadOnlyDictionary<string, long> Price { get; set; } = new Dictionary<string, long>();
    public decimal? TargetUsd { get; set; }
    public long Redemptions { get; set; }
    public long CoinsRedeemed { get; set; }
    public decimal DollarsRedeemed { get; set; }
    public DateTimeOffset? LastRedeemedAt { get; set; }
    public decimal? PercentOfTarget { get; set; }
    public decimal? RemainingUsd { get; set; }
    public string? Description { get; set; }
    public bool Archived { get; set; }
}

/// <summary>
/// A ledger entry an earning write produced (REWARD-4's toast): an <c>earn</c> with positive
/// amounts or a <c>reversal</c> with negative ones, for which item and trigger.
/// </summary>
public sealed class AwardRow
{
    public Guid Id { get; set; }
    public string Entry { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string AwardKey { get; set; } = "";
    public CreatedSubject Subject { get; set; } = new();
    public IReadOnlyDictionary<string, long> Amounts { get; set; } = new Dictionary<string, long>();
}

/// <summary>
/// The response of a write that can earn (<c>POST /api/status</c>, <c>/api/adhere</c>): the
/// ledger entries its reconcile wrote, empty when it changed nothing.
/// </summary>
public sealed class EarnResult
{
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
}

/// <summary>
/// <c>POST /api/redeem</c>: what was spent (positive amounts), the note, the dollars and coin
/// rate when it spent Coins, the catalog Reward afterwards (null for a custom redemption),
/// and the wallet afterwards.
/// </summary>
public sealed class RedemptionResult
{
    public Guid Id { get; set; }
    public IReadOnlyDictionary<string, long> Amounts { get; set; } = new Dictionary<string, long>();
    public string? Note { get; set; }
    public decimal? Dollars { get; set; }
    public decimal? CoinRate { get; set; }
    public RewardRow? Reward { get; set; }
    public WalletSummary Wallet { get; set; } = new();
}

/// <summary>Maps the Application's rewards records onto the API's shapes.</summary>
public static class RewardRows
{
    public static WalletSummary From(Wallet wallet) => new()
    {
        OpenedAt = wallet.OpenedAt,
        CoinValueUsd = wallet.CoinValueUsd,
        Balances = wallet.Lines.Select(l => new WalletCurrencyLine
        {
            Currency = l.Currency.Key,
            Label = l.Currency.Label,
            Spendable = l.Currency.Spendable,
            Earned = l.Earned,
            Reversed = l.Reversed,
            Spent = l.Spent,
            Balance = l.Balance
        }).ToList()
    };

    public static LedgerRow From(LedgerEntry entry) => new()
    {
        Id = entry.EventId,
        OccurredAt = entry.OccurredAt,
        Entry = entry.Entry,
        Provenance = entry.Provenance,
        Amounts = entry.Amounts,
        Trigger = entry.Trigger,
        AwardKey = entry.AwardKey,
        Subject = entry.Subject is { } subject ? Subject(subject) : null,
        Note = entry.Note,
        Dollars = entry.Dollars,
        CoinRate = entry.CoinRate
    };

    public static RewardRow From(CatalogReward reward) => new()
    {
        Id = reward.Reward.Id,
        Urn = reward.Reward.Urn,
        Name = reward.Reward.Title,
        Kind = reward.Kind,
        Price = reward.Price,
        TargetUsd = reward.TargetUsd,
        Redemptions = reward.Redemptions,
        CoinsRedeemed = reward.CoinsRedeemed,
        DollarsRedeemed = reward.DollarsRedeemed,
        LastRedeemedAt = reward.LastRedeemedAt,
        PercentOfTarget = reward.PercentOfTarget,
        RemainingUsd = reward.RemainingUsd,
        Description = reward.Description,
        Archived = reward.Archived
    };

    public static AwardRow From(Award award) => new()
    {
        Id = award.EventId,
        Entry = award.Entry,
        Trigger = award.Trigger,
        AwardKey = award.AwardKey,
        Subject = Subject(award.Subject),
        Amounts = award.Amounts
    };

    public static EarnResult Earned(IEnumerable<Award> awards) => new() { Awards = awards.Select(From).ToList() };

    public static RedemptionResult From(Redemption redemption) => new()
    {
        Id = redemption.EventId,
        Amounts = redemption.Amounts,
        Note = redemption.Note,
        Dollars = redemption.Dollars,
        CoinRate = redemption.CoinRate,
        Reward = redemption.Reward is { } reward ? From(reward) : null,
        Wallet = From(redemption.Wallet)
    };

    private static CreatedSubject Subject(SubjectRef subject) => new()
    {
        Id = subject.Id,
        Urn = subject.Urn,
        Type = subject.Type,
        Title = subject.Title
    };
}
