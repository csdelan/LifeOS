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
/// would take a balance below zero — there is no overdraft.
/// </para>
/// <para>
/// Earning is reconciled, not reacted to (rewards plan §3.3): <see cref="ReconcileAsync"/>
/// compares what <c>bsk.v_reward_due</c> says an item is due with what the ledger has
/// paid it, then earns each due award not yet paid — priced now from the item's bounty
/// or its type default — and reverses each paid award no longer due, by exactly what it
/// paid. It is idempotent, so running it again, or a missed run healed later by
/// <see cref="ReconcileAllAsync"/>, never pays twice.
/// </para>
/// </summary>
public sealed class RewardService(
    ILedgerRepository ledger, ISubjectRepository subjects, IClock clock, string sourceId)
{
    // A reconcile that loses a race on an award re-reads and tries again; two retries
    // are plenty for one person's windows racing.
    private const int ReconcileAttempts = 3;

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
    /// Brings the ledger in line with the record for <paramref name="subject"/>: earns
    /// every award it is due and has not been paid, and reverses every award it was paid
    /// that is no longer due. Returns the entries written (none when it was already in line).
    /// <para>
    /// <paramref name="causeEventId"/> is the write that prompted this reconcile (a status
    /// change), cited by any reversal. Before the wallet has ever opened, a cause that
    /// makes something pay opens it, dated at that trigger; without a cause nothing opens,
    /// so a sweep never pays history from before launch.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Award>> ReconcileAsync(
        SubjectRef subject, Guid? causeEventId = null, CancellationToken cancellationToken = default)
    {
        if (await ledger.GetOpenedAtAsync(cancellationToken) is null
            && !await OpenForAsync(subject, causeEventId, cancellationToken))
        {
            return [];
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ReconcileOnceAsync(subject, causeEventId, cancellationToken);
            }
            catch (LedgerConflictException) when (attempt < ReconcileAttempts)
            {
                // Another reconcile changed one of these awards first; re-read and retry.
            }
        }
    }

    /// <summary>
    /// Reconciles every item the ledger could disagree with — those with an award due and
    /// those with an award paid — healing any trigger whose own reconcile never ran (a
    /// crash, a direct import). Never opens the wallet. Returns every entry written.
    /// </summary>
    public async Task<IReadOnlyList<Award>> ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        var awards = new List<Award>();
        foreach (var subject in await ledger.GetReconcileCandidatesAsync(cancellationToken))
        {
            awards.AddRange(await ReconcileAsync(subject, causeEventId: null, cancellationToken));
        }

        return awards;
    }

    /// <summary>
    /// Opens the wallet at the first trigger <paramref name="causeEventId"/> makes pay on
    /// <paramref name="subject"/>, if it makes any. Returns whether the wallet is now open.
    /// </summary>
    private async Task<bool> OpenForAsync(
        SubjectRef subject, Guid? causeEventId, CancellationToken cancellationToken)
    {
        if (causeEventId is not { } cause || !BountyTriggers.Earns(subject.Type))
        {
            return false;
        }

        var caused = (await ledger.GetTriggersAsync(subject.Id, cancellationToken))
            .Where(t => t.SourceEventIds.Contains(cause))
            .ToList();
        if (caused.Count == 0)
        {
            return false;
        }

        var stored = await ReadBountyAsync(subject, cancellationToken);
        var paying = caused.Where(t => Bounty.Price(subject.Type, t.Trigger, stored).Count > 0).ToList();
        if (paying.Count == 0)
        {
            return false;
        }

        await OpenAsync(paying.Min(t => t.TriggeredAt), cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<Award>> ReconcileOnceAsync(
        SubjectRef subject, Guid? causeEventId, CancellationToken cancellationToken)
    {
        var due = await ledger.GetDueAsync(subject.Id, cancellationToken);
        var paid = await ledger.GetPaidAsync(subject.Id, cancellationToken);
        var paidKeys = paid.Select(p => p.AwardKey).ToHashSet(StringComparer.Ordinal);
        var dueKeys = due.Select(d => d.AwardKey).ToHashSet(StringComparer.Ordinal);

        var now = clock.UtcNow;
        var writes = new List<LedgerWrite>();
        var awards = new List<Award>();

        var unpaid = due.Where(d => !paidKeys.Contains(d.AwardKey)).ToList();
        if (unpaid.Count > 0)
        {
            var stored = await ReadBountyAsync(subject, cancellationToken);
            foreach (var award in unpaid)
            {
                var amounts = Bounty.Price(subject.Type, award.Trigger, stored);
                if (amounts.Count == 0)
                {
                    // Priced to pay nothing: there is nothing to record.
                    continue;
                }

                // Dated at the trigger, so the history shows the day it was earned even
                // when a sweep heals it later.
                writes.Add(Entry(
                    subject, LedgerEntries.Earn, award.AwardKey, award.Trigger, amounts,
                    occurredAt: award.TriggeredAt, recordedAt: now, derivedFrom: award.SourceEventIds,
                    expectPaid: false));
                awards.Add(new Award(Guid.Empty, subject, LedgerEntries.Earn, award.Trigger, award.AwardKey, amounts));
            }
        }

        var undone = paid.Where(p => !dueKeys.Contains(p.AwardKey)).ToList();
        if (undone.Count > 0)
        {
            var cause = causeEventId
                        ?? await ledger.GetLatestSubjectEventAsync(subject.Id, cancellationToken);
            foreach (var award in undone)
            {
                var trigger = award.Trigger ?? AwardKeys.TriggerOf(award.AwardKey);
                var amounts = award.Net.ToDictionary(kv => kv.Key, kv => -kv.Value);
                writes.Add(Entry(
                    subject, LedgerEntries.Reversal, award.AwardKey, trigger, amounts,
                    occurredAt: now, recordedAt: now, derivedFrom: [cause ?? award.LastEventId],
                    expectPaid: true));
                awards.Add(new Award(Guid.Empty, subject, LedgerEntries.Reversal, trigger, award.AwardKey, amounts));
            }
        }

        if (writes.Count == 0)
        {
            return [];
        }

        var ids = await ledger.AppendAsync(writes, cancellationToken);
        return awards.Select((award, i) => award with { EventId = ids[i] }).ToList();
    }

    private LedgerWrite Entry(
        SubjectRef subject, string entry, string awardKey, string trigger,
        IReadOnlyDictionary<string, long> amounts, DateTimeOffset occurredAt, DateTimeOffset recordedAt,
        IReadOnlyList<Guid> derivedFrom, bool expectPaid)
    {
        var payload = JsonSerializer.Serialize(new
        {
            entry,
            amounts,
            award_key = awardKey,
            trigger,
            subject_id = subject.Id.ToString()
        });

        var newEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Derived,
            OccurredAt: occurredAt,
            RecordedAt: recordedAt,
            SourceId: sourceId,
            PayloadJson: payload,
            DerivedFrom: derivedFrom);

        return new LedgerWrite(
            newEvent,
            [new SubjectEventEdge(subject.Id, SubjectEventRelations.Concerns, Provenances.Derived)],
            GuardBalance: false,
            AwardKey: awardKey,
            ExpectPaid: expectPaid);
    }

    private async Task<Dictionary<string, Dictionary<string, long>>> ReadBountyAsync(
        SubjectRef subject, CancellationToken cancellationToken)
        => Bounty.Parse(await subjects.GetAttributeValueAsync(subject.Id, Bounty.AttributeKey, cancellationToken));

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

/// <summary>
/// A ledger entry a reconcile wrote: an <c>earn</c> (positive amounts) or a
/// <c>reversal</c> (negative), for which item, trigger and award key.
/// </summary>
public sealed record Award(
    Guid EventId, SubjectRef Subject, string Entry, string Trigger, string AwardKey,
    IReadOnlyDictionary<string, long> Amounts);

/// <summary>When the wallet opened, and whether this call opened it.</summary>
public sealed record WalletOpening(DateTimeOffset OpenedAt, bool Created);

/// <summary>A redemption: its ledger event, what was spent (positive amounts), the note, and the wallet afterwards.</summary>
public sealed record Redemption(
    Guid EventId, IReadOnlyDictionary<string, long> Amounts, string Note, Wallet Wallet);
