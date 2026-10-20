using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Shared formatting for the rewards verbs (<c>wallet</c>, <c>ledger</c>, <c>redeem</c>,
/// <c>bounty</c>, <c>rewards</c>) and the verbs that can earn (<c>status</c>,
/// <c>timer</c>): amounts read "+10 XP, +2 Coins" / "−45 Freedom", and every verb's
/// <c>--json</c> carries the wallet and awards in the same shape.
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

    /// <summary>The entries a reconcile wrote, as JSON-ready data.</summary>
    public static object AwardsJson(IEnumerable<Award> awards) => awards.Select(a => new
    {
        id = a.EventId,
        entry = a.Entry,
        trigger = a.Trigger,
        awardKey = a.AwardKey,
        subject = new { urn = a.Subject.Urn, type = a.Subject.Type, title = a.Subject.Title },
        amounts = a.Amounts
    });

    /// <summary>Prints one line per award: "Earned +10 XP, +2 Coins (completed: Write report)."</summary>
    public static void WriteAwards(IEnumerable<Award> awards)
    {
        foreach (var award in awards)
        {
            var verb = award.Entry == LedgerEntries.Reversal ? "Reversed" : "Earned";
            Console.WriteLine($"{verb} {Amounts(award.Amounts)} ({award.Trigger}: {award.Subject.Title}).");
        }
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
