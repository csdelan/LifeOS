using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk domains simulate [&lt;domain&gt;]</c> — the simulator (xp-domains plan §3.10, §7.4.11):
/// seeded runs of an earning pattern under a domain's rules, multiplier included, reporting for
/// each rank the share of runs whose best rank reaches it and when (10th / 50th / 90th
/// percentile), the final and best level spreads, rank drops a year in the run's second half,
/// the lowest level in the last two years, and the average active share. For calibrating the
/// rules (§2.4). <c>--sleep</c> sets the passive XP a day, <c>--lapse</c> a break and
/// <c>--exception</c> (repeatable) a break marked as exception days.
/// <para>
/// <c>--replay</c> starts from the domain's real days (exception days and the passive split
/// included) and projects from tomorrow. Without <c>--avg</c> or <c>--sd</c>, it projects the
/// real days themselves: each full day is one of them at random, days off come at the real rate
/// unless <c>--off</c> says otherwise, and sleep is the real days' average unless <c>--sleep</c>
/// says otherwise. Without <c>--replay</c> nothing is read, so no database is needed.
/// </para>
/// <para>
/// Environment is held by the home (environment plan §4.9), so it simulates a routine instead:
/// <c>--walk-every</c>, <c>--decor-every</c>, <c>--checks</c> and <c>--check</c> say when spaces
/// are rated, <c>--standard</c> and <c>--start</c> how well (Tidiness/Cleanliness/Aesthetics),
/// <c>--catch-up</c> and <c>--catch-up-xp</c> a messy start, and <c>--xp</c> and <c>--off</c> the
/// XP. Its <c>--replay</c> starts from the real spaces, ratings, XP and exception days. Health's
/// pattern options are refused for it, and its routine options for Health.
/// </para>
/// </summary>
internal static partial class DomainsSimulateCommand
{
    private static readonly SimulationPattern Defaults = new();

    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var domainArgument = new Argument<string?>("domain")
        {
            Description = "The domain whose rules to simulate (default health).",
            Arity = ArgumentArity.ZeroOrOne
        };
        var avgOption = new Option<decimal?>("--avg") { Description = $"Average base XP on a full day (default {Defaults.AverageXp})." };
        var sdOption = new Option<decimal?>("--sd") { Description = $"How much a full day varies, as a standard deviation (default {Defaults.SpreadXp})." };
        var essentialsOption = new Option<decimal?>("--essentials") { Description = "The XP a full day always includes; a half day does half of it (default 0)." };
        var offOption = new Option<string?>("--off") { Description = "Share of days with no XP, e.g. 8% (default 8%)." };
        var halfOption = new Option<string?>("--half") { Description = "Share of half days, which do half the essentials (or earn half, without essentials), e.g. 5% (default 5%)." };
        var growthOption = new Option<decimal?>("--growth") { Description = "Extra base XP a day for each year in (default 0)." };
        var lapseOption = new Option<string?>("--lapse") { Description = "A break with no XP: <start>:<length>, e.g. 3y:6m (d, w, m or y)." };
        var exceptionOption = new Option<string[]>("--exception")
        {
            Description = "A break with no XP marked as exception days: <start>:<length>, e.g. 3y:6m. Repeatable.",
            AllowMultipleArgumentsPerToken = false
        };
        var sleepOption = new Option<decimal?>("--sleep") { Description = $"Passive (sleep) XP a day, at most the day's base XP (default {Defaults.SleepXp})." };
        var yearsOption = new Option<int>("--years") { Description = "Years to simulate.", DefaultValueFactory = _ => 10 };
        var runsOption = new Option<int>("--runs") { Description = "Seeded runs.", DefaultValueFactory = _ => 20 };
        var seedOption = new Option<int>("--seed") { Description = "The first run's seed; the same seed gives the same runs.", DefaultValueFactory = _ => 1 };
        var noMultiplierOption = new Option<bool>("--no-multiplier") { Description = "Pay every day at 1.0×, to see what the multiplier adds." };
        var replayOption = new Option<bool>("--replay") { Description = "Start from the domain's real days and project from tomorrow." };

        // Environment's routine (environment plan §4.9).
        var walkEveryOption = new Option<int?>("--walk-every") { Description = $"Environment: days between walk-throughs, which rate every space (default {HomeDefaults.WalkEvery})." };
        var decorEveryOption = new Option<int?>("--decor-every") { Description = "Environment: days between Aesthetics ratings (default 0: with each walk-through)." };
        var checksOption = new Option<string?>("--checks") { Description = "Environment: quick checks of the check spaces between walk-throughs: none, midweek or daily (default midweek)." };
        var checkOption = new Option<string[]>("--check")
        {
            Description = "Environment: a space the checks rate (default Office and Kitchen). Repeatable.",
            AllowMultipleArgumentsPerToken = false
        };
        var standardOption = new Option<string?>("--standard") { Description = $"Environment: the settled Tidiness/Cleanliness/Aesthetics ratings (default {HomeDefaults.Tidiness}/{HomeDefaults.Cleanliness}/{HomeDefaults.Aesthetics})." };
        var startOption = new Option<string?>("--start") { Description = $"Environment: the ratings a catch-up starts from (default {HomeDefaults.StartTidiness}/{HomeDefaults.StartCleanliness}/{HomeDefaults.StartAesthetics})." };
        var catchUpOption = new Option<string?>("--catch-up") { Description = "Environment: how long catching up a messy home takes, e.g. 3m (d, w, m or y; default none)." };
        var catchUpXpOption = new Option<decimal?>("--catch-up-xp") { Description = $"Environment: XP a day while catching up (default {HomeDefaults.CatchUpXp})." };
        var xpOption = new Option<decimal?>("--xp") { Description = $"Environment: XP a day once settled, on average (default {HomeDefaults.SteadyXp})." };

        var command = new Command("simulate", "Simulate years of earning under a domain's rules: when each rank arrives.");
        command.Arguments.Add(domainArgument);
        foreach (var option in new Option[]
                 {
                     avgOption, sdOption, essentialsOption, offOption, halfOption, growthOption, lapseOption, exceptionOption,
                     sleepOption, yearsOption,
                     runsOption, seedOption, noMultiplierOption, replayOption,
                     walkEveryOption, decorEveryOption, checksOption, checkOption, standardOption, startOption, catchUpOption,
                     catchUpXpOption, xpOption
                 })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                var currency = DomainService.Resolve(parseResult.GetValue(domainArgument) ?? Currencies.HealthXp);
                var rules = LifeDomains.For(currency.Key)
                            ?? throw new ArgumentException($"{currency.Label} collects XP only: it has no rules to simulate yet.");
                var options = new SimulationOptions(
                    parseResult.GetValue(yearsOption), parseResult.GetValue(runsOption), parseResult.GetValue(seedOption),
                    Multiplier: !parseResult.GetValue(noMultiplierOption));
                var homeOptions = new Option[]
                {
                    walkEveryOption, decorEveryOption, checksOption, checkOption, standardOption, startOption, catchUpOption,
                    catchUpXpOption, xpOption
                };
                if (rules.Consistency is HomeConditionRule)
                {
                    Refuse(parseResult, [avgOption, sdOption, essentialsOption, halfOption, growthOption, sleepOption],
                        $"{LifeDomains.Name(currency.Key)} is held by the home: set its routine with --walk-every, --checks, --standard, --catch-up and --xp");
                    var (standardT, standardC, standardA) = Ratings(parseResult.GetValue(standardOption), "--standard")
                                                             ?? (HomeDefaults.Tidiness, HomeDefaults.Cleanliness, HomeDefaults.Aesthetics);
                    var (startT, startC, startA) = Ratings(parseResult.GetValue(startOption), "--start")
                                                   ?? (HomeDefaults.StartTidiness, HomeDefaults.StartCleanliness, HomeDefaults.StartAesthetics);
                    var checkSpaces = parseResult.GetValue(checkOption) is { Length: > 0 } named ? named.ToList() : null;
                    var home = new HomeSimulationPattern(
                        WalkEvery: parseResult.GetValue(walkEveryOption) ?? HomeDefaults.WalkEvery,
                        DecorEvery: parseResult.GetValue(decorEveryOption) ?? HomeDefaults.DecorEvery,
                        Checks: Checks(parseResult.GetValue(checksOption)) ?? HomeDefaults.Checks,
                        CheckSpaces: checkSpaces,
                        Tidiness: standardT,
                        Cleanliness: standardC,
                        Aesthetics: standardA,
                        StartTidiness: startT,
                        StartCleanliness: startC,
                        StartAesthetics: startA,
                        CatchUpDays: parseResult.GetValue(catchUpOption) is { } catchUp
                            ? Days(catchUp) ?? throw new ArgumentException($"--catch-up is a number with d, w, m or y, e.g. 3m; got '{catchUp}'.")
                            : 0,
                        CatchUpXp: parseResult.GetValue(catchUpXpOption) ?? HomeDefaults.CatchUpXp,
                        SteadyXp: parseResult.GetValue(xpOption) ?? HomeDefaults.SteadyXp,
                        OffShare: Share(parseResult.GetValue(offOption), "--off") ?? HomeDefaults.OffShare,
                        Lapse: Lapse(parseResult.GetValue(lapseOption)),
                        Exceptions: (parseResult.GetValue(exceptionOption) ?? [])
                            .Select(text => Lapse(text, "--exception")!)
                            .ToList());

                    SimulationResult homeResult;
                    if (parseResult.GetValue(replayOption))
                    {
                        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
                        await using var provider = Cli.BuildServices(connectionString);
                        var credit = await provider.GetRequiredService<ILedgerRepository>()
                            .GetDomainDaysAsync([currency.Key], cancellationToken);
                        var history = DomainService.Fold(rules, credit).Periods
                            .Select(p => new DomainDay(p.Start, p.Base, p.Paid, p.Passive))
                            .ToList();
                        var marked = credit.ExceptionDays.Select(d => d.Day).ToHashSet();
                        homeResult = HomeSimulator.Run(rules, home, options, credit.Today.AddDays(1), credit.Home, history, marked);
                    }
                    else
                    {
                        homeResult = HomeSimulator.Run(rules, home, options, DateOnly.FromDateTime(DateTime.Today));
                    }

                    if (asJson)
                    {
                        Cli.WriteJson(Json(currency, homeResult));
                    }
                    else
                    {
                        Write(currency, homeResult);
                    }

                    return 0;
                }

                Refuse(parseResult, homeOptions, $"{LifeDomains.Name(currency.Key)} isn't held by the home: set its pattern with --avg, --sd, --off and --sleep");
                var avg = parseResult.GetValue(avgOption);
                var sd = parseResult.GetValue(sdOption);
                var off = Share(parseResult.GetValue(offOption), "--off");
                var half = Share(parseResult.GetValue(halfOption), "--half");
                var sleep = parseResult.GetValue(sleepOption);
                var pattern = new SimulationPattern(
                    AverageXp: avg ?? Defaults.AverageXp,
                    SpreadXp: sd ?? Defaults.SpreadXp,
                    Essentials: parseResult.GetValue(essentialsOption) ?? 0,
                    OffShare: off ?? Defaults.OffShare,
                    HalfShare: half ?? Defaults.HalfShare,
                    GrowthPerYear: parseResult.GetValue(growthOption) ?? 0,
                    Lapse: Lapse(parseResult.GetValue(lapseOption)),
                    SleepXp: sleep ?? Defaults.SleepXp,
                    Exceptions: (parseResult.GetValue(exceptionOption) ?? [])
                        .Select(text => Lapse(text, "--exception")!)
                        .ToList());

                SimulationResult result;
                if (parseResult.GetValue(replayOption))
                {
                    var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
                    await using var provider = Cli.BuildServices(connectionString);
                    var credit = await provider.GetRequiredService<ILedgerRepository>()
                        .GetDomainDaysAsync([currency.Key], cancellationToken);
                    var history = DomainService.Fold(rules, credit).Periods
                        .Select(p => new DomainDay(p.Start, p.Base, p.Paid, p.Passive))
                        .ToList();
                    var marked = credit.ExceptionDays.Select(d => d.Day).ToHashSet();
                    if (avg is null && sd is null && Replayed(history, marked, credit.Today) is { } real)
                    {
                        // Project the real days themselves.
                        pattern = pattern with
                        {
                            Sample = real.Active,
                            OffShare = off ?? real.OffShare,
                            HalfShare = half ?? 0,
                            SleepXp = sleep ?? real.Sleep
                        };
                    }

                    result = DomainSimulator.Run(rules, pattern, options, credit.Today.AddDays(1), history, marked);
                }
                else
                {
                    result = DomainSimulator.Run(rules, pattern, options, DateOnly.FromDateTime(DateTime.Today));
                }

                if (asJson)
                {
                    Cli.WriteJson(Json(currency, result));
                }
                else
                {
                    Write(currency, result);
                }

                return 0;
            });
        });

        return command;
    }

    /// <summary>
    /// The real full days (base XP above zero), the share of days off and the sleep XP a full day
    /// averages, over the complete days since the start that aren't exception days (today too
    /// when it's the only one), or null with nothing to replay.
    /// </summary>
    private static (IReadOnlyList<long> Active, decimal OffShare, decimal Sleep)? Replayed(
        IReadOnlyList<DomainDay> history, IReadOnlySet<DateOnly> exceptions, DateOnly today)
    {
        var complete = history.Where(d => d.Day < today && !exceptions.Contains(d.Day)).ToList();
        var days = complete.Count > 0 ? complete : history;
        var full = days.Where(d => d.Base > 0).ToList();
        return full.Count == 0
            ? null
            : (full.Select(d => d.Base).ToList(),
                (decimal)(days.Count - full.Count) / days.Count,
                Math.Round((decimal)full.Average(d => d.Passive), MidpointRounding.AwayFromZero));
    }

    private static void Write(Currency currency, SimulationResult result)
    {
        var options = result.Options;
        Console.WriteLine(
            $"Simulating {LifeDomains.Name(currency.Key)}: {Plural(options.Runs, "run")} of {Plural(options.Years, "year")}, "
            + $"{Day(result.From)} to {Day(result.End)} (seed {options.Seed})");
        if (result.ReplayedDays > 0)
        {
            Console.WriteLine($"  replaying {Plural(result.ReplayedDays, "real day")} first");
        }

        var multiplier = options.Multiplier ? "on" : "off";
        if (result.Home is { } home && result.Rules.Consistency is HomeConditionRule condition)
        {
            Console.WriteLine($"  routine: {home.Describe()}");
            Console.WriteLine(
                $"  rules: a kept-up day has a Home Score of {HomeConditionRule.Text(condition.Bar(2))}–{HomeConditionRule.Text(condition.Bar(condition.MaxLevel))} by level,"
                + $" on {Percent(condition.ShareFrom)}–{Percent(condition.ShareTo)} of the last {condition.WindowDays} days; multiplier {multiplier}");
        }
        else
        {
            Console.WriteLine($"  pattern: {result.Pattern.Describe()}");
            var consistency = (ConsistencyRule)result.Rules.Consistency;
            Console.WriteLine(
                $"  rules: an active day is {consistency.ActiveMinimum(1)}–{consistency.ActiveMinimum(consistency.MaxLevel)} action XP by level;"
                + $" the level follows the last {consistency.WindowDays} days; multiplier {multiplier}");
        }
        Console.WriteLine();
        Console.WriteLine($"  {"Best rank",-13}{"Reached",-10}{"p10",-9}{"p50",-9}{"p90",-9}median date");
        foreach (var rank in result.Ranks)
        {
            var reached = $"{rank.RunsReached}/{rank.Runs}";
            Console.WriteLine(rank.Years is { } years && rank.Dates is { } dates
                ? $"  {rank.Rule.Title,-13}{reached,-10}{Years(years.P10),-9}{Years(years.P50),-9}{Years(years.P90),-9}{dates.P50:yyyy-MM-dd}"
                : $"  {rank.Rule.Title,-13}{reached,-10}never");
        }

        Console.WriteLine();
        var ranks = string.Join(", ", result.FinalRanks.Select(kv => $"{result.Rules.Ranks[kv.Key].Title} {kv.Value}"));
        Console.WriteLine(
            $"  final level: p10 {result.FinalLevel.P10} · p50 {result.FinalLevel.P50} · p90 {result.FinalLevel.P90}; final ranks: {ranks}");
        Console.WriteLine(
            $"  best level: p10 {result.BestLevel.P10} · p50 {result.BestLevel.P50} · p90 {result.BestLevel.P90}");
        Console.WriteLine(
            $"  rank drops a year from {Day(result.DropsFrom)}: p10 {Rate(result.RankDropsPerYear.P10)} · p50 {Rate(result.RankDropsPerYear.P50)} · p90 {Rate(result.RankDropsPerYear.P90)}");
        Console.WriteLine(
            $"  lowest level in the last 2 years: p10 {result.LowestLevel.P10} · p50 {result.LowestLevel.P50} · p90 {result.LowestLevel.P90}");
        Console.WriteLine(
            $"  {(result.Home is null ? "active" : "kept-up")} days: {Percent(result.AverageActiveShare)} of the projected days, on average");
    }

    private static object Json(Currency currency, SimulationResult result) => new
    {
        domain = currency.Key,
        from = result.From,
        end = result.End,
        years = result.Options.Years,
        runs = result.Options.Runs,
        seed = result.Options.Seed,
        multiplier = result.Options.Multiplier,
        replayedDays = result.ReplayedDays,
        routine = result.Home is { } home
            ? new
            {
                walkEvery = home.WalkEvery,
                decorEvery = home.DecorEvery,
                checks = home.Checks.ToString().ToLowerInvariant(),
                checkSpaces = home.CheckSpacesOrDefault,
                standard = new { tidiness = home.Tidiness, cleanliness = home.Cleanliness, aesthetics = home.Aesthetics },
                start = new { tidiness = home.StartTidiness, cleanliness = home.StartCleanliness, aesthetics = home.StartAesthetics },
                catchUpDays = home.CatchUpDays,
                catchUpXp = home.CatchUpXp,
                steadyXp = home.SteadyXp,
                offShare = home.OffShare,
                lapse = home.Lapse is { } homeLapse ? new { afterDays = homeLapse.AfterDays, lengthDays = homeLapse.LengthDays } : null,
                exceptions = (home.Exceptions ?? []).Select(x => new { afterDays = x.AfterDays, lengthDays = x.LengthDays }),
                spaces = home.SpacesOrDefault.Select(s => new { name = s.Name, weight = s.Weight, pace = s.Pace }),
                description = home.Describe()
            }
            : null,
        pattern = result.Home is not null ? null : new
        {
            averageXp = result.Pattern.Sample is null ? result.Pattern.AverageXp : (decimal?)null,
            spreadXp = result.Pattern.Sample is null ? result.Pattern.SpreadXp : (decimal?)null,
            sampledDays = result.Pattern.Sample?.Count,
            essentials = result.Pattern.Essentials,
            offShare = result.Pattern.OffShare,
            halfShare = result.Pattern.HalfShare,
            growthPerYear = result.Pattern.GrowthPerYear,
            lapse = result.Pattern.Lapse is { } lapse ? new { afterDays = lapse.AfterDays, lengthDays = lapse.LengthDays } : null,
            exceptions = (result.Pattern.Exceptions ?? []).Select(x => new { afterDays = x.AfterDays, lengthDays = x.LengthDays }),
            sleepXp = result.Pattern.SleepXp,
            description = result.Pattern.Describe()
        },
        ranks = result.Ranks.Select(r => new
        {
            rank = r.Rank,
            title = r.Rule.Title,
            reached = r.RunsReached,
            share = r.Share,
            years = r.Years is { } y ? new { p10 = Round(y.P10), p50 = Round(y.P50), p90 = Round(y.P90) } : null,
            dates = r.Dates is { } d ? new { p10 = d.P10, p50 = d.P50, p90 = d.P90 } : null
        }),
        finalLevel = new { p10 = result.FinalLevel.P10, p50 = result.FinalLevel.P50, p90 = result.FinalLevel.P90 },
        finalRanks = result.FinalRanks.Select(kv => new { rank = kv.Key, title = result.Rules.Ranks[kv.Key].Title, runs = kv.Value }),
        bestLevel = new { p10 = result.BestLevel.P10, p50 = result.BestLevel.P50, p90 = result.BestLevel.P90 },
        rankDropsPerYear = new
        {
            from = result.DropsFrom,
            p10 = Round(result.RankDropsPerYear.P10),
            p50 = Round(result.RankDropsPerYear.P50),
            p90 = Round(result.RankDropsPerYear.P90)
        },
        lowestLevelLastTwoYears = new { p10 = result.LowestLevel.P10, p50 = result.LowestLevel.P50, p90 = result.LowestLevel.P90 },
        averageActiveShare = Math.Round(result.AverageActiveShare, 4)
    };

    /// <summary>Environment's routine defaults (environment plan §4.9).</summary>
    private static readonly HomeSimulationPattern HomeDefaults = new();

    /// <summary>Refuses <paramref name="options"/> that don't apply to the domain, saying what does.</summary>
    private static void Refuse(ParseResult parseResult, IEnumerable<Option> options, string instead)
    {
        var given = options.Where(o => parseResult.GetResult(o) is { Implicit: false }).Select(o => o.Name).ToList();
        if (given.Count > 0)
        {
            throw new ArgumentException($"{string.Join(", ", given)} doesn't apply here. {instead}.");
        }
    }

    /// <summary>Ratings given as Tidiness/Cleanliness/Aesthetics, e.g. "4.5/4/3.5"; null when not given.</summary>
    internal static (decimal Tidiness, decimal Cleanliness, decimal Aesthetics)? Ratings(string? text, string option)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var values = text.Split('/')
            .Select(p => decimal.TryParse(p.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
            .ToList();
        return values.Count == 3 && values.All(v => v is >= 1 and <= 5)
            ? (values[0]!.Value, values[1]!.Value, values[2]!.Value)
            : throw new ArgumentException($"{option} is Tidiness/Cleanliness/Aesthetics, each 1 to 5, e.g. 4.5/4/3.5; got '{text}'.");
    }

    /// <summary>The checks named: none, midweek or daily; null when not given.</summary>
    private static HomeChecks? Checks(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "none" => HomeChecks.None,
        "midweek" or "wednesday" or "weekly" => HomeChecks.Midweek,
        "daily" => HomeChecks.Daily,
        _ => throw new ArgumentException($"--checks is none, midweek or daily; got '{text}'.")
    };

    /// <summary>A share given as a percentage, "8%" or "8"; null when not given.</summary>
    internal static decimal? Share(string? text, string option)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return decimal.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent)
               && percent is >= 0 and <= 100
            ? percent / 100m
            : throw new ArgumentException($"{option} is a percentage from 0 to 100, e.g. 8%; got '{text}'.");
    }

    /// <summary>A span given as <c>&lt;start&gt;:&lt;length&gt;</c>, e.g. <c>3y:6m</c>, for <paramref name="option"/>; null when not given.</summary>
    internal static SimulationLapse? Lapse(string? text, string option = "--lapse")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(':');
        return parts.Length == 2 && Days(parts[0]) is { } after && Days(parts[1]) is { } length && length > 0
            ? new SimulationLapse(after, length)
            : throw new ArgumentException($"{option} is <start>:<length>, each a number with d, w, m or y, e.g. 3y:6m; got '{text}'.");
    }

    /// <summary>A span such as "6m" in days: d, w, m (a twelfth of a year) or y (365.25 days).</summary>
    private static int? Days(string text)
    {
        var match = SpanPattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var count = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var days = char.ToLowerInvariant(match.Groups[2].Value[0]) switch
        {
            'd' => count,
            'w' => count * 7,
            'm' => count * 365.25m / 12,
            _ => count * 365.25m
        };
        return (int)Math.Round(days, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*([dwmyDWMY])$")]
    private static partial Regex SpanPattern();

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Years(double years) => years.ToString("0.0", CultureInfo.InvariantCulture) + " y";

    private static double Round(double years) => Math.Round(years, 2);

    private static string Rate(double perYear) => perYear.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(decimal share) => (share * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
