using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Measurements;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Lab visits and their results (MEASURE-4, measurements plan §4.9):
/// <list type="bullet">
/// <item><c>bsk lab add [--at …] [--lab …] [--note …] "Glucose=104@70..100" "LDL=131@..99" …</c> logs a visit
/// with its results, all or none; <c>--unit "TSH=mIU/L"</c> (repeatable) adds a test that isn't
/// tracked or on the starter list;</item>
/// <item><c>bsk lab list</c> lists the visits, newest first, and <c>bsk lab show &lt;visit&gt;</c> one with its results;</item>
/// <item><c>bsk lab set &lt;visit&gt; [--at …] [--lab …] [--note …] [results…] [--drop &lt;test&gt;]</c> changes one:
/// a result given replaces that test's (keeping its range unless it gives one), and the rest stay;</item>
/// <item><c>bsk lab report &lt;visit&gt; &lt;file&gt;</c> adds the report (a PDF or a photo), <c>--remove</c> takes it off;</item>
/// <item><c>bsk lab delete &lt;visit&gt;</c> deletes it with its results.</item>
/// </list>
/// A result is <c>test=value</c>, with the range printed beside it after an <c>@</c>: <c>low..high</c>,
/// <c>..high</c> (up to) or <c>low..</c> (at least). <c>--at</c> takes a home-zone time, as
/// <c>bsk measure</c> does; a date alone is noon. A visit is named by its id or its first 4+ hex digits.
/// </summary>
internal static class LabCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("lab", "Lab visits and their results (CMP, lipid panel, A1c): add, list, show, set, report and delete.");
        command.Subcommands.Add(CreateAdd(connectionOption, jsonOption));
        command.Subcommands.Add(CreateList(connectionOption, jsonOption));
        command.Subcommands.Add(CreateShow(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSet(connectionOption, jsonOption));
        command.Subcommands.Add(CreateReport(connectionOption, jsonOption));
        command.Subcommands.Add(CreateDelete(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateAdd(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var resultsArgument = new Argument<string[]>("results")
        {
            Description = "Each result as test=value, with its printed range after @: \"Glucose=104@70..100\", \"LDL=131@..99\".",
            Arity = ArgumentArity.ZeroOrMore
        };
        var atOption = AtOption();
        var labOption = LabOption();
        var noteOption = NoteOption();
        var unitOption = UnitOption();

        var command = new Command("add", "Log a lab visit with its results, e.g. bsk lab add --at 2026-11-09 --lab Quest \"Glucose=104@70..100\".");
        command.Arguments.Add(resultsArgument);
        foreach (var option in new Option[] { atOption, labOption, noteOption, unitOption })
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
                var service = provider.GetRequiredService<LabVisitService>();
                var at = await MeasureCommand.ParseAtAsync(
                    parseResult.GetValue(atOption), provider.GetRequiredService<MetricReadingService>(), cancellationToken);
                var units = ParseUnits(parseResult.GetValue(unitOption) ?? []);
                var results = (parseResult.GetValue(resultsArgument) ?? []).Select(text => ParseResult(text, units)).ToList();
                var read = await service.LogAsync(
                    new LabVisitEntry(at, parseResult.GetValue(labOption), parseResult.GetValue(noteOption), results),
                    cancellationToken);
                return Write(read, asJson, "Logged");
            });
        });
        return command;
    }

    private static Command CreateList(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("list", "The lab visits, newest first, with how many results were outside their range.");
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var list = await provider.GetRequiredService<LabVisitService>().ListAsync(cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(new { today = Day(list.Today), visits = list.Visits.Select(Json) });
                    return 0;
                }

                if (list.Visits.Count == 0)
                {
                    Console.WriteLine("No lab visits yet. Log one: bsk lab add --at 2026-11-09 --lab Quest \"Glucose=104@70..100\"");
                    return 0;
                }

                foreach (var read in list.Visits)
                {
                    Console.WriteLine($"  {Short(read.Visit.Id)}  {Heading(read)}");
                }

                return 0;
            });
        });
        return command;
    }

    private static Command CreateShow(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var visitArgument = VisitArgument();
        var command = new Command("show", "A lab visit with its results, as its report prints them.");
        command.Arguments.Add(visitArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var read = await provider.GetRequiredService<LabVisitService>().GetAsync(
                    parseResult.GetValue(visitArgument)!, cancellationToken);
                return Write(read, asJson, null);
            });
        });
        return command;
    }

    private static Command CreateSet(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var visitArgument = VisitArgument();
        var resultsArgument = new Argument<string[]>("results")
        {
            Description = "Results to add or replace, as test=value[@range]; a replaced result keeps its range unless one is given.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var atOption = AtOption();
        var labOption = LabOption();
        var noteOption = NoteOption();
        var unitOption = UnitOption();
        var dropOption = new Option<string[]>("--drop")
        {
            Description = "Delete this test's result from the visit. Repeatable.",
            AllowMultipleArgumentsPerToken = false
        };

        var command = new Command("set", "Change a lab visit: its time, lab or note, or its results.");
        command.Arguments.Add(visitArgument);
        command.Arguments.Add(resultsArgument);
        foreach (var option in new Option[] { atOption, labOption, noteOption, unitOption, dropOption })
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
                var service = provider.GetRequiredService<LabVisitService>();
                var current = await service.GetAsync(parseResult.GetValue(visitArgument)!, cancellationToken);
                var at = await MeasureCommand.ParseAtAsync(
                    parseResult.GetValue(atOption), provider.GetRequiredService<MetricReadingService>(), cancellationToken);
                var units = ParseUnits(parseResult.GetValue(unitOption) ?? []);
                var changes = (parseResult.GetValue(resultsArgument) ?? []).Select(text => ParseResult(text, units)).ToList();
                var drops = parseResult.GetValue(dropOption) ?? [];
                foreach (var drop in drops)
                {
                    if (!current.Results.Any(r => Same(r.Metric.Name, drop)))
                    {
                        throw new ArgumentException($"This visit has no {drop.Trim()} result to drop.");
                    }
                }

                var results = new List<LabResultEntry>();
                foreach (var kept in current.Results.Where(r => !drops.Any(d => Same(r.Metric.Name, d))))
                {
                    var change = changes.FirstOrDefault(c => Same(c.Test, kept.Metric.Name));
                    var reading = kept.Reading;
                    results.Add(change is null
                        ? new LabResultEntry(kept.Metric.Name, reading.Value, reading.Low, reading.High, reading.Note, Reading: reading.Id.ToString())
                        : change with
                        {
                            Low = change.Low ?? (change.High is null ? reading.Low : null),
                            High = change.High ?? (change.Low is null ? reading.High : null),
                            Note = change.Note ?? reading.Note,
                            Reading = reading.Id.ToString()
                        });
                }

                results.AddRange(changes.Where(c => !current.Results.Any(r => Same(r.Metric.Name, c.Test))));
                var read = await service.SaveAsync(
                    current.Visit.Id.ToString(),
                    new LabVisitEntry(at, parseResult.GetValue(labOption), parseResult.GetValue(noteOption), results),
                    cancellationToken);
                return Write(read, asJson, "Changed");
            });
        });
        return command;
    }

    private static Command CreateReport(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var visitArgument = VisitArgument();
        var fileArgument = new Argument<string?>("file")
        {
            Description = "The report: a PDF, or a JPEG, PNG or WebP photo of it, up to 10 MB.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var removeOption = new Option<bool>("--remove") { Description = "Take the report off the visit." };

        var command = new Command("report", "Add a lab visit's report (a PDF or a photo), or take it off with --remove.");
        command.Arguments.Add(visitArgument);
        command.Arguments.Add(fileArgument);
        command.Options.Add(removeOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var file = parseResult.GetValue(fileArgument);
            var remove = parseResult.GetValue(removeOption);
            return Cli.RunAsync(asJson, async () =>
            {
                if (remove == (file is not null))
                {
                    throw new ArgumentException("Give the report's file, or --remove to take it off.");
                }

                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<LabVisitService>();
                var visit = parseResult.GetValue(visitArgument)!;
                var read = remove
                    ? await service.RemoveReportAsync(visit, cancellationToken)
                    : await service.AttachReportAsync(
                        visit, await File.ReadAllBytesAsync(file!, cancellationToken), Path.GetFileName(file), cancellationToken);
                return Write(read, asJson, remove ? "Took the report off" : "Added the report to");
            });
        });
        return command;
    }

    private static Command CreateDelete(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var visitArgument = VisitArgument();
        var command = new Command("delete", "Delete a lab visit and its results.");
        command.Arguments.Add(visitArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var read = await provider.GetRequiredService<LabVisitService>().DeleteAsync(
                    parseResult.GetValue(visitArgument)!, cancellationToken);
                if (asJson)
                {
                    Cli.WriteJson(new { visit = (object?)null });
                    return 0;
                }

                Console.WriteLine($"Deleted {Short(read.Visit.Id)}: {Heading(read)}");
                return 0;
            });
        });
        return command;
    }

    /// <summary>
    /// A result: <c>test=value</c>, split at the last '=', with the printed range after an '@':
    /// <c>low..high</c>, <c>..high</c> or <c>low..</c>. <paramref name="units"/> gives a new test its unit.
    /// </summary>
    internal static LabResultEntry ParseResult(string text, IReadOnlyDictionary<string, string> units)
    {
        var at = text.LastIndexOf('=');
        if (at <= 0)
        {
            throw new ArgumentException($"A result is test=value, e.g. \"Glucose=104@70..100\"; got '{text}'.");
        }

        var test = text[..at].Trim();
        var rest = text[(at + 1)..];
        var range = rest.IndexOf('@');
        var value = Number(range < 0 ? rest : rest[..range], test);
        decimal? low = null, high = null;
        if (range >= 0)
        {
            var span = rest[(range + 1)..];
            var dots = span.IndexOf("..", StringComparison.Ordinal);
            if (dots < 0)
            {
                throw new ArgumentException($"{test}'s range is low..high, ..high or low.., e.g. 70..100; got '{span}'.");
            }

            low = span[..dots].Trim().Length == 0 ? null : Number(span[..dots], test);
            high = span[(dots + 2)..].Trim().Length == 0 ? null : Number(span[(dots + 2)..], test);
            if (low is null && high is null)
            {
                throw new ArgumentException($"{test}'s range needs a low end, a high end or both, e.g. 70..100.");
            }
        }

        return new LabResultEntry(test, value, low, high, Unit: units.GetValueOrDefault(test));
    }

    /// <summary><c>--unit</c>'s "TSH=mIU/L" pairs, by test name in any case.</summary>
    internal static IReadOnlyDictionary<string, string> ParseUnits(IEnumerable<string> pairs)
    {
        var units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            var at = pair.IndexOf('=');
            if (at <= 0 || at == pair.Length - 1)
            {
                throw new ArgumentException($"--unit takes test=unit, e.g. \"TSH=mIU/L\"; got '{pair}'.");
            }

            units[pair[..at].Trim()] = pair[(at + 1)..].Trim();
        }

        return units;
    }

    internal static object Json(LabVisitRead read) => new
    {
        id = read.Visit.Id,
        lab = read.Visit.Lab,
        note = read.Visit.Note,
        collectedAt = read.Visit.CollectedAt,
        collectedOn = Day(read.Visit.CollectedOn),
        collectedLocal = read.Visit.CollectedLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        report = read.Visit.Report is { } report
            ? new { id = report.ArtifactId, filename = report.Filename, contentType = report.ContentType, byteSize = report.ByteSize }
            : null,
        results = read.Results.Select(r => MeasureCommand.Json(r.Reading, r.Metric)),
        outside = read.Outside,
        loggedAt = read.Visit.LoggedAt,
        versions = read.Visit.Versions
    };

    private static int Write(LabVisitRead read, bool asJson, string? done)
    {
        if (asJson)
        {
            Cli.WriteJson(new { visit = Json(read) });
            return 0;
        }

        Console.WriteLine(done is null ? $"{Short(read.Visit.Id)}  {Heading(read)}" : $"{done} {Short(read.Visit.Id)}: {Heading(read)}");
        if (read.Visit.Note is { } note)
        {
            Console.WriteLine($"  {note}");
        }

        if (read.Visit.Report is { } report)
        {
            Console.WriteLine($"  Report: {report.Filename ?? "(no name)"}, {report.ContentType}, {report.ByteSize:N0} bytes");
        }

        var width = read.Results.Count == 0 ? 0 : read.Results.Max(r => r.Metric.Name.Length);
        foreach (var result in read.Results)
        {
            var m = result.Metric;
            var r = result.Reading;
            var range = r.HasOwnRange
                ? MetricLimits.RangeText(r.Low, r.High, m.Decimals, "") ?? ""
                : m.RangeText() is { } normal ? $"({normal})" : "";
            Console.WriteLine(
                $"  {m.Name.PadRight(width)}  {MetricLimits.Format(r.Value, m.Decimals),10} {r.Unit,-14} {range}{MetricCommand.FlagText(result.Flag)}"
                + (r.Note is null ? "" : $" — {r.Note}"));
        }

        return 0;
    }

    /// <summary>A visit in a line: "Mon Nov 9 2026, 8:15 AM · Quest — 21 results, 3 outside the range".</summary>
    private static string Heading(LabVisitRead read)
    {
        var when = read.Visit.CollectedLocal.ToString("ddd MMM d yyyy, h:mm tt", CultureInfo.InvariantCulture);
        var lab = read.Visit.Lab is { } l ? $" · {l}" : "";
        var count = read.Results.Count == 1 ? "1 result" : $"{read.Results.Count} results";
        var outside = read.Results.Count == 0 ? "" : $", {read.Outside} outside the range";
        return $"{when}{lab} — {count}{outside}";
    }

    private static Argument<string> VisitArgument()
        => new("visit") { Description = "The lab visit's id, or its first 4+ hex digits." };

    private static Option<string?> AtOption()
        => new("--at")
        {
            Description = "When the sample was collected (home-zone time, e.g. \"2026-11-09 08:15\", or 2026-11-09 for noon). Now by default."
        };

    private static Option<string?> LabOption()
        => new("--lab") { Description = "The lab, e.g. Quest Diagnostics (up to 60 characters; \"\" clears it)." };

    private static Option<string?> NoteOption()
        => new("--note") { Description = "A one-line note on the visit, up to 140 characters (\"\" clears it)." };

    private static Option<string[]> UnitOption()
        => new("--unit")
        {
            Description = "A new test's unit, as test=unit, e.g. --unit \"TSH=mIU/L\". Repeatable.",
            AllowMultipleArgumentsPerToken = false
        };

    private static decimal Number(string text, string test)
        => decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"'{text.Trim()}' isn't a number: give {test}'s result as e.g. 104 or 1.02.");

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
