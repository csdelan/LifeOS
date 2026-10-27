using System.Globalization;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>Which days the check spaces get a quick check (Tidiness and Cleanliness) between walk-throughs.</summary>
public enum HomeChecks
{
    /// <summary>No checks: only walk-throughs.</summary>
    None,

    /// <summary>Three days after each weekly walk-through (Sunday's walk, Wednesday's check).</summary>
    Midweek,

    /// <summary>Every day but the walk-through's.</summary>
    Daily
}

/// <summary>A space to simulate: its name, weight (1–10) and pace (1–5).</summary>
public sealed record SimulatedSpace(string Name, int Weight, int Pace);

/// <summary>
/// How a simulated home is kept and rated (environment plan §3, §4.9):
/// <list type="bullet">
/// <item>a walk-through every <paramref name="WalkEvery"/> days rates every space's Tidiness and
/// Cleanliness, and Aesthetics too unless <paramref name="DecorEvery"/> sets its own interval;</item>
/// <item>quick checks of the <paramref name="CheckSpaces"/> (Office and Kitchen by default) rate
/// Tidiness and Cleanliness on the <paramref name="Checks"/> days;</item>
/// <item>a rating lands within half a point of the standard, in half points: the settled
/// <paramref name="Tidiness"/>, <paramref name="Cleanliness"/> and <paramref name="Aesthetics"/>,
/// climbing to them from the start values over <paramref name="CatchUpDays"/> when catching up a
/// messy home;</item>
/// <item>paid XP is <paramref name="CatchUpXp"/> a day while catching up and
/// <paramref name="SteadyXp"/> after, on average, with <paramref name="OffShare"/> of days earning
/// none and the rest varying by 40%;</item>
/// <item><paramref name="Lapse"/> is a break with no ratings and no XP, and
/// <paramref name="Exceptions"/> are breaks marked as exception days, which also pause fades.</item>
/// </list>
/// The spaces are Chris's nine (§2.1) unless <paramref name="Spaces"/> says otherwise. Days count
/// from the projection's first day, which is the first walk-through.
/// </summary>
public sealed record HomeSimulationPattern(
    int WalkEvery = 7,
    int DecorEvery = 0,
    HomeChecks Checks = HomeChecks.Midweek,
    IReadOnlyList<string>? CheckSpaces = null,
    decimal Tidiness = 4.5m,
    decimal Cleanliness = 4.0m,
    decimal Aesthetics = 3.5m,
    decimal StartTidiness = 2m,
    decimal StartCleanliness = 2m,
    decimal StartAesthetics = 3m,
    int CatchUpDays = 0,
    decimal CatchUpXp = 80,
    decimal SteadyXp = 25,
    decimal OffShare = 0.15m,
    SimulationLapse? Lapse = null,
    IReadOnlyList<SimulationLapse>? Exceptions = null,
    IReadOnlyList<SimulatedSpace>? Spaces = null)
{
    /// <summary>Chris's nine spaces (environment plan §2.1).</summary>
    public static readonly IReadOnlyList<SimulatedSpace> ChrisSpaces =
    [
        new("Office", 5, 5),
        new("Kitchen", 4, 5),
        new("Master Bath", 4, 3),
        new("Living Area", 3, 4),
        new("Stairway/Foyer/Halls", 3, 4),
        new("Other Baths", 3, 2),
        new("Bedroom", 2, 1),
        new("Exercise Room", 2, 1),
        new("Garage", 1, 2)
    ];

    /// <summary>The spaces checked between walk-throughs by default.</summary>
    public static readonly IReadOnlyList<string> DefaultCheckSpaces = ["Office", "Kitchen"];

    /// <summary>The spaces simulated.</summary>
    public IReadOnlyList<SimulatedSpace> SpacesOrDefault => Spaces ?? ChrisSpaces;

    /// <summary>The spaces checked between walk-throughs.</summary>
    public IReadOnlyList<string> CheckSpacesOrDefault => CheckSpaces ?? DefaultCheckSpaces;

    /// <summary>Throws <see cref="ArgumentException"/> for a routine no one could follow.</summary>
    public void Validate()
    {
        if (WalkEvery < 1 || DecorEvery < 0)
        {
            throw new ArgumentException("A walk-through comes every day or more, and decor every day or more (0 for with each walk-through).");
        }

        foreach (var (rating, name) in new[]
                 {
                     (Tidiness, "tidiness"), (Cleanliness, "cleanliness"), (Aesthetics, "aesthetics"),
                     (StartTidiness, "starting tidiness"), (StartCleanliness, "starting cleanliness"),
                     (StartAesthetics, "starting aesthetics")
                 })
        {
            if (rating is < 1 or > 5)
            {
                throw new ArgumentException($"The {name} standard is a rating from 1 to 5; got {Number(rating)}.");
            }
        }

        if (CatchUpDays < 0 || CatchUpXp < 0 || SteadyXp < 0)
        {
            throw new ArgumentException("The catch-up and the XP a day can't be below zero.");
        }

        if (OffShare is < 0 or >= 1)
        {
            throw new ArgumentException("Days off are a share of days, 0 to under 100%.");
        }

        if (Lapse is { } lapse && (lapse.AfterDays < 0 || lapse.LengthDays < 1))
        {
            throw new ArgumentException("A lapse starts at or after day 0 and lasts at least a day.");
        }

        if (Exceptions?.Any(x => x.AfterDays < 0 || x.LengthDays < 1) == true)
        {
            throw new ArgumentException("Exception days start at or after day 0 and last at least a day.");
        }

        if (Spaces is { Count: 0 })
        {
            throw new ArgumentException("Simulate at least one space.");
        }

        foreach (var space in Spaces ?? [])
        {
            SpaceLimits.CheckWeight(space.Weight);
            SpaceLimits.CheckPace(space.Pace);
        }
    }

    /// <summary>The routine in words, e.g. "a walk-through every 7 days, midweek checks of Office and Kitchen, …".</summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            WalkEvery == 7 ? "a weekly walk-through" : $"a walk-through every {Plural(WalkEvery, "day")}",
        };
        if (DecorEvery > 0)
        {
            parts.Add($"decor every {Plural(DecorEvery, "day")}");
        }

        if (Checks != HomeChecks.None)
        {
            parts.Add($"{(Checks == HomeChecks.Daily ? "daily" : "midweek")} checks of {string.Join(" and ", CheckSpacesOrDefault)}");
        }

        parts.Add($"settled at tidiness {Number(Tidiness)}, cleanliness {Number(Cleanliness)}, decor {Number(Aesthetics)}");
        parts.Add(CatchUpDays > 0
            ? $"catching up from {Number(StartTidiness)}/{Number(StartCleanliness)}/{Number(StartAesthetics)} over {Plural(CatchUpDays, "day")} at {Number(CatchUpXp)} XP a day, then {Number(SteadyXp)}"
            : $"{Number(SteadyXp)} XP a day");
        parts.Add($"{Number(OffShare * 100)}% of days without XP");
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

    /// <summary>Whether day <paramref name="elapsed"/> is in the lapse.</summary>
    public bool InLapse(int elapsed)
        => Lapse is { } lapse && elapsed >= lapse.AfterDays && elapsed < lapse.AfterDays + lapse.LengthDays;

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}

/// <summary>
/// The Environment simulator (environment plan §4.9): seeded runs of a home routine under a
/// domain held by the home. Each run draws its ratings, works out every day's Home Score with the
/// real <see cref="HomeScore"/>, then walks the days with a <see cref="DomainWalker"/>, pricing
/// each day's XP at the multiplier the kept-up days before it earn, as
/// <see cref="DomainSimulator"/> does for Health. Every simulated day is settled.
/// </summary>
public static class HomeSimulator
{
    /// <summary>
    /// Simulates <paramref name="pattern"/> from <paramref name="from"/> for the options' years,
    /// after replaying <paramref name="history"/>: the real spaces and ratings, the real days'
    /// credit and the real exception days, all before <paramref name="from"/>. With real spaces,
    /// the projection rates those; otherwise the pattern's.
    /// </summary>
    public static SimulationResult Run(
        DomainRules rules,
        HomeSimulationPattern pattern,
        SimulationOptions options,
        DateOnly from,
        HomeInputs? history = null,
        IReadOnlyList<DomainDay>? historyDays = null,
        IReadOnlyCollection<DateOnly>? historyExceptions = null)
    {
        if (rules.Consistency is not HomeConditionRule)
        {
            throw new ArgumentException($"{LifeDomains.Name(rules.Currency)} isn't held by the home.");
        }

        pattern.Validate();
        options.Validate();
        var realDays = historyDays ?? [];
        var realHome = history ?? HomeInputs.None;
        if (realDays.Any(d => d.Day >= from) || realHome.Ratings.Any(r => r.Day >= from))
        {
            throw new ArgumentException("Replayed days must all come before the projection starts.");
        }

        var end = from.AddYears(options.Years).AddDays(-1);
        var real = realDays
            .GroupBy(d => d.Day)
            .ToDictionary(g => g.Key, g => new DomainDay(g.Key, g.Sum(d => d.Base), g.Sum(d => d.Paid), g.Sum(d => d.Passive)));
        var realStarts = real.Keys.Concat(realHome.Ratings.Select(r => r.Day)).ToList();
        var realFrom = realStarts.Count > 0 ? realStarts.Min() : from;
        var realExceptions = (historyExceptions ?? []).Where(d => d < from).ToHashSet();
        var spaces = realHome.Spaces.Count > 0
            ? realHome.Spaces
            : pattern.SpacesOrDefault.Select((s, i) => new Space(
                    SpaceId(i), $"urn:bsk:space:simulated-{i}", s.Name, s.Weight, s.Pace, null, null, DateTimeOffset.UnixEpoch))
                .ToList();
        var rated = spaces.Where(s => s.ArchivedOn is null).ToList();
        var checkedIds = rated
            .Where(s => pattern.CheckSpacesOrDefault.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToHashSet();

        var runs = new List<DomainProgress>(options.Runs);
        var keptShares = new List<decimal>(options.Runs);
        for (var run = 0; run < options.Runs; run++)
        {
            var seed = options.Seed + run;
            var ratingRandom = new Random(seed * 7919 + 1);
            var xpRandom = new Random(seed * 104729 + 17);

            var exceptions = new HashSet<DateOnly>(realExceptions);
            var ratings = new List<SpaceRating>(realHome.Ratings);
            for (var day = from; day <= end; day = day.AddDays(1))
            {
                var elapsed = day.DayNumber - from.DayNumber;
                if (pattern.IsException(elapsed))
                {
                    exceptions.Add(day);
                }

                if (!pattern.IsException(elapsed) && !pattern.InLapse(elapsed))
                {
                    ratings.AddRange(Rate(pattern, rated, checkedIds, day, elapsed, ratingRandom));
                }
            }

            var scoreFrom = ratings.Count > 0 ? ratings.Min(r => r.Day) : from;
            var scores = HomeScore.Scores(new HomeInputs(spaces, ratings), exceptions, scoreFrom, end);

            var walker = new DomainWalker(rules);
            var started = false;
            var days = new List<DomainDay>(end.DayNumber - realFrom.DayNumber + 1);
            int projected = 0, kept = 0;
            for (var day = realFrom; day < from; day = day.AddDays(1))
            {
                Step(real.GetValueOrDefault(day) ?? new DomainDay(day, 0, 0), realExceptions.Contains(day));
            }

            for (var day = from; day <= end; day = day.AddDays(1))
            {
                var elapsed = day.DayNumber - from.DayNumber;
                var exception = pattern.IsException(elapsed);
                var xp = exception || pattern.InLapse(elapsed) ? 0 : DrawXp(pattern, elapsed, xpRandom);
                var multiplier = options.Multiplier ? walker.NextMultiplier() : 1m;
                var standing = Step(new DomainDay(day, xp, MultiplierRule.Apply(xp, multiplier)), exception);
                if (!exception)
                {
                    projected++;
                    kept += standing?.Active == true ? 1 : 0;
                }
            }

            runs.Add(DomainProgress.Fold(rules, days, end, exceptions, end, scores));
            keptShares.Add(projected == 0 ? 0m : (decimal)kept / projected);

            // Walks a priced day: the walk starts at the first paid XP or Home Score, as the fold's does.
            DomainStanding? Step(DomainDay day, bool exception)
            {
                days.Add(day);
                long? score = scores.TryGetValue(day.Day, out var s) ? s : null;
                started |= day.Paid > 0 || score is not null;
                return started
                    ? walker.Step(new DomainPeriod(day.Day, day.Day, day.Base, day.Paid, day.Passive, score), exception, settled: true)
                    : null;
            }
        }

        var xpPattern = new SimulationPattern(
            AverageXp: pattern.SteadyXp,
            SpreadXp: 0.4m * pattern.SteadyXp,
            OffShare: pattern.OffShare,
            HalfShare: 0,
            SleepXp: 0,
            Lapse: pattern.Lapse,
            Exceptions: pattern.Exceptions);
        return DomainSimulator.Summarize(rules, xpPattern, options, from, end, real.Count, runs, keptShares) with
        {
            Home = pattern
        };
    }

    /// <summary>
    /// The ratings of <paramref name="day"/>: on a walk-through, every space's Tidiness and
    /// Cleanliness (and Aesthetics when decor is due); on a check day, the checked spaces'
    /// Tidiness and Cleanliness.
    /// </summary>
    private static IEnumerable<SpaceRating> Rate(
        HomeSimulationPattern pattern, IReadOnlyList<Space> spaces, IReadOnlySet<Guid> checkedIds, DateOnly day, int elapsed,
        Random random)
    {
        var progress = pattern.CatchUpDays > 0 ? Math.Min(1m, (decimal)elapsed / pattern.CatchUpDays) : 1m;
        var tidiness = pattern.StartTidiness + (pattern.Tidiness - pattern.StartTidiness) * progress;
        var cleanliness = pattern.StartCleanliness + (pattern.Cleanliness - pattern.StartCleanliness) * progress;
        var aesthetics = pattern.StartAesthetics + (pattern.Aesthetics - pattern.StartAesthetics) * progress;

        var walk = elapsed % pattern.WalkEvery == 0;
        var decor = elapsed == 0 || (pattern.DecorEvery > 0 ? elapsed % pattern.DecorEvery == 0 : walk);
        var weekday = elapsed % 7;
        var check = pattern.Checks switch
        {
            HomeChecks.Daily => weekday != 0,
            HomeChecks.Midweek => weekday == 3,
            _ => false
        };
        var ratedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        foreach (var space in spaces)
        {
            var parts = walk || (check && checkedIds.Contains(space.Id));
            if (!parts && !decor)
            {
                continue;
            }

            yield return new SpaceRating(
                new Guid(day.DayNumber, 0, 0, space.Id.ToByteArray()[..8]),
                space.Id,
                day,
                parts ? Draw(random, tidiness) : null,
                parts ? Draw(random, cleanliness) : null,
                decor ? HalfRound(aesthetics) : null,
                null,
                ratedAt,
                Guid.Empty);
        }
    }

    /// <summary>A rating within half a point of <paramref name="target"/>, in half points, from 1 to 5, each equally likely.</summary>
    private static decimal Draw(Random random, decimal target)
    {
        var options = new List<decimal>();
        for (var rating = 1m; rating <= 5m; rating += 0.5m)
        {
            if (Math.Abs(rating - target) <= 0.5m)
            {
                options.Add(rating);
            }
        }

        return options.Count == 0 ? HalfRound(target) : options[random.Next(options.Count)];
    }

    private static decimal HalfRound(decimal value)
        => Math.Clamp(Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2, 1m, 5m);

    /// <summary>A day's base XP: none on <see cref="HomeSimulationPattern.OffShare"/> of days, otherwise around the phase's average, varying by 40%.</summary>
    private static long DrawXp(HomeSimulationPattern pattern, int elapsed, Random random)
    {
        if (random.NextDouble() < (double)pattern.OffShare)
        {
            return 0;
        }

        var mean = (double)((elapsed < pattern.CatchUpDays ? pattern.CatchUpXp : pattern.SteadyXp) / (1 - pattern.OffShare));
        return Math.Max(0, (long)Math.Round(mean + DomainSimulator.Normal(random) * 0.4 * mean, MidpointRounding.AwayFromZero));
    }

    private static Guid SpaceId(int index) => new(index + 1, 0, 0, new byte[8]);
}
