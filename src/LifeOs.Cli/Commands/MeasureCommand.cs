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
/// <item><c>bsk measure &lt;metric&gt; &lt;value&gt; [--at …] [--note …]</c> logs one, now or at a past time, and
/// <c>--and "Diastolic BP=78"</c> (repeatable) logs others with it, all or none (MEASURE-3);</item>
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
        var andOption = new Option<string[]>("--and")
        {
            Description = "Another metric's reading taken at the same time, as name=value, e.g. --and \"Diastolic BP=78\". Repeatable.",
            AllowMultipleArgumentsPerToken = false
        };
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
        foreach (var option in new Option[] { atOption, noteOption, andOption, valueOption, editOption, deleteOption, listOption, daysOption })
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
            var together = parseResult.GetValue(andOption) ?? [];

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

                    if (together.Length > 0)
                    {
                        throw new ArgumentException("--and goes with a new reading, not --list.");
                    }

                    var list = await service.ListAsync(
                        words.Length == 1 ? words[0] : null,
                        parseResult.GetValue(daysOption) ?? MetricReadingService.DefaultDays,
                        cancellationToken);
                    if (asJson)
                    {
                        Cli.WriteJson(new { today = Day(list.Today), readings = list.Readings.Select(r => MeasurementRows.Reading(r, Find(metrics, r))) });
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
                if (together.Length > 0 && (delete is not null || edit is not null))
                {
                    throw new ArgumentException("--and goes with a new reading, not --edit or --delete.");
                }

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

                    if (together.Length > 0)
                    {
                        var entries = new List<ReadingEntry> { new(words[0], ParseValue(words[1])) };
                        entries.AddRange(together.Select(ParseEntry));
                        var logged = await service.LogManyAsync(entries, at, note, cancellationToken);
                        if (asJson)
                        {
                            Cli.WriteJson(new MetricReadingsResult
                            {
                                Readings = logged.Select(r => MeasurementRows.Reading(r, Find(metrics, r))).ToList()
                            });
                            return 0;
                        }

                        foreach (var r in logged)
                        {
                            Console.WriteLine($"Logged {Short(r.Id)}: {Describe(r, metrics)}");
                        }

                        return 0;
                    }

                    reading = await service.LogAsync(words[0], ParseValue(words[1]), at, note, cancellationToken);
                    done = "Logged";
                }

                if (asJson)
                {
                    Cli.WriteJson(new MetricReadingResult
                    {
                        Reading = delete is not null ? null : MeasurementRows.Reading(reading, Find(metrics, reading))
                    });
                    return 0;
                }

                Console.WriteLine($"{done} {Short(reading.Id)}: {Describe(reading, metrics)}");
                return 0;
            });
        });
        return command;
    }

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

    /// <summary>An <c>--and</c> reading: "Diastolic BP=78", split at its last '='.</summary>
    internal static ReadingEntry ParseEntry(string text)
    {
        var at = text.LastIndexOf('=');
        return at > 0
            ? new ReadingEntry(text[..at].Trim(), ParseValue(text[(at + 1)..]))
            : throw new ArgumentException($"--and takes name=value, e.g. \"Diastolic BP=78\"; got '{text}'.");
    }

    private static Metric? Find(IReadOnlyList<Metric> metrics, MetricReading reading)
        => metrics.FirstOrDefault(m => m.Id == reading.MetricId);

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
        var flag = metric?.Flag(reading) is { } f ? $" ({f})" : "";
        return $"{metric?.Name ?? "?"} {value.Trim()}{flag} on {when}{note}";
    }

    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
