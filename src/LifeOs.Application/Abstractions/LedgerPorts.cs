using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// The rewards ledger (D11).

/// <summary>
/// Reads and appends the rewards ledger (REWARD-1 / D11). Nothing about a wallet is
/// stored except its append-only <c>ledger</c> events; balances and history are folded
/// by <c>bsk.v_wallet</c> / <c>bsk.v_ledger</c>. Every write is serialized under one
/// wallet-wide lock, so a balance check and the redemption it guards cannot interleave
/// with another write.
/// </summary>
public interface ILedgerRepository
{
    /// <summary>The totals of every currency the ledger has touched; untouched currencies have no row.</summary>
    Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The entries that carry amounts, newest first, optionally within
    /// [<paramref name="from"/>, <paramref name="to"/>) and capped at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<LedgerEntry>> GetEntriesAsync(
        DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Each local day's totals per currency for the days [<paramref name="from"/>,
    /// <paramref name="to"/>), read in <paramref name="timeZone"/> (an IANA name such as
    /// <c>America/Chicago</c>): an entry falls on the date its <c>occurred_at</c> reads there,
    /// so an evening completion is filed on its own evening, not the next UTC day. Days and
    /// currencies without entries have no row. Throws <see cref="ArgumentException"/> for a
    /// time zone the database doesn't know.
    /// </summary>
    Task<IReadOnlyList<LedgerDayTotal>> GetDailyTotalsAsync(
        DateOnly from, DateOnly to, string timeZone, CancellationToken cancellationToken = default);

    /// <summary>
    /// The award keys with <paramref name="trigger"/> the ledger currently holds (keys that net
    /// to non-zero) — what a subject-less award, a rank-up, has been paid.
    /// </summary>
    Task<IReadOnlySet<string>> GetPaidKeysAsync(string trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every award key with one of <paramref name="triggers"/> the ledger has ever written,
    /// paid now or not, with its net and latest earn — what a re-pricing reconcile compares
    /// against (food days, food-log plan §3.3).
    /// </summary>
    Task<IReadOnlyList<KeyedAward>> GetKeyedAwardsAsync(
        IReadOnlyCollection<string> triggers, CancellationToken cancellationToken = default);

    /// <summary>When the wallet was opened (its earliest <c>open</c> entry), or <c>null</c> before launch.</summary>
    Task<DateTimeOffset?> GetOpenedAtAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="openEvent"/> unless the wallet is already open, under the
    /// wallet lock. Returns when the wallet opened and whether this call opened it.
    /// </summary>
    Task<(DateTimeOffset OpenedAt, bool Created)> OpenAsync(
        NewEvent openEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends ledger entries, each event with its edges, as one transaction under the
    /// wallet lock. If a <see cref="LedgerWrite.GuardBalance"/> entry would leave any of
    /// its currencies below zero, nothing is written and
    /// <see cref="InsufficientBalanceException"/> is thrown. If an entry's
    /// <see cref="LedgerWrite.AwardKey"/> is no longer in its
    /// <see cref="LedgerWrite.ExpectPaid"/> state, or its latest earn is no longer
    /// <see cref="LedgerWrite.ExpectLatestEarn"/>, nothing is written and
    /// <see cref="LedgerConflictException"/> is thrown. Entries are checked in order, each
    /// against the ledger with the entries before it applied. Returns the new event ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<LedgerWrite> writes, CancellationToken cancellationToken = default);

    /// <summary>The awards <c>bsk.v_reward_due</c> says the subject is due right now.</summary>
    Task<IReadOnlyList<DueAward>> GetDueAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every award the record supports for the subject, whatever the wallet's start line
    /// (<c>bsk.v_reward_trigger</c>) — read only to open the wallet at the first trigger
    /// it pays.
    /// </summary>
    Task<IReadOnlyList<DueAward>> GetTriggersAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>The awards the ledger currently holds for the subject (keys that net to non-zero).</summary>
    Task<IReadOnlyList<PaidAward>> GetPaidAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subject reconcile could change: those with an award due, and those with an
    /// award paid — the sweep behind <c>bsk rewards reconcile</c>.
    /// </summary>
    Task<IReadOnlyList<SubjectRef>> GetReconcileCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest non-ledger event about the subject (a status change, an adherence
    /// recording, a timer action), or <c>null</c> — what a reversal cites when no
    /// triggering write is at hand.
    /// </summary>
    Task<Guid?> GetLatestSubjectEventAsync(Guid subjectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The reward catalog's reads (<c>bsk.v_reward</c>, REWARD-3): what the catalog service and a
/// redemption need of the ledger, and nothing that writes it.
/// </summary>
public interface IRewardCatalogReader
{
    /// <summary>
    /// The reward catalog (<c>bsk.v_reward</c>, REWARD-3) by name, with what has been
    /// redeemed against each Reward; archived Rewards only when asked.
    /// </summary>
    Task<IReadOnlyList<CatalogReward>> GetCatalogAsync(
        bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>One Reward's catalog row, archived or not, or <c>null</c> when no Reward has that id.</summary>
    Task<CatalogReward?> GetCatalogRewardAsync(Guid rewardId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The life-domain credits a fold reads (<c>bsk.v_award_credit</c>, xp-domains plan §3.4): what
/// the domain, Home Score and exception-day reads need of the ledger, and nothing that writes it.
/// </summary>
public interface IDomainCreditReader
{
    /// <summary>
    /// The credit of each of <paramref name="currencies"/> per local day (<c>bsk.v_award_credit</c>,
    /// xp-domains plan §3.4): every currently paid award counts on its latest earn's credited
    /// day, or, for an earn from before credited days, on its instant's date in the home zone.
    /// Days without credit have no row. Also answers today's date in the home zone, so a fold
    /// and its data agree on what today is, and the day the wallet opened there.
    /// <para>
    /// Each day splits out its passive base XP (<see cref="LifeDomains.PassiveTriggers"/>), and
    /// the answer carries the days marked as exceptions (<c>bsk.v_exception_day</c>) and the
    /// last settled day, all read together (xp-domains plan §7.4.2), so every fold sees the
    /// same inputs.
    /// </para>
    /// </summary>
    Task<DomainCredit> GetDomainDaysAsync(
        IReadOnlyCollection<string> currencies, CancellationToken cancellationToken = default);

    /// <summary>
    /// Today's date in the home zone and the days marked as exceptions among the last
    /// <paramref name="days"/> (today included), newest first (<c>bsk.v_exception_day</c>,
    /// REWARD-8).
    /// </summary>
    Task<(DateOnly Today, IReadOnlyList<ExceptionDay> Days)> GetExceptionDaysAsync(
        int days, CancellationToken cancellationToken = default);

    /// <summary>
    /// The earn behind the latest credit of <paramref name="currency"/> on or before
    /// <paramref name="day"/> (<c>bsk.v_award_credit</c>), or null — what a rank-up cites.
    /// </summary>
    Task<Guid?> GetLatestCreditEventAsync(
        string currency, DateOnly day, CancellationToken cancellationToken = default);
}
