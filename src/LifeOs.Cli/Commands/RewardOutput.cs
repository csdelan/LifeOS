using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Shared formatting for the rewards verbs (<c>wallet</c>, <c>ledger</c>, <c>redeem</c>):
/// amounts read "+10 XP, +2 Coins" / "−45 Freedom", and every verb's <c>--json</c>
/// carries the wallet in the same shape.
/// </summary>
internal static class RewardOutput
{
    /// <summary>"+10 XP, +2 Coins" when <paramref name="signed"/>, otherwise "10 XP, 2 Coins".</summary>
    public static string Amounts(IReadOnlyDictionary<string, long> amounts, bool signed = true)
        => string.Join(", ", amounts.Select(kv =>
            $"{(signed && kv.Value > 0 ? "+" : "")}{kv.Value} {Currencies.LabelFor(kv.Key)}"));

    /// <summary>The wallet as JSON-ready data.</summary>
    public static object WalletJson(Wallet wallet) => new
    {
        openedAt = wallet.OpenedAt,
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

    /// <summary>A local-time stamp for a ledger line: <c>2026-10-20 14:03</c>.</summary>
    public static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
