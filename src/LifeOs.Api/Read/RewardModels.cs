using System.Globalization;
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
/// One local day of the rewards history (REWARD-5), oldest first in
/// <c>GET /api/ledger/daily</c>: per currency, what that day's entries earned, gave back and
/// spent (positive totals), and <see cref="Net"/>, the signed sum of all of them. Currencies
/// at zero are left out; a day without entries has no row. Weeks and months are roll-ups.
/// </summary>
public sealed class LedgerDayRow
{
    /// <summary>The local date, <c>yyyy-MM-dd</c>, in the time zone the read asked for.</summary>
    public string Date { get; set; } = "";
    public IReadOnlyDictionary<string, long> Earned { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> Reversed { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> Spent { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> Net { get; set; } = new Dictionary<string, long>();
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
/// amounts or a <c>reversal</c> with negative ones, for which item and trigger. A rank-up
/// (trigger <c>rank</c>) has no item: its <see cref="Note"/> says which rank, e.g.
/// "Health Apprentice".
/// </summary>
public sealed class AwardRow
{
    public Guid Id { get; set; }
    public string Entry { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string AwardKey { get; set; } = "";
    public CreatedSubject? Subject { get; set; }
    public IReadOnlyDictionary<string, long> Amounts { get; set; } = new Dictionary<string, long>();
    public string? Note { get; set; }
}

/// <summary>
/// A life domain whose level or rank a write changed (xp-domains plan §3.8): its currency key,
/// the level (1–100) and rank (0 is Novice) before and after, and the title of the rank after.
/// </summary>
public sealed class DomainChangeRow
{
    public string Domain { get; set; } = "";
    public int LevelFrom { get; set; }
    public int LevelTo { get; set; }
    public int RankFrom { get; set; }
    public int RankTo { get; set; }
    public string RankTitle { get; set; } = "";
}

/// <summary>
/// The response of a write that can earn (<c>POST /api/status</c>, <c>/api/adhere</c>,
/// <c>/api/domains/settle</c>): the ledger entries its reconcile wrote, empty when it changed
/// nothing, and each life domain whose level or rank changed.
/// </summary>
public sealed class EarnResult
{
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
    public IReadOnlyList<DomainChangeRow> Progress { get; set; } = [];
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

    public static LedgerDayRow From(LedgerDay day) => new()
    {
        Date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Earned = day.Earned,
        Reversed = day.Reversed,
        Spent = day.Spent,
        Net = day.Net
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
        Subject = award.Subject is { } subject ? Subject(subject) : null,
        Amounts = award.Amounts,
        Note = award.Note
    };

    public static DomainChangeRow From(DomainChange change) => new()
    {
        Domain = change.Domain,
        LevelFrom = change.LevelFrom,
        LevelTo = change.LevelTo,
        RankFrom = change.RankFrom,
        RankTo = change.RankTo,
        RankTitle = change.RankTitle
    };

    public static EarnResult Earned(IEnumerable<Award> awards, IEnumerable<DomainChange> progress) => new()
    {
        Awards = awards.Select(From).ToList(),
        Progress = progress.Select(From).ToList()
    };

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
