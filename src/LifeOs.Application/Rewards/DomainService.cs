using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// One life domain as it reads now: its currency, the XP it has collected, and — for a
/// configured domain — its progression folded under its rules. A domain without rules
/// collects XP only.
/// </summary>
public sealed record DomainReading(Currency Currency, long TotalXp, DomainProgress? Progress)
{
    /// <summary>Whether the domain has rules (only Health in the pilot).</summary>
    public bool Configured => Progress is not null;
}

/// <summary>Every life domain, in display order, as of <see cref="Today"/> in the home zone.</summary>
public sealed record DomainsReading(DateOnly Today, IReadOnlyList<DomainReading> Domains);

/// <summary>
/// One day of a domain's series: its credit, and whether it succeeded — null for a domain
/// without rules, which has no success test.
/// </summary>
public sealed record DomainSeriesDay(DateOnly Day, long Base, long Paid, bool? Succeeded);

/// <summary>A domain's reading and its daily series up to today.</summary>
public sealed record DomainSeries(DomainReading Domain, IReadOnlyList<DomainSeriesDay> Days);

/// <summary>
/// One life domain over a report's period: the XP credited to the period's days and, for a
/// configured domain, its period report (whose days end yesterday until today succeeds). A
/// domain without rules reports the XP credited to the period's days ending today.
/// </summary>
public sealed record DomainReportEntry(
    DomainReading Domain, DateOnly From, DateOnly To, long Base, long Paid, DomainPeriodReport? Period);

/// <summary>Every life domain over its last <see cref="Days"/> days, as of <see cref="Today"/> in the home zone.</summary>
public sealed record DomainReportReading(DateOnly Today, int Days, IReadOnlyList<DomainReportEntry> Domains);

/// <summary>
/// A configured life domain whose level or rank a write changed (xp-domains plan §3.8): its
/// currency, the level and rank (an index into its ranks) before and after, and the title of
/// the rank after.
/// </summary>
public sealed record DomainChange(string Domain, int LevelFrom, int LevelTo, int RankFrom, int RankTo, string RankTitle);

/// <summary>
/// Reads life-domain progression (REWARD-6 / REWARD-7, D12, xp-domains plan §3.4–3.5):
/// the ledger's daily credit per domain, folded under each configured domain's rules on every
/// read. Nothing here is stored, so a retuned rule or a corrected entry shows at once.
/// </summary>
public sealed class DomainService(ILedgerRepository ledger)
{
    /// <summary>Every life domain, configured or not.</summary>
    public async Task<DomainsReading> GetAsync(CancellationToken cancellationToken = default)
    {
        var credit = await ledger.GetDomainDaysAsync(
            Currencies.LifeDomains.Select(c => c.Key).ToList(), cancellationToken);
        return new DomainsReading(credit.Today, Currencies.LifeDomains.Select(c => Read(c, credit)).ToList());
    }

    /// <summary>
    /// One domain (see <see cref="Resolve"/>) and its last <paramref name="days"/> days, today
    /// included. Throws <see cref="ArgumentException"/> for something that isn't a life domain
    /// or a day count below 1.
    /// </summary>
    public async Task<DomainSeries> GetDaysAsync(
        string domain, int days, CancellationToken cancellationToken = default)
    {
        if (days < 1)
        {
            throw new ArgumentException($"Ask for at least 1 day; got {days}.");
        }

        var currency = Resolve(domain);
        var credit = await ledger.GetDomainDaysAsync([currency.Key], cancellationToken);
        var reading = Read(currency, credit);
        var from = credit.Today.AddDays(1 - days);

        IEnumerable<DomainSeriesDay> series;
        if (reading.Progress is { } progress)
        {
            var periods = progress.Periods.ToDictionary(p => p.Start);
            series = Dates(from, credit.Today).Select(day => periods.TryGetValue(day, out var p)
                ? new DomainSeriesDay(day, p.Base, p.Paid, p.Succeeded)
                : new DomainSeriesDay(day, 0, 0, false));
        }
        else
        {
            var byDay = credit.Days.ToDictionary(d => d.Day);
            series = Dates(from, credit.Today).Select(day => byDay.TryGetValue(day, out var d)
                ? new DomainSeriesDay(day, d.Base, d.Paid, null)
                : new DomainSeriesDay(day, 0, 0, null));
        }

        return new DomainSeries(reading, series.ToList());
    }

    /// <summary>
    /// Every life domain over its last <paramref name="days"/> days (xp-domains plan §3.11,
    /// §3.13): a configured domain's consistency over the days it has history for, its XP and
    /// the levels and ranks it gained; a domain without rules, its XP. Throws
    /// <see cref="ArgumentException"/> for a day count below 1.
    /// </summary>
    public async Task<DomainReportReading> GetReportAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days < 1)
        {
            throw new ArgumentException($"Ask for at least 1 day; got {days}.");
        }

        var credit = await ledger.GetDomainDaysAsync(
            Currencies.LifeDomains.Select(c => c.Key).ToList(), cancellationToken);
        var entries = Currencies.LifeDomains.Select(currency =>
        {
            var reading = Read(currency, credit);
            if (reading.Progress is { } progress)
            {
                var period = progress.Report(days);
                return new DomainReportEntry(reading, period.From, period.To, period.Base, period.Paid, period);
            }

            var from = credit.Today.AddDays(1 - days);
            var inPeriod = DaysOf(currency.Key, credit).Where(d => d.Day >= from && d.Day <= credit.Today).ToList();
            return new DomainReportEntry(reading, from, credit.Today, inPeriod.Sum(d => d.Base), inPeriod.Sum(d => d.Paid), null);
        }).ToList();
        return new DomainReportReading(credit.Today, days, entries);
    }

    /// <summary>
    /// The life domain <paramref name="reference"/> names: its key (<c>health_xp</c>), its label
    /// (<c>Health XP</c>) or its first word (<c>health</c>), in any case.
    /// </summary>
    public static Currency Resolve(string reference)
    {
        var wanted = reference.Trim();
        return Currencies.LifeDomains.FirstOrDefault(c =>
                   string.Equals(c.Key, wanted, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(c.Label, wanted, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(c.Label.Split(' ')[0], wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(
                   $"'{reference}' isn't a life domain. Expected one of: {string.Join(", ", Currencies.LifeDomains.Select(c => c.Key))}.");
    }

    /// <summary>One configured domain's progression, folded from <paramref name="credit"/>.</summary>
    public static DomainProgress Fold(DomainRules rules, DomainCredit credit)
        => DomainProgress.Fold(rules, DaysOf(rules.Currency, credit), credit.Today);

    private static DomainReading Read(Currency currency, DomainCredit credit)
    {
        if (LifeDomains.For(currency.Key) is { } rules)
        {
            var progress = Fold(rules, credit);
            return new DomainReading(currency, progress.TotalXp, progress);
        }

        // XP only: what's been paid on days up to today, as a configured domain counts it.
        return new DomainReading(
            currency, DaysOf(currency.Key, credit).Where(d => d.Day <= credit.Today).Sum(d => d.Paid), null);
    }

    private static IEnumerable<DomainDay> DaysOf(string currency, DomainCredit credit)
        => credit.Days.Where(d => d.Currency == currency).Select(d => new DomainDay(d.Day, d.Base, d.Paid, d.Passive));

    private static IEnumerable<DateOnly> Dates(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
