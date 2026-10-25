namespace LifeOs.Domain;

/// <summary>
/// The day-by-day walk behind <see cref="DomainProgress.Fold"/> (xp-domains plan §7.4.4). It
/// steps one calendar day at a time from the domain's start, carrying lifetime XP, the running
/// sums the consistency tests read, the shown level and the bests. The simulator steps it too,
/// pricing each day at <see cref="NextMultiplier"/> before stepping, so a simulated day and a
/// real one are read the same way.
/// </summary>
public sealed class DomainWalker
{
    private readonly DomainRules rules;
    private readonly int maxLevel;

    // The distinct active minimums, ascending, and each level's index into them.
    private readonly int[] minimums;
    private readonly int[] minimumOf;

    // Over counted days: their dates, and running sums of base XP and of the days whose
    // action XP reached each minimum. Index k holds the first k counted days.
    private readonly List<DateOnly> counted = [];
    private readonly List<long> baseSums = [0];
    private readonly List<int>[] activeSums;

    // Over every non-exception day walked, settled or not: the running count of active days,
    // which the multiplier reads.
    private readonly List<int> successSums = [0];

    private readonly List<RankReached> reached = [];
    private long total;
    private long shown = FallRule.Scale;
    private int consistency = 1;
    private int below;
    private int bestLevel = 1;
    private int bestRank;

    public DomainWalker(DomainRules rules)
    {
        this.rules = rules;
        maxLevel = Math.Min(rules.Curve.MaxLevel, rules.Consistency.MaxLevel);
        minimums = Enumerable.Range(1, maxLevel).Select(rules.Consistency.ActiveMinimum).Distinct().Order().ToArray();
        minimumOf = new int[maxLevel + 1];
        for (var level = 1; level <= maxLevel; level++)
        {
            minimumOf[level] = Array.IndexOf(minimums, rules.Consistency.ActiveMinimum(level));
        }

        activeSums = minimums.Select(_ => new List<int> { 0 }).ToArray();
    }

    public DomainRules Rules => rules;

    /// <summary>The shown level so far, as a whole level (rounded up).</summary>
    public int Level => FallRule.LevelOf(shown);

    /// <summary>How many counted days the walk has seen.</summary>
    public int CountedDays => counted.Count;

    /// <summary>The first day the best rank reached each rank above the first, in order.</summary>
    public IReadOnlyList<RankReached> RanksReached => reached;

    /// <summary>
    /// The multiplier XP credited to the next day would be paid at (xp-domains plan §7.4.5): the
    /// active share of the last 30 non-exception days walked, days before the start counting as
    /// not active. It's <see cref="DomainProgress.MultiplierFor"/> of the day after the last step.
    /// </summary>
    public decimal NextMultiplier()
    {
        var window = rules.Multiplier.WindowDays;
        if (window == 0)
        {
            return rules.Multiplier.For(0m);
        }

        var walked = successSums.Count - 1;
        var active = successSums[walked] - successSums[Math.Max(0, walked - window)];
        return rules.Multiplier.For((decimal)active / window);
    }

    /// <summary>
    /// Walks one calendar day: <paramref name="period"/>'s credit, whether it's an
    /// <paramref name="exception"/> day, and whether it has <paramref name="settled"/>. Days must
    /// be stepped in order, one after another, from the domain's start.
    /// </summary>
    public DomainStanding Step(DomainPeriod period, bool exception, bool settled)
    {
        var day = period.End;
        var levelBefore = Level;
        total += period.Paid;
        var lifetime = rules.Curve.LevelFor(total);

        // The multiplier's successful day, at the level going into it (§7.4.4 step 5).
        var activeMinimum = rules.Consistency.ActiveMinimum(levelBefore);
        var active = !exception
            && rules.Success.Succeeded(new PeriodFacts(day, day, period.Base, period.Paid, period.Action, levelBefore));
        if (!exception)
        {
            successSums.Add(successSums[^1] + (active ? 1 : 0));
        }

        var isCounted = !exception && settled;
        if (isCounted)
        {
            counted.Add(day);
            baseSums.Add(baseSums[^1] + period.Base);
            for (var i = 0; i < minimums.Length; i++)
            {
                activeSums[i].Add(activeSums[i][^1] + (period.Action >= minimums[i] ? 1 : 0));
            }

            consistency = ConsistencyLevelNow();
        }

        // The target, and the shown level following it (§7.2).
        var target = Math.Min(lifetime, consistency);
        var falling = false;
        var paused = false;
        if (target >= levelBefore)
        {
            shown = target * FallRule.Scale;
            below = 0;
        }
        else if (isCounted)
        {
            below++;
            if (BackOnTrack(levelBefore))
            {
                paused = true;
            }
            else if (below > rules.Fall.HoldDays)
            {
                var next = Math.Max(target * FallRule.Scale, rules.Fall.Next(shown));
                falling = next < shown;
                shown = next;
            }
        }
        else if (exception)
        {
            paused = true;
        }

        var level = Level;
        var rank = rules.RankFor(level);
        bestLevel = Math.Max(bestLevel, level);
        while (bestRank < rank)
        {
            bestRank++;
            reached.Add(new RankReached(bestRank, rules.Ranks[bestRank], day));
        }

        return new DomainStanding(
            day,
            total,
            lifetime,
            consistency,
            target,
            shown,
            level,
            rank,
            bestLevel,
            bestRank,
            isCounted,
            exception,
            active,
            falling,
            paused,
            activeMinimum);
    }

    /// <summary>
    /// Whether the last <see cref="FallRule.BackOnTrackDays"/> counted days pass
    /// <paramref name="level"/>'s average and active-day tests (no age test), which pauses a fall.
    /// </summary>
    public bool BackOnTrack(int level)
    {
        var days = rules.Fall.BackOnTrackDays;
        var k = counted.Count;
        if (days <= 0 || k < days)
        {
            return false;
        }

        var sums = activeSums[minimumOf[Math.Clamp(level, 1, maxLevel)]];
        return baseSums[k] - baseSums[k - days] >= (long)rules.Consistency.Average(level) * days
            && sums[k] - sums[k - days] >= rules.Consistency.ActiveDaysNeeded(level, days);
    }

    /// <summary>The counted days the consistency level reads now: the last <c>min(90, counted days)</c>.</summary>
    public CountedWindow Window()
    {
        var k = counted.Count;
        var n = Math.Min(rules.Consistency.WindowDays, k);
        return n == 0
            ? new CountedWindow(0, null, null, 0)
            : new CountedWindow(n, counted[k - n], counted[k - 1], baseSums[k] - baseSums[k - n]);
    }

    /// <summary>What <paramref name="level"/> needs over the window now, against where each test stands.</summary>
    public ConsistencyNeed NeedFor(int level)
    {
        var rule = rules.Consistency;
        var k = counted.Count;
        var n = Math.Min(rule.WindowDays, k);
        var sums = activeSums[minimumOf[Math.Clamp(level, 1, maxLevel)]];
        var average = rule.Average(level);
        return new ConsistencyNeed(
            level,
            n,
            average,
            (long)average * n,
            baseSums[k] - baseSums[k - n],
            rule.ActiveMinimum(level),
            rule.ActiveDaysNeeded(level, n),
            sums[k] - sums[k - n],
            rule.AgeNeeded(level),
            sums[k]);
    }

    /// <summary>
    /// The consistency level over the counted days so far (§7.2): levels are tested upward from
    /// 2, and the first that fails ends the walk, since every level reads the same window.
    /// </summary>
    private int ConsistencyLevelNow()
    {
        var rule = rules.Consistency;
        var k = counted.Count;
        var n = Math.Min(rule.WindowDays, k);
        if (n == 0)
        {
            return 1;
        }

        var windowBase = baseSums[k] - baseSums[k - n];
        var level = 1;
        for (var candidate = 2; candidate <= maxLevel; candidate++)
        {
            var sums = activeSums[minimumOf[candidate]];
            if (!rule.Qualifies(candidate, sums[k], n, windowBase, sums[k] - sums[k - n]))
            {
                break;
            }

            level = candidate;
        }

        return level;
    }
}
