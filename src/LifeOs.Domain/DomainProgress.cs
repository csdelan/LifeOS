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

    /// <summary>Whether every day of the window is on or after the domain's start.</summary>
    public bool Covered => CoveredDays == Days;

    /// <summary>The successful days a share of <paramref name="share"/> takes in this window.</summary>
    public int Required(decimal share) => (int)Math.Ceiling(share * Days);

    /// <summary>Whether the window's successful share is at or above <paramref name="share"/>.</summary>
    public bool Reaches(decimal share) => SuccessfulDays >= Required(share);

    /// <summary>How many more successful days the window needs to reach <paramref name="share"/>.</summary>
    public int DaysNeededFor(decimal share) => Math.Max(0, Required(share) - SuccessfulDays);
}

/// <summary>Where a domain stood at the end of <paramref name="Day"/>: its XP, raw level, level and rank (an index into the ranks).</summary>
public sealed record DomainStanding(DateOnly Day, long TotalXp, int RawLevel, int Level, int Rank);

/// <summary>A rank the walk reached (an index into the ranks, never Novice), and the day it qualified.</summary>
public sealed record RankReached(int Rank, RankRule Rule, DateOnly QualifiedOn);

/// <summary>
/// A configured domain over its last <paramref name="Days"/> days (xp-domains plan §3.11,
/// §3.13). The days run <paramref name="From"/> to <paramref name="To"/>, ending today once
/// today has succeeded and yesterday until then, as rank gates read them, so an unfinished
/// today never counts against it. <paramref name="DaysCounted"/> is how many of them are on or
/// after the domain's start; consistency and the average are over those days, so a young
/// domain's figures say how much history backs them. <paramref name="Before"/> is where the
/// domain stood at the end of the day before <paramref name="From"/>, <paramref name="Now"/>
/// where it stands today, and <paramref name="RanksReached"/> the ranks entered since
/// <paramref name="From"/>.
/// </summary>
public sealed record DomainPeriodReport(
    int Days,
    DateOnly From,
    DateOnly To,
    int DaysCounted,
    int SuccessfulDays,
    long Base,
    long Paid,
    DomainStanding Before,
    DomainStanding Now,
    IReadOnlyList<RankReached> RanksReached)
{
    /// <summary>The successful share of the days counted (0–1); 0 before the start.</summary>
    public decimal Consistency => DaysCounted == 0 ? 0m : (decimal)SuccessfulDays / DaysCounted;

    /// <summary>The base XP a counted day earned, on average; 0 before the start.</summary>
    public decimal AverageBase => DaysCounted == 0 ? 0m : (decimal)Base / DaysCounted;

    /// <summary>Whether every day of the period counted: the domain is at least as old as the period.</summary>
    public bool FullHistory => DaysCounted == Days;
}

/// <summary>
/// The next rank's gate as it reads today (xp-domains plan §3.5): its rule, the window ending
/// today (today counting once it has succeeded), and the domain's age in counted days.
/// <paramref name="LevelReached"/> says whether the raw level has reached the rank's start.
/// </summary>
public sealed record NextRank(int Rank, RankRule Rule, ConsistencyWindow Window, int DaysOld, bool LevelReached)
{
    /// <summary>The successful days the bar takes over the window, e.g. 18 for 60% of 30.</summary>
    public int Required => Window.Required(Rule.MinConsistency);

    /// <summary>The successful days the window still needs.</summary>
    public int DaysNeeded => Window.DaysNeededFor(Rule.MinConsistency);

    /// <summary>Whether the domain is at least as old as the window.</summary>
    public bool OldEnough => DaysOld >= Rule.WindowDays;

    /// <summary>Whether the window's share is at or above the bar.</summary>
    public bool BarMet => Window.Reaches(Rule.MinConsistency);
}

/// <summary>
/// A life domain's progression, folded from its daily credit (D12, xp-domains plan §3.5).
/// Every progression number in the app comes from <see cref="Fold"/>; nothing is cached or
/// stored, so a retuned rule or a corrected entry shows on the next read.
/// <para>
/// The fold walks each day from the domain's start to today: whether it succeeded, the XP
/// so far and the level it reaches, and the rank held. A rank is entered on the first day its
/// level is reached, the domain is as old as its window, and the window's successful share is
/// at its bar; several can be entered on one day. Once entered, a rank holds for every later
/// day, so inactivity never costs one — only a changed past (a retuned rule, a corrected
/// entry) can. The level stops at the top of the rank held; XP beyond it is banked.
/// </para>
/// </summary>
public sealed class DomainProgress
{
    // successes[i] is how many of the first i periods succeeded, so any window is O(1).
    private readonly int[] successes;
    private readonly List<DomainStanding> standings;
    private readonly List<RankReached> reached = [];

    private DomainProgress(DomainRules rules, DateOnly today, DateOnly? start, IReadOnlyList<DomainPeriod> periods)
    {
        Rules = rules;
        Today = today;
        Start = start;
        Periods = periods;
        successes = new int[periods.Count + 1];
        for (var i = 0; i < periods.Count; i++)
        {
            successes[i + 1] = successes[i] + (periods[i].Succeeded ? 1 : 0);
        }

        // Periods run from the start to today, so today's is the last.
        TodayPeriod = periods.Count > 0 ? periods[^1] : null;
        Consistency = WindowBefore(today, rules.Multiplier.WindowDays);
        MultiplierNow = rules.Multiplier.For(Consistency.Share);
        NextTier = rules.Multiplier.NextAbove(Consistency.Share);

        standings = Walk();
        var now = standings.Count > 0 ? standings[^1] : new DomainStanding(today, 0, 1, 1, 0);
        TotalXp = now.TotalXp;
        RawLevel = now.RawLevel;
        Level = now.Level;
        Rank = now.Rank;

        var curve = rules.Curve;
        if (Capped)
        {
            XpIntoLevel = curve.CostToNext(Level);
            BankedXp = TotalXp - curve.TotalFor(Level + 1);
        }
        else
        {
            XpIntoLevel = TotalXp - curve.TotalFor(Level);
            XpToNext = Level == curve.MaxLevel ? 0 : curve.TotalFor(Level + 1) - TotalXp;
        }

        if (Rank + 1 < rules.Ranks.Count)
        {
            var rule = rules.Ranks[Rank + 1];
            Next = new NextRank(Rank + 1, rule, WindowEndingOn(today, rule.WindowDays), DaysOld, RawLevel >= rule.StartLevel);
        }
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

    /// <summary>Where the domain stood at the end of each period, alongside <see cref="Periods"/>.</summary>
    public IReadOnlyList<DomainStanding> Standings => standings;

    /// <summary>Today's period, or null before the start.</summary>
    public DomainPeriod? TodayPeriod { get; }

    /// <summary>Today's base XP, which the success rule judges.</summary>
    public long TodayBase => TodayPeriod?.Base ?? 0;

    /// <summary>Whether today has already succeeded.</summary>
    public bool TodaySucceeded => TodayPeriod?.Succeeded ?? false;

    /// <summary>
    /// The domain's age for rank gates: the days from its start through today, today counting
    /// once it has succeeded (0 before the start).
    /// </summary>
    public int DaysOld => Start is { } start
        ? Math.Max(0, (TodaySucceeded ? Today : Today.AddDays(-1)).DayNumber - start.DayNumber + 1)
        : 0;

    /// <summary>The level <see cref="TotalXp"/> reaches on the curve, before the rank's cap.</summary>
    public int RawLevel { get; }

    /// <summary>The level: <see cref="RawLevel"/>, stopped at the top of the rank held.</summary>
    public int Level { get; }

    /// <summary>The rank held, an index into the rules' ranks (0 is Novice).</summary>
    public int Rank { get; }

    /// <summary>The rank held.</summary>
    public RankRule RankRule => Rules.Ranks[Rank];

    /// <summary>The day the rank held qualified, or null for Novice.</summary>
    public DateOnly? RankQualifiedOn => reached.Count > 0 ? reached[^1].QualifiedOn : null;

    /// <summary>Every rank above Novice the walk entered, in order, with the day each qualified.</summary>
    public IReadOnlyList<RankReached> RanksReached => reached;

    /// <summary>The highest level the rank held allows (9 for Novice).</summary>
    public int TopLevel => Rules.TopLevelOf(Rank);

    /// <summary>Whether the rank's top holds the level below what the XP reaches.</summary>
    public bool Capped => RawLevel > Level;

    /// <summary>The XP into the current level; the whole level while capped.</summary>
    public long XpIntoLevel { get; }

    /// <summary>The XP still to go to the next level; 0 while capped and at the top level.</summary>
    public long XpToNext { get; }

    /// <summary>While capped, the XP beyond what the next level takes; otherwise 0.</summary>
    public long BankedXp { get; }

    /// <summary>The next rank's gate, or null at the last rank.</summary>
    public NextRank? Next { get; }

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

        var credit = new Dictionary<DateOnly, (long Base, long Paid)>();
        foreach (var day in days)
        {
            if (day.Day <= today)
            {
                var (dayBase, dayPaid) = credit.GetValueOrDefault(day.Day);
                credit[day.Day] = (dayBase + day.Base, dayPaid + day.Paid);
            }
        }

        var paidDays = credit.Where(kv => kv.Value.Paid > 0).Select(kv => kv.Key).ToList();
        if (paidDays.Count == 0)
        {
            return new DomainProgress(rules, today, start: null, periods: []);
        }

        var start = paidDays.Min();
        var periods = new List<DomainPeriod>(today.DayNumber - start.DayNumber + 1);
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            var (dayBase, dayPaid) = credit.GetValueOrDefault(day);
            var succeeded = rules.Success.Succeeded(new PeriodFacts(day, day, dayBase, dayPaid));
            periods.Add(new DomainPeriod(day, day, dayBase, dayPaid, succeeded));
        }

        return new DomainProgress(rules, today, start, periods);
    }

    /// <summary>Whether <paramref name="day"/> succeeded; days before the start and after today never did.</summary>
    public bool SucceededOn(DateOnly day) => IndexOf(day) is { } i && Periods[i].Succeeded;

    /// <summary>
    /// Where the domain stood at the end of <paramref name="day"/>: level 1 and Novice before
    /// the start, and as it stands today for any later day.
    /// </summary>
    public DomainStanding StandingOn(DateOnly day)
    {
        if (Start is not { } start || day < start)
        {
            return new DomainStanding(day, 0, 1, 1, 0);
        }

        return standings[Math.Min(day.DayNumber - start.DayNumber, standings.Count - 1)] with { Day = day };
    }

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

    /// <summary>
    /// The domain over its last <paramref name="days"/> days (§3.13): the window a rank gate of
    /// that length would read today, the XP credited to it, and the level and rank it started
    /// from against now.
    /// </summary>
    public DomainPeriodReport Report(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var window = WindowEndingOn(Today, days);
        long dayBase = 0;
        long paid = 0;
        foreach (var period in Periods)
        {
            if (period.Start >= window.From && period.End <= window.To)
            {
                dayBase += period.Base;
                paid += period.Paid;
            }
        }

        return new DomainPeriodReport(
            days,
            window.From,
            window.To,
            window.CoveredDays,
            window.SuccessfulDays,
            dayBase,
            paid,
            StandingOn(window.From.AddDays(-1)),
            StandingOn(Today),
            reached.Where(r => r.QualifiedOn >= window.From).ToList());
    }

    /// <summary>Walks every period from the start, carrying the XP so far and the rank held.</summary>
    private List<DomainStanding> Walk()
    {
        var walked = new List<DomainStanding>(Periods.Count);
        var curve = Rules.Curve;
        var ranks = Rules.Ranks;
        long total = 0;
        var rank = 0;
        foreach (var period in Periods)
        {
            total += period.Paid;
            var rawLevel = curve.LevelFor(total);
            while (rank + 1 < ranks.Count && Qualifies(ranks[rank + 1], rawLevel, period.End))
            {
                rank++;
                reached.Add(new RankReached(rank, ranks[rank], period.End));
            }

            walked.Add(new DomainStanding(period.End, total, rawLevel, Math.Min(rawLevel, Rules.TopLevelOf(rank)), rank));
        }

        return walked;
    }

    /// <summary>
    /// Whether <paramref name="rule"/>'s gate is open on <paramref name="day"/>: the level is
    /// reached, the domain is as old as the window, the window's share is at the bar, and the
    /// rank lists no milestones (their evaluator isn't built, so a rank that lists any waits).
    /// </summary>
    private bool Qualifies(RankRule rule, int rawLevel, DateOnly day)
    {
        if (rawLevel < rule.StartLevel || rule.Milestones.Count > 0)
        {
            return false;
        }

        var window = WindowEndingOn(day, rule.WindowDays);
        return window.Covered && window.Reaches(rule.MinConsistency);
    }

    /// <summary>The index of <paramref name="day"/>'s period, or null outside the start..today.</summary>
    private int? IndexOf(DateOnly day)
    {
        if (Start is not { } start)
        {
            return null;
        }

        var i = day.DayNumber - start.DayNumber;
        return i >= 0 && i < Periods.Count ? i : null;
    }

    private ConsistencyWindow Window(DateOnly from, DateOnly to)
    {
        var days = Math.Max(0, to.DayNumber - from.DayNumber + 1);
        if (days == 0 || Start is not { } start)
        {
            return new ConsistencyWindow(from, to, days, 0, 0);
        }

        var covered = Math.Max(0, to.DayNumber - Math.Max(from.DayNumber, start.DayNumber) + 1);
        var low = Math.Clamp(from.DayNumber - start.DayNumber, 0, Periods.Count);
        var high = Math.Clamp(to.DayNumber - start.DayNumber + 1, 0, Periods.Count);
        var successful = high > low ? successes[high] - successes[low] : 0;
        return new ConsistencyWindow(from, to, days, covered, successful);
    }
}
