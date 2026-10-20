using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// The rewards wallet (REWARD-1 / REWARD-3 / D11) — the write path behind
/// <c>bsk wallet</c>, <c>bsk ledger</c> and <c>bsk redeem</c>. Every earn, reversal and
/// redemption is an append-only <c>ledger</c> event carrying signed amounts per
/// currency; balances and history are folds over those events, so nothing is mutated
/// and every figure traces back to the entries behind it.
/// <para>
/// A redemption is <c>declared</c> with a note saying what it was for, may only spend
/// spendable currencies (XP and Resolve are tracked, never spent), and is refused if it
/// would take a balance below zero — there is no overdraft. Earning arrives with the
/// bounty / reconcile engine (rewards plan R2); <see cref="OpenAsync"/> writes the start
/// line it pays from.
/// </para>
/// </summary>
public sealed class RewardService(ILedgerRepository ledger, IClock clock, string sourceId)
{
    /// <summary>Every currency's totals, in display order, plus when the wallet opened.</summary>
    public async Task<Wallet> GetWalletAsync(CancellationToken cancellationToken = default)
    {
        var openedAt = await ledger.GetOpenedAtAsync(cancellationToken);
        var balances = await ledger.GetBalancesAsync(cancellationToken);
        return new Wallet(openedAt, Lines(balances));
    }

    /// <summary>
    /// Ledger entries newest first, optionally within [<paramref name="from"/>,
    /// <paramref name="to"/>) and capped at <paramref name="limit"/>.
    /// </summary>
    public Task<IReadOnlyList<LedgerEntry>> GetLedgerAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (from is { } start && to is { } end && start >= end)
        {
            throw new ArgumentException("The ledger range must end after it starts.");
        }

        if (limit is <= 0)
        {
            throw new ArgumentException("The ledger limit must be above zero.");
        }

        return ledger.GetEntriesAsync(from, to, limit, cancellationToken);
    }

    /// <summary>
    /// Opens the wallet: writes the <c>open</c> start line, dated <paramref name="at"/>
    /// (default now), unless it is already open. Nothing that happened before the
    /// opening ever pays. Safe to call repeatedly — it returns the existing opening.
    /// </summary>
    public async Task<WalletOpening> OpenAsync(
        DateTimeOffset? at = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var openEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Declared,
            OccurredAt: at ?? now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: JsonSerializer.Serialize(new { entry = LedgerEntries.Open }));

        var (openedAt, created) = await ledger.OpenAsync(openEvent, cancellationToken);
        return new WalletOpening(openedAt, created);
    }

    /// <summary>
    /// Spends <paramref name="amounts"/> (currency key → a whole number above zero) on
    /// something described by <paramref name="note"/>. Refused, with nothing written,
    /// when a currency is unknown or tracked, an amount is not positive, the note is
    /// blank, or any balance would go below zero.
    /// </summary>
    public async Task<Redemption> RedeemAsync(
        IReadOnlyDictionary<string, long> amounts, string? note,
        CancellationToken cancellationToken = default)
    {
        var spend = NormalizeSpend(amounts);
        var trimmedNote = note?.Trim();
        if (string.IsNullOrEmpty(trimmedNote))
        {
            throw new ArgumentException("Say what this redemption is for: a note is required.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            entry = LedgerEntries.Redeem,
            amounts = spend.ToDictionary(kv => kv.Key, kv => -kv.Value),
            note = trimmedNote
        });

        var now = clock.UtcNow;
        var redeemEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);

        var ids = await ledger.AppendAsync(
            [new LedgerWrite(redeemEvent, [], GuardBalance: true)], cancellationToken);

        return new Redemption(ids[0], spend, trimmedNote, await GetWalletAsync(cancellationToken));
    }

    /// <summary>
    /// Validates a spend and returns it keyed by canonical currency key, in display order.
    /// </summary>
    private static Dictionary<string, long> NormalizeSpend(IReadOnlyDictionary<string, long>? amounts)
    {
        if (amounts is null || amounts.Count == 0)
        {
            throw new ArgumentException("Say what to redeem, e.g. freedom=45.");
        }

        var byKey = new Dictionary<string, long>();
        foreach (var (key, amount) in amounts)
        {
            var currency = Currencies.Find(key) ?? throw new ArgumentException(
                $"Unknown currency '{key}'. Expected one of: {string.Join(", ", Currencies.All.Select(c => c.Key))}.");

            if (!currency.Spendable)
            {
                throw new ArgumentException($"{currency.Label} is tracked, not spent; it can't be redeemed.");
            }

            if (amount <= 0)
            {
                throw new ArgumentException($"Redeem a whole number of {currency.Label} above zero; got {amount}.");
            }

            if (!byKey.TryAdd(currency.Key, amount))
            {
                throw new ArgumentException($"{currency.Label} is listed twice.");
            }
        }

        return Currencies.All
            .Where(c => byKey.ContainsKey(c.Key))
            .ToDictionary(c => c.Key, c => byKey[c.Key]);
    }

    /// <summary>
    /// One line per known currency in display order, zero where the ledger never touched
    /// it, then any currency the ledger holds that the vocabulary no longer names — kept
    /// visible rather than silently dropped.
    /// </summary>
    private static List<WalletLine> Lines(IReadOnlyList<WalletBalance> balances)
    {
        var byKey = balances.ToDictionary(b => b.Currency, StringComparer.OrdinalIgnoreCase);
        var lines = Currencies.All
            .Select(c => byKey.TryGetValue(c.Key, out var b)
                ? new WalletLine(c, b.Earned, b.Reversed, b.Spent, b.Balance)
                : new WalletLine(c, 0, 0, 0, 0))
            .ToList();

        lines.AddRange(balances
            .Where(b => Currencies.Find(b.Currency) is null)
            .OrderBy(b => b.Currency, StringComparer.Ordinal)
            .Select(b => new WalletLine(
                new Currency(b.Currency, b.Currency, Spendable: false), b.Earned, b.Reversed, b.Spent, b.Balance)));

        return lines;
    }
}

/// <summary>One currency in the wallet: its totals and balance.</summary>
public sealed record WalletLine(Currency Currency, long Earned, long Reversed, long Spent, long Balance);

/// <summary>The wallet: when it opened (null before launch) and every currency's line, in display order.</summary>
public sealed record Wallet(DateTimeOffset? OpenedAt, IReadOnlyList<WalletLine> Lines)
{
    /// <summary>The balance of <paramref name="currency"/>, or 0 when the wallet has never held it.</summary>
    public long BalanceOf(string currency)
        => Lines.FirstOrDefault(l => string.Equals(l.Currency.Key, currency, StringComparison.OrdinalIgnoreCase))
            ?.Balance ?? 0;
}

/// <summary>When the wallet opened, and whether this call opened it.</summary>
public sealed record WalletOpening(DateTimeOffset OpenedAt, bool Created);

/// <summary>A redemption: its ledger event, what was spent (positive amounts), the note, and the wallet afterwards.</summary>
public sealed record Redemption(
    Guid EventId, IReadOnlyDictionary<string, long> Amounts, string Note, Wallet Wallet);
