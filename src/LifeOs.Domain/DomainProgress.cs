namespace LifeOs.Domain;

/// <summary>
/// One day's credit in one life domain (xp-domains plan §3.4): what the ledger pays now for
/// the awards credited to that day — <paramref name="Base"/> before any multiplier,
/// <paramref name="Paid"/> as paid. <paramref name="Passive"/> is the part of the base from
/// passive awards (sleep, §7.4.2); the rest is <see cref="Action"/>.
/// </summary>
public sealed record DomainDay(DateOnly Day, long Base, long Paid, long Passive = 0)
{
    /// <summary>The base XP from things done, not slept: <see cref="Base"/> less <see cref="Passive"/>.</summary>
    public long Action => Base - Passive;
}

/// <summary>One period of a domain's walk (a calendar day, while only a daily cadence is built) and its credit.</summary>
public sealed record DomainPeriod(DateOnly Start, DateOnly End, long Base, long Paid, long Passive = 0)
{
    /// <summary>The base XP from things done, not slept.</summary>
    public long Action => Base - Passive;
}

/// <summary>
/// The multiplier's window (xp-domains plan §7.4.4): the <paramref name="Days"/> non-exception
/// days before a day, reaching back from <paramref name="To"/> to <paramref name="From"/> (further
/// than <paramref name="Days"/> calendar days when exception days are skipped). Days before the
/// domain's start count as unsuccessful; <paramref name="CoveredDays"/> says how many of the
/// window's days are on or after the start, and <paramref name="SuccessfulDays"/> how many were active.
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

/// <summary>
/// The counted days the consistency level reads (xp-domains plan §7.2): the last
/// <paramref name="Days"/> (at most 90), <paramref name="From"/> to <paramref name="To"/>, and
/// their base XP. Null dates before any day has counted.
/// </summary>
public sealed record CountedWindow(int Days, DateOnly? From, DateOnly? To, long Base)
{
    /// <summary>The window's base XP a day, on average; 0 before any day has counted.</summary>
    public decimal Average => Days == 0 ? 0m : (decimal)Base / Days;
}

/// <summary>
/// What <paramref name="Level"/> needs over the window of <paramref name="WindowDays"/> counted
/// days, against where each test stands (xp-domains plan §7.2): base XP (an average of
/// <paramref name="Average"/> a day), active days (action XP ≥ <paramref name="ActiveMinimum"/>)
/// in the window, and active days so far for the age test.
/// </summary>
public sealed record ConsistencyNeed(
    int Level,
    int WindowDays,
    int Average,
    long BaseNeeded,
    long BaseNow,
    int ActiveMinimum,
    int ActiveDaysNeeded,
    int ActiveDaysNow,
    int AgeNeeded,
    int AgeNow)
{
    /// <summary>The window's base XP a day, on average.</summary>
    public decimal AverageNow => WindowDays == 0 ? 0m : (decimal)BaseNow / WindowDays;

    public bool AverageMet => WindowDays > 0 && BaseNow >= BaseNeeded;

    public bool ActiveDaysMet => WindowDays > 0 && ActiveDaysNow >= ActiveDaysNeeded;

    public bool AgeMet => AgeNow >= AgeNeeded;

    /// <summary>Whether all three pass, so the level qualifies.</summary>
    public bool Met => AverageMet && ActiveDaysMet && AgeMet;
}

/// <summary>Which of the two levels sets the target when they differ.</summary>
public enum LevelLimit
{
    /// <summary>Lifetime XP hasn't paid for the consistency level yet.</summary>
    Lifetime,

    /// <summary>The last 90 days don't support the lifetime level.</summary>
    Consistency
}

/// <summary>A day the shown level moved, from <paramref name="From"/> to <paramref name="To"/>.</summary>
public sealed record LevelChange(DateOnly Day, int From, int To);

/// <summary>
/// Where a domain stood at the end of <paramref name="Day"/> (xp-domains plan §7.4.4): lifetime
/// XP and level, the consistency level, the target (the lower of the two), the shown level
/// exactly (<paramref name="ShownExact"/>, in ten-thousandths) and as shown, the rank (an index
/// into the ranks), and the bests so far. <paramref name="Counted"/>, <paramref name="Exception"/>
/// and <paramref name="Active"/> say what kind of day it was; <paramref name="Falling"/> that the
/// shown level fell that day, and <paramref name="Paused"/> that a fall was due but paused (back
/// on track, or an exception day). <paramref name="ActiveMinimum"/> is the action XP that made
/// the day active: the minimum of the level going into it.
/// </summary>
public sealed record DomainStanding(
    DateOnly Day,
    long TotalXp,
    int LifetimeLevel,
    int ConsistencyLevel,
    int Target,
    long ShownExact,
    int Level,
    int Rank,
    int BestLevel,
    int BestRank,
    bool Counted,
    bool Exception,
    bool Active,
    bool Falling,
    bool Paused,
    int ActiveMinimum);

/// <summary>A rank the best rank reached (an index into the ranks, never the first), and the first day it did.</summary>
public sealed record RankReached(int Rank, RankRule Rule, DateOnly QualifiedOn);

/// <summary>
/// A configured domain over the <paramref name="Days"/> calendar days <paramref name="From"/> to
/// <paramref name="To"/>, which ends at the last settled day so an unfinished day never counts
/// (xp-domains plan §7.4.4). <paramref name="DaysCounted"/> are the settled, non-exception days on
/// or after the start, <paramref name="ActiveDays"/> those that were active, and
/// <paramref name="CountedBase"/> their base XP. <paramref name="Base"/> and <paramref name="Paid"/>
/// are every day's XP, exception days included. <paramref name="Before"/> is where the domain stood
/// at the end of the day before <paramref name="From"/>, <paramref name="End"/> at the end of
/// <paramref name="To"/>; <paramref name="RanksReached"/> are the ranks the best rank first reached
/// in the period, and <paramref name="Standings"/> every day of it, for the rank-over-time chart.
/// </summary>
public sealed record DomainPeriodReport(
    int Days,
    DateOnly From,
    DateOnly To,
    int DaysCounted,
    int ExceptionDays,
    int ActiveDays,
    long CountedBase,
    long Base,
    long Paid,
    DomainStanding Before,
    DomainStanding End,
    IReadOnlyList<RankReached> RanksReached,
    IReadOnlyList<DomainStanding> Standings)
{
    /// <summary>The active share of the days counted (0–1); 0 when none counted.</summary>
    public decimal ActiveShare => DaysCounted == 0 ? 0m : (decimal)ActiveDays / DaysCounted;

    /// <summary>The base XP a counted day earned, on average; 0 when none counted.</summary>
    public decimal AverageBase => DaysCounted == 0 ? 0m : (decimal)CountedBase / DaysCounted;

    /// <summary>The best rank at the end of the period.</summary>
    public int BestRank => End.BestRank;
}

/// <summary>
/// A life domain's progression, folded from its daily credit (D12, D17, xp-domains plan §7.4.4).
/// Every progression number in the app comes from <see cref="Fold"/>; nothing is cached or
/// stored, so a retuned rule, a corrected entry or a marked exception day shows on the next read.
/// <para>
/// The fold walks each calendar day from the domain's start to today (<see cref="DomainWalker"/>):
/// lifetime XP and its level; whether the day counts (settled and not an exception day); the
/// consistency level the last 90 counted days support; the shown level, which rises to the target
/// at once and falls 1% a day toward it; the rank the shown level is in; and the best level and
/// rank, which never fall from inactivity.
/// </para>
/// </summary>
public sealed class DomainProgress
{
    private readonly List<DomainStanding> standings;
    private readonly HashSet<DateOnly> exceptions;

    private DomainProgress(
        DomainRules rules,
        DateOnly today,
        DateOnly settledThrough,
        DateOnly? start,
        IReadOnlyList<DomainPeriod> periods,
        HashSet<DateOnly> exceptions)
    {
        Rules = rules;
        Today = today;
        SettledThrough = settledThrough;
        Start = start;
        Periods = periods;
        this.exceptions = exceptions;

        var walker = new DomainWalker(rules);
        standings = new List<DomainStanding>(periods.Count);
        foreach (var period in periods)
        {
            standings.Add(walker.Step(period, exceptions.Contains(period.End), period.End <= settledThrough));
        }

        RanksReached = walker.RanksReached.ToList();
        var now = StandingOn(today);
        Now = now;

        var curve = rules.Curve;
        XpIntoLevel = now.TotalXp - curve.TotalFor(now.LifetimeLevel);
        XpToNext = now.LifetimeLevel == curve.MaxLevel ? 0 : curve.TotalFor(now.LifetimeLevel + 1) - now.TotalXp;

        Window = walker.Window();
        NextConsistency = now.ConsistencyLevel < Math.Min(curve.MaxLevel, rules.Consistency.MaxLevel)
            ? walker.NeedFor(now.ConsistencyLevel + 1)
            : null;

        LimitedBy = now.LifetimeLevel < now.ConsistencyLevel ? LevelLimit.Lifetime
            : now.ConsistencyLevel < now.LifetimeLevel ? LevelLimit.Consistency
            : null;
        var above = now.Level > now.Target;
        Paused = above && (TodayException || walker.BackOnTrack(now.Level));
        Falling = above && !Paused;
        if (above)
        {
            var i = standings.Count - 1;
            while (i > 0 && standings[i - 1].Level > standings[i - 1].Target)
            {
                i--;
            }

            FallingSince = standings[i].Day;
            FallingFrom = i > 0 ? standings[i - 1].Level : 1;
        }

        for (var i = standings.Count - 1; i >= 0; i--)
        {
            var before = i > 0 ? standings[i - 1].Level : 1;
            if (standings[i].Level != before)
            {
                LastChange = new LevelChange(standings[i].Day, before, standings[i].Level);
                break;
            }
        }

        BestLevelOn = standings.FirstOrDefault(s => s.Level == now.BestLevel)?.Day;

        Consistency = WindowBefore(today);
        MultiplierNow = rules.Multiplier.For(Consistency.Share);
        NextTier = rules.Multiplier.NextAbove(Consistency.Share);
    }

    public DomainRules Rules { get; }

    /// <summary>Today, in the home zone.</summary>
    public DateOnly Today { get; }

    /// <summary>The last settled day (xp-domains plan §7.4.2): yesterday from 4 a.m., the day before until then.</summary>
    public DateOnly SettledThrough { get; }

    /// <summary>The first day with paid XP, or null before there is any.</summary>
    public DateOnly? Start { get; }

    /// <summary>Every period from the start to today (empty before the start).</summary>
    public IReadOnlyList<DomainPeriod> Periods { get; }

    /// <summary>Where the domain stood at the end of each period, alongside <see cref="Periods"/>.</summary>
    public IReadOnlyList<DomainStanding> Standings => standings;

    /// <summary>The days marked as exceptions, up to today.</summary>
    public IReadOnlySet<DateOnly> ExceptionDays => exceptions;

    /// <summary>Where the domain stands today.</summary>
    public DomainStanding Now { get; }

    // Lifetime (§7.4.4).

    /// <summary>All the XP paid, on days up to today.</summary>
    public long TotalXp => Now.TotalXp;

    /// <summary>The level <see cref="TotalXp"/> reaches on the curve: the ceiling the level can't pass. Never falls.</summary>
    public int LifetimeLevel => Now.LifetimeLevel;

    /// <summary>The XP into the lifetime level.</summary>
    public long XpIntoLevel { get; }

    /// <summary>The XP still to go to the next lifetime level; 0 at the top.</summary>
    public long XpToNext { get; }

    // Consistency.

    /// <summary>The highest level the last 90 counted days support.</summary>
    public int ConsistencyLevel => Now.ConsistencyLevel;

    /// <summary>The counted days the consistency level reads.</summary>
    public CountedWindow Window { get; }

    /// <summary>What the next consistency level needs, or null at the top.</summary>
    public ConsistencyNeed? NextConsistency { get; }

    // Shown.

    /// <summary>The shown level: it follows <see cref="Target"/>, rising at once and falling slowly.</summary>
    public int Level => Now.Level;

    /// <summary>The shown level exactly, in ten-thousandths of a level.</summary>
    public long ShownExact => Now.ShownExact;

    /// <summary>The level the shown level follows: the lower of the lifetime and consistency levels.</summary>
    public int Target => Now.Target;

    /// <summary>The rank the shown level is in, an index into the rules' ranks (0 is the first).</summary>
    public int Rank => Now.Rank;

    /// <summary>The rank the shown level is in.</summary>
    public RankRule RankRule => Rules.Ranks[Rank];

    /// <summary>Which level sets the target, or null when they're equal.</summary>
    public LevelLimit? LimitedBy { get; }

    /// <summary>Whether the shown level is above the target and falling toward it.</summary>
    public bool Falling { get; }

    /// <summary>
    /// Whether the shown level is above the target but its fall is paused: the last 7 counted
    /// days are back on track for it, or today is an exception day.
    /// </summary>
    public bool Paused { get; }

    /// <summary>While the shown level is above the target, the first day it was; otherwise null.</summary>
    public DateOnly? FallingSince { get; }

    /// <summary>While the shown level is above the target, the level the day before <see cref="FallingSince"/>.</summary>
    public int? FallingFrom { get; }

    /// <summary>The last day the shown level moved, and from what; null if it never has.</summary>
    public LevelChange? LastChange { get; }

    /// <summary>Whether today is marked as an exception day.</summary>
    public bool TodayException => exceptions.Contains(Today);

    // Best.

    /// <summary>The highest shown level ever reached.</summary>
    public int BestLevel => Now.BestLevel;

    /// <summary>The first day the shown level reached <see cref="BestLevel"/>, or null before the start.</summary>
    public DateOnly? BestLevelOn { get; }

    /// <summary>The highest rank ever reached, an index into the rules' ranks.</summary>
    public int BestRank => Now.BestRank;

    /// <summary>The highest rank ever reached.</summary>
    public RankRule BestRankRule => Rules.Ranks[BestRank];

    /// <summary>The first day the best rank reached each rank above the first, in order: when its Gems are due.</summary>
    public IReadOnlyList<RankReached> RanksReached { get; }

    // Current.

    /// <summary>Today's period, or null before the start.</summary>
    public DomainPeriod? TodayPeriod => Periods.Count > 0 && Periods[^1].End == Today ? Periods[^1] : null;

    /// <summary>Today's base XP.</summary>
    public long TodayBase => TodayPeriod?.Base ?? 0;

    /// <summary>Today's action XP, which the multiplier's successful day judges.</summary>
    public long TodayAction => TodayPeriod?.Action ?? 0;

    /// <summary>The action XP that makes today active: the minimum of the shown level going into today.</summary>
    public int ActiveMinimum => Now.ActiveMinimum;

    /// <summary>Whether today is already active.</summary>
    public bool TodayActive => Now.Active;

    /// <summary>
    /// The multiplier's window for today: the 30 non-exception days before it, days before the
    /// start counting as not active.
    /// </summary>
    public ConsistencyWindow Consistency { get; }

    /// <summary>The multiplier today's XP would be paid at, from <see cref="Consistency"/>.</summary>
    public decimal MultiplierNow { get; }

    /// <summary>The next multiplier tier above now, or null at the top.</summary>
    public MultiplierTier? NextTier { get; }

    /// <summary>
    /// Folds one domain's daily credit under <paramref name="rules"/>, up to
    /// <paramref name="today"/> (in the home zone). Credit on days after today hasn't happened
    /// yet and is left out. <paramref name="exceptions"/> are the days marked as exceptions, and
    /// <paramref name="settledThrough"/> the last settled day (yesterday when not given); days
    /// after it don't count yet.
    /// </summary>
    public static DomainProgress Fold(
        DomainRules rules,
        IEnumerable<DomainDay> days,
        DateOnly today,
        IEnumerable<DateOnly>? exceptions = null,
        DateOnly? settledThrough = null)
    {
        if (rules.Cadence != Cadence.Daily)
        {
            throw new NotSupportedException($"Only a daily cadence is built; {rules.Currency} has {rules.Cadence}.");
        }

        var settled = settledThrough ?? today.AddDays(-1);
        if (settled > today)
        {
            settled = today;
        }

        var marked = (exceptions ?? []).Where(d => d <= today).ToHashSet();
        var credit = new Dictionary<DateOnly, (long Base, long Paid, long Passive)>();
        foreach (var day in days)
        {
            if (day.Day <= today)
            {
                var (dayBase, dayPaid, dayPassive) = credit.GetValueOrDefault(day.Day);
                credit[day.Day] = (dayBase + day.Base, dayPaid + day.Paid, dayPassive + day.Passive);
            }
        }

        var paidDays = credit.Where(kv => kv.Value.Paid > 0).Select(kv => kv.Key).ToList();
        if (paidDays.Count == 0)
        {
            return new DomainProgress(rules, today, settled, start: null, periods: [], marked);
        }

        var start = paidDays.Min();
        var periods = new List<DomainPeriod>(today.DayNumber - start.DayNumber + 1);
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            var (dayBase, dayPaid, dayPassive) = credit.GetValueOrDefault(day);
            periods.Add(new DomainPeriod(day, day, dayBase, dayPaid, dayPassive));
        }

        return new DomainProgress(rules, today, settled, start, periods, marked);
    }

    /// <summary>Whether <paramref name="day"/> was active; days before the start and after today never were.</summary>
    public bool ActiveOn(DateOnly day) => IndexOf(day) is { } i && standings[i].Active;

    /// <summary>
    /// Where the domain stood at the end of <paramref name="day"/>: level 1 before the start, and
    /// as it stands today for any later day.
    /// </summary>
    public DomainStanding StandingOn(DateOnly day)
    {
        if (Start is not { } start || day < start)
        {
            var minimum = Rules.Consistency.ActiveMinimum(1);
            return new DomainStanding(
                day, 0, 1, 1, 1, FallRule.Scale, 1, 0, 1, 0,
                Counted: false, Exception: exceptions.Contains(day), Active: false, Falling: false, Paused: false, minimum);
        }

        return standings[Math.Min(day.DayNumber - start.DayNumber, standings.Count - 1)] with { Day = day };
    }

    /// <summary>
    /// The multiplier's window for XP credited to <paramref name="day"/> (§7.4.4): the 30
    /// complete non-exception days before it. Monday's XP, even logged on Friday, is priced from
    /// the days before Monday.
    /// </summary>
    public ConsistencyWindow WindowBefore(DateOnly day)
    {
        var size = Rules.Multiplier.WindowDays;
        var to = day.AddDays(-1);
        var from = day;
        int taken = 0, covered = 0, active = 0;
        for (var d = to; taken < size; d = d.AddDays(-1))
        {
            if (exceptions.Contains(d))
            {
                continue;
            }

            if (IndexOf(d) is { } i)
            {
                covered++;
                active += standings[i].Active ? 1 : 0;
            }

            taken++;
            from = d;
        }

        return new ConsistencyWindow(from, to, size, covered, active);
    }

    /// <summary>The multiplier for XP credited to <paramref name="day"/> (§3.6, §7.4.5): the tier of the window before it.</summary>
    public decimal MultiplierFor(DateOnly day) => Rules.Multiplier.For(WindowBefore(day).Share);

    /// <summary>
    /// The domain over the last <paramref name="days"/> calendar days ending at
    /// <see cref="SettledThrough"/> (§7.4.4): what counted, what was active, the XP, where it
    /// stood at the start against the end, and each day's standing.
    /// </summary>
    public DomainPeriodReport Report(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var to = SettledThrough;
        var from = to.AddDays(1 - days);
        int counted = 0, exceptionDays = 0, active = 0;
        long countedBase = 0, dayBase = 0, paid = 0;
        var daily = new List<DomainStanding>(days);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var standing = StandingOn(day);
            daily.Add(standing);
            exceptionDays += exceptions.Contains(day) ? 1 : 0;
            if (IndexOf(day) is not { } i)
            {
                continue;
            }

            var period = Periods[i];
            dayBase += period.Base;
            paid += period.Paid;
            if (standing.Counted)
            {
                counted++;
                countedBase += period.Base;
                active += standing.Active ? 1 : 0;
            }
        }

        return new DomainPeriodReport(
            days,
            from,
            to,
            counted,
            exceptionDays,
            active,
            countedBase,
            dayBase,
            paid,
            StandingOn(from.AddDays(-1)),
            StandingOn(to),
            RanksReached.Where(r => r.QualifiedOn >= from && r.QualifiedOn <= to).ToList(),
            daily);
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
}
