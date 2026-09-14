using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk adhere &lt;habit&gt; &lt;result&gt; [--on YYYY-MM-DD] [--note …]</c> — record
/// how a habit occurrence turned out (GEN-3): <c>followed</c>, <c>partial</c>, or
/// <c>missed</c>. Defaults to today. Recording is append-only, so correcting a past
/// occurrence is just another recording for that date (the projection takes the
/// latest). Partial is only accepted when the habit allows it.
/// </summary>
internal static class AdhereCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var habitArgument = new Argument<string>("habit")
        {
            Description = "The habit (urn, short id, or title)."
        };
        var resultArgument = new Argument<string>("result")
        {
            Description = "followed | partial | missed."
        };
        var onOption = new Option<string?>("--on")
        {
            Description = "The occurrence date (YYYY-MM-DD). Defaults to today."
        };
        var noteOption = new Option<string?>("--note") { Description = "An optional note for this occurrence." };

        var command = new Command("adhere", "Record a habit occurrence as followed / partial / missed.");
        command.Arguments.Add(habitArgument);
        command.Arguments.Add(resultArgument);
        command.Options.Add(onOption);
        command.Options.Add(noteOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var habit = parseResult.GetValue(habitArgument)!;
            var result = parseResult.GetValue(resultArgument)!;
            var onText = parseResult.GetValue(onOption);
            var note = parseResult.GetValue(noteOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var occurrence = ParseOccurrence(onText);

                await using var provider = Cli.BuildServices(connectionString);
                var recorded = await provider.GetRequiredService<AdherenceService>()
                    .RecordAsync(habit, result, occurrence, note, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        eventId = recorded.EventId,
                        habit = recorded.Habit.Urn,
                        result = recorded.Result,
                        occurrence = recorded.Occurrence.ToString("yyyy-MM-dd")
                    });
                }
                else
                {
                    Console.WriteLine(
                        $"Recorded {recorded.Result} for {recorded.Habit.Urn} on {recorded.Occurrence:yyyy-MM-dd}.");
                }

                return 0;
            });
        });

        return command;
    }

    private static DateOnly ParseOccurrence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date))
        {
            throw new ArgumentException($"'{text}' is not a valid date (expected YYYY-MM-DD).");
        }

        return date;
    }
}
