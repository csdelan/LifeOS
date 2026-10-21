namespace LifeOs.Domain;

/// <summary>
/// One day's credit in one life domain (xp-domains plan §3.4): what the ledger pays now for
/// the awards credited to that day — <paramref name="Base"/> before any multiplier,
/// <paramref name="Paid"/> as paid.
/// </summary>
public sealed record DomainDay(DateOnly Day, long Base, long Paid);

/// <summary>One period of a domain's walk: its days, its credit, and whether its success rule passed.</summary>
public sealed record DomainPeriod(DateOnly Start, DateOnly End, long Base, long Paid, bool Succeeded);

/// <summary>
/// The successful periods in a window of <paramref name="Days"/> days, <paramref name="From"/>
/// to <paramref name="To"/>. Days before the domain's start count as unsuccessful;
/// <paramref name="CoveredDays"/> says how many of the window's days are on or after the start.
/// </summary>
public sealed record ConsistencyWindow(DateOnly From, DateOnly To, int Days, int CoveredDays, int SuccessfulDays)
{
    /// <summary>The successful share of the window (0–1).</summary>
    public decimal Share => Days == 0 ? 0m : (decimal)SuccessfulDays / Days;

    /// <summary>How many more successful days the window needs to reach <paramref name="share"/>.</summary>
    public int DaysNeededFor(decimal share) => Math.Max(0, (int)Math.Ceiling(share * Days) - SuccessfulDays);
}

/// <summary>
/// A life domain's progression, folded from its daily credit (D12, xp-domains plan §3.5).
/// Every progression number in the app comes from <see cref="Fold"/>; nothing is cached or
/// stored, so a retuned rule or a corrected entry shows on the next read.
/// <para>
/// This is the fold's daily part (phase X3): the periods from the domain's start to today,
/// which of them succeeded, consistency, and the multiplier for any day. Levels and ranks
/// join in phase X4.
/// </para>
/// </summary>
public sealed class DomainProgress
{
    private readonly Dictionary<DateOnly, DomainPeriod> byDay;

    private DomainProgress(
        DomainRules rules, DateOnly today, DateOnly? start, long totalXp, IReadOnlyList<DomainPeriod> periods)
    {
        Rules = rules;
        Today = today;
        Start = start;
        TotalXp = totalXp;
        Periods = periods;
        byDay = periods.ToDictionary(p => p.Start);
        TodayPeriod = byDay.GetValueOrDefault(today);
        Consistency = WindowBefore(today, rules.Multiplier.WindowDays);
        MultiplierNow = rules.Multiplier.For(Consistency.Share);
        NextTier = rules.Multiplier.NextAbove(Consistency.Share);
    }

    public DomainRules Rules { get; }

    /// <summary>Today, in the home zone.</summary>
    public DateOnly Today { get; }

    /// <summary>The first day with paid XP, or null before there is any.</summary>
    public DateOnly? Start { get; }

    /// <summary>All the XP paid, on days up to today.</summary>
    public long TotalXp { get; }

    /// <summary>Every period from the start to today (empty before the start).</summary>
    public IReadOnlyList<DomainPeriod> Periods { get; }

    /// <summary>Today's period, or null before the start.</summary>
    public DomainPeriod? TodayPeriod { get; }

    /// <summary>Today's base XP, which the success rule judges.</summary>
    public long TodayBase => TodayPeriod?.Base ?? 0;

    /// <summary>Whether today has already succeeded.</summary>
    public bool TodaySucceeded => TodayPeriod?.Succeeded ?? false;

    /// <summary>
    /// Consistency now: the successful share of the multiplier's window of complete days
    /// before today (the last 30), days before the start counting as unsuccessful.
    /// </summary>
    public ConsistencyWindow Consistency { get; }

    /// <summary>The multiplier today's XP would be paid at, from <see cref="Consistency"/>.</summary>
    public decimal MultiplierNow { get; }

    /// <summary>The next multiplier tier above now, or null at the top.</summary>
    public MultiplierTier? NextTier { get; }

    /// <summary>
    /// Folds one domain's daily credit under <paramref name="rules"/>, up to
    /// <paramref name="today"/> (in the home zone). Credit on days after today hasn't
    /// happened yet and is left out.
    /// </summary>
    public static DomainProgress Fold(DomainRules rules, IEnumerable<DomainDay> days, DateOnly today)
    {
        if (rules.Cadence != Cadence.Daily)
        {
            throw new NotSupportedException($"Only a daily cadence is built; {rules.Currency} has {rules.Cadence}.");
        }

        var credit = days
            .Where(d => d.Day <= today)
            .GroupBy(d => d.Day)
            .ToDictionary(g => g.Key, g => (Base: g.Sum(d => d.Base), Paid: g.Sum(d => d.Paid)));
        var paidDays = credit.Where(kv => kv.Value.Paid > 0).Select(kv => kv.Key).ToList();
        if (paidDays.Count == 0)
        {
            return new DomainProgress(rules, today, start: null, totalXp: 0, periods: []);
        }

        var start = paidDays.Min();
        var periods = new List<DomainPeriod>(today.DayNumber - start.DayNumber + 1);
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            var (dayBase, dayPaid) = credit.GetValueOrDefault(day);
            var succeeded = rules.Success.Succeeded(new PeriodFacts(day, day, dayBase, dayPaid));
            periods.Add(new DomainPeriod(day, day, dayBase, dayPaid, succeeded));
        }

        return new DomainProgress(rules, today, start, periods.Sum(p => p.Paid), periods);
    }

    /// <summary>Whether <paramref name="day"/> succeeded; days before the start and after today never did.</summary>
    public bool SucceededOn(DateOnly day) => byDay.TryGetValue(day, out var period) && period.Succeeded;

    /// <summary>
    /// The <paramref name="days"/> complete days before <paramref name="day"/>. The multiplier
    /// reads this window: Monday's XP, even logged on Friday, is priced from the days before
    /// Monday.
    /// </summary>
    public ConsistencyWindow WindowBefore(DateOnly day, int days) => Window(day.AddDays(-days), day.AddDays(-1));

    /// <summary>
    /// The <paramref name="days"/> days ending on <paramref name="end"/>, which rank gates read.
    /// Today counts only once it has succeeded; until then a window ending today ends the day
    /// before, so today can only help.
    /// </summary>
    public ConsistencyWindow WindowEndingOn(DateOnly end, int days)
    {
        if (end >= Today && !TodaySucceeded)
        {
            end = Today.AddDays(-1);
        }
        else if (end > Today)
        {
            end = Today;
        }

        return Window(end.AddDays(1 - days), end);
    }

    /// <summary>The multiplier for XP credited to <paramref name="day"/> (§3.6): the tier of the window before it.</summary>
    public decimal MultiplierFor(DateOnly day) => Rules.Multiplier.For(WindowBefore(day, Rules.Multiplier.WindowDays).Share);

    private ConsistencyWindow Window(DateOnly from, DateOnly to)
    {
        var days = Math.Max(0, to.DayNumber - from.DayNumber + 1);
        var covered = 0;
        var successful = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (Start is { } start && day >= start)
            {
                covered++;
            }

            if (SucceededOn(day))
            {
                successful++;
            }
        }

        return new ConsistencyWindow(from, to, days, covered, successful);
    }
}
