using System.Globalization;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// The life domains' progression where it meets the ledger (xp-domains plan §3.7, §7.4): it folds
/// the configured domains, pays each rank-up they have reached, and says which domains' level or
/// rank moved. <see cref="RewardService"/> prices and pays an item; this pays what the domains
/// reach, after an item's earn, a food or activity day, an exception day or a room rating moved
/// them, or time alone did (code review CR48).
/// </summary>
public sealed class RankUpService(
    ILedgerRepository ledger, IDomainCreditReader credit, IClock clock, EventSource eventSource)
{
    // A payment that loses a race on a rank re-reads and tries again.
    private const int ReconcileAttempts = 3;

    /// <summary>
    /// Pays each rank-up the configured life domains have reached (xp-domains plan §3.7, §7.4.6):
    /// <see cref="RankUpGems"/> once per rank, under the award key
    /// <c>rank:&lt;domain&gt;:&lt;start level&gt;</c>, the first time the best rank reached it, on
    /// or after the day the wallet opened. It earns only: a rank award is never reversed, so
    /// losing a rank and reaching it again never pays twice, and a rank paid under X4's gates is
    /// never paid again. Returns the entries written.
    /// </summary>
    public async Task<IReadOnlyList<Award>> PayAsync(CancellationToken cancellationToken = default)
        => (await PayRankUpsAsync(cancellationToken)).Awards.Select(r => r.Award).ToList();

    /// <summary>
    /// <see cref="PayAsync"/> for <c>POST /api/domains/settle</c>, which the
    /// Character page calls when it opens: a rank reached by time alone (an old miss leaving
    /// the window, the domain turning old enough) pays then, and its domain's change comes back
    /// so it can be celebrated.
    /// </summary>
    public async Task<Earned> SettleAsync(CancellationToken cancellationToken = default)
    {
        var (ranks, folds) = await PayRankUpsAsync(cancellationToken);
        return new Earned(ranks.Select(r => r.Award).ToList(), DomainChanges(folds, folds, ranks));
    }

    /// <summary>
    /// Runs <paramref name="write"/>, a change to what the life-domain fold reads beside the
    /// ledger (an exception day, xp-domains plan §7.4.3), between two folds: any rank-up the
    /// change unlocks is paid, and each configured domain whose level or rank moved comes back,
    /// as an earning write answers.
    /// </summary>
    public async Task<Earned> AroundAsync(
        Func<CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        var before = await FoldAsync(cancellationToken);
        await write(cancellationToken);
        return await PayAfterAsync(before, cancellationToken);
    }

    /// <summary>
    /// After a write that moved the domains' credit: pays any rank-up now reached, and answers it
    /// with each configured domain whose level or rank moved since <paramref name="before"/>.
    /// </summary>
    public async Task<Earned> PayAfterAsync(
        IReadOnlyList<DomainProgress> before, CancellationToken cancellationToken = default)
    {
        var (ranks, after) = await PayRankUpsAsync(cancellationToken);
        return new Earned(ranks.Select(r => r.Award).ToList(), DomainChanges(before, after, ranks));
    }

    /// <summary>Folds the configured domains, then pays their rank-ups, re-reading after a lost race.</summary>
    private async Task<(IReadOnlyList<RankUp> Awards, IReadOnlyList<DomainProgress> Folds)> PayRankUpsAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var read = await ReadDomainsAsync(cancellationToken);
            var folds = Fold(read);
            try
            {
                return (await PayRankUpsAsync(read.OpenedOn, folds, cancellationToken), folds);
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
            if (await credit.GetLatestCreditEventAsync(rules.Currency, reached.QualifiedOn, cancellationToken)
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
        => credit.GetDomainDaysAsync(LifeDomains.Configured.Select(r => r.Currency).ToList(), cancellationToken);

    private static IReadOnlyList<DomainProgress> Fold(DomainCredit read)
        => LifeDomains.Configured.Select(r => DomainService.Fold(r, read)).ToList();

    /// <summary>The configured domains folded as they stand now.</summary>
    public async Task<IReadOnlyList<DomainProgress>> FoldAsync(CancellationToken cancellationToken = default)
        => Fold(await ReadDomainsAsync(cancellationToken));

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
            SourceId: eventSource.Id,
            PayloadJson: payload,
            DerivedFrom: [cause]);

        return new LedgerWrite(newEvent, [], GuardBalance: false, AwardKey: awardKey, ExpectPaid: false);
    }

    /// <summary>A rank-up award, with its domain and rank (an index into the domain's ranks).</summary>
    private sealed record RankUp(Award Award, string Currency, int Rank);
}
