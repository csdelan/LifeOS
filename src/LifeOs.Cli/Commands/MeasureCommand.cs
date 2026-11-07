using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Measurements;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Readings of a metric (MEASURE-1, measurements plan §4.6):
/// <list type="bullet">
/// <item><c>bsk measure &lt;metric&gt; &lt;value&gt; [--at …] [--note …]</c> logs one, now or at a past time;</item>
/// <item><c>bsk measure --edit &lt;id&gt; [--value N] [--at …] [--note …]</c> changes one, and
/// <c>bsk measure --delete &lt;id&gt;</c> deletes one;</item>
/// <item><c>bsk measure --list [&lt;metric&gt;] [--days N]</c> lists them, newest first.</item>
/// </list>
/// <c>--at</c> takes a home-zone time: <c>7:10</c>, <c>"yesterday 7:00"</c>, <c>"2026-11-01 07:10"</c>,
/// a date alone (noon that day), or an ISO time with an offset.
/// </summary>
internal static class MeasureCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var wordsArgument = new Argument<string[]>("words")
        {
            Description = "<metric> <value>: the metric by name, then the reading, e.g. Weight 192.4.",
            Arity = new ArgumentArity(0, 2)
        };
        var atOption = new Option<string?>("--at")
        {
            Description = "When it was taken (home-zone time, e.g. 7:10, \"yesterday 7:00\", \"2026-11-01 07:10\" or 2026-11-01 for noon). Now by default."
        };
        var noteOption = new Option<string?>("--note") { Description = "A one-line note, up to 140 characters." };
        var valueOption = new Option<decimal?>("--value") { Description = "With --edit, the new value." };
        var editOption = new Option<string?>("--edit") { Description = "Change the reading with this id (or the start of it)." };
        var deleteOption = new Option<string?>("--delete") { Description = "Delete the reading with this id (or the start of it)." };
        var listOption = new Option<bool>("--list") { Description = "List the readings, newest first (of one metric when named)." };
        var daysOption = new Option<int?>("--days")
        {
            Description = $"With --list, how many days back (default {MetricReadingService.DefaultDays})."
        };

        var command = new Command("measure", "Log a reading of a metric, e.g. bsk measure Weight 192.4.");
        command.Arguments.Add(wordsArgument);
        foreach (var option in new Option[] { atOption, noteOption, valueOption, editOption, deleteOption, listOption, daysOption })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var words = parseResult.GetValue(wordsArgument) ?? [];
            var edit = parseResult.GetValue(editOption);
            var delete = parseResult.GetValue(deleteOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<MetricReadingService>();
                var metrics = await provider.GetRequiredService<MetricService>().ListAsync(true, cancellationToken);

                if (parseResult.GetValue(listOption))
                {
                    if (words.Length > 1)
                    {
                        throw new ArgumentException("--list takes at most a metric's name.");
                    }

                    var list = await service.ListAsync(
                        words.Length == 1 ? words[0] : null,
                        parseResult.GetValue(daysOption) ?? MetricReadingService.DefaultDays,
                        cancellationToken);
                    if (asJson)
                    {
                        Cli.WriteJson(new { today = Day(list.Today), readings = list.Readings.Select(Json) });
                        return 0;
                    }

                    if (list.Readings.Count == 0)
                    {
                        Console.WriteLine($"No readings in the last {list.Days} days.");
                        return 0;
                    }

                    foreach (var r in list.Readings)
                    {
                        Console.WriteLine($"  {Short(r.Id)}  {Describe(r, metrics)}");
                    }

                    return 0;
                }

                var at = await ParseAtAsync(parseResult.GetValue(atOption), service, cancellationToken);
                var note = parseResult.GetValue(noteOption);
                MetricReading reading;
                string done;
                if (delete is not null)
                {
                    reading = await service.DeleteAsync(delete, cancellationToken);
                    done = "Deleted";
                }
                else if (edit is not null)
                {
                    if (words.Length > 0)
                    {
                        throw new ArgumentException("--edit takes its changes as options (--value 192.4, --at 7:10, --note …).");
                    }

                    reading = await service.EditAsync(
                        edit, new ReadingEdit(parseResult.GetValue(valueOption), at, note), cancellationToken);
                    done = "Changed";
                }
                else
                {
                    if (words.Length != 2)
                    {
                        throw new ArgumentException("Give the metric and the value, e.g. bsk measure Weight 192.4.");
                    }

                    if (parseResult.GetValue(valueOption) is not null)
                    {
                        throw new ArgumentException("--value goes with --edit; a new reading takes the value after the metric.");
                    }

                    reading = await service.LogAsync(words[0], ParseValue(words[1]), at, note, cancellationToken);
                    done = "Logged";
                }

                if (asJson)
                {
                    Cli.WriteJson(new { reading = delete is not null ? null : Json(reading) });
                    return 0;
                }

                Console.WriteLine($"{done} {Short(reading.Id)}: {Describe(reading, metrics)}");
                return 0;
            });
        });
        return command;
    }

    internal static object Json(MetricReading reading) => new
    {
        id = reading.Id,
        metricId = reading.MetricId,
        value = reading.Value,
        unit = reading.Unit,
        note = reading.Note,
        takenAt = reading.TakenAt,
        takenOn = Day(reading.TakenOn),
        takenLocal = reading.TakenLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        loggedAt = reading.LoggedAt,
        versions = reading.Versions
    };

    /// <summary>A time for <c>--at</c>: the Food Log's forms, plus a date alone, which is noon that day.</summary>
    internal static async Task<DateTimeOffset?> ParseAtAsync(
        string? text, MetricReadingService service, CancellationToken cancellationToken)
    {
        var value = text?.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return await service.InstantAtAsync(day.ToDateTime(new TimeOnly(12, 0)), cancellationToken);
        }

        if (string.Equals(value, "yesterday", StringComparison.OrdinalIgnoreCase))
        {
            var today = await service.TodayAsync(cancellationToken);
            return await service.InstantAtAsync(today.AddDays(-1).ToDateTime(new TimeOnly(12, 0)), cancellationToken);
        }

        return await FoodCommand.ParseAtAsync(text, service.InstantAtAsync, service.TodayAsync, cancellationToken);
    }

    private static decimal ParseValue(string text)
        => decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"'{text}' isn't a number: give the reading as e.g. 192.4.");

    private static string Describe(MetricReading reading, IReadOnlyList<Metric> metrics)
    {
        var metric = metrics.FirstOrDefault(m => m.Id == reading.MetricId);
        var value = metric is null
            ? $"{reading.Value.ToString(CultureInfo.InvariantCulture)} {reading.Unit}"
            : $"{MetricLimits.Format(reading.Value, metric.Decimals)} {reading.Unit}";
        var when = reading.TakenLocal.ToString("ddd MMM d yyyy, h:mm tt", CultureInfo.InvariantCulture);
        var note = reading.Note is null ? "" : $" — {reading.Note}";
        return $"{metric?.Name ?? "?"} {value.Trim()} on {when}{note}";
    }

    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
