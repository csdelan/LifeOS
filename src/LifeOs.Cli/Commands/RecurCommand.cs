using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk recur &lt;subject&gt; --freq …</c> — set (or clear) a subject's structured
/// recurrence (D8), shared by Habits, Appointments and Reviews. Examples:
/// <c>--freq daily</c>; <c>--freq weekly --on sun,wed</c>;
/// <c>--freq interval --every 10 --unit days</c>; <c>--freq monthly --on last</c> or
/// <c>--freq monthly --day 15</c>; <c>--freq trigger --cue "after lunch"</c>;
/// <c>--clear</c>. The recurrence is validated and stored as a jsonb object.
/// <para>
/// On a Task it makes a repeating Task (TASKS-3, D16), which also takes
/// <c>--each week</c> (once each week on any day) and <c>--from completion</c> (every N after
/// the last one closed: <c>--freq interval --every 30 --unit days --from completion</c>), and
/// <c>--start</c>, the date it repeats from (default its due date, else today).
/// </para>
/// </summary>
internal static class RecurCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The subject to set recurrence on (urn, short id, or title)."
        };
        var freqOption = new Option<string?>("--freq")
        {
            Description = "daily | weekly | interval | monthly | trigger."
        };
        var onOption = new Option<string[]>("--on")
        {
            Description = "weekly: weekdays (sun..sat); monthly: 'last'. Repeatable or comma-separated.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        var everyOption = new Option<int?>("--every") { Description = "interval: how many units between occurrences." };
        var unitOption = new Option<string?>("--unit") { Description = "interval: days | weeks | months." };
        var dayOption = new Option<int?>("--day") { Description = "monthly: the day of month (1-31)." };
        var cueOption = new Option<string?>("--cue") { Description = "trigger: the cue that prompts the occurrence." };
        var clearOption = new Option<bool>("--clear") { Description = "Remove any recurrence from the subject." };
        var eachOption = new Option<string?>("--each") { Description = "Tasks: once each period on any day (week)." };
        var fromOption = new Option<string?>("--from") { Description = "Tasks, with --freq interval: count from 'completion'." };
        var startOption = new Option<string?>("--start") { Description = "Tasks: the date it repeats from (yyyy-MM-dd)." };

        var command = new Command("recur", "Set or clear a subject's recurrence (D8).");
        command.Arguments.Add(subjectArgument);
        foreach (var option in new Option[]
                 { freqOption, onOption, everyOption, unitOption, dayOption, cueOption, clearOption, eachOption, fromOption, startOption })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;
            var clear = parseResult.GetValue(clearOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var service = provider.GetRequiredService<RecurrenceService>();

                if (clear)
                {
                    var cleared = await service.ClearAsync(subject, cancellationToken);
                    Report(asJson, cleared.Urn, null, []);
                    return 0;
                }

                var freq = (parseResult.GetValue(freqOption) ?? "").Trim().ToLowerInvariant();
                var on = (parseResult.GetValue(onOption) ?? [])
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToList();
                var every = parseResult.GetValue(everyOption);
                var unit = parseResult.GetValue(unitOption);
                var day = parseResult.GetValue(dayOption);
                var cue = parseResult.GetValue(cueOption);
                var each = parseResult.GetValue(eachOption);
                var from = parseResult.GetValue(fromOption);
                var start = ParseStart(parseResult.GetValue(startOption));

                if (!string.IsNullOrWhiteSpace(each))
                {
                    if (freq.Length > 0 && freq != Recurrence.FreqEach)
                    {
                        throw new ArgumentException("Use --each on its own, without --freq.");
                    }

                    freq = Recurrence.FreqEach;
                }

                if (freq.Length == 0)
                {
                    throw new ArgumentException("A --freq is required (or --each, or --clear).");
                }

                var recurrenceJson = Recurrence.Build(freq, on, every, unit, day, cue, each, from);

                var updated = await service.SetAsync(subject, recurrenceJson, start, cancellationToken);
                Report(asJson, updated.Subject.Urn, updated.Recurrence, updated.Made);
                return 0;
            });
        });

        return command;
    }

    private static DateOnly? ParseStart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"--start takes a date as yyyy-MM-dd; got '{text}'.");
    }

    private static void Report(bool asJson, string urn, string? recurrenceJson, IReadOnlyList<SubjectRef> made)
    {
        if (asJson)
        {
            Cli.WriteJson(new
            {
                urn,
                recurrence = recurrenceJson,
                repetitions = made.Select(r => new { urn = r.Urn, title = r.Title })
            });
            return;
        }

        Console.WriteLine(recurrenceJson is null
            ? $"Cleared recurrence on {urn}."
            : $"Set recurrence on {urn}: {recurrenceJson}.");
        foreach (var repetition in made)
        {
            Console.WriteLine($"  made {repetition.Urn}");
        }
    }
}
