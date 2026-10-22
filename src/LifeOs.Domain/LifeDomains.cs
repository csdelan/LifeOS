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
/// as the ledger pays it. It can grow (habits followed, commitments kept, a recovery-day
/// flag) without changing the fold.
/// </summary>
public sealed record PeriodFacts(DateOnly Start, DateOnly End, long BaseXp, long PaidXp);

/// <summary>
/// Decides whether a period was a success for a domain (xp-domains plan §3.2). Replaceable
/// per domain: swapping the rule changes no other code. Rules judge <see cref="PeriodFacts.BaseXp"/>,
/// so the multiplier can never make a day succeed.
/// </summary>
public interface ISuccessRule
{
    bool Succeeded(PeriodFacts facts);

    /// <summary>The rule in words, e.g. "at least 70 base XP".</summary>
    string Describe();
}

/// <summary>A period succeeds when its base XP reaches <paramref name="Minimum"/>.</summary>
public sealed record MinimumBaseXp(long Minimum) : ISuccessRule
{
    public bool Succeeded(PeriodFacts facts) => facts.BaseXp >= Minimum;

    public string Describe() => $"at least {Minimum} base XP";
}

/// <summary>
/// One rank (xp-domains plan §2.2): entered at <paramref name="StartLevel"/> once the share of
/// successful periods over the last <paramref name="WindowDays"/> reaches
/// <paramref name="MinConsistency"/> (0–1) and the domain is at least that old.
/// <paramref name="Milestones"/> are achievements a rank could also require (Goals
/// completed, say): designed, not built, so every pilot rank lists none.
/// </summary>
public sealed record RankRule(
    int StartLevel, string Title, decimal MinConsistency, int WindowDays, IReadOnlyList<string> Milestones);

/// <summary>A multiplier tier: a successful share at or above <paramref name="MinShare"/> (0–1) earns <paramref name="Multiplier"/>.</summary>
public sealed record MultiplierTier(decimal MinShare, decimal Multiplier);

/// <summary>
/// The consistency multiplier (xp-domains plan §2.3): the successful share of the
/// <paramref name="WindowDays"/> complete days before a day picks the highest tier it reaches;
/// below every tier it's 1.0.
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

/// <summary>Gems paid once for entering each rank, and <paramref name="Top"/> for the last one.</summary>
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
/// L+1 costs <c>Base + PerLevel·(L−1) + RampPerLevel·max(0, L−RampFrom)</c>. Health's
/// <c>RampCurve(300, 25, 45, 70)</c> reaches level 100 at 254,925 XP.
/// </summary>
public sealed record RampCurve(long Base, long PerLevel, int RampFrom, long RampPerLevel, int MaxLevel = 100)
    : ILevelCurve
{
    public long CostToNext(int level)
    {
        Check(level);
        return level == MaxLevel ? 0 : Base + PerLevel * (level - 1) + RampPerLevel * Math.Max(0, level - RampFrom);
    }

    public long TotalFor(int level)
    {
        Check(level);

        // The sum of CostToNext(1 .. level−1), in closed form.
        long steps = level - 1;
        long ramped = Math.Max(0, level - 1 - RampFrom);
        return Base * steps + PerLevel * steps * (steps - 1) / 2 + RampPerLevel * ramped * (ramped + 1) / 2;
    }

    public int LevelFor(long totalXp)
    {
        // The highest level whose total is within reach.
        int low = 1, high = MaxLevel;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (TotalFor(middle) <= totalXp)
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

    private void Check(int level)
    {
        if (level < 1 || level > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Levels run from 1 to {MaxLevel}.");
        }
    }
}

/// <summary>
/// One configured life domain's rules (xp-domains plan §3.2): the currency it reads, the
/// period its success rule judges, the level curve, the ranks, the multiplier and the rank-up
/// Gems. Retuning means editing these numbers and deploying: every reading is folded from
/// the XP on each read, so a change shows at once.
/// </summary>
public sealed record DomainRules(
    string Currency,
    Cadence Cadence,
    ILevelCurve Curve,
    ISuccessRule Success,
    IReadOnlyList<RankRule> Ranks,
    MultiplierRule Multiplier,
    RankUpGems RankUpGems)
{
    /// <summary>The highest level <paramref name="rank"/> (an index into <see cref="Ranks"/>) allows: 9 for Novice.</summary>
    public int TopLevelOf(int rank) => rank + 1 < Ranks.Count ? Ranks[rank + 1].StartLevel - 1 : Curve.MaxLevel;

    /// <summary>The Gems for entering <paramref name="rank"/>: <see cref="RankUpGems.Top"/> for the last one.</summary>
    public int GemsFor(int rank) => rank == Ranks.Count - 1 ? RankUpGems.Top : RankUpGems.PerRank;

    /// <summary>The award key for entering <paramref name="rank"/> (xp-domains plan §3.7), e.g. <c>rank:health_xp:10</c>.</summary>
    public string RankAwardKey(int rank) => $"{LifeDomains.RankTrigger}:{Currency}:{Ranks[rank].StartLevel}";
}

/// <summary>
/// The life domains with rules (REWARD-6 / REWARD-7, D12). Only Health is configured in the
/// pilot. Social, Environment and Finance collect XP and nothing else until each gets rules of
/// its own; because progression is folded from history, their whole history counts the day
/// they do. The drift test keeps these consistent with <see cref="Currencies"/>.
/// </summary>
public static class LifeDomains
{
    /// <summary>The trigger of a rank-up award (xp-domains plan §3.7).</summary>
    public const string RankTrigger = "rank";

    private static readonly IReadOnlyList<string> NoMilestones = [];

    /// <summary>
    /// Health, the pilot domain (xp-domains plan §2): a successful day is 60+ base XP at every
    /// rank. It was 70 until activity XP's calibration (activity plan §3.8, 2026-10-22), and is
    /// still to recalibrate after a few weeks of real XP (§2.4).
    /// </summary>
    public static readonly DomainRules Health = new(
        Currencies.HealthXp,
        Cadence.Daily,
        new RampCurve(Base: 300, PerLevel: 25, RampFrom: 45, RampPerLevel: 70),
        new MinimumBaseXp(60),
        [
            new(1, "Novice", 0m, 0, NoMilestones),
            new(10, "Apprentice", 0.50m, 14, NoMilestones),
            new(20, "Journeyman", 0.60m, 30, NoMilestones),
            new(30, "Adept", 0.65m, 60, NoMilestones),
            new(40, "Expert", 0.70m, 90, NoMilestones),
            new(50, "Veteran", 0.75m, 120, NoMilestones),
            new(60, "Elite", 0.78m, 180, NoMilestones),
            new(70, "Master", 0.80m, 240, NoMilestones),
            new(80, "Champion", 0.85m, 300, NoMilestones),
            new(90, "Legend", 0.88m, 365, NoMilestones),
            new(100, "Grandmaster", 0.90m, 365, NoMilestones),
        ],
        new MultiplierRule(30, [new(0.85m, 1.3m), new(0.70m, 1.2m), new(0.50m, 1.1m)]),
        new RankUpGems(PerRank: 1, Top: 5));

    /// <summary>Every configured domain.</summary>
    public static readonly IReadOnlyList<DomainRules> Configured = [Health];

    /// <summary>A domain's name: its currency label's first word, e.g. "Health" for <c>health_xp</c>.</summary>
    public static string Name(string currency)
        => Currencies.Find(currency)?.Label.Split(' ')[0] ?? currency;

    /// <summary>The rules for <paramref name="currency"/>, or null for a domain that only collects XP.</summary>
    public static DomainRules? For(string currency)
        => Configured.FirstOrDefault(r => string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase));
}
