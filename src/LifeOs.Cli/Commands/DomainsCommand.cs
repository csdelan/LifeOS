using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk domains</c> — every life domain's XP and, for a domain with rules, its level and
/// rank (with any XP banked above the rank's top), what the next rank still needs, today
/// against its success rule, its consistency over the last 30 complete days, and the
/// multiplier that earns (REWARD-6 / REWARD-7, xp-domains plan §3.5).
/// <para>
/// <c>bsk domains [&lt;domain&gt;] --days &lt;n&gt;</c> (or just a domain) adds the daily series —
/// each day's base and paid XP and whether it succeeded, the last 14 days by default — for that
/// domain, or for every domain when none is named. Days are read in the home zone
/// (<c>Rewards:HomeTimeZone</c>).
/// </para>
/// <para><c>bsk domains simulate</c> is the simulator (<see cref="DomainsSimulateCommand"/>).</para>
/// </summary>
internal static class DomainsCommand
{
    private const int DefaultDays = 14;

    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var domainArgument = new Argument<string?>("domain")
        {
            Description = "A life domain (health, social, environment, finance) to list day by day.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var daysOption = new Option<int?>("--days")
        {
            Description = $"List each day, today included: this many (default {DefaultDays} when a domain is named)."
        };

        var command = new Command("domains", "Show life-domain XP, levels, ranks, consistency and multipliers.");
        command.Arguments.Add(domainArgument);
        command.Options.Add(daysOption);
        command.Subcommands.Add(DomainsSimulateCommand.Create(connectionOption, jsonOption));

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var domain = parseResult.GetValue(domainArgument);
            var days = parseResult.GetValue(daysOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<DomainService>();

                if (domain is null && days is null)
                {
                    var reading = await service.GetAsync(cancellationToken);
                    if (asJson)
                    {
                        Cli.WriteJson(new { today = reading.Today, domains = reading.Domains.Select(ReadingJson) });
                        return 0;
                    }

                    Console.WriteLine($"Life domains — {Day(reading.Today)}");
                    foreach (var d in reading.Domains)
                    {
                        WriteReading(d);
                    }

                    return 0;
                }

                // One domain, or every domain when --days comes without one.
                var keys = domain is null
                    ? Currencies.LifeDomains.Select(c => c.Key).ToList()
                    : [DomainService.Resolve(domain).Key];
                var allSeries = new List<DomainSeries>();
                foreach (var key in keys)
                {
                    allSeries.Add(await service.GetDaysAsync(key, days ?? DefaultDays, cancellationToken));
                }

                if (asJson)
                {
                    var json = allSeries.Select(SeriesJson).ToList();
                    Cli.WriteJson(domain is null ? new { domains = json } : json[0]);
                    return 0;
                }

                for (var i = 0; i < allSeries.Count; i++)
                {
                    if (i > 0)
                    {
                        Console.WriteLine();
                    }

                    WriteSeries(allSeries[i]);
                }

                return 0;
            });
        });

        return command;
    }

    private static void WriteSeries(DomainSeries series)
    {
        WriteReading(series.Domain);
        foreach (var d in series.Days)
        {
            Console.WriteLine($"    {Day(d.Day)}{d.Base,8} base{d.Paid,8} paid{(d.Succeeded == true ? "   success" : "")}");
        }
    }

    private static object SeriesJson(DomainSeries series) => new
    {
        domain = ReadingJson(series.Domain),
        days = series.Days.Select(d => new { day = d.Day, @base = d.Base, paid = d.Paid, succeeded = d.Succeeded })
    };

    private static void WriteReading(DomainReading d)
    {
        Console.WriteLine($"  {d.Currency.Label,-16}{d.TotalXp,8}");
        if (d.Progress is not { } p)
        {
            Console.WriteLine("    collects XP only (no rules yet)");
            return;
        }

        if (p.Start is null)
        {
            Console.WriteLine($"    not started: level 1 ({p.RankRule.Title}); a successful day is {p.Rules.Success.Describe()}");
            return;
        }

        Console.WriteLine(p.Capped
            ? $"    level       {p.Level} — the top of {p.RankRule.Title}; {Xp(p.BankedXp)} banked toward level {p.RawLevel}"
            : p.XpToNext == 0
                ? $"    level       {p.Level} ({p.RankRule.Title})"
                : $"    level       {p.Level} ({p.RankRule.Title}) — {Xp(p.XpIntoLevel)} of {Xp(p.Rules.Curve.CostToNext(p.Level))} toward level {p.Level + 1}");
        if (p.RankQualifiedOn is { } qualifiedOn)
        {
            Console.WriteLine($"    rank        {p.RankRule.Title} since {Day(qualifiedOn)}");
        }

        if (p.Next is { } rank)
        {
            var rule = rank.Rule;
            var gate = $"level {rule.StartLevel}, {Percent(rule.MinConsistency)} of {rule.WindowDays} days successful";
            var standing = new List<string>
            {
                rank.LevelReached ? "level reached" : $"level {p.RawLevel} so far",
                $"{rank.Window.SuccessfulDays} of {rank.Required} successful days"
            };
            if (!rank.OldEnough)
            {
                standing.Add($"{rank.DaysOld} of {rule.WindowDays} days old");
            }

            Console.WriteLine($"    next rank   {rule.Title} at {gate}: {string.Join(", ", standing)}");
        }
        else
        {
            Console.WriteLine("    next rank   none: the top rank");
        }

        Console.WriteLine(p.TodaySucceeded
            ? $"    today       {p.TodayBase} base XP — succeeded ({p.Rules.Success.Describe()})"
            : $"    today       {p.TodayBase} base XP — not yet ({p.Rules.Success.Describe()})");
        var c = p.Consistency;
        Console.WriteLine(
            $"    consistency {c.SuccessfulDays} of the last {c.Days} days ({Percent(c.Share)})"
            + (c.CoveredDays < c.Days ? $", {c.Days - c.CoveredDays} of them before the start" : ""));
        Console.WriteLine(p.NextTier is { } next
            ? $"    multiplier  ×{Multiplier(p.MultiplierNow)} — ×{Multiplier(next.Multiplier)} at {Percent(next.MinShare)} ({Plural(c.DaysNeededFor(next.MinShare), "more successful day")})"
            : $"    multiplier  ×{Multiplier(p.MultiplierNow)} — the top tier");
    }

    private static object ReadingJson(DomainReading d) => new
    {
        currency = d.Currency.Key,
        label = d.Currency.Label,
        totalXp = d.TotalXp,
        configured = d.Configured,
        progress = d.Progress is not { } p
            ? null
            : new
            {
                start = p.Start,
                level = p.Level,
                rawLevel = p.RawLevel,
                topLevel = p.TopLevel,
                capped = p.Capped,
                xpIntoLevel = p.XpIntoLevel,
                xpToNext = p.XpToNext,
                bankedXp = p.BankedXp,
                rank = new { index = p.Rank, title = p.RankRule.Title, qualifiedOn = p.RankQualifiedOn },
                ranksReached = p.RanksReached.Select(r => new { index = r.Rank, title = r.Rule.Title, qualifiedOn = r.QualifiedOn }),
                nextRank = p.Next is not { } rank
                    ? null
                    : new
                    {
                        index = rank.Rank,
                        title = rank.Rule.Title,
                        level = rank.Rule.StartLevel,
                        bar = rank.Rule.MinConsistency,
                        windowDays = rank.Rule.WindowDays,
                        successfulDays = rank.Window.SuccessfulDays,
                        requiredDays = rank.Required,
                        daysNeeded = rank.DaysNeeded,
                        daysOld = rank.DaysOld,
                        levelReached = rank.LevelReached,
                        oldEnough = rank.OldEnough,
                        barMet = rank.BarMet
                    },
                success = p.Rules.Success.Describe(),
                todayBase = p.TodayBase,
                todaySucceeded = p.TodaySucceeded,
                consistency = new
                {
                    from = p.Consistency.From,
                    to = p.Consistency.To,
                    days = p.Consistency.Days,
                    coveredDays = p.Consistency.CoveredDays,
                    successfulDays = p.Consistency.SuccessfulDays,
                    share = p.Consistency.Share
                },
                multiplier = p.MultiplierNow,
                nextTier = p.NextTier is not { } next
                    ? null
                    : new
                    {
                        minShare = next.MinShare,
                        multiplier = next.Multiplier,
                        daysNeeded = p.Consistency.DaysNeededFor(next.MinShare)
                    }
            }
    };

    private static string Day(DateOnly day) => day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Xp(long xp) => xp.ToString("#,0", CultureInfo.InvariantCulture) + " XP";

    private static string Percent(decimal share)
        => (share * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Multiplier(decimal value) => value.ToString("0.0#", CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
