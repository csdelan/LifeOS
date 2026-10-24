using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk tasks settle</c>: makes the repetitions every repeating Task is owed by today
/// (TASKS-3, D16). A repetition on a calendar date that passed while nothing ran gets its
/// next; nothing's status changes. The web calls the same thing when Tasks or Focus opens.
/// </summary>
internal static class TasksCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var settle = new Command("settle", "Make the repetitions repeating Tasks are owed by today.");
        settle.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<TaskSeriesService>().SettleAsync(cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        series = result.Series,
                        made = result.Made.Select(r => new { urn = r.Urn, title = r.Title })
                    });
                    return 0;
                }

                Console.WriteLine(result.Made.Count == 0
                    ? $"Nothing to make: {result.Series} repeating task(s) up to date."
                    : $"Made {result.Made.Count} repetition(s) across {result.Series} repeating task(s).");
                foreach (var made in result.Made)
                {
                    Console.WriteLine($"  {made.Urn}  {made.Title}");
                }

                return 0;
            });
        });

        var command = new Command("tasks", "Repeating Tasks (D16).");
        command.Subcommands.Add(settle);
        return command;
    }
}
