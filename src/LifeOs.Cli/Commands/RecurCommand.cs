using System.CommandLine;
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

        var command = new Command("recur", "Set or clear a subject's recurrence (D8).");
        command.Arguments.Add(subjectArgument);
        foreach (var option in new Option[] { freqOption, onOption, everyOption, unitOption, dayOption, cueOption, clearOption })
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
                    Report(asJson, cleared.Urn, null);
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

                var recurrenceJson = freq switch
                {
                    Recurrence.FreqDaily => Recurrence.Daily(),
                    Recurrence.FreqWeekly => Recurrence.Weekly(on),
                    Recurrence.FreqInterval => Recurrence.Interval(
                        every ?? throw new ArgumentException("interval recurrence needs --every."), unit ?? ""),
                    Recurrence.FreqMonthly => day is { } d
                        ? Recurrence.MonthlyDay(d)
                        : on.Contains("last", StringComparer.OrdinalIgnoreCase)
                            ? Recurrence.MonthlyLast()
                            : throw new ArgumentException("monthly recurrence needs --day <n> or --on last."),
                    Recurrence.FreqTrigger => Recurrence.Trigger(cue ?? ""),
                    "" => throw new ArgumentException("A --freq is required (or use --clear)."),
                    _ => throw new ArgumentException(
                        $"Unknown --freq '{freq}'. Expected: daily, weekly, interval, monthly, trigger.")
                };

                var updated = await service.SetAsync(subject, recurrenceJson, cancellationToken);
                Report(asJson, updated.Urn, recurrenceJson);
                return 0;
            });
        });

        return command;
    }

    private static void Report(bool asJson, string urn, string? recurrenceJson)
    {
        if (asJson)
        {
            Cli.WriteJson(new { urn, recurrence = recurrenceJson });
        }
        else
        {
            Console.WriteLine(recurrenceJson is null
                ? $"Cleared recurrence on {urn}."
                : $"Set recurrence on {urn}: {recurrenceJson}.");
        }
    }
}
