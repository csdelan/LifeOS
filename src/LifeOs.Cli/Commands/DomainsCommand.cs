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
/// <para>
/// <c>bsk domains [&lt;domain&gt;] --report &lt;n&gt;</c> is the period report (xp-domains plan
/// §3.13): each domain over its last <c>n</c> days — 7, 30, 90, 180 or 365, as the Character
/// page offers — with a configured domain's consistency over the days it has history for, its
/// XP and base XP a day, and the levels and ranks it gained; a domain without rules, its XP.
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
        var reportOption = new Option<int?>("--report")
        {
            Description = "Report each domain over its last this many days (7, 30, 90, 180 or 365): consistency, XP, levels and ranks gained."
        };

        var command = new Command("domains", "Show life-domain XP, levels, ranks, consistency and multipliers.");
        command.Arguments.Add(domainArgument);
        command.Options.Add(daysOption);
        command.Options.Add(reportOption);
        command.Subcommands.Add(DomainsSimulateCommand.Create(connectionOption, jsonOption));

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var domain = parseResult.GetValue(domainArgument);
            var days = parseResult.GetValue(daysOption);
            var report = parseResult.GetValue(reportOption);

            return Cli.RunAsync(asJson, async () =>
            {
                if (days is not null && report is not null)
                {
                    throw new ArgumentException("Use --days or --report, not both.");
                }

                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<DomainService>();

                if (report is { } reportDays)
                {
                    var reading = await service.GetReportAsync(reportDays, cancellationToken);
                    var entries = domain is null
                        ? reading.Domains
                        : reading.Domains.Where(e => e.Domain.Currency.Key == DomainService.Resolve(domain).Key).ToList();
                    if (asJson)
                    {
                        Cli.WriteJson(new { today = reading.Today, days = reading.Days, domains = entries.Select(ReportJson) });
                        return 0;
                    }

                    WriteReport(reading, entries);
                    return 0;
                }

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

    private static void WriteReport(DomainReportReading reading, IReadOnlyList<DomainReportEntry> entries)
    {
        Console.WriteLine($"Life domains — the last {Plural(reading.Days, "day")} to {Day(reading.Today)}");
        foreach (var e in entries)
        {
            var xp = e.Base == e.Paid ? Xp(e.Paid) : $"{Xp(e.Paid)} ({e.Base.ToString("#,0", CultureInfo.InvariantCulture)} base)";
            Console.WriteLine($"  {e.Domain.Currency.Label,-16}{xp}");
            if (e.Period is not { } p)
            {
                Console.WriteLine("    collects XP only (no rules yet)");
                continue;
            }

            var rules = e.Domain.Progress!.Rules;
            Console.WriteLine($"    days        {Day(p.From)} to {Day(p.To)}"
                + (p.To < reading.Today ? " (today counts once it succeeds)" : ""));
            if (p.DaysCounted == 0)
            {
                Console.WriteLine("    consistency no days yet: the domain starts with its first XP");
            }
            else
            {
                Console.WriteLine($"    consistency {p.SuccessfulDays} of {Plural(p.DaysCounted, "day")} successful ({Percent(p.Consistency)})"
                    + (p.FullHistory ? "" : $" — {p.DaysCounted} of the {p.Days} days since the start"));
                Console.WriteLine($"    per day     {p.AverageBase.ToString("0.#", CultureInfo.InvariantCulture)} base XP on average");
            }

            var levels = p.Before.Level == p.Now.Level
                ? $"{p.Now.Level}, unchanged"
                : $"{p.Before.Level} → {p.Now.Level}";
            Console.WriteLine($"    level       {levels} ({rules.Ranks[p.Now.Rank].Title})");
            Console.WriteLine(p.RanksReached.Count == 0
                ? "    ranks       none reached"
                : $"    ranks       {string.Join(", ", p.RanksReached.Select(r => $"{r.Rule.Title} on {Day(r.QualifiedOn)}"))}");
        }
    }

    private static object ReportJson(DomainReportEntry e) => new
    {
        currency = e.Domain.Currency.Key,
        label = e.Domain.Currency.Label,
        configured = e.Domain.Configured,
        from = e.From,
        to = e.To,
        @base = e.Base,
        paid = e.Paid,
        period = e.Period is not { } p
            ? null
            : new
            {
                daysCounted = p.DaysCounted,
                successfulDays = p.SuccessfulDays,
                consistency = p.Consistency,
                averageBase = p.AverageBase,
                fullHistory = p.FullHistory,
                levelFrom = p.Before.Level,
                levelTo = p.Now.Level,
                rankFrom = p.Before.Rank,
                rankTo = p.Now.Rank,
                ranksReached = p.RanksReached.Select(r => new { index = r.Rank, title = r.Rule.Title, qualifiedOn = r.QualifiedOn })
            }
    };

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
