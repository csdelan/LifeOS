using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk timer &lt;action&gt; &lt;task&gt;</c> — drive a Task's optional timer
/// (TASKS-2): <c>start</c>, <c>pause</c>, <c>resume</c>, <c>stop</c>, <c>complete</c>
/// (stop, then mark the Task Completed), or <c>show</c> to print it. Starting or
/// resuming pauses any other running timer. Every action is an append-only event; the
/// recorded time is folded from them.
/// </summary>
internal static class TimerCommand
{
    private const string Complete = "complete";
    private const string Show = "show";

    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var actionArgument = new Argument<string>("action")
        {
            Description = "start | pause | resume | stop | complete | show."
        };
        var taskArgument = new Argument<string>("task")
        {
            Description = "The task (urn, short id, or title)."
        };

        var command = new Command("timer", "Start, pause, resume, stop or complete a Task's timer.");
        command.Arguments.Add(actionArgument);
        command.Arguments.Add(taskArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var action = parseResult.GetValue(actionArgument)!.Trim().ToLowerInvariant();
            var task = parseResult.GetValue(taskArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var timers = provider.GetRequiredService<TimerService>();

                TimerResult? result = null;
                TimerSnapshot timer;
                switch (action)
                {
                    case Show:
                        timer = await timers.GetAsync(task, cancellationToken);
                        break;
                    case Complete:
                        result = await timers.CompleteAsync(task, cancellationToken);
                        timer = result.Timer;
                        break;
                    default:
                        result = await timers.ApplyAsync(task, action, cancellationToken);
                        timer = result.Timer;
                        break;
                }

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        task = timer.Urn,
                        title = timer.Title,
                        state = timer.State,
                        runningSince = timer.RunningSince,
                        totalSeconds = timer.TotalSeconds,
                        sessionSeconds = timer.SessionSeconds,
                        paused = (result?.Paused ?? []).Select(p => new { urn = p.Urn, title = p.Title }),
                        completed = result?.Completed ?? false,
                        awards = RewardOutput.AwardsJson(result?.Awards ?? []),
                        progress = RewardOutput.ProgressJson(result?.Progress ?? [])
                    });
                    return 0;
                }

                foreach (var other in result?.Paused ?? [])
                {
                    Console.WriteLine($"Paused the timer on {other.Urn}.");
                }

                var running = timer.State == TimerStates.Running ? $" since {timer.RunningSince:u}" : "";
                Console.WriteLine(
                    $"{timer.Urn}: {timer.State}{running}; {FormatSeconds(timer.TotalSeconds)} recorded" +
                    (result?.Completed == true ? "; marked Completed." : "."));
                RewardOutput.WriteAwards(result?.Awards ?? []);
                RewardOutput.WriteProgress(result?.Progress ?? []);
                return 0;
            });
        });

        return command;
    }

    private static string FormatSeconds(long seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : $"{span.Minutes}m {span.Seconds:00}s";
    }
}
