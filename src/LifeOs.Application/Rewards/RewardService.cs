using System.Globalization;
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
    /// that is no longer due. When that wrote life-domain XP, it also pays any rank-up the
    /// domain has reached (<see cref="ReconcileDomainsAsync"/>). Returns the entries written
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
                var (awards, before) = await ReconcileOnceAsync(subject, causeEventId, cancellationToken);
                if (before is null)
                {
                    return new Earned(awards, []);
                }

                // It wrote domain XP: pay any rank-up that reached, and say what changed.
                var (ranks, after) = await PayRankUpsAsync(cancellationToken);
                return new Earned([.. awards, .. ranks.Select(r => r.Award)], DomainChanges(before, after, ranks));
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
    public async Task<IReadOnlyList<Award>> ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        var awards = new List<Award>();
        foreach (var subject in await ledger.GetReconcileCandidatesAsync(cancellationToken))
        {
            awards.AddRange(await ReconcileAsync(subject, causeEventId: null, cancellationToken));
        }

        awards.AddRange(await ReconcileDomainsAsync(cancellationToken));
        return awards;
    }

    /// <summary>
    /// Pays each rank-up the configured life domains have reached (xp-domains plan §3.7, §7.4.6):
    /// <see cref="RankUpGems"/> once per rank, under the award key
    /// <c>rank:&lt;domain&gt;:&lt;start level&gt;</c>, the first time the best rank reached it, on
    /// or after the day the wallet opened. It earns only: a rank award is never reversed, so
    /// losing a rank and reaching it again never pays twice, and a rank paid under X4's gates is
    /// never paid again. Returns the entries written.
    /// </summary>
    public async Task<IReadOnlyList<Award>> ReconcileDomainsAsync(CancellationToken cancellationToken = default)
        => (await PayRankUpsAsync(cancellationToken)).Awards.Select(r => r.Award).ToList();

    /// <summary>
    /// <see cref="ReconcileDomainsAsync"/> for <c>POST /api/domains/settle</c>, which the
    /// Character page calls when it opens: a rank reached by time alone (an old miss leaving
    /// the window, the domain turning old enough) pays then, and its domain's change comes back
    /// so it can be celebrated.
    /// </summary>
    public async Task<Earned> SettleDomainsAsync(CancellationToken cancellationToken = default)
    {
        var (ranks, folds) = await PayRankUpsAsync(cancellationToken);
        return new Earned(ranks.Select(r => r.Award).ToList(), DomainChanges(folds, folds, ranks));
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
                => domains ??= Fold(await ReadDomainsAsync(cancellationToken));

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

                var (ranks, after) = await PayRankUpsAsync(cancellationToken);
                return new Earned([.. awards, .. ranks.Select(r => r.Award)], DomainChanges(before, after, ranks));
            }
            catch (LedgerConflictException) when (attempt < ReconcileAttempts)
            {
                // Another writer changed one of these awards first; re-read and re-plan.
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="write"/>, a change to what the life-domain fold reads beside the
    /// ledger (an exception day, xp-domains plan §7.4.3), between two folds: any rank-up the
    /// change unlocks is paid, and each configured domain whose level or rank moved comes back,
    /// as an earning write answers.
    /// </summary>
    public async Task<Earned> ChangeDomainInputsAsync(
        Func<CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        var before = Fold(await ReadDomainsAsync(cancellationToken));
        await write(cancellationToken);
        var (ranks, after) = await PayRankUpsAsync(cancellationToken);
        return new Earned(ranks.Select(r => r.Award).ToList(), DomainChanges(before, after, ranks));
    }

    /// <summary>Folds the configured domains, then pays their rank-ups, re-reading after a lost race.</summary>
    private async Task<(IReadOnlyList<RankUp> Awards, IReadOnlyList<DomainProgress> Folds)> PayRankUpsAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var credit = await ReadDomainsAsync(cancellationToken);
            var folds = Fold(credit);
            try
            {
                return (await PayRankUpsAsync(credit.OpenedOn, folds, cancellationToken), folds);
            }
            catch (LedgerConflictException) when (attempt < ReconcileAttempts)
            {
                // Another writer paid one of these first; re-read and retry.
            }
        }
    }

    private async Task<IReadOnlyList<RankUp>> PayRankUpsAsync(
        DateOnly? openedOn, IReadOnlyList<DomainProgress> folds, CancellationToken cancellationToken)
    {
        if (openedOn is not { } opened)
        {
            return [];
        }

        var due = folds
            .SelectMany(p => p.RanksReached.Where(r => r.QualifiedOn >= opened).Select(r => (Progress: p, Reached: r)))
            .Where(d => d.Progress.Rules.GemsFor(d.Reached.Rank) > 0)
            .ToList();
        if (due.Count == 0)
        {
            return [];
        }

        var paid = await ledger.GetPaidKeysAsync(LifeDomains.RankTrigger, cancellationToken);
        var now = clock.UtcNow;
        var writes = new List<LedgerWrite>();
        var rankUps = new List<RankUp>();
        foreach (var (progress, reached) in due)
        {
            var rules = progress.Rules;
            var key = rules.RankAwardKey(reached.Rank);
            if (paid.Contains(key))
            {
                continue;
            }

            // A rank needs XP, so the domain has credit on or before the day it qualified; if a
            // racing reversal just took it, the next reconcile pays the rank instead.
            if (await ledger.GetLatestCreditEventAsync(rules.Currency, reached.QualifiedOn, cancellationToken)
                is not { } cause)
            {
                continue;
            }

            var amounts = new Dictionary<string, long> { [Currencies.Gems] = rules.GemsFor(reached.Rank) };
            var note = $"{LifeDomains.Name(rules.Currency)} {reached.Rule.Title}";
            writes.Add(RankEntry(rules.Currency, key, amounts, note, reached.QualifiedOn, now, cause));
            rankUps.Add(new RankUp(
                new Award(Guid.Empty, null, LedgerEntries.Earn, LifeDomains.RankTrigger, key, amounts, note),
                rules.Currency, reached.Rank));
        }

        if (writes.Count == 0)
        {
            return [];
        }

        var ids = await ledger.AppendAsync(writes, cancellationToken);
        return rankUps.Select((r, i) => r with { Award = r.Award with { EventId = ids[i] } }).ToList();
    }

    /// <summary>
    /// Each configured domain whose shown level, rank or bests moved from <paramref name="before"/>
    /// to <paramref name="after"/> (xp-domains plan §7.4.7). A rank-up paid in
    /// <paramref name="rankUps"/> counts as reached now, even when days settling reached it
    /// earlier: its domain and its best move from below the lowest rank paid, so the rank still
    /// announces.
    /// </summary>
    private static IReadOnlyList<DomainChange> DomainChanges(
        IReadOnlyList<DomainProgress> before, IReadOnlyList<DomainProgress> after, IReadOnlyList<RankUp> rankUps)
    {
        var changes = new List<DomainChange>();
        foreach (var now in after)
        {
            var then = before.First(b => b.Rules.Currency == now.Rules.Currency);
            var (levelFrom, rankFrom, bestLevelFrom, bestRankFrom) = (then.Level, then.Rank, then.BestLevel, then.BestRank);
            var paid = rankUps.Where(r => r.Currency == now.Rules.Currency).Select(r => r.Rank).ToList();
            if (paid.Count > 0)
            {
                var below = paid.Min() - 1;
                var belowTop = now.Rules.Ranks[paid.Min()].StartLevel - 1;
                rankFrom = Math.Min(rankFrom, below);
                levelFrom = Math.Min(levelFrom, belowTop);
                bestRankFrom = Math.Min(bestRankFrom, below);
                bestLevelFrom = Math.Min(bestLevelFrom, belowTop);
            }

            if (levelFrom != now.Level || rankFrom != now.Rank
                || bestLevelFrom != now.BestLevel || bestRankFrom != now.BestRank)
            {
                changes.Add(new DomainChange(
                    now.Rules.Currency,
                    levelFrom,
                    now.Level,
                    rankFrom,
                    now.Rank,
                    now.RankRule.Title,
                    bestLevelFrom,
                    now.BestLevel,
                    bestRankFrom,
                    now.BestRank));
            }
        }

        return changes;
    }

    private Task<DomainCredit> ReadDomainsAsync(CancellationToken cancellationToken)
        => ledger.GetDomainDaysAsync(LifeDomains.Configured.Select(r => r.Currency).ToList(), cancellationToken);

    private static IReadOnlyList<DomainProgress> Fold(DomainCredit credit)
        => LifeDomains.Configured.Select(r => DomainService.Fold(r, credit)).ToList();

    /// <summary>A rank-up award, with its domain and rank (an index into the domain's ranks).</summary>
    private sealed record RankUp(Award Award, string Currency, int Rank);

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

        var bounty = await ReadBountyAsync(subject, cancellationToken);
        var paying = caused.Where(t => bounty.Price(subject.Type, t).Count > 0).ToList();
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
    /// read from one fold of the domains as they stand now.
    /// </summary>
    private async Task<(IReadOnlyList<Award> Awards, IReadOnlyList<DomainProgress>? DomainsBefore)> ReconcileOnceAsync(
        SubjectRef subject, Guid? causeEventId, CancellationToken cancellationToken)
    {
        var due = await ledger.GetDueAsync(subject.Id, cancellationToken);
        var paid = await ledger.GetPaidAsync(subject.Id, cancellationToken);
        var paidKeys = paid.Select(p => p.AwardKey).ToHashSet(StringComparer.Ordinal);
        var dueKeys = due.Select(d => d.AwardKey).ToHashSet(StringComparer.Ordinal);

        var now = clock.UtcNow;
        var writes = new List<LedgerWrite>();
        var awards = new List<Award>();
        IReadOnlyList<DomainProgress>? domains = null;
        async Task<IReadOnlyList<DomainProgress>> DomainsAsync()
            => domains ??= Fold(await ReadDomainsAsync(cancellationToken));

        var unpaid = due.Where(d => !paidKeys.Contains(d.AwardKey)).ToList();
        if (unpaid.Count > 0)
        {
            var bounty = await ReadBountyAsync(subject, cancellationToken);
            foreach (var award in unpaid)
            {
                var price = bounty.Price(subject.Type, award);
                if (price.Count == 0)
                {
                    // Priced to pay nothing: there is nothing to record.
                    continue;
                }

                // Domain XP pays at its credited day's multiplier, frozen in the entry: nothing
                // re-prices it later. A day logged late is priced from the days before it.
                var earn = DomainPricing.CouldMultiply(price)
                    ? PriceOn(price, award, await DomainsAsync())
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
            ? await DomainsAsync()
            : null;
        var ids = await ledger.AppendAsync(writes, cancellationToken);
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

    /// <summary>
    /// A rank-up's earn (xp-domains plan §3.7): no subject, so no <c>concerns</c> edge; the
    /// payload names the domain and says which rank in its note, and it's credited to the day
    /// the rank qualified, citing the domain's latest earn credited by then.
    /// </summary>
    private LedgerWrite RankEntry(
        string currency, string awardKey, IReadOnlyDictionary<string, long> amounts, string note,
        DateOnly qualifiedOn, DateTimeOffset now, Guid cause)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                entry = LedgerEntries.Earn,
                amounts,
                award_key = awardKey,
                trigger = LifeDomains.RankTrigger,
                domain = currency,
                note,
                credited_on = qualifiedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });

        var newEvent = new NewEvent(
            Kind: EventKinds.Ledger,
            Provenance: Provenances.Derived,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            PayloadJson: payload,
            DerivedFrom: [cause]);

        return new LedgerWrite(newEvent, [], GuardBalance: false, AwardKey: awardKey, ExpectPaid: false);
    }

    private async Task<StoredBounty> ReadBountyAsync(SubjectRef subject, CancellationToken cancellationToken)
    {
        var json = await subjects.GetAttributeValueAsync(subject.Id, Bounty.AttributeKey, cancellationToken);

        // A repetition of a repeating Task pays the series' price unless it has its own (D16),
        // so every repetition pays the same and changing the series' price changes the ones
        // completed after.
        if (json is null
            && subject.Type == SubjectTypes.Task
            && await subjects.GetAttributeValueAsync(subject.Id, TaskSeries.SeriesKey, cancellationToken) is { } seriesUrn
            && await subjects.FindByUrnAsync(seriesUrn, cancellationToken) is { } series)
        {
            json = await subjects.GetAttributeValueAsync(series.Id, Bounty.AttributeKey, cancellationToken);
        }

        return new StoredBounty(Bounty.Parse(json), Bounty.ParseEveryMinutes(json) ?? Bounty.DefaultEveryMinutes);
    }

    /// <summary>An item's stored bounty, read once per reconcile.</summary>
    private sealed record StoredBounty(Dictionary<string, Dictionary<string, long>> Amounts, int EveryMinutes)
    {
        /// <summary>
        /// What <paramref name="award"/> pays: its trigger's price, or — for the timer time
        /// on a completed task — that price once per <see cref="EveryMinutes"/> recorded.
        /// </summary>
        public Dictionary<string, long> Price(string type, DueAward award)
        {
            var price = Bounty.Price(type, award.Trigger, Amounts);
            return string.Equals(award.Trigger, BountyTriggers.Timed, StringComparison.OrdinalIgnoreCase)
                ? Bounty.PriceTimed(price, award.TimedSeconds ?? 0, EveryMinutes)
                : price;
        }
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
