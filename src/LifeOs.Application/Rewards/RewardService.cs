using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// The rewards wallet (REWARD-1 / D11) — the write path behind <c>bsk wallet</c> and
/// <c>bsk ledger</c>; a redemption is <see cref="RedemptionService"/>'s. Every earn, reversal and
/// redemption is an append-only <c>ledger</c> event carrying signed amounts per
/// currency; balances and history are folds over those events, so nothing is mutated
/// and every figure traces back to the entries behind it.
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
    ILedgerRepository ledger, RankUpService rankUps, ItemPrices prices, IClock clock,
    EventSource eventSource, decimal coinValueUsd = CoinRates.DefaultUsd)
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
    /// The rewards history by local day (REWARD-5) for the days [<paramref name="from"/>,
    /// <paramref name="to"/>) as they read in <paramref name="timeZone"/> (an IANA name):
    /// one <see cref="LedgerDay"/> per day with entries, oldest first. See
    /// <see cref="LedgerDays.Validate"/> for what is refused.
    /// </summary>
    public async Task<IReadOnlyList<LedgerDay>> GetDailyAsync(
        DateOnly from, DateOnly to, string timeZone, CancellationToken cancellationToken = default)
    {
        LedgerDays.Validate(from, to, timeZone);
        return LedgerDays.Fold(await ledger.GetDailyTotalsAsync(from, to, timeZone, cancellationToken));
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
            SourceId: eventSource.Id,
            PayloadJson: JsonSerializer.Serialize(new { entry = LedgerEntries.Open }));

        var (openedAt, created) = await ledger.OpenAsync(openEvent, cancellationToken);
        return new WalletOpening(openedAt, created);
    }

    /// <summary>
    /// Brings the ledger in line with the record for <paramref name="subject"/>: earns
    /// every award it is due and has not been paid, and reverses every award it was paid
    /// that is no longer due. When that wrote life-domain XP, it also pays any rank-up the
    /// domain has reached (<see cref="RankUpService.PayAsync"/>). Returns the entries written
    /// (none when it was already in line).
    /// <para>
    /// <paramref name="causeEventId"/> is the write that prompted this reconcile (a status
    /// change), cited by any reversal. Before the wallet has ever opened, a cause that
    /// makes something pay opens it, dated at that trigger; without a cause nothing opens,
    /// so a sweep never pays history from before launch.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Award>> ReconcileAsync(
        SubjectRef subject, Guid? causeEventId = null, CancellationToken cancellationToken = default)
        => (await ReconcileWithProgressAsync(subject, causeEventId, cancellationToken)).Awards;

    /// <summary>
    /// <see cref="ReconcileAsync"/>, also answering each configured life domain whose level or
    /// rank it changed (xp-domains plan §3.8) — what an earning write says back.
    /// </summary>
    public async Task<Earned> ReconcileWithProgressAsync(
        SubjectRef subject, Guid? causeEventId = null, CancellationToken cancellationToken = default)
    {
        if (await ledger.GetOpenedAtAsync(cancellationToken) is null
            && !await OpenForAsync(subject, causeEventId, cancellationToken))
        {
            return Earned.Nothing;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (awards, before) = await ReconcileOnceAsync(
                    subject, causeEventId, new DomainFolds(rankUps.FoldAsync), cancellationToken);
                if (before is null)
                {
                    return new Earned(awards, []);
                }

                // It wrote domain XP: pay any rank-up that reached, and say what changed.
                var ranked = await rankUps.PayAfterAsync(before, cancellationToken);
                return new Earned([.. awards, .. ranked.Awards], ranked.Progress);
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
    /// crash, a direct import), then pays any rank-up still due. Never opens the wallet.
    /// Returns every entry written.
    /// </summary>
    /// <remarks>
    /// The domains are folded once and the fold is kept from item to item until a write changes
    /// their credit, and rank-ups are paid once at the end, not after each item (code review
    /// CR14): every fold reads all of the domains' history.
    /// </remarks>
    public async Task<IReadOnlyList<Award>> ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        // With no cause nothing opens the wallet, so a closed one has nothing to reconcile.
        if (await ledger.GetOpenedAtAsync(cancellationToken) is null)
        {
            return [];
        }

        var awards = new List<Award>();
        var domains = new DomainFolds(rankUps.FoldAsync);
        foreach (var subject in await ledger.GetReconcileCandidatesAsync(cancellationToken))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    awards.AddRange((await ReconcileOnceAsync(subject, causeEventId: null, domains, cancellationToken)).Awards);
                    break;
                }
                catch (LedgerConflictException) when (attempt < ReconcileAttempts)
                {
                    // Another reconcile changed one of these awards first; re-read and retry.
                    domains.Changed();
                }
            }
        }

        awards.AddRange(await rankUps.PayAsync(cancellationToken));
        return awards;
    }

    /// <summary>
    /// Writes awards that belong to no item (food days, food-log plan §3.4) the way an item's
    /// reconcile does: <paramref name="plan"/> decides the entries, given the domains as they
    /// stand (read once, only if it asks) to price at; they're appended as one batch; and when
    /// they carried life-domain XP, any rank-up reached is paid and each domain whose level or
    /// rank moved comes back. A lost race re-plans from a fresh read.
    /// </summary>
    public async Task<Earned> EarnAsync(
        Func<Func<Task<IReadOnlyList<DomainProgress>>>, CancellationToken, Task<IReadOnlyList<PlannedAward>>> plan,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            IReadOnlyList<DomainProgress>? domains = null;
            async Task<IReadOnlyList<DomainProgress>> DomainsAsync()
                => domains ??= await rankUps.FoldAsync(cancellationToken);

            try
            {
                var planned = await plan(DomainsAsync, cancellationToken);
                if (planned.Count == 0)
                {
                    return Earned.Nothing;
                }

                var before = planned.Any(p => p.Award.Amounts.Keys.Any(k => LifeDomains.For(k) is not null))
                    ? await DomainsAsync()
                    : null;
                var ids = await ledger.AppendAsync(planned.Select(p => p.Write).ToList(), cancellationToken);
                var awards = planned.Select((p, i) => p.Award with { EventId = ids[i] }).ToList();
                if (before is null)
                {
                    return new Earned(awards, []);
                }

                var ranked = await rankUps.PayAfterAsync(before, cancellationToken);
                return new Earned([.. awards, .. ranked.Awards], ranked.Progress);
            }
            catch (LedgerConflictException) when (attempt < ReconcileAttempts)
            {
                // Another writer changed one of these awards first; re-read and re-plan.
            }
        }
    }

    /// <summary>
    /// The configured domains folded on first use and kept until <see cref="Changed"/>: a write
    /// that moved their credit, or a lost race.
    /// </summary>
    private sealed class DomainFolds(Func<CancellationToken, Task<IReadOnlyList<DomainProgress>>> read)
    {
        private IReadOnlyList<DomainProgress>? folds;

        public async Task<IReadOnlyList<DomainProgress>> GetAsync(CancellationToken cancellationToken)
            => folds ??= await read(cancellationToken);

        public void Changed() => folds = null;
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

        var price = await prices.ReadAsync(subject, cancellationToken);
        var paying = caused.Where(t => price.For(t).Count > 0).ToList();
        if (paying.Count == 0)
        {
            return false;
        }

        await OpenAsync(paying.Min(t => t.TriggeredAt), cancellationToken);
        return true;
    }

    /// <summary>
    /// One reconcile of <paramref name="subject"/>: the entries written and, when they carry
    /// configured life-domain XP, the domains as they stood just before (else null). An earn's
    /// domain XP is paid at the multiplier of the day it's credited to (xp-domains plan §3.6),
    /// read from <paramref name="domains"/>, one fold of the domains as they stand now, which a
    /// write of domain XP marks changed.
    /// </summary>
    private async Task<(IReadOnlyList<Award> Awards, IReadOnlyList<DomainProgress>? DomainsBefore)> ReconcileOnceAsync(
        SubjectRef subject, Guid? causeEventId, DomainFolds domains, CancellationToken cancellationToken)
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
            var bounty = await prices.ReadAsync(subject, cancellationToken);
            foreach (var award in unpaid)
            {
                var price = bounty.For(award);
                if (price.Count == 0)
                {
                    // Priced to pay nothing: there is nothing to record.
                    continue;
                }

                // Domain XP pays at its credited day's multiplier, frozen in the entry: nothing
                // re-prices it later. A day logged late is priced from the days before it.
                var earn = DomainPricing.CouldMultiply(price)
                    ? PriceOn(price, award, await domains.GetAsync(cancellationToken))
                    : new PricedEarn(price);

                // Dated at the trigger, so the history shows the day it was earned even
                // when a sweep heals it later; credited to the day it happened, which for a
                // habit day logged late is earlier still (xp-domains plan §3.3).
                writes.Add(Entry(
                    subject, LedgerEntries.Earn, award.AwardKey, award.Trigger, earn.Amounts,
                    occurredAt: award.TriggeredAt, recordedAt: now, derivedFrom: award.SourceEventIds,
                    expectPaid: false, creditedOn: award.CreditedOn, basis: earn.Base, multipliers: earn.Multipliers));
                awards.Add(new Award(
                    Guid.Empty, subject, LedgerEntries.Earn, award.Trigger, award.AwardKey, earn.Amounts,
                    Base: earn.Base, Multipliers: earn.Multipliers));
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
            return ([], null);
        }

        var before = awards.Any(a => a.Amounts.Keys.Any(k => LifeDomains.For(k) is not null))
            ? await domains.GetAsync(cancellationToken)
            : null;
        var ids = await ledger.AppendAsync(writes, cancellationToken);
        if (before is not null)
        {
            domains.Changed();
        }

        return (awards.Select((award, i) => award with { EventId = ids[i] }).ToList(), before);
    }

    /// <summary>
    /// <paramref name="price"/> at the multiplier of the day <paramref name="award"/> is
    /// credited to (today, in the home zone, should the view ever leave that day out).
    /// </summary>
    private static PricedEarn PriceOn(
        IReadOnlyDictionary<string, long> price, DueAward award, IReadOnlyList<DomainProgress> domains)
        => DomainPricing.Price(price, award.CreditedOn ?? domains[0].Today, domains);

    /// <summary>
    /// A ledger entry for an item. An earn paid at a multiplier above 1.0 also stamps
    /// <paramref name="basis"/> (its payload's <c>base</c>: those currencies before the
    /// multiplier, which success rules read) and <paramref name="multipliers"/>.
    /// </summary>
    private LedgerWrite Entry(
        SubjectRef subject, string entry, string awardKey, string trigger,
        IReadOnlyDictionary<string, long> amounts, DateTimeOffset occurredAt, DateTimeOffset recordedAt,
        IReadOnlyList<Guid> derivedFrom, bool expectPaid, DateOnly? creditedOn = null,
        IReadOnlyDictionary<string, long>? basis = null, IReadOnlyDictionary<string, decimal>? multipliers = null)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                entry,
                amounts,
                award_key = awardKey,
                trigger,
                subject_id = subject.Id.ToString(),
                credited_on = creditedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                @base = basis,
                multipliers
            },
            OmitNulls);

        var newEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Derived,
            OccurredAt: occurredAt,
            RecordedAt: recordedAt,
            SourceId: eventSource.Id,
            PayloadJson: payload,
            DerivedFrom: derivedFrom);

        return new LedgerWrite(
            newEvent,
            [new SubjectEventEdge(subject.Id, SubjectEventRelations.Concerns, Provenances.Derived)],
            GuardBalance: false,
            AwardKey: awardKey,
            ExpectPaid: expectPaid);
    }
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
/// <c>reversal</c> (negative), for which item, trigger and award key. A rank-up has no item:
/// its <paramref name="Note"/> says what it was for, e.g. "Health Apprentice". An earn whose
/// life-domain XP was paid at a multiplier above 1.0 carries <paramref name="Base"/> (those
/// currencies before it) and <paramref name="Multipliers"/> (xp-domains plan §3.6), so it can
/// read "28 Health XP (25 × 1.1)".
/// </summary>
public sealed record Award(
    Guid EventId, SubjectRef? Subject, string Entry, string Trigger, string AwardKey,
    IReadOnlyDictionary<string, long> Amounts, string? Note = null,
    IReadOnlyDictionary<string, long>? Base = null, IReadOnlyDictionary<string, decimal>? Multipliers = null);

/// <summary>
/// What an earning write did: the ledger entries it wrote, and each configured life domain
/// whose level or rank changed (xp-domains plan §3.8).
/// </summary>
public sealed record Earned(IReadOnlyList<Award> Awards, IReadOnlyList<DomainChange> Progress)
{
    public static readonly Earned Nothing = new([], []);
}

/// <summary>
/// One entry <see cref="RewardService.EarnAsync"/> writes: the ledger write, and the award it
/// reports (its event id filled in once written).
/// </summary>
public sealed record PlannedAward(LedgerWrite Write, Award Award);

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
