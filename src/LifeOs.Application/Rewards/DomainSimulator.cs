using System.Globalization;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>A break with no XP: <paramref name="LengthDays"/> days, starting <paramref name="AfterDays"/> days in.</summary>
public sealed record SimulationLapse(int AfterDays, int LengthDays);

/// <summary>
/// How a simulated person earns, day by day (xp-domains plan §3.10). Each day is one of:
/// <list type="bullet">
/// <item>off, with no XP (<paramref name="OffShare"/> of days, 0–1);</item>
/// <item>a full day: a normal draw around <paramref name="AverageXp"/> (plus
/// <paramref name="GrowthPerYear"/> for each year in) with spread <paramref name="SpreadXp"/>,
/// never below <paramref name="Essentials"/> — the XP a full day always includes;</item>
/// <item>a half day (<paramref name="HalfShare"/>): half the essentials, and the rest of a full
/// day as usual — or, with no essentials set, half a full day.</item>
/// </list>
/// <paramref name="Sample"/>, when given, replaces the normal draw: a full day is one of those
/// days at random, plus the growth — how a replay projects someone's own days forward.
/// <paramref name="Lapse"/> is a break with no XP at all.
/// </summary>
public sealed record SimulationPattern(
    decimal AverageXp = 90,
    decimal SpreadXp = 25,
    decimal Essentials = 0,
    decimal OffShare = 0.08m,
    decimal HalfShare = 0.05m,
    decimal GrowthPerYear = 0,
    SimulationLapse? Lapse = null,
    IReadOnlyList<long>? Sample = null)
{
    /// <summary>Throws <see cref="ArgumentException"/> for a pattern no one could follow.</summary>
    public void Validate()
    {
        if (AverageXp < 0 || SpreadXp < 0 || Essentials < 0)
        {
            throw new ArgumentException("The average, spread and essentials are XP a day: none can be below zero.");
        }

        if (OffShare is < 0 or > 1 || HalfShare is < 0 or > 1 || OffShare + HalfShare > 1)
        {
            throw new ArgumentException("Days off and half days are shares of days, 0–100%, and together at most 100%.");
        }

        if (Lapse is { } lapse && (lapse.AfterDays < 0 || lapse.LengthDays < 1))
        {
            throw new ArgumentException("A lapse starts at or after day 0 and lasts at least a day.");
        }

        if (Sample is { Count: 0 } || Sample?.Any(x => x < 0) == true)
        {
            throw new ArgumentException("A sample needs at least one day, none below zero.");
        }
    }

    /// <summary>The pattern in words, e.g. "full days about 95 base XP (spread 15, essentials 70), 5% of days off".</summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            Sample is { } sample
                ? $"full days drawn from {Plural(sample.Count, "real day")} (average {Number((decimal)sample.Average())})"
                : $"full days about {Number(AverageXp)} base XP (spread {Number(SpreadXp)}"
                  + (Essentials > 0 ? $", essentials {Number(Essentials)})" : ")"),
            $"{Percent(OffShare)} of days off"
        };
        if (Sample is not null && Essentials > 0)
        {
            parts.Add($"essentials {Number(Essentials)}");
        }

        if (HalfShare > 0)
        {
            parts.Add($"{Percent(HalfShare)} half days");
        }

        if (GrowthPerYear != 0)
        {
            parts.Add($"{(GrowthPerYear > 0 ? "+" : "")}{Number(GrowthPerYear)} a day per year");
        }

        if (Lapse is { } lapse)
        {
            parts.Add($"{Plural(lapse.LengthDays, "day")} off after {Plural(lapse.AfterDays, "day")}");
        }

        return string.Join(", ", parts);
    }

    private static string Number(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(decimal share) => (share * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}

/// <summary>
/// How many runs, over how many years, from which seed. <paramref name="Multiplier"/> false
/// pays every day at 1.0×, to see what the multiplier adds.
/// </summary>
public sealed record SimulationOptions(int Years = 10, int Runs = 20, int Seed = 1, bool Multiplier = true)
{
    /// <summary>Throws <see cref="ArgumentException"/> for a run that's too short, too long or empty.</summary>
    public void Validate()
    {
        if (Years is < 1 or > 50)
        {
            throw new ArgumentException($"Simulate 1 to 50 years; got {Years}.");
        }

        if (Runs is < 1 or > 1000)
        {
            throw new ArgumentException($"Simulate 1 to 1,000 runs; got {Runs}.");
        }
    }
}

/// <summary>A percentile spread: the 10th, 50th and 90th.</summary>
public sealed record Spread<T>(T P10, T P50, T P90);

/// <summary>
/// One rank across the runs: how many reached it and, among those, when — in years since the
/// domain's start, and as dates.
/// </summary>
public sealed record RankOutcome(
    int Rank, RankRule Rule, int RunsReached, int Runs, Spread<double>? Years, Spread<DateOnly>? Dates)
{
    /// <summary>The share of runs that reached the rank (0–1).</summary>
    public double Share => (double)RunsReached / Runs;
}

/// <summary>
/// A simulation's outcome: the projection's first and last day (and, for a replay, the real
/// days it started from), each rank's outcome, the final level spread, how many runs ended at
/// each rank, the average consistency over a whole run, and every run's fold.
/// </summary>
public sealed record SimulationResult(
    DomainRules Rules,
    SimulationPattern Pattern,
    SimulationOptions Options,
    DateOnly From,
    DateOnly End,
    int ReplayedDays,
    IReadOnlyList<RankOutcome> Ranks,
    Spread<int> FinalLevel,
    IReadOnlyDictionary<int, int> FinalRanks,
    decimal AverageConsistency,
    IReadOnlyList<DomainProgress> Runs);

/// <summary>
/// The 10-year simulator (xp-domains plan §3.10): it runs <see cref="DomainProgress.Fold"/>
/// and the §3.6 multiplier pricing over seeded synthetic days, so a retune can be checked
/// against the spread of realistic outcomes — in the regression suite, and as
/// <c>bsk domains simulate</c> for calibrating.
/// <para>
/// Each run draws its days, then folds them twice. The first fold reads which days
/// succeeded (success reads base XP, which the multiplier never touches), so it gives each
/// day's multiplier — the tier of the 30 complete days before it. The second folds the days
/// as paid, <c>round(base × multiplier)</c> with halves up, for levels and ranks.
/// </para>
/// </summary>
public static class DomainSimulator
{
    private const double DaysPerYear = 365.25;

    /// <summary>Simulates <paramref name="pattern"/> from <paramref name="from"/> for the options' years.</summary>
    public static SimulationResult Run(
        DomainRules rules, SimulationPattern pattern, SimulationOptions options, DateOnly from)
        => Run(rules, pattern, options, from, history: []);

    /// <summary>
    /// Replays <paramref name="history"/> (real days, all before <paramref name="from"/>, kept
    /// exactly as paid), then projects <paramref name="pattern"/> from <paramref name="from"/>
    /// for the options' years.
    /// </summary>
    public static SimulationResult Run(
        DomainRules rules, SimulationPattern pattern, SimulationOptions options, DateOnly from,
        IReadOnlyList<DomainDay> history)
    {
        pattern.Validate();
        options.Validate();
        if (history.Any(d => d.Day >= from))
        {
            throw new ArgumentException("Replayed days must all come before the projection starts.");
        }

        var end = from.AddYears(options.Years).AddDays(-1);
        var runs = new List<DomainProgress>(options.Runs);
        for (var run = 0; run < options.Runs; run++)
        {
            var random = new Random(options.Seed + run);
            var drawn = Draw(pattern, from, end, random);
            var unpriced = DomainProgress.Fold(rules, history.Concat(drawn.Select(d => new DomainDay(d.Day, d.Base, d.Base))), end);
            var paid = drawn.Select(d => new DomainDay(
                d.Day, d.Base, options.Multiplier ? Price(d.Base, unpriced.MultiplierFor(d.Day)) : d.Base));
            runs.Add(DomainProgress.Fold(rules, history.Concat(paid), end));
        }

        var ranks = Enumerable.Range(1, rules.Ranks.Count - 1).Select(rank => Outcome(rules, rank, runs)).ToList();
        var levels = runs.Select(r => r.Level).Order().ToList();
        var finalRanks = runs.GroupBy(r => r.Rank).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var consistency = runs.Average(r => r.Periods.Count == 0 ? 0m : (decimal)r.Periods.Count(p => p.Succeeded) / r.Periods.Count);
        return new SimulationResult(
            rules, pattern, options, from, end, history.Select(d => d.Day).Distinct().Count(), ranks,
            new Spread<int>(Percentile(levels, 0.10), Percentile(levels, 0.50), Percentile(levels, 0.90)),
            finalRanks, consistency, runs);
    }

    /// <summary>What <paramref name="baseXp"/> pays at <paramref name="multiplier"/>, halves rounded up (25 × 1.1 = 28).</summary>
    public static long Price(long baseXp, decimal multiplier)
        => (long)Math.Round(baseXp * multiplier, MidpointRounding.AwayFromZero);

    private static RankOutcome Outcome(DomainRules rules, int rank, IReadOnlyList<DomainProgress> runs)
    {
        var reached = runs
            .Select(r => (r.Start, Reached: r.RanksReached.FirstOrDefault(x => x.Rank == rank)))
            .Where(x => x.Reached is not null && x.Start is not null)
            .Select(x => (Day: x.Reached!.QualifiedOn, Years: (x.Reached.QualifiedOn.DayNumber - x.Start!.Value.DayNumber) / DaysPerYear))
            .ToList();
        if (reached.Count == 0)
        {
            return new RankOutcome(rank, rules.Ranks[rank], 0, runs.Count, null, null);
        }

        var years = reached.Select(x => x.Years).Order().ToList();
        var days = reached.Select(x => x.Day).Order().ToList();
        return new RankOutcome(
            rank, rules.Ranks[rank], reached.Count, runs.Count,
            new Spread<double>(Percentile(years, 0.10), Percentile(years, 0.50), Percentile(years, 0.90)),
            new Spread<DateOnly>(Percentile(days, 0.10), Percentile(days, 0.50), Percentile(days, 0.90)));
    }

    /// <summary>The nearest-rank percentile of sorted <paramref name="values"/>.</summary>
    private static T Percentile<T>(IReadOnlyList<T> values, double p)
        => values[Math.Clamp((int)Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1)];

    /// <summary>Each day's base XP from <paramref name="from"/> to <paramref name="end"/>.</summary>
    private static List<(DateOnly Day, long Base)> Draw(SimulationPattern pattern, DateOnly from, DateOnly end, Random random)
    {
        var days = new List<(DateOnly, long)>(end.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= end; day = day.AddDays(1))
        {
            var elapsed = day.DayNumber - from.DayNumber;
            if (pattern.Lapse is { } lapse && elapsed >= lapse.AfterDays && elapsed < lapse.AfterDays + lapse.LengthDays)
            {
                days.Add((day, 0));
                continue;
            }

            var roll = random.NextDouble();
            if (roll < (double)pattern.OffShare)
            {
                days.Add((day, 0));
                continue;
            }

            var growth = (double)pattern.GrowthPerYear * elapsed / DaysPerYear;
            var full = pattern.Sample is { } sample
                ? sample[random.Next(sample.Count)] + growth
                : (double)pattern.AverageXp + growth + (double)pattern.SpreadXp * Normal(random);
            var essentials = (long)pattern.Essentials;
            var xp = Math.Max(0, Math.Max((long)Math.Round(full, MidpointRounding.AwayFromZero), essentials));
            if (roll < (double)(pattern.OffShare + pattern.HalfShare))
            {
                xp = essentials > 0 ? xp - essentials / 2 : xp / 2;
            }

            days.Add((day, xp));
        }

        return days;
    }

    /// <summary>A standard normal draw (Box–Muller).</summary>
    private static double Normal(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
