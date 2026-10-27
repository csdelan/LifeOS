using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk domains</c> — every life domain's XP and, for a domain with rules (xp-domains plan
/// §7.4.8): its level and rank with the best of each ("Level 47, Expert (best 52, Veteran)"), the
/// target and what limits it, whether the level is falling or paused, the lifetime level and its
/// XP to go, the consistency level and what the next one needs, today against the active minimum,
/// the multiplier, and recent exception days (REWARD-6 / REWARD-7 / REWARD-8).
/// <para>
/// <c>bsk domains [&lt;domain&gt;] --days &lt;n&gt;</c> (or just a domain) adds the daily series —
/// each day's base, action and paid XP, whether it was active, its level and rank, and any
/// exception comment, the last 14 days by default — for that domain, or for every domain when
/// none is named. Days are read in the home zone (<c>Rewards:HomeTimeZone</c>).
/// </para>
/// <para>
/// <c>bsk domains [&lt;domain&gt;] --report &lt;n&gt;</c> is the period report (§3.13, §7.4.4):
/// each domain over its last <c>n</c> days to the last settled day — 7, 30, 90, 180 or 365, as
/// the reports offer — with a configured domain's counted, exception and active days, base XP a
/// day, its level and rank at the start and end, its best, and the ranks first reached; a domain
/// without rules, its XP.
/// </para>
/// <para><c>bsk domains simulate</c> is the simulator (<see cref="DomainsSimulateCommand"/>).</para>
/// <para>
/// <c>bsk domains exception</c> and <c>bsk domains exceptions</c> mark, unmark and list
/// exception days (<see cref="DomainsExceptionCommand"/>).
/// </para>
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
            Description = "Report each domain over its last this many days (7, 30, 90, 180 or 365): days counted and active, XP, level and rank."
        };

        var command = new Command("domains", "Show life-domain XP, levels, ranks, consistency, multipliers and exception days.");
        command.Arguments.Add(domainArgument);
        command.Options.Add(daysOption);
        command.Options.Add(reportOption);
        command.Subcommands.Add(DomainsSimulateCommand.Create(connectionOption, jsonOption));
        command.Subcommands.Add(DomainsExceptionCommand.CreateMark(connectionOption, jsonOption));
        command.Subcommands.Add(DomainsExceptionCommand.CreateList(connectionOption, jsonOption));

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

            var ranks = e.Domain.Progress!.Rules.Ranks;
            Console.WriteLine($"    days        {Day(p.From)} to {Day(p.To)}"
                + (p.To < reading.Today ? " (to the last settled day)" : ""));
            if (p.DaysCounted == 0)
            {
                Console.WriteLine("    counted     no days yet"
                    + (p.ExceptionDays > 0 ? $" ({Plural(p.ExceptionDays, "exception day")})" : ""));
            }
            else
            {
                Console.WriteLine($"    counted     {Plural(p.DaysCounted, "day")}"
                    + (p.ExceptionDays > 0 ? $" and {Plural(p.ExceptionDays, "exception day")}" : "")
                    + $"; {p.ActiveDays} active ({Percent(p.ActiveShare)})");
                Console.WriteLine($"    per day     {p.AverageBase.ToString("0.#", CultureInfo.InvariantCulture)} base XP on average");
            }

            var levels = p.Before.Level == p.End.Level
                ? $"{p.End.Level}, unchanged"
                : $"{p.Before.Level} → {p.End.Level}";
            Console.WriteLine($"    level       {levels} ({ranks[p.End.Rank].Title}); best {p.End.BestLevel} ({ranks[p.BestRank].Title})");
            Console.WriteLine(p.RanksReached.Count == 0
                ? "    new ranks   none"
                : $"    new ranks   {string.Join(", ", p.RanksReached.Select(r => $"{r.Rule.Title} on {Day(r.QualifiedOn)}"))}");
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
                exceptionDays = p.ExceptionDays,
                activeDays = p.ActiveDays,
                activeShare = p.ActiveShare,
                averageBase = p.AverageBase,
                levelFrom = p.Before.Level,
                levelTo = p.End.Level,
                rankFrom = p.Before.Rank,
                rankTo = p.End.Rank,
                bestLevelFrom = p.Before.BestLevel,
                bestLevelTo = p.End.BestLevel,
                bestRank = p.BestRank,
                ranksReached = p.RanksReached.Select(r => new { index = r.Rank, title = r.Rule.Title, qualifiedOn = r.QualifiedOn }),
                ranks = p.Standings.Select(s => new { day = s.Day, level = s.Level, rank = s.Rank, bestRank = s.BestRank, exception = s.Exception })
            }
    };

    private static void WriteSeries(DomainSeries series)
    {
        WriteReading(series.Domain);
        var ranks = series.Domain.Progress?.Rules.Ranks;
        foreach (var d in series.Days)
        {
            var line = $"    {Day(d.Day)}{d.Base,8} base{d.Action,8} action{d.Paid,8} paid";
            if (d.Standing is { } s && ranks is not null)
            {
                line += $"  {(d.Active == true ? "active" : ""),-6}  level {s.Level} {ranks[s.Rank].Title}";
            }

            if (d.Exception is { } comment)
            {
                line += $"  exception: {comment}";
            }

            Console.WriteLine(line);
        }
    }

    private static object SeriesJson(DomainSeries series) => new
    {
        domain = ReadingJson(series.Domain),
        days = series.Days.Select(d => new
        {
            day = d.Day,
            @base = d.Base,
            action = d.Action,
            passive = d.Passive,
            paid = d.Paid,
            exception = d.Exception,
            counted = d.Counted,
            active = d.Active,
            activeMinimum = d.Standing?.ActiveMinimum,
            lifetimeLevel = d.Standing?.LifetimeLevel,
            consistencyLevel = d.Standing?.ConsistencyLevel,
            level = d.Standing?.Level,
            rank = d.Standing?.Rank
        })
    };

    private static void WriteReading(DomainReading d)
    {
        Console.WriteLine($"  {d.Currency.Label,-16}{d.TotalXp,8}");
        if (d.Progress is not { } p)
        {
            Console.WriteLine("    collects XP only (no rules yet)");
            return;
        }

        if (p.Rules.Consistency is HomeConditionRule home)
        {
            WriteHomeReading(p, home);
            return;
        }

        if (p.Start is null)
        {
            Console.WriteLine($"    not started: level 1 ({p.RankRule.Title}); an active day is {p.Rules.Success.Describe(1)}");
            return;
        }

        var consistency = (ConsistencyRule)p.Rules.Consistency;
        var best = p.BestLevel > p.Level ? $" (best {p.BestLevel}, {p.BestRankRule.Title})" : "";
        Console.WriteLine($"    level       {p.Level}, {p.RankRule.Title}{best}");
        Console.WriteLine(p.LimitedBy switch
        {
            LevelLimit.Lifetime => $"    target      {p.Target}: lifetime XP limits it (the last 90 days support {p.ConsistencyLevel})",
            LevelLimit.Consistency => $"    target      {p.Target}: the last 90 days limit it (lifetime XP reaches {p.LifetimeLevel})",
            _ => $"    target      {p.Target}: lifetime XP and the last 90 days agree"
        });
        if (p.Falling)
        {
            var days = p.Rules.Fall.BackOnTrackDays;
            Console.WriteLine(
                $"    falling     {Number(p.Rules.Fall.PercentPerDay)}% a day toward {p.Target}"
                + (p.FallingSince is { } since ? $", since {Day(since)} (from {p.FallingFrom})" : "")
                + $"; {Plural(days, "day")} back on track pauses it: {consistency.Average(p.Level)}+ base XP a day,"
                + $" {consistency.ActiveDaysNeeded(p.Level, days)} active ({consistency.ActiveMinimum(p.Level)}+ action XP)");
        }
        else if (p.Paused)
        {
            Console.WriteLine(p.TodayException
                ? "    paused      today is an exception day"
                : $"    paused      the last {Plural(p.Rules.Fall.BackOnTrackDays, "counted day")} are back on track");
        }

        Console.WriteLine(p.XpToNext == 0
            ? $"    lifetime    level {p.LifetimeLevel}, the top"
            : $"    lifetime    level {p.LifetimeLevel} — {Xp(p.XpIntoLevel)} of {Xp(p.Rules.Curve.CostToNext(p.LifetimeLevel))} toward level {p.LifetimeLevel + 1}");
        var window = p.Window;
        var over = window.Days == 0
            ? "no counted days yet"
            : $"over {Plural(window.Days, "counted day")} ({window.Average.ToString("0.#", CultureInfo.InvariantCulture)} base XP a day)";
        Console.WriteLine($"    consistency level {p.ConsistencyLevel} {over}");
        if (p.NextConsistency is { } next)
        {
            Console.WriteLine(
                $"    next        level {next.Level} needs an average of {next.Average} ({next.AverageNow.ToString("0.#", CultureInfo.InvariantCulture)}{Met(next.AverageMet)}),"
                + $" {next.ActiveDaysNeeded} of {next.WindowDays} days active at {next.ActiveMinimum}+ action XP ({next.ActiveDaysNow}{Met(next.ActiveDaysMet)}),"
                + $" {next.AgeNeeded} active days so far ({next.AgeNow}{Met(next.AgeMet)})");
        }

        Console.WriteLine(p.TodayException
            ? $"    today       an exception day ({p.TodayAction} action XP)"
            : p.TodayActive
                ? $"    today       {p.TodayAction} action XP — active ({p.ActiveMinimum}+ counts)"
                : $"    today       {p.TodayAction} of {p.ActiveMinimum} action XP to count as active ({p.TodayBase} base)");
        var c = p.Consistency;
        Console.WriteLine(p.NextTier is { } tier
            ? $"    multiplier  ×{Multiplier(p.MultiplierNow)} — {c.SuccessfulDays} of the last {c.Days} days active; ×{Multiplier(tier.Multiplier)} at {Percent(tier.MinShare)} ({Plural(c.DaysNeededFor(tier.MinShare), "more active day")})"
            : $"    multiplier  ×{Multiplier(p.MultiplierNow)} — {c.SuccessfulDays} of the last {c.Days} days active, the top tier");
        var recent = p.ExceptionDays.Where(x => x > p.Today.AddDays(-30)).Order().ToList();
        if (recent.Count > 0)
        {
            Console.WriteLine($"    exceptions  {string.Join(", ", recent.Select(Day))} (the last 30 days)");
        }
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
                settledThrough = p.SettledThrough,
                lifetime = new
                {
                    level = p.LifetimeLevel,
                    levelXp = p.Rules.Curve.CostToNext(p.LifetimeLevel),
                    xpIntoLevel = p.XpIntoLevel,
                    xpToNext = p.XpToNext
                },
                consistency = new
                {
                    kind = p.Rules.Consistency.Kind,
                    level = p.ConsistencyLevel,
                    windowDays = p.Window.Days,
                    from = p.Window.From,
                    to = p.Window.To,
                    averageBase = p.Window.Average,
                    averageHomeScore = p.Rules.Consistency is HomeConditionRule ? p.Window.AverageHomeScore : (decimal?)null,
                    held = p.HeldConsistency is not { } held
                        ? null
                        : new
                        {
                            level = held.Level,
                            bar = (p.Rules.Consistency as HomeConditionRule)?.Bar(held.Level),
                            activeMinimum = held.ActiveMinimum,
                            activeDays = held.ActiveDaysNow,
                            windowDays = held.WindowDays
                        },
                    next = p.NextConsistency is not { } next
                        ? null
                        : new
                        {
                            level = next.Level,
                            windowDays = next.WindowDays,
                            average = next.Average,
                            averageNow = next.AverageNow,
                            averageMet = next.AverageMet,
                            activeMinimum = next.ActiveMinimum,
                            activeDaysNeeded = next.ActiveDaysNeeded,
                            activeDays = next.ActiveDaysNow,
                            activeDaysMet = next.ActiveDaysMet,
                            ageNeeded = next.AgeNeeded,
                            age = next.AgeNow,
                            ageMet = next.AgeMet,
                            bar = (p.Rules.Consistency as HomeConditionRule)?.Bar(next.Level)
                        }
                },
                shown = new
                {
                    level = p.Level,
                    exact = (decimal)p.ShownExact / FallRule.Scale,
                    target = p.Target,
                    rank = new { index = p.Rank, title = p.RankRule.Title },
                    limitedBy = p.LimitedBy?.ToString().ToLowerInvariant(),
                    falling = p.Falling,
                    paused = p.Paused,
                    fallingSince = p.FallingSince,
                    fallingFrom = p.FallingFrom,
                    lastChange = p.LastChange is not { } change ? null : new { day = change.Day, from = change.From, to = change.To }
                },
                best = new
                {
                    level = p.BestLevel,
                    levelOn = p.BestLevelOn,
                    rank = new { index = p.BestRank, title = p.BestRankRule.Title },
                    ranksReached = p.RanksReached.Select(r => new { index = r.Rank, title = r.Rule.Title, qualifiedOn = r.QualifiedOn })
                },
                current = new
                {
                    todayBase = p.TodayBase,
                    todayAction = p.TodayAction,
                    todayHomeScore = p.TodayHomeScore is { } todayScore ? todayScore / (decimal)HomeRules.Unit : (decimal?)null,
                    activeMinimum = p.ActiveMinimum,
                    todayActive = p.TodayActive,
                    todayException = p.TodayException,
                    rule = p.Rules.Success.Describe(p.Level),
                    window = new
                    {
                        from = p.Consistency.From,
                        to = p.Consistency.To,
                        days = p.Consistency.Days,
                        coveredDays = p.Consistency.CoveredDays,
                        activeDays = p.Consistency.SuccessfulDays,
                        share = p.Consistency.Share
                    },
                    multiplier = p.MultiplierNow,
                    nextTier = p.NextTier is not { } tier
                        ? null
                        : new
                        {
                            minShare = tier.MinShare,
                            multiplier = tier.Multiplier,
                            daysNeeded = p.Consistency.DaysNeededFor(tier.MinShare)
                        }
                },
                exceptionDays = p.ExceptionDays.Order()
            }
    };

    /// <summary>
    /// A domain held by the home (REWARD-9, environment plan §4.7): the level, what limits it,
    /// the condition level in Home Score bars and kept-up days, today's Home Score against the
    /// bar, and the multiplier in kept-up days.
    /// </summary>
    private static void WriteHomeReading(DomainProgress p, HomeConditionRule home)
    {
        if (p.Start is null)
        {
            Console.WriteLine($"    not started: level 1 ({p.RankRule.Title}); rate your spaces to start your level");
            return;
        }

        var best = p.BestLevel > p.Level ? $" (best {p.BestLevel}, {p.BestRankRule.Title})" : "";
        Console.WriteLine($"    level       {p.Level}, {p.RankRule.Title}{best}");
        Console.WriteLine(p.LimitedBy switch
        {
            LevelLimit.Lifetime => $"    target      {p.Target}: lifetime XP limits it (your home supports {p.ConsistencyLevel})",
            LevelLimit.Consistency => $"    target      {p.Target}: your home holds it (your effort has earned level {p.LifetimeLevel})",
            _ => $"    target      {p.Target}: lifetime XP and your home agree"
        });
        if (p.Falling)
        {
            var days = p.Rules.Fall.BackOnTrackDays;
            Console.WriteLine(
                $"    falling     {Number(p.Rules.Fall.PercentPerDay)}% a day toward {p.Target}"
                + (p.FallingSince is { } since ? $", since {Day(since)} (from {p.FallingFrom})" : "")
                + $"; a Home Score of {Bar(home, p.Level)}+ on {home.ReachedDaysNeeded(p.Level, days)} of the last {Plural(days, "counted day")} pauses it");
        }
        else if (p.Paused)
        {
            Console.WriteLine(p.TodayException
                ? "    paused      today is an exception day"
                : $"    paused      the last {Plural(p.Rules.Fall.BackOnTrackDays, "counted day")} are kept up");
        }

        Console.WriteLine(p.XpToNext == 0
            ? $"    lifetime    level {p.LifetimeLevel}, the top"
            : $"    lifetime    level {p.LifetimeLevel} — {Xp(p.XpIntoLevel)} of {Xp(p.Rules.Curve.CostToNext(p.LifetimeLevel))} toward level {p.LifetimeLevel + 1}");
        var window = p.Window;
        Console.WriteLine(window.Days == 0
            ? $"    condition   level {p.ConsistencyLevel}: no counted days yet — rate your spaces to start your level"
            : $"    condition   level {p.ConsistencyLevel} over {Plural(window.Days, "counted day")} (Home Score {Score(window.AverageHomeScore)} a day)");
        if (p.HeldConsistency is { } held)
        {
            Console.WriteLine($"    held by     a Home Score of {Bar(home, held.Level)}+ on {held.ActiveDaysNow} of the last {Plural(held.WindowDays, "counted day")}");
        }

        if (p.NextConsistency is { } next && window.Days > 0)
        {
            Console.WriteLine(
                $"    next        level {next.Level} needs {Bar(home, next.Level)}+ on {next.ActiveDaysNeeded} of {next.WindowDays} days ({next.ActiveDaysNow}{Met(next.ActiveDaysMet)}),"
                + $" {next.AgeNeeded} such days so far ({next.AgeNow}{Met(next.AgeMet)})");
        }

        var barToday = Bar(home, p.StandingOn(p.Today.AddDays(-1)).Level);
        Console.WriteLine(p.TodayException
            ? "    today       an exception day"
            : p.TodayHomeScore is not { } score
                ? "    today       no Home Score yet: rate all three parts of a space"
                : p.TodayActive
                    ? $"    today       Home Score {HomeCommand.Shown(score)} — kept up ({barToday}+ counts)"
                    : $"    today       Home Score {HomeCommand.Shown(score)}, below the {barToday} bar");
        var c = p.Consistency;
        Console.WriteLine(p.NextTier is { } tier
            ? $"    multiplier  ×{Multiplier(p.MultiplierNow)} — {c.SuccessfulDays} of the last {c.Days} days kept up; ×{Multiplier(tier.Multiplier)} at {Percent(tier.MinShare)} ({Plural(c.DaysNeededFor(tier.MinShare), "more kept-up day")})"
            : $"    multiplier  ×{Multiplier(p.MultiplierNow)} — {c.SuccessfulDays} of the last {c.Days} days kept up, the top tier");
        var recent = p.ExceptionDays.Where(x => x > p.Today.AddDays(-30)).Order().ToList();
        if (recent.Count > 0)
        {
            Console.WriteLine($"    exceptions  {string.Join(", ", recent.Select(Day))} (the last 30 days)");
        }
    }

    private static string Bar(HomeConditionRule home, int level) => HomeConditionRule.Text(home.Bar(level));

    private static string Score(decimal points) => (Math.Floor(points * 10m) / 10m).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Met(bool met) => met ? ", met" : "";

    private static string Day(DateOnly day) => day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Xp(long xp) => xp.ToString("#,0", CultureInfo.InvariantCulture) + " XP";

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Percent(decimal share)
        => (share * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Multiplier(decimal value) => value.ToString("0.0#", CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
