namespace LifeOs.Domain;

/// <summary>
/// How long a period a domain's success rule judges (xp-domains plan §3.2). Only
/// <see cref="Daily"/> is built; a weekly cadence (for Social or Finance, say) is designed
/// for later.
/// </summary>
public enum Cadence
{
    Daily
}

/// <summary>
/// What a success rule sees of one period: its first and last day, and the domain XP
/// credited to it — <paramref name="BaseXp"/> before any multiplier, <paramref name="PaidXp"/>
/// as the ledger pays it, and <paramref name="ActionXp"/>, the base XP less passive awards
/// such as sleep (xp-domains plan §7.4.2). <paramref name="ShownLevel"/> is the shown level
/// going into the period, which the walk supplies because it comes from the days before.
/// <paramref name="HomeScore"/> is the period's Home Score in ten-thousandths of a point, for a
/// domain held by the home (environment plan §4.5); null when it has none.
/// </summary>
public sealed record PeriodFacts(
    DateOnly Start, DateOnly End, long BaseXp, long PaidXp, long ActionXp, int ShownLevel, long? HomeScore = null);

/// <summary>
/// Decides whether a period was a success for a domain: the multiplier's successful day
/// (xp-domains plan §7.1). Replaceable per domain: swapping the rule changes no other code.
/// Rules judge XP before any multiplier, so the multiplier can never make a day succeed.
/// </summary>
public interface ISuccessRule
{
    bool Succeeded(PeriodFacts facts);

    /// <summary>The rule in words at <paramref name="level"/>, e.g. "at least 17 action XP".</summary>
    string Describe(int level);
}

/// <summary>A period succeeds when its base XP reaches <paramref name="Minimum"/>, at any level.</summary>
public sealed record MinimumBaseXp(long Minimum) : ISuccessRule
{
    public bool Succeeded(PeriodFacts facts) => facts.BaseXp >= Minimum;

    public string Describe(int level) => $"at least {Minimum} base XP";
}

/// <summary>
/// A period succeeds when it's <b>active at the shown level</b> (xp-domains plan §7.1): its
/// action XP reaches <see cref="ConsistencyRule.ActiveMinimum"/> of the shown level going into it.
/// </summary>
public sealed record ActiveAtShownLevel(ConsistencyRule Consistency) : ISuccessRule
{
    public bool Succeeded(PeriodFacts facts) => facts.ActionXp >= Consistency.ActiveMinimum(facts.ShownLevel);

    public string Describe(int level) => $"at least {Consistency.ActiveMinimum(level)} action XP (base XP less sleep)";
}

/// <summary>
/// A period succeeds when it's <b>kept up at the shown level</b> (environment plan §2.4): its Home
/// Score reaches the bar of the shown level going into it (level 2's bar at level 1).
/// </summary>
public sealed record KeptUpAtShownLevel(HomeConditionRule Condition) : ISuccessRule
{
    public bool Succeeded(PeriodFacts facts)
        => facts.HomeScore is { } score && score >= Condition.Threshold(facts.ShownLevel);

    public string Describe(int level) => $"a Home Score of at least {HomeConditionRule.Text(Condition.Bar(level))}";
}

/// <summary>
/// What the last counted days must show for a level to hold (xp-domains plan §7.2,
/// environment plan §4.5): the consistency test of a domain, replaceable per domain. The walk
/// keeps the days that count, and for each level reads three tests over the last
/// <c>N = min(WindowDays, counted days)</c> of them:
/// <list type="bullet">
/// <item><b>age</b>: days so far whose <see cref="Measure"/> reached the level's
/// <see cref="Threshold"/> ≥ <see cref="AgeNeeded"/>;</item>
/// <item><b>base</b>: the window's base XP ≥ <see cref="BaseNeeded"/>;</item>
/// <item><b>reached days</b>: the window's days that reached the threshold ≥
/// <see cref="ReachedDaysNeeded"/>.</item>
/// </list>
/// Health reads action XP against an active minimum and a base XP average
/// (<see cref="ConsistencyRule"/>); Environment reads the Home Score against a bar
/// (<see cref="HomeConditionRule"/>).
/// </summary>
public interface IConsistencyRule
{
    /// <summary>What the rule reads, for the web and the CLI: <c>xp</c> or <c>home</c>.</summary>
    string Kind { get; }

    /// <summary>The most counted days the tests read.</summary>
    int WindowDays { get; }

    /// <summary>The highest level the rule can support.</summary>
    int MaxLevel { get; }

    /// <summary>Whether a settled, non-exception day counts: Health's all do; Environment's need a Home Score.</summary>
    bool Counts(DomainPeriod period);

    /// <summary>What a day's test reads: action XP, or the Home Score in ten-thousandths.</summary>
    long Measure(DomainPeriod period);

    /// <summary>The <see cref="Measure"/> a day needs to reach <paramref name="level"/>.</summary>
    long Threshold(int level);

    /// <summary>The day's test in whole units, for display: action XP, or the bar in whole points rounded up.</summary>
    int DayMinimum(int level);

    /// <summary>The window's base XP <paramref name="level"/> needs over <paramref name="days"/> counted days; 0 when the rule has no base test.</summary>
    long BaseNeeded(int level, int days);

    /// <summary>The window's days at the threshold <paramref name="level"/> needs over <paramref name="days"/> counted days.</summary>
    int ReachedDaysNeeded(int level, int days);

    /// <summary>The days at the threshold so far <paramref name="level"/> needs.</summary>
    int AgeNeeded(int level);

    /// <summary>
    /// Whether <paramref name="level"/> passes all three tests, given its days at the threshold
    /// so far, and the window's days, base XP and days at the threshold.
    /// </summary>
    bool Qualifies(int level, int reachedSoFar, int days, long windowBase, int windowReached)
        => days > 0
            && reachedSoFar >= AgeNeeded(level)
            && windowBase >= BaseNeeded(level, days)
            && windowReached >= ReachedDaysNeeded(level, days);
}

/// <summary>
/// What the last days must show for a level to hold (xp-domains plan §7.2). Level L (2 and
/// up) qualifies over a window of the last N counted days when all three pass:
/// <list type="bullet">
/// <item><b>age</b>: active days so far (action XP ≥ <see cref="ActiveMinimum"/>) ≥ <see cref="AgeNeeded"/>;</item>
/// <item><b>average</b>: the window's base XP ≥ <see cref="Average"/> · N;</item>
/// <item><b>active days</b>: the window's active days ≥ <see cref="ActiveDaysNeeded"/>.</item>
/// </list>
/// The average runs from <paramref name="AverageFrom"/> at level 2 to <paramref name="AverageTo"/>
/// at <paramref name="MaxLevel"/>; an active day takes <paramref name="ActiveShare"/> of the
/// average in action XP, at least <paramref name="ActiveFloor"/>; and the share of days active
/// runs from <paramref name="ShareFrom"/> to <paramref name="ShareTo"/>.
/// </summary>
public sealed record ConsistencyRule(
    int WindowDays,
    int AverageFrom,
    int AverageTo,
    decimal ActiveShare,
    int ActiveFloor,
    decimal ShareFrom,
    decimal ShareTo,
    int MaxLevel = 100) : IConsistencyRule
{
    private int Span => MaxLevel - 2;

    /// <summary>Health's kind: base and action XP.</summary>
    public string Kind => "xp";

    /// <summary>Every settled, non-exception day counts.</summary>
    public bool Counts(DomainPeriod period) => true;

    /// <summary>A day's action XP: base XP less sleep.</summary>
    public long Measure(DomainPeriod period) => period.Action;

    /// <summary>The level's <see cref="ActiveMinimum"/>.</summary>
    public long Threshold(int level) => ActiveMinimum(level);

    /// <summary>The level's <see cref="ActiveMinimum"/>.</summary>
    public int DayMinimum(int level) => ActiveMinimum(level);

    /// <summary>The level's <see cref="Average"/> over <paramref name="days"/>.</summary>
    public long BaseNeeded(int level, int days) => (long)Average(level) * days;

    /// <summary>The level's <see cref="ActiveDaysNeeded"/>.</summary>
    public int ReachedDaysNeeded(int level, int days) => ActiveDaysNeeded(level, days);

    /// <summary>The average base XP a day level <paramref name="level"/> needs: 30 at level 2, 80 at 100 for Health.</summary>
    public int Average(int level)
        => AverageFrom + (int)Math.Round((decimal)(AverageTo - AverageFrom) * (level - 2) / Span, MidpointRounding.AwayFromZero);

    /// <summary>The action XP that makes a day active at <paramref name="level"/>: 20 to 32 for Health.</summary>
    public int ActiveMinimum(int level)
        => Math.Max(ActiveFloor, (int)Math.Round(ActiveShare * Average(level), MidpointRounding.AwayFromZero));

    /// <summary>
    /// The active days level <paramref name="level"/> needs in a window of <paramref name="days"/>:
    /// <c>⌈P(L) · N⌉</c>, where P runs from <see cref="ShareFrom"/> to <see cref="ShareTo"/>.
    /// Worked in exact decimals, so 70% of 90 is 63, never 64.
    /// </summary>
    public int ActiveDaysNeeded(int level, int days)
        => (int)Math.Ceiling(days * (ShareFrom * Span + (ShareTo - ShareFrom) * (level - 2)) / Span);

    /// <summary>The active days so far level <paramref name="level"/> needs: <c>min(WindowDays, L + 1)</c>.</summary>
    public int AgeNeeded(int level) => Math.Min(WindowDays, level + 1);

    /// <summary>
    /// Whether <paramref name="level"/> passes all three tests, given its active days so far,
    /// and the window's days, base XP and active days.
    /// </summary>
    public bool Qualifies(int level, int activeSoFar, int days, long windowBase, int windowActive)
        => days > 0
            && activeSoFar >= AgeNeeded(level)
            && windowBase >= (long)Average(level) * days
            && windowActive >= ActiveDaysNeeded(level, days);
}

/// <summary>
/// What the home must show for a level to hold (REWARD-9, D18, environment plan §2.3, §4.5):
/// the condition level. Level L (2 and up) qualifies over the last N counted days — settled,
/// non-exception days with a Home Score — when both pass:
/// <list type="bullet">
/// <item><b>kept up</b>: the window's days with a Home Score at or above the
/// <see cref="Bar"/> ≥ <c>⌈P(L) · N⌉</c>, P running from <paramref name="ShareFrom"/> to
/// <paramref name="ShareTo"/>;</item>
/// <item><b>age</b>: such days so far ≥ <c>min(WindowDays, L + 1)</c>.</item>
/// </list>
/// The bar runs from <paramref name="BarFrom"/> points at level 2 to <paramref name="BarTo"/> at
/// <paramref name="MaxLevel"/>, exactly: <c>Q(L) = BarFrom + (BarTo − BarFrom)(L − 2) / 98</c>.
/// With the Home Score H in ten-thousandths, a day passes when
/// <c>98 · H ≥ (98 · BarFrom + (BarTo − BarFrom)(L − 2)) · 10,000</c>, which is H at or above
/// <see cref="Threshold"/>: that, rounded up to a whole ten-thousandth. There's no base test.
/// </summary>
public sealed record HomeConditionRule(
    int WindowDays,
    int BarFrom,
    int BarTo,
    decimal ShareFrom,
    decimal ShareTo,
    int MaxLevel = 100) : IConsistencyRule
{
    private int Span => MaxLevel - 2;

    /// <summary>Environment's kind: the Home Score.</summary>
    public string Kind => "home";

    /// <summary>A settled, non-exception day counts when it has a Home Score.</summary>
    public bool Counts(DomainPeriod period) => period.HomeScore is not null;

    /// <summary>The day's Home Score, in ten-thousandths; 0 without one.</summary>
    public long Measure(DomainPeriod period) => period.HomeScore ?? 0;

    /// <summary>
    /// The least Home Score (ten-thousandths) that keeps <paramref name="level"/> up:
    /// <c>⌈(98 · BarFrom + (BarTo − BarFrom)(L − 2)) · 10,000 / 98⌉</c>. Level 1 takes level 2's bar.
    /// </summary>
    public long Threshold(int level)
    {
        var steps = Math.Max(level, 2) - 2;
        var scaled = ((long)BarFrom * Span + (long)(BarTo - BarFrom) * steps) * HomeRules.Unit;
        return (scaled + Span - 1) / Span;
    }

    /// <summary>The bar of <paramref name="level"/> in points, to four places, rounded up: what the Home Score must reach.</summary>
    public decimal Bar(int level) => Threshold(level) / (decimal)HomeRules.Unit;

    /// <summary>The bar in whole points, rounded up.</summary>
    public int DayMinimum(int level) => (int)((Threshold(level) + HomeRules.Unit - 1) / HomeRules.Unit);

    /// <summary>No base test: 0.</summary>
    public long BaseNeeded(int level, int days) => 0;

    /// <summary>
    /// The kept-up days <paramref name="level"/> needs in a window of <paramref name="days"/>:
    /// <c>⌈P(L) · N⌉</c>, worked in exact decimals.
    /// </summary>
    public int ReachedDaysNeeded(int level, int days)
        => (int)Math.Ceiling(days * (ShareFrom * Span + (ShareTo - ShareFrom) * (Math.Max(level, 2) - 2)) / Span);

    /// <summary>The kept-up days so far <paramref name="level"/> needs: <c>min(WindowDays, L + 1)</c>.</summary>
    public int AgeNeeded(int level) => Math.Min(WindowDays, level + 1);

    /// <summary>A bar as shown: rounded up to a tenth, e.g. "68.2".</summary>
    public static string Text(decimal bar)
        => (Math.Ceiling(bar * 10m) / 10m).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// How the shown level falls toward a lower target (xp-domains plan §7.2), in ten-thousandths
/// of a level: <paramref name="PercentPerDay"/> of itself a day, after <paramref name="HoldDays"/>
/// counted days below the target. The fall pauses while the last <paramref name="BackOnTrackDays"/>
/// counted days pass the shown level's own average and active-day tests.
/// </summary>
public sealed record FallRule(decimal PercentPerDay, int HoldDays, int BackOnTrackDays)
{
    /// <summary>A level in ten-thousandths: level 47 is 470,000.</summary>
    public const long Scale = 10_000;

    /// <summary>The shown level a day's fall leaves, before the target's floor: <c>⌊Ŝ · (100 − p) / 100⌋</c>.</summary>
    public long Next(long exact) => (long)Math.Floor(exact * (100 - PercentPerDay) / 100);

    /// <summary>The whole level <paramref name="exact"/> (ten-thousandths) shows: rounded up.</summary>
    public static int LevelOf(long exact) => (int)((exact + Scale - 1) / Scale);
}

/// <summary>
/// One rank (xp-domains plan §7.1): held while the shown level is at or above
/// <paramref name="StartLevel"/> and below the next rank's start. Health's start every 10
/// levels, so rank = <c>min(10, ⌊S / 10⌋)</c>.
/// </summary>
public sealed record RankRule(int StartLevel, string Title);

/// <summary>A multiplier tier: a successful share at or above <paramref name="MinShare"/> (0–1) earns <paramref name="Multiplier"/>.</summary>
public sealed record MultiplierTier(decimal MinShare, decimal Multiplier);

/// <summary>
/// The consistency multiplier (xp-domains plan §2.3, §7.1): the successful share of the
/// <paramref name="WindowDays"/> complete days before a day, exception days skipped, picks the
/// highest tier it reaches; below every tier it's 1.0.
/// </summary>
public sealed record MultiplierRule(int WindowDays, IReadOnlyList<MultiplierTier> Tiers)
{
    /// <summary>The multiplier for a successful share (0–1).</summary>
    public decimal For(decimal share)
        => Tiers.Where(t => share >= t.MinShare).Select(t => t.Multiplier).DefaultIfEmpty(1.0m).Max();

    /// <summary>The next tier above <paramref name="share"/>, or null at the top.</summary>
    public MultiplierTier? NextAbove(decimal share)
        => Tiers.Where(t => share < t.MinShare).OrderBy(t => t.MinShare).FirstOrDefault();

    /// <summary>
    /// What <paramref name="amount"/> pays at <paramref name="multiplier"/> (xp-domains plan
    /// §3.6): whole XP, rounding halves up, so 25 at 1.1 pays 28.
    /// </summary>
    public static long Apply(long amount, decimal multiplier)
        => (long)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
}

/// <summary>Gems paid once for the best rank first reaching each rank, and <paramref name="Top"/> for the last one.</summary>
public sealed record RankUpGems(int PerRank, int Top);

/// <summary>
/// A domain's level curve (xp-domains plan §3.2): how much XP each level costs. Levels run from
/// 1, at 0 XP, to <see cref="MaxLevel"/>. Replaceable per domain.
/// </summary>
public interface ILevelCurve
{
    /// <summary>The highest level.</summary>
    int MaxLevel { get; }

    /// <summary>The XP to go from <paramref name="level"/> to the next; 0 at <see cref="MaxLevel"/>.</summary>
    long CostToNext(int level);

    /// <summary>The total XP that reaches <paramref name="level"/> (0 for level 1).</summary>
    long TotalFor(int level);

    /// <summary>The level <paramref name="totalXp"/> reaches, at most <see cref="MaxLevel"/>.</summary>
    int LevelFor(long totalXp);
}

/// <summary>
/// A level curve that rises steadily, then faster (xp-domains plan §2.1): going from level L to
/// L+1 costs <c>Base + PerLevel·(L−1) + RampPerLevel·max(0, L−RampFrom)</c>. Health used
/// <c>RampCurve(300, 25, 45, 70)</c> until levels v2; it stays as a shape a domain can pick.
/// </summary>
public sealed record RampCurve(long Base, long PerLevel, int RampFrom, long RampPerLevel, int MaxLevel = 100)
    : ILevelCurve
{
    public long CostToNext(int level)
    {
        LevelCurves.Check(level, MaxLevel);
        return level == MaxLevel ? 0 : Base + PerLevel * (level - 1) + RampPerLevel * Math.Max(0, level - RampFrom);
    }

    public long TotalFor(int level)
    {
        LevelCurves.Check(level, MaxLevel);

        // The sum of CostToNext(1 .. level−1), in closed form.
        long steps = level - 1;
        long ramped = Math.Max(0, level - 1 - RampFrom);
        return Base * steps + PerLevel * steps * (steps - 1) / 2 + RampPerLevel * ramped * (ramped + 1) / 2;
    }

    public int LevelFor(long totalXp) => LevelCurves.LevelFor(this, totalXp);
}

/// <summary>
/// Levels v2's curve (xp-domains plan §7.2): the total XP that reaches level L is
/// <c>⌈Linear·n + Quadratic·C(n,2) + Cubic·C(n,3)⌉</c> with n = L − 1, in whole numbers.
/// Health's <c>CubicCurve(60, 24, 0.85)</c> reaches level 10 at 1,476 XP and 100 at 255,686.
/// </summary>
public sealed record CubicCurve(long Linear, long Quadratic, decimal Cubic, int MaxLevel = 100) : ILevelCurve
{
    public long CostToNext(int level)
    {
        LevelCurves.Check(level, MaxLevel);
        return level == MaxLevel ? 0 : TotalFor(level + 1) - TotalFor(level);
    }

    public long TotalFor(int level)
    {
        LevelCurves.Check(level, MaxLevel);
        long n = level - 1;
        var pairs = n * (n - 1) / 2;
        var triples = n * (n - 1) * (n - 2) / 6;
        return Linear * n + Quadratic * pairs + (long)Math.Ceiling(Cubic * triples);
    }

    public int LevelFor(long totalXp) => LevelCurves.LevelFor(this, totalXp);
}

internal static class LevelCurves
{
    public static void Check(int level, int maxLevel)
    {
        if (level < 1 || level > maxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Levels run from 1 to {maxLevel}.");
        }
    }

    /// <summary>The highest level whose total is within reach, by binary search.</summary>
    public static int LevelFor(ILevelCurve curve, long totalXp)
    {
        int low = 1, high = curve.MaxLevel;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (curve.TotalFor(middle) <= totalXp)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}

/// <summary>
/// One configured life domain's rules (xp-domains plan §3.2, §7.4.1): the currency it reads,
/// the period its success rule judges, the lifetime curve, what consistency each level takes,
/// how the shown level falls, the multiplier's successful day, the ranks, the multiplier and
/// the rank-up Gems. Retuning means editing these numbers and deploying: every reading is
/// folded from the XP on each read, so a change shows at once.
/// </summary>
public sealed record DomainRules(
    string Currency,
    Cadence Cadence,
    ILevelCurve Curve,
    IConsistencyRule Consistency,
    FallRule Fall,
    ISuccessRule Success,
    IReadOnlyList<RankRule> Ranks,
    MultiplierRule Multiplier,
    RankUpGems RankUpGems)
{
    /// <summary>The rank <paramref name="level"/> falls in: an index into <see cref="Ranks"/>.</summary>
    public int RankFor(int level)
    {
        var rank = 0;
        for (var i = 1; i < Ranks.Count && Ranks[i].StartLevel <= level; i++)
        {
            rank = i;
        }

        return rank;
    }

    /// <summary>The Gems for the best rank first reaching <paramref name="rank"/>: <see cref="RankUpGems.Top"/> for the last one.</summary>
    public int GemsFor(int rank) => rank == Ranks.Count - 1 ? RankUpGems.Top : RankUpGems.PerRank;

    /// <summary>The award key for <paramref name="rank"/>'s Gems (xp-domains plan §3.7), e.g. <c>rank:health_xp:10</c>.</summary>
    public string RankAwardKey(int rank) => $"{LifeDomains.RankTrigger}:{Currency}:{Ranks[rank].StartLevel}";

    /// <summary>
    /// The award triggers whose XP is passive (xp-domains plan §7.4.2): earned without doing
    /// anything that day, like sleep. A day's action XP is its base XP less these.
    /// </summary>
    public IReadOnlyList<string> PassiveTriggers { get; init; } = [];
}

/// <summary>
/// The life domains with rules (REWARD-6 / REWARD-7 / REWARD-8 / REWARD-9, D12, D17, D18):
/// Health, held by its last 90 days of XP, and Environment, held by the Home Score. Social and
/// Finance collect XP and nothing else until each gets rules of its own; because progression is folded from history, their whole history
/// counts the day they do. The drift test keeps these consistent with <see cref="Currencies"/>.
/// </summary>
public static class LifeDomains
{
    /// <summary>The trigger of a rank-up award (xp-domains plan §3.7).</summary>
    public const string RankTrigger = "rank";

    /// <summary>Health's consistency test (xp-domains plan §7.2): base XP averaging 30 to 80, with half to 90% of days active.</summary>
    public static readonly ConsistencyRule HealthConsistency = new(
        WindowDays: 90,
        AverageFrom: 30,
        AverageTo: 80,
        ActiveShare: 0.4m,
        ActiveFloor: 20,
        ShareFrom: 0.50m,
        ShareTo: 0.90m);

    /// <summary>
    /// Health, the pilot domain (xp-domains plan §7.2): its level follows the last 90 counted
    /// days, falls 1% a day while they don't support it, and a day is active when its action
    /// XP reaches the shown level's minimum. Still to recalibrate after a few weeks of real
    /// XP (§2.4).
    /// </summary>
    public static readonly DomainRules Health = new(
        Currencies.HealthXp,
        Cadence.Daily,
        new CubicCurve(Linear: 60, Quadratic: 24, Cubic: 0.85m),
        HealthConsistency,
        new FallRule(PercentPerDay: 1, HoldDays: 0, BackOnTrackDays: 7),
        new ActiveAtShownLevel(HealthConsistency),
        [
            new(1, "Novice"),
            new(10, "Apprentice"),
            new(20, "Journeyman"),
            new(30, "Adept"),
            new(40, "Expert"),
            new(50, "Veteran"),
            new(60, "Elite"),
            new(70, "Master"),
            new(80, "Champion"),
            new(90, "Legend"),
            new(100, "Grandmaster"),
        ],
        new MultiplierRule(30, [new(0.85m, 1.3m), new(0.70m, 1.2m), new(0.50m, 1.1m)]),
        new RankUpGems(PerRank: 1, Top: 5))
    {
        PassiveTriggers = [ActivityAwards.SleepTrigger]
    };

    /// <summary>
    /// Environment's condition test (environment plan §2.3): a Home Score bar from 30 at level 2
    /// to 85 at 100, kept up on half to 90% of the last 90 counted days.
    /// </summary>
    public static readonly HomeConditionRule EnvironmentCondition = new(
        WindowDays: 90,
        BarFrom: 30,
        BarTo: 85,
        ShareFrom: 0.50m,
        ShareTo: 0.90m);

    /// <summary>
    /// Environment (REWARD-9, D18, environment plan §2.3–§2.4): its level is the lower of the
    /// lifetime level, on Health's curve at a quarter of the price, and the condition level the
    /// last 90 counted days' Home Scores support. The shown level, its 1% fall, the ranks, the
    /// multiplier and the Gems are Health's; a successful day is one kept up at the shown level.
    /// No XP is passive.
    /// </summary>
    public static readonly DomainRules Environment = new(
        Currencies.EnvironmentXp,
        Cadence.Daily,
        new CubicCurve(Linear: 15, Quadratic: 6, Cubic: 0.2125m),
        EnvironmentCondition,
        Health.Fall,
        new KeptUpAtShownLevel(EnvironmentCondition),
        Health.Ranks,
        Health.Multiplier,
        Health.RankUpGems);

    /// <summary>Every configured domain.</summary>
    public static readonly IReadOnlyList<DomainRules> Configured = [Health, Environment];

    /// <summary>
    /// The hour a day settles (xp-domains plan §7.4.2): a day is over at 4 a.m. the next
    /// morning in the home zone, matching Oura's day (<see cref="ActivityAwards.DayStartHour"/>).
    /// </summary>
    public const int SettleHour = ActivityAwards.DayStartHour;

    /// <summary>Every configured domain's passive triggers, once each: what the credit read splits out.</summary>
    public static readonly IReadOnlyList<string> PassiveTriggers =
        Configured.SelectMany(r => r.PassiveTriggers).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>A domain's name: its currency label's first word, e.g. "Health" for <c>health_xp</c>.</summary>
    public static string Name(string currency)
        => Currencies.Find(currency)?.Label.Split(' ')[0] ?? currency;

    /// <summary>The rules for <paramref name="currency"/>, or null for a domain that only collects XP.</summary>
    public static DomainRules? For(string currency)
        => Configured.FirstOrDefault(r => string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase));
}
