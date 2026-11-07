using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Measurements;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// The metrics Chris measures (MEASURE-1, MEASURE-2, measurements plan §4.6):
/// <list type="bullet">
/// <item><c>bsk metric add Weight [--unit lb] [--decimals 1] [--target 185] [--better lower]</c> adds one;
/// a starter-list name fills in what isn't given;</item>
/// <item><c>bsk metric list [--archived]</c> lists them with their newest reading;</item>
/// <item><c>bsk metric set &lt;metric&gt; [--name …] [--unit …] [--decimals N] [--target N | --no-target] [--better …]</c>;</item>
/// <item><c>bsk metric archive &lt;metric&gt;</c> and <c>bsk metric restore &lt;metric&gt;</c>;</item>
/// <item><c>bsk metric show &lt;metric&gt; [--days N]</c>: the readings by day, the trend and the goal date.</item>
/// </list>
/// <c>&lt;metric&gt;</c> is a name (unique among the metrics not archived), a urn, a short id or an id.
/// </summary>
internal static class MetricCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command(
            "metric", "Things you measure (Weight, Waist, HbA1c): add, list, set, archive, restore and show.");
        command.Subcommands.Add(CreateAdd(connectionOption, jsonOption));
        command.Subcommands.Add(CreateList(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSet(connectionOption, jsonOption));
        command.Subcommands.Add(CreateArchive(connectionOption, jsonOption, archive: true));
        command.Subcommands.Add(CreateArchive(connectionOption, jsonOption, archive: false));
        command.Subcommands.Add(CreateShow(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateAdd(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var nameArgument = new Argument<string>("name") { Description = "The metric's name, e.g. Weight." };
        var unitOption = UnitOption();
        var decimalsOption = DecimalsOption();
        var targetOption = TargetOption();
        var betterOption = BetterOption();

        var command = new Command("add", "Add a metric to measure. Weight fills in lb, 1 decimal place and lower is better.");
        command.Arguments.Add(nameArgument);
        foreach (var option in new Option[] { unitOption, decimalsOption, targetOption, betterOption })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var metric = await provider.GetRequiredService<MetricService>().AddAsync(
                    parseResult.GetValue(nameArgument)!, parseResult.GetValue(unitOption),
                    parseResult.GetValue(decimalsOption), parseResult.GetValue(targetOption),
                    parseResult.GetValue(betterOption), cancellationToken);
                return Write(metric, asJson, "Added");
            });
        });
        return command;
    }

    private static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var archivedOption = new Option<bool>("--archived") { Description = "Include archived metrics." };

        var command = new Command("list", "The metrics, with each one's unit, goal and newest reading.");
        command.Options.Add(archivedOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var archived = parseResult.GetValue(archivedOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var metrics = await provider.GetRequiredService<MetricReads>().ListAsync(archived, cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        metrics = metrics.Select(s => new
                        {
                            metric = Json(s.Metric),
                            readings = s.Readings,
                            last = s.Last is { } last ? MeasureCommand.Json(last) : null
                        })
                    });
                    return 0;
                }

                if (metrics.Count == 0)
                {
                    Console.WriteLine("No metrics yet. Add one: bsk metric add Weight --target 185");
                    return 0;
                }

                var width = Math.Max(6, metrics.Max(s => s.Metric.Name.Length));
                foreach (var s in metrics)
                {
                    var m = s.Metric;
                    var goal = m.Target is { } t ? $"goal {m.Format(t)}" : "no goal";
                    var last = s.Last is { } r ? $"last {m.Format(r.Value)} on {Day(r.TakenOn)}" : "no readings";
                    Console.WriteLine(
                        $"{m.Name.PadRight(width)}  {m.Unit,-6}  {goal}, {last}{(m.Archived ? "  (archived)" : "")}");
                }

                return 0;
            });
        });
        return command;
    }

    private static Command CreateSet(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var metricArgument = new Argument<string>("metric") { Description = "The metric, by name or id." };
        var nameOption = new Option<string?>("--name") { Description = "A new name." };
        var unitOption = UnitOption();
        var decimalsOption = DecimalsOption();
        var targetOption = TargetOption();
        var noTargetOption = new Option<bool>("--no-target") { Description = "Remove the target." };
        var betterOption = BetterOption();

        var command = new Command(
            "set", "Rename a metric or change its unit (only while it has no readings), decimals, target or which way is better.");
        command.Arguments.Add(metricArgument);
        foreach (var option in new Option[] { nameOption, unitOption, decimalsOption, targetOption, noTargetOption, betterOption })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var metric = await provider.GetRequiredService<MetricService>().SetAsync(
                    parseResult.GetValue(metricArgument)!,
                    new MetricChange(
                        parseResult.GetValue(nameOption), parseResult.GetValue(unitOption),
                        parseResult.GetValue(decimalsOption), parseResult.GetValue(targetOption),
                        parseResult.GetValue(noTargetOption), parseResult.GetValue(betterOption)),
                    cancellationToken);
                return Write(metric, asJson, "Updated");
            });
        });
        return command;
    }

    private static Command CreateArchive(Option<string?> connectionOption, Option<bool> jsonOption, bool archive)
    {
        var metricArgument = new Argument<string>("metric") { Description = "The metric, by name or id." };
        var command = archive
            ? new Command("archive", "Archive a metric: it takes no new readings, and its history stays.")
            : new Command("restore", "Bring an archived metric back.");
        command.Arguments.Add(metricArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<MetricService>();
                var reference = parseResult.GetValue(metricArgument)!;
                var metric = archive
                    ? await service.ArchiveAsync(reference, cancellationToken)
                    : await service.RestoreAsync(reference, cancellationToken);
                return Write(metric, asJson, archive ? "Archived" : "Restored");
            });
        });
        return command;
    }

    private static Command CreateShow(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var metricArgument = new Argument<string>("metric") { Description = "The metric, by name or id." };
        var daysOption = new Option<int?>("--days") { Description = "The last N days, today among them (all history by default)." };

        var command = new Command("show", "A metric's readings by day, with the trend and the date it's heading for its goal.");
        command.Arguments.Add(metricArgument);
        command.Options.Add(daysOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var read = await provider.GetRequiredService<MetricReads>().ReadAsync(
                    parseResult.GetValue(metricArgument)!, parseResult.GetValue(daysOption), cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        today = Day(read.Today),
                        from = read.From is { } f ? Day(f) : null,
                        metric = Json(read.Metric),
                        readings = read.Readings.Select(MeasureCommand.Json),
                        days = read.Days.Select(p => new { day = Day(p.Day), value = p.Value, trend = p.Trend, readingId = p.ReadingId }),
                        goal = GoalJson(read.Goal)
                    });
                    return 0;
                }

                var m = read.Metric;
                Console.WriteLine($"{m.Name} ({m.Unit}){(m.Target is { } t ? $", goal {m.Format(t)}" : "")}");
                if (read.Days.Count == 0)
                {
                    Console.WriteLine(read.From is null ? "  No readings yet." : "  No readings in that range.");
                }

                foreach (var p in read.Days)
                {
                    Console.WriteLine(
                        $"  {p.Day.ToString("ddd MMM d yyyy", CultureInfo.InvariantCulture),-16}  {MetricLimits.Format(p.Value, m.Decimals),10}"
                        + $"  trend {MetricLimits.Format(p.Trend, m.Decimals)}");
                }

                Console.WriteLine(Describe(read.Goal, m));
                return 0;
            });
        });
        return command;
    }

    /// <summary>A goal in words (measurements plan §2.3): "185 lb by Mar 14 2027, at -1.2 lb a week (from 23 days)."</summary>
    internal static string Describe(MetricGoal goal, Metric metric)
    {
        if (goal.Target is not { } target)
        {
            return "No goal set: bsk metric set " + metric.Name + " --target N";
        }

        var aim = metric.Format(target);
        var rate = goal.RatePerWeek is { } r
            ? $"{(r > 0 ? "+" : "")}{MetricLimits.Format(r, metric.Decimals + 1)} {metric.Unit} a week"
            : "";
        var basis = goal.BasisDays == 1 ? "1 day" : $"{goal.BasisDays} days";
        return goal.Status switch
        {
            GoalStatuses.TooFew => $"Goal {aim}: log another day to see when you'll reach it.",
            GoalStatuses.Reached => $"At or past your goal of {aim}.",
            GoalStatuses.Heading => $"{aim} by {goal.ReachedBy!.Value.ToString("MMM d yyyy", CultureInfo.InvariantCulture)}, at {rate} (from {basis}).",
            GoalStatuses.Far => $"{aim} is more than 2 years away at {rate} (from {basis}).",
            GoalStatuses.NotHeading => $"Not heading toward {aim}: {rate} (from {basis}).",
            _ => ""
        };
    }

    internal static object Json(Metric metric) => new
    {
        id = metric.Id,
        urn = metric.Urn,
        name = metric.Name,
        unit = metric.Unit,
        decimals = metric.Decimals,
        target = metric.Target,
        better = metric.Better,
        catalog = metric.Catalog,
        archivedAt = metric.ArchivedAt,
        archivedOn = metric.ArchivedOn is { } on ? Day(on) : null
    };

    internal static object GoalJson(MetricGoal goal) => new
    {
        status = goal.Status,
        target = goal.Target,
        direction = goal.Direction,
        now = goal.Now,
        ratePerWeek = goal.RatePerWeek,
        reachedBy = goal.ReachedBy is { } by ? Day(by) : null,
        basis = new
        {
            days = goal.BasisDays,
            from = goal.BasisFrom is { } f ? Day(f) : null,
            to = goal.BasisTo is { } t ? Day(t) : null
        }
    };

    private static int Write(Metric metric, bool asJson, string done)
    {
        if (asJson)
        {
            Cli.WriteJson(Json(metric));
            return 0;
        }

        var goal = metric.Target is { } t ? $", goal {metric.Format(t)}" : "";
        var better = metric.Better is { } b ? $", {b} is better" : "";
        var archived = metric.Archived ? " (archived)" : "";
        Console.WriteLine($"{done} {metric.Name}: {metric.Unit}, {metric.Decimals} decimal place{(metric.Decimals == 1 ? "" : "s")}{goal}{better}{archived}.");
        return 0;
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Option<string?> UnitOption() => new("--unit") { Description = "The unit, e.g. lb, in, % or mg/dL." };

    private static Option<int?> DecimalsOption() => new("--decimals")
    {
        Description = $"How many decimal places a reading takes, 0–{MetricLimits.MaxDecimals}."
    };

    private static Option<decimal?> TargetOption() => new("--target") { Description = "Your goal, e.g. 185." };

    private static Option<string?> BetterOption() => new("--better") { Description = "Which way is better: lower or higher." };
}
