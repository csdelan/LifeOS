using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;

namespace LifeOs.Api.Read;

/// <summary>
/// The rewards reads (REWARD-1 / REWARD-3 / REWARD-5) on the <c>bsk_reader</c> connection,
/// like every other API read. It reuses the ledger repository's folds of
/// <c>bsk.v_wallet</c>, <c>bsk.v_ledger</c> and <c>bsk.v_reward</c> — only their SELECTs,
/// which the reader role is granted — so the wallet, the history and the catalog read
/// exactly as <c>bsk wallet</c>, <c>bsk ledger</c> and <c>bsk rewards list</c> do. No
/// writes live here; redeeming goes through <see cref="RewardService"/>.
/// <para>
/// The life domains (REWARD-6 / REWARD-7) fold <c>bsk.v_award_credit</c> through
/// <see cref="DomainService"/>, on the days of <paramref name="homeTimeZone"/>
/// (<c>Rewards:HomeTimeZone</c>), as <c>bsk domains</c> does.
/// </para>
/// </summary>
public sealed class RewardReader(string readerConnectionString, decimal coinValueUsd, string homeTimeZone = HomeZone.Default)
{
    private readonly NpgsqlLedgerRepository ledger = new(readerConnectionString, homeTimeZone);

    public async Task<WalletSummary> GetWalletAsync(CancellationToken cancellationToken = default)
    {
        var openedAt = await ledger.GetOpenedAtAsync(cancellationToken);
        var balances = await ledger.GetBalancesAsync(cancellationToken);
        return RewardRows.From(Wallet.From(openedAt, balances, coinValueUsd));
    }

    /// <summary>Entries newest first, within [<paramref name="from"/>, <paramref name="to"/>), at most <paramref name="limit"/>.</summary>
    public async Task<IReadOnlyList<LedgerRow>> GetLedgerAsync(
        DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken cancellationToken = default)
    {
        if (from is { } start && to is { } end && start >= end)
        {
            throw new ArgumentException("The ledger range must end after it starts.");
        }

        var entries = await ledger.GetEntriesAsync(from, to, limit, cancellationToken);
        return entries.Select(RewardRows.From).ToList();
    }

    /// <summary>
    /// The history by local day in <paramref name="timeZone"/>, oldest first — the same read
    /// as <see cref="RewardService.GetDailyAsync"/>, on the reader connection.
    /// </summary>
    public async Task<IReadOnlyList<LedgerDayRow>> GetDailyAsync(
        DateOnly from, DateOnly to, string timeZone, CancellationToken cancellationToken = default)
    {
        LedgerDays.Validate(from, to, timeZone);
        var totals = await ledger.GetDailyTotalsAsync(from, to, timeZone, cancellationToken);
        return LedgerDays.Fold(totals).Select(RewardRows.From).ToList();
    }

    public async Task<IReadOnlyList<RewardRow>> GetRewardsAsync(
        bool includeArchived, CancellationToken cancellationToken = default)
        => (await ledger.GetCatalogAsync(includeArchived, cancellationToken)).Select(RewardRows.From).ToList();

    /// <summary>Every life domain as it reads now (<c>GET /api/domains</c>).</summary>
    public async Task<DomainsRead> GetDomainsAsync(CancellationToken cancellationToken = default)
        => DomainRows.From(await new DomainService(ledger).GetAsync(cancellationToken));

    /// <summary>
    /// One life domain's last <paramref name="days"/> days, today included
    /// (<c>GET /api/domains/{key}/days</c>). Throws <see cref="ArgumentException"/> for
    /// something that isn't a life domain, or a day count outside 1 to
    /// <see cref="DomainRows.MaxDays"/>.
    /// </summary>
    public async Task<DomainDaysRead> GetDomainDaysAsync(
        string domain, int days, CancellationToken cancellationToken = default)
    {
        if (days > DomainRows.MaxDays)
        {
            throw new ArgumentException($"Ask for at most {DomainRows.MaxDays} days; got {days}.");
        }

        return DomainRows.From(await new DomainService(ledger).GetDaysAsync(domain, days, cancellationToken));
    }

    /// <summary>
    /// Every life domain over its last <paramref name="days"/> days (xp-domains plan §3.13), at
    /// most <see cref="DomainRows.MaxDays"/>.
    /// </summary>
    public async Task<DomainReportRead> GetDomainReportAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days > DomainRows.MaxDays)
        {
            throw new ArgumentException($"Ask for at most {DomainRows.MaxDays} days; got {days}.");
        }

        return DomainRows.From(await new DomainService(ledger).GetReportAsync(days, cancellationToken));
    }
}
