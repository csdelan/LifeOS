using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// The rewards wallet (REWARD-1 / REWARD-3 / D11) — the write path behind
/// <c>bsk wallet</c>, <c>bsk ledger</c> and <c>bsk redeem</c>. Every earn, reversal and
/// redemption is an append-only <c>ledger</c> event carrying signed amounts per
/// currency; balances and history are folds over those events, so nothing is mutated
/// and every figure traces back to the entries behind it.
/// <para>
/// A redemption is <c>declared</c>, may only spend spendable currencies (XP and Resolve
/// are tracked, never spent), and is refused if it would take a balance below zero —
/// there is no overdraft. It is either against a catalog Reward (REWARD-3) — an instant
/// reward at its price, or Coins deposited into a savings goal — or a custom one whose
/// note says what it was for. Coins are real money: every redemption that spends them
/// records the dollars moved at the configured rate (<see cref="CoinValueUsd"/>), so a
/// later rate change never rewrites a past deposit.
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
    ILedgerRepository ledger, ISubjectRepository subjects, SubjectService resolver, IClock clock,
    string sourceId, decimal coinValueUsd = CoinRates.DefaultUsd)
{
    // A reconcile that loses a race on an award re-reads and tries again; two retries
    // are plenty for one person's windows racing.
    private const int ReconcileAttempts = 3;

    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>What a Coin is worth in dollars, as configured (<see cref="CoinRates.ConfigurationKey"/>).</summary>
    public decimal CoinValueUsd { get; } = coinValueUsd > 0
        ? coinValueUsd
        : throw new ArgumentOutOfRangeException(nameof(coinValueUsd), coinValueUsd, "A Coin must be worth more than $0.");

    /// <summary>Every currency's totals, in display order, plus when the wallet opened.</summary>
    public async Task<Wallet> GetWalletAsync(CancellationToken cancellationToken = default)
    {
        var openedAt = await ledger.GetOpenedAtAsync(cancellationToken);
        var balances = await ledger.GetBalancesAsync(cancellationToken);
        return Wallet.From(openedAt, balances, CoinValueUsd);
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
    /// something described by <paramref name="note"/> — a custom redemption, with no
    /// catalog Reward. Refused, with nothing written, when a currency is unknown or
    /// tracked, an amount is not positive, the note is blank, or any balance would go
    /// below zero.
    /// </summary>
    public Task<Redemption> RedeemAsync(
        IReadOnlyDictionary<string, long> amounts, string? note,
        CancellationToken cancellationToken = default)
        => RedeemAsync(reward: null, amounts, note, cancellationToken);

    /// <summary>
    /// Redeems against the catalog Reward named by <paramref name="reward"/> (urn, short id
    /// or title), or — when it is null — makes a custom redemption that needs a note.
    /// <list type="bullet">
    /// <item>An instant reward costs its catalog price; <paramref name="amounts"/>, when
    /// given, override it for this redemption.</item>
    /// <item>A savings goal takes the Coins in <paramref name="amounts"/> as a deposit,
    /// and only Coins.</item>
    /// </list>
    /// The note is optional against a Reward. Refused, with nothing written, for anything
    /// that isn't a Reward, an archived Reward, an instant reward with no price and no
    /// amounts, invalid amounts, or a spend any balance can't cover.
    /// </summary>
    public async Task<Redemption> RedeemAsync(
        string? reward, IReadOnlyDictionary<string, long>? amounts, string? note,
        CancellationToken cancellationToken = default)
    {
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (string.IsNullOrWhiteSpace(reward))
        {
            if (amounts is null || amounts.Count == 0)
            {
                throw new ArgumentException("Say what to redeem, e.g. freedom=45.");
            }

            var spend = RewardPrice.Normalize(amounts);
            if (trimmedNote is null)
            {
                throw new ArgumentException("Say what this redemption is for: a note is required.");
            }

            return await WriteRedemptionAsync(null, spend, trimmedNote, cancellationToken);
        }

        var subject = await resolver.ResolveAsync(reward, cancellationToken);
        if (subject.Type != SubjectTypes.Reward)
        {
            throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}, not a Reward; redeem against a Reward, or give a note instead.");
        }

        var item = await ledger.GetCatalogRewardAsync(subject.Id, cancellationToken)
                   ?? throw new SubjectNotFoundException(subject.Urn);
        if (item.Archived)
        {
            throw new InvalidOperationException($"'{subject.Title}' is archived; restore it to redeem against it.");
        }

        var cost = item.Kind == RewardKinds.Savings ? DepositFor(item, amounts) : CostOf(item, amounts);
        return await WriteRedemptionAsync(subject, cost, trimmedNote, cancellationToken);
    }

    private async Task<Redemption> WriteRedemptionAsync(
        SubjectRef? reward, Dictionary<string, long> spend, string? note, CancellationToken cancellationToken)
    {
        // Coins are real money: stamp what this moved and the rate it moved at.
        decimal? dollars = null;
        decimal? rate = null;
        if (spend.TryGetValue(Currencies.Coins, out var coins))
        {
            rate = CoinValueUsd;
            dollars = CoinRates.Dollars(coins, CoinValueUsd);
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                entry = LedgerEntries.Redeem,
                amounts = spend.ToDictionary(kv => kv.Key, kv => -kv.Value),
                subject_id = reward?.Id.ToString(),
                note,
                dollars,
                coin_rate = rate
            },
            OmitNulls);

        var now = clock.UtcNow;
        var redeemEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload);
        IReadOnlyList<SubjectEventEdge> edges = reward is null
            ? []
            : [new SubjectEventEdge(reward.Id, SubjectEventRelations.Concerns, Provenances.Declared)];

        var ids = await ledger.AppendAsync(
            [new LedgerWrite(redeemEvent, edges, GuardBalance: true)], cancellationToken);

        var item = reward is null ? null : await ledger.GetCatalogRewardAsync(reward.Id, cancellationToken);
        return new Redemption(
            ids[0], spend, note, dollars, rate, item, await GetWalletAsync(cancellationToken));
    }

    /// <summary>A deposit into a savings goal: Coins, and only Coins.</summary>
    private static Dictionary<string, long> DepositFor(
        CatalogReward item, IReadOnlyDictionary<string, long>? amounts)
    {
        if (amounts is null || amounts.Count == 0)
        {
            throw new ArgumentException(
                $"Say how many Coins to put toward '{item.Reward.Title}', e.g. coins=50.");
        }

        var deposit = RewardPrice.Normalize(amounts);
        if (deposit.Keys.Any(k => k != Currencies.Coins))
        {
            throw new ArgumentException($"'{item.Reward.Title}' is a savings goal: only Coins go toward it.");
        }

        return deposit;
    }

    /// <summary>An instant reward's cost: the amounts given, else its catalog price.</summary>
    private static Dictionary<string, long> CostOf(
        CatalogReward item, IReadOnlyDictionary<string, long>? amounts)
    {
        if (amounts is { Count: > 0 })
        {
            return RewardPrice.Normalize(amounts);
        }

        return item.Price.Count > 0
            ? new Dictionary<string, long>(item.Price)
            : throw new ArgumentException(
                $"'{item.Reward.Title}' has no price yet: say what it costs this time, e.g. freedom=45, or give it a price.");
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
}

/// <summary>One currency in the wallet: its totals and balance.</summary>
public sealed record WalletLine(Currency Currency, long Earned, long Reversed, long Spent, long Balance);

/// <summary>
/// The wallet: when it opened (null before launch), every currency's line in display
/// order, and what a Coin is worth in dollars.
/// </summary>
public sealed record Wallet(DateTimeOffset? OpenedAt, IReadOnlyList<WalletLine> Lines, decimal CoinValueUsd)
{
    /// <summary>
    /// The wallet from the ledger's folded balances (<c>bsk.v_wallet</c>): one line per
    /// known currency in display order, zero where the ledger never touched it, then any
    /// currency the ledger holds that the vocabulary no longer names — kept visible rather
    /// than silently dropped.
    /// </summary>
    public static Wallet From(DateTimeOffset? openedAt, IReadOnlyList<WalletBalance> balances, decimal coinValueUsd)
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

        return new Wallet(openedAt, lines, coinValueUsd);
    }

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

/// <summary>
/// A redemption: its ledger event, what was spent (positive amounts), the note (optional
/// against a Reward), the dollars and coin rate stamped when it spent Coins, the catalog
/// Reward as it stands afterwards (null for a custom redemption), and the wallet afterwards.
/// </summary>
public sealed record Redemption(
    Guid EventId, IReadOnlyDictionary<string, long> Amounts, string? Note, decimal? Dollars,
    decimal? CoinRate, CatalogReward? Reward, Wallet Wallet);
