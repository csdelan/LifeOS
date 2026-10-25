using System.Globalization;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>A break with no XP: <paramref name="LengthDays"/> days, starting <paramref name="AfterDays"/> days in.</summary>
public sealed record SimulationLapse(int AfterDays, int LengthDays);

/// <summary>
/// How a simulated person earns, day by day (xp-domains plan §3.10, §7.4.11). Each day is one of:
/// <list type="bullet">
/// <item>off, with no XP (<paramref name="OffShare"/> of days, 0–1);</item>
/// <item>a full day: a normal draw around <paramref name="AverageXp"/> (plus
/// <paramref name="GrowthPerYear"/> for each year in) with spread <paramref name="SpreadXp"/>,
/// never below <paramref name="Essentials"/> — the XP a full day always includes;</item>
/// <item>a half day (<paramref name="HalfShare"/>): half the essentials, and the rest of a full
/// day as usual — or, with no essentials set, half a full day.</item>
/// </list>
/// Up to <paramref name="SleepXp"/> of a day's base XP is passive (sleep, §7.4.2); the rest is
/// action XP. <paramref name="Sample"/>, when given, replaces the normal draw: a full day is one
/// of those days at random, plus the growth — how a replay projects someone's own days forward.
/// <paramref name="Lapse"/> is a break with no XP at all, and <paramref name="Exceptions"/> are
/// breaks with no XP marked as exception days.
/// </summary>
public sealed record SimulationPattern(
    decimal AverageXp = 90,
    decimal SpreadXp = 25,
    decimal Essentials = 0,
    decimal OffShare = 0.08m,
    decimal HalfShare = 0.05m,
    decimal GrowthPerYear = 0,
    SimulationLapse? Lapse = null,
    IReadOnlyList<long>? Sample = null,
    decimal SleepXp = 15,
    IReadOnlyList<SimulationLapse>? Exceptions = null)
{
    /// <summary>Throws <see cref="ArgumentException"/> for a pattern no one could follow.</summary>
    public void Validate()
    {
        if (AverageXp < 0 || SpreadXp < 0 || Essentials < 0 || SleepXp < 0)
        {
            throw new ArgumentException("The average, spread, essentials and sleep are XP a day: none can be below zero.");
        }

        if (OffShare is < 0 or > 1 || HalfShare is < 0 or > 1 || OffShare + HalfShare > 1)
        {
            throw new ArgumentException("Days off and half days are shares of days, 0–100%, and together at most 100%.");
        }

        if (Lapse is { } lapse && (lapse.AfterDays < 0 || lapse.LengthDays < 1))
        {
            throw new ArgumentException("A lapse starts at or after day 0 and lasts at least a day.");
        }

        if (Exceptions?.Any(x => x.AfterDays < 0 || x.LengthDays < 1) == true)
        {
            throw new ArgumentException("Exception days start at or after day 0 and last at least a day.");
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

        parts.Add($"up to {Number(SleepXp)} XP of a day sleep");

        if (GrowthPerYear != 0)
        {
            parts.Add($"{(GrowthPerYear > 0 ? "+" : "")}{Number(GrowthPerYear)} a day per year");
        }

        if (Lapse is { } lapse)
        {
            parts.Add($"{Plural(lapse.LengthDays, "day")} off after {Plural(lapse.AfterDays, "day")}");
        }

        foreach (var marked in Exceptions ?? [])
        {
            parts.Add($"{Plural(marked.LengthDays, "exception day")} after {Plural(marked.AfterDays, "day")}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>Whether day <paramref name="elapsed"/> (0 is the first) falls in an exception span.</summary>
    public bool IsException(int elapsed)
        => Exceptions?.Any(x => elapsed >= x.AfterDays && elapsed < x.AfterDays + x.LengthDays) == true;

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
/// One rank across the runs: how many runs' best rank reached it and, among those, when — in
/// years since the domain's start, and as dates.
/// </summary>
public sealed record RankOutcome(
    int Rank, RankRule Rule, int RunsReached, int Runs, Spread<double>? Years, Spread<DateOnly>? Dates)
{
    /// <summary>The share of runs that reached the rank (0–1).</summary>
    public double Share => (double)RunsReached / Runs;
}

/// <summary>
/// A simulation's outcome (xp-domains plan §7.4.11): the projection's first and last day (and,
/// for a replay, the real days it started from), each rank's outcome, the final shown level
/// spread, how many runs ended at each rank, the best level spread, rank drops a year from
/// <paramref name="DropsFrom"/> (the run's second half: years 6–10 of 10), the lowest shown level
/// in the last two years, the average active share of the projected days, and every run's fold.
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
    Spread<int> BestLevel,
    DateOnly DropsFrom,
    Spread<double> RankDropsPerYear,
    Spread<int> LowestLevel,
    decimal AverageActiveShare,
    IReadOnlyList<DomainProgress> Runs);

/// <summary>
/// The 10-year simulator (xp-domains plan §3.10, §7.4.11): it runs the v2 fold and the
/// multiplier pricing over seeded synthetic days, so a retune can be checked against the spread
/// of realistic outcomes — in the regression suite, and as <c>bsk domains simulate</c> for
/// calibrating.
/// <para>
/// Each run draws its days, then prices them one at a time: a day's multiplier comes from the
/// active days before it, and whether a day is active depends on the shown level going into it,
/// which depends on the XP paid so far. So a <see cref="DomainWalker"/> steps each day after
/// pricing it, <c>round(base × multiplier)</c> with halves up, and the priced days are folded
/// once more for the run's outcome. Every simulated day is settled.
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
    /// exactly as paid) and its <paramref name="historyExceptions"/>, then projects
    /// <paramref name="pattern"/> from <paramref name="from"/> for the options' years.
    /// </summary>
    public static SimulationResult Run(
        DomainRules rules,
        SimulationPattern pattern,
        SimulationOptions options,
        DateOnly from,
        IReadOnlyList<DomainDay> history,
        IReadOnlyCollection<DateOnly>? historyExceptions = null)
    {
        pattern.Validate();
        options.Validate();
        if (history.Any(d => d.Day >= from))
        {
            throw new ArgumentException("Replayed days must all come before the projection starts.");
        }

        var end = from.AddYears(options.Years).AddDays(-1);
        var real = history
            .GroupBy(d => d.Day)
            .ToDictionary(g => g.Key, g => new DomainDay(g.Key, g.Sum(d => d.Base), g.Sum(d => d.Paid), g.Sum(d => d.Passive)));
        var realFrom = real.Count > 0 ? real.Keys.Min() : from;
        var realExceptions = (historyExceptions ?? []).Where(d => d < from).ToHashSet();

        var runs = new List<DomainProgress>(options.Runs);
        var activeShares = new List<decimal>(options.Runs);
        for (var run = 0; run < options.Runs; run++)
        {
            var random = new Random(options.Seed + run);
            var walker = new DomainWalker(rules);
            var started = false;
            var days = new List<DomainDay>(end.DayNumber - realFrom.DayNumber + 1);
            var exceptions = new HashSet<DateOnly>(realExceptions);
            int projected = 0, active = 0;

            for (var day = realFrom; day < from; day = day.AddDays(1))
            {
                Step(real.GetValueOrDefault(day) ?? new DomainDay(day, 0, 0), realExceptions.Contains(day));
            }

            foreach (var drawn in Draw(pattern, from, end, random))
            {
                var multiplier = options.Multiplier ? walker.NextMultiplier() : 1m;
                if (drawn.Exception)
                {
                    exceptions.Add(drawn.Day);
                }

                var standing = Step(
                    new DomainDay(drawn.Day, drawn.Base, MultiplierRule.Apply(drawn.Base, multiplier), drawn.Passive),
                    drawn.Exception);
                if (!drawn.Exception)
                {
                    projected++;
                    active += standing?.Active == true ? 1 : 0;
                }
            }

            runs.Add(DomainProgress.Fold(rules, days, end, exceptions, end));
            activeShares.Add(projected == 0 ? 0m : (decimal)active / projected);

            // Walks a priced day: the walk starts on the first day with paid XP, as the fold's does.
            DomainStanding? Step(DomainDay day, bool exception)
            {
                days.Add(day);
                started |= day.Paid > 0;
                return started
                    ? walker.Step(new DomainPeriod(day.Day, day.Day, day.Base, day.Paid, day.Passive), exception, settled: true)
                    : null;
            }
        }

        var dropsFrom = from.AddYears(options.Years / 2);
        var lowestFrom = options.Years > 2 ? end.AddYears(-2).AddDays(1) : from;
        var ranks = Enumerable.Range(1, rules.Ranks.Count - 1).Select(rank => Outcome(rules, rank, runs)).ToList();
        var finalRanks = runs.GroupBy(r => r.Rank).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var drops = runs.Select(r => DropsPerYear(r, dropsFrom, end)).Order().ToList();
        var lowest = runs
            .Select(r => r.Standings.Where(s => s.Day >= lowestFrom).Select(s => s.Level).DefaultIfEmpty(1).Min())
            .Order()
            .ToList();
        return new SimulationResult(
            rules,
            pattern,
            options,
            from,
            end,
            real.Count,
            ranks,
            SpreadOf(runs.Select(r => r.Level).Order().ToList()),
            finalRanks,
            SpreadOf(runs.Select(r => r.BestLevel).Order().ToList()),
            dropsFrom,
            SpreadOf(drops),
            SpreadOf(lowest),
            activeShares.Average(),
            runs);
    }

    /// <summary>How often the rank fell from one day to the next, per year, from <paramref name="from"/> to <paramref name="end"/>.</summary>
    private static double DropsPerYear(DomainProgress run, DateOnly from, DateOnly end)
    {
        var drops = 0;
        var standings = run.Standings;
        for (var i = 1; i < standings.Count; i++)
        {
            if (standings[i].Day >= from && standings[i].Rank < standings[i - 1].Rank)
            {
                drops++;
            }
        }

        return drops / ((end.DayNumber - from.DayNumber + 1) / DaysPerYear);
    }

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
        return new RankOutcome(rank, rules.Ranks[rank], reached.Count, runs.Count, SpreadOf(years), SpreadOf(days));
    }

    private static Spread<T> SpreadOf<T>(IReadOnlyList<T> sorted)
        => new(Percentile(sorted, 0.10), Percentile(sorted, 0.50), Percentile(sorted, 0.90));

    /// <summary>The nearest-rank percentile of sorted <paramref name="values"/>.</summary>
    private static T Percentile<T>(IReadOnlyList<T> values, double p)
        => values[Math.Clamp((int)Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1)];

    /// <summary>Each day's base and passive XP from <paramref name="from"/> to <paramref name="end"/>, and whether it's an exception day.</summary>
    private static List<(DateOnly Day, long Base, long Passive, bool Exception)> Draw(
        SimulationPattern pattern, DateOnly from, DateOnly end, Random random)
    {
        var days = new List<(DateOnly, long, long, bool)>(end.DayNumber - from.DayNumber + 1);
        var sleep = (long)Math.Round(pattern.SleepXp, MidpointRounding.AwayFromZero);
        for (var day = from; day <= end; day = day.AddDays(1))
        {
            var elapsed = day.DayNumber - from.DayNumber;
            if (pattern.IsException(elapsed))
            {
                days.Add((day, 0, 0, true));
                continue;
            }

            if (pattern.Lapse is { } lapse && elapsed >= lapse.AfterDays && elapsed < lapse.AfterDays + lapse.LengthDays)
            {
                days.Add((day, 0, 0, false));
                continue;
            }

            var roll = random.NextDouble();
            if (roll < (double)pattern.OffShare)
            {
                days.Add((day, 0, 0, false));
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

            days.Add((day, xp, Math.Min(xp, sleep), false));
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
