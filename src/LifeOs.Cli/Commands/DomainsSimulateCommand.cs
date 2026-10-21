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
/// <c>bsk domains simulate [&lt;domain&gt;]</c> — the simulator (xp-domains plan §3.10): seeded
/// runs of an earning pattern under a domain's rules, multiplier included, reporting for each
/// rank the share of runs that reach it and when (10th / 50th / 90th percentile), the final
/// level spread, and the average consistency. For calibrating the rules (§2.4).
/// <para>
/// <c>--replay</c> starts from the domain's real days and projects from tomorrow. Without
/// <c>--avg</c> or <c>--sd</c>, it projects the real days themselves: each full day is one of
/// them at random, and days off come at the real rate unless <c>--off</c> says otherwise.
/// Without <c>--replay</c> nothing is read, so no database is needed.
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
        var yearsOption = new Option<int>("--years") { Description = "Years to simulate.", DefaultValueFactory = _ => 10 };
        var runsOption = new Option<int>("--runs") { Description = "Seeded runs.", DefaultValueFactory = _ => 20 };
        var seedOption = new Option<int>("--seed") { Description = "The first run's seed; the same seed gives the same runs.", DefaultValueFactory = _ => 1 };
        var noMultiplierOption = new Option<bool>("--no-multiplier") { Description = "Pay every day at 1.0×, to see what the multiplier adds." };
        var replayOption = new Option<bool>("--replay") { Description = "Start from the domain's real days and project from tomorrow." };

        var command = new Command("simulate", "Simulate years of earning under a domain's rules: when each rank arrives.");
        command.Arguments.Add(domainArgument);
        foreach (var option in new Option[]
                 {
                     avgOption, sdOption, essentialsOption, offOption, halfOption, growthOption, lapseOption, yearsOption,
                     runsOption, seedOption, noMultiplierOption, replayOption
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
                var avg = parseResult.GetValue(avgOption);
                var sd = parseResult.GetValue(sdOption);
                var off = Share(parseResult.GetValue(offOption), "--off");
                var half = Share(parseResult.GetValue(halfOption), "--half");
                var pattern = new SimulationPattern(
                    AverageXp: avg ?? Defaults.AverageXp,
                    SpreadXp: sd ?? Defaults.SpreadXp,
                    Essentials: parseResult.GetValue(essentialsOption) ?? 0,
                    OffShare: off ?? Defaults.OffShare,
                    HalfShare: half ?? Defaults.HalfShare,
                    GrowthPerYear: parseResult.GetValue(growthOption) ?? 0,
                    Lapse: Lapse(parseResult.GetValue(lapseOption)));
                var options = new SimulationOptions(
                    parseResult.GetValue(yearsOption), parseResult.GetValue(runsOption), parseResult.GetValue(seedOption),
                    Multiplier: !parseResult.GetValue(noMultiplierOption));

                SimulationResult result;
                if (parseResult.GetValue(replayOption))
                {
                    var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
                    await using var provider = Cli.BuildServices(connectionString);
                    var credit = await provider.GetRequiredService<ILedgerRepository>()
                        .GetDomainDaysAsync([currency.Key], cancellationToken);
                    var history = DomainService.Fold(rules, credit).Periods
                        .Select(p => new DomainDay(p.Start, p.Base, p.Paid))
                        .ToList();
                    if (avg is null && sd is null && Replayed(history, credit.Today) is { } real)
                    {
                        // Project the real days themselves.
                        pattern = pattern with
                        {
                            Sample = real.Active,
                            OffShare = off ?? real.OffShare,
                            HalfShare = half ?? 0
                        };
                    }

                    result = DomainSimulator.Run(rules, pattern, options, credit.Today.AddDays(1), history);
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
    /// The real full days (base XP above zero) and the share of days off, over the complete
    /// days since the start (today too when it's the only one), or null with nothing to replay.
    /// </summary>
    private static (IReadOnlyList<long> Active, decimal OffShare)? Replayed(IReadOnlyList<DomainDay> history, DateOnly today)
    {
        var complete = history.Where(d => d.Day < today).ToList();
        var days = complete.Count > 0 ? complete : history;
        var active = days.Where(d => d.Base > 0).Select(d => d.Base).ToList();
        return active.Count == 0 ? null : (active, (decimal)(days.Count - active.Count) / days.Count);
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

        Console.WriteLine($"  pattern: {result.Pattern.Describe()}");
        Console.WriteLine(
            $"  rules: a successful day is {result.Rules.Success.Describe()}; multiplier {(options.Multiplier ? "on" : "off")}");
        Console.WriteLine();
        Console.WriteLine($"  {"Rank",-13}{"Reached",-10}{"p10",-9}{"p50",-9}{"p90",-9}median date");
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
        Console.WriteLine($"  consistency over a run: {Percent(result.AverageConsistency)} of days successful");
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
        pattern = new
        {
            averageXp = result.Pattern.Sample is null ? result.Pattern.AverageXp : (decimal?)null,
            spreadXp = result.Pattern.Sample is null ? result.Pattern.SpreadXp : (decimal?)null,
            sampledDays = result.Pattern.Sample?.Count,
            essentials = result.Pattern.Essentials,
            offShare = result.Pattern.OffShare,
            halfShare = result.Pattern.HalfShare,
            growthPerYear = result.Pattern.GrowthPerYear,
            lapse = result.Pattern.Lapse is { } lapse ? new { afterDays = lapse.AfterDays, lengthDays = lapse.LengthDays } : null,
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
        averageConsistency = Math.Round(result.AverageConsistency, 4)
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

    /// <summary>A lapse given as <c>&lt;start&gt;:&lt;length&gt;</c>, e.g. <c>3y:6m</c>; null when not given.</summary>
    internal static SimulationLapse? Lapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(':');
        return parts.Length == 2 && Days(parts[0]) is { } after && Days(parts[1]) is { } length && length > 0
            ? new SimulationLapse(after, length)
            : throw new ArgumentException($"--lapse is <start>:<length>, each a number with d, w, m or y, e.g. 3y:6m; got '{text}'.");
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

    private static string Percent(decimal share) => (share * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
