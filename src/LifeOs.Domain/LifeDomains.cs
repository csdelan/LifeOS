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
/// </summary>
public sealed record PeriodFacts(DateOnly Start, DateOnly End, long BaseXp, long PaidXp, long ActionXp, int ShownLevel);

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
    int MaxLevel = 100)
{
    private int Span => MaxLevel - 2;

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
    ConsistencyRule Consistency,
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
/// The life domains with rules (REWARD-6 / REWARD-7 / REWARD-8, D12, D17). Only Health is
/// configured in the pilot. Social, Environment and Finance collect XP and nothing else until
/// each gets rules of its own; because progression is folded from history, their whole history
/// counts the day they do. The drift test keeps these consistent with <see cref="Currencies"/>.
/// </summary>
public static class LifeDomains
{
    /// <summary>The trigger of a rank-up award (xp-domains plan §3.7).</summary>
    public const string RankTrigger = "rank";

    private static readonly ConsistencyRule HealthConsistency = new(
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

    /// <summary>Every configured domain.</summary>
    public static readonly IReadOnlyList<DomainRules> Configured = [Health];

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
