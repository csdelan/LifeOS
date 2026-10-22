using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Activity;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk oura</c> — the Oura connection and what it brings in (ACTIVITY-1..4, activity plan
/// §3.1, §3.6). The CLI never connects: it uses the connection made on the Activity page, with
/// <c>Oura__ClientId</c>, <c>Oura__ClientSecret</c> and <c>Integrations__TokenKey</c> from the
/// environment. The database it points at decides which connection that is (dev and staging
/// each have their own).
/// <list type="bullet">
/// <item><c>bsk oura status</c> — the connection's state. Never prints a token.</item>
/// <item><c>bsk oura refresh</c> — spends the refresh token for a new pair now, to check
/// refreshing works (activity plan A6).</item>
/// <item><c>bsk oura sync [--from &lt;date&gt;]</c> — reads the last 14 days and the rest of
/// the history; with <c>--from</c>, every day since then.</item>
/// <item><c>bsk oura steps | workouts | sleep [--days N]</c> — what LifeOS holds.</item>
/// <item><c>bsk oura note &lt;workout&gt; "text" | --clear</c> — a one-line note on a workout.</item>
/// </list>
/// </summary>
internal static class OuraCommand
{
    private const int DefaultDays = 14;

    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command(
            "oura", "Your Oura Ring: 'oura status', 'oura sync', 'oura steps', 'oura workouts', 'oura sleep', 'oura note'.");
        command.Subcommands.Add(CreateStatus(connectionOption, jsonOption));
        command.Subcommands.Add(CreateRefresh(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSync(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSteps(connectionOption, jsonOption));
        command.Subcommands.Add(CreateWorkouts(connectionOption, jsonOption));
        command.Subcommands.Add(CreateSleep(connectionOption, jsonOption));
        command.Subcommands.Add(CreateNote(connectionOption, jsonOption));
        return command;
    }

    // ---------------------------------------------------------------------------------
    // status / refresh / sync
    // ---------------------------------------------------------------------------------

    private static Command CreateStatus(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("status", "Show whether Oura is connected, its scopes and the last sync.");
        command.SetAction((parseResult, cancellationToken) => Run(
            parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                WriteStatus(await services.GetRequiredService<OuraConnectionService>().GetStatusAsync(ct), asJson);
            }, cancellationToken));
        return command;
    }

    private static Command CreateRefresh(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("refresh", "Refresh Oura's access token now (refresh tokens are single-use).");
        command.SetAction((parseResult, cancellationToken) => Run(
            parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                WriteStatus(await services.GetRequiredService<OuraConnectionService>().RefreshAsync(ct), asJson);
            }, cancellationToken));
        return command;
    }

    private static Command CreateSync(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var fromOption = new Option<string?>("--from")
        {
            Description = "Read every day from this one (YYYY-MM-DD) through today, instead of the last 14 days and the history."
        };
        var command = new Command(
            "sync", "Read Oura's last 14 days, then the rest of the history until it's all in. Only what changed is written.");
        command.Options.Add(fromOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var fromText = parseResult.GetValue(fromOption);
            return Run(parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                var from = fromText is null ? (DateOnly?)null : ParseDay(fromText);
                var result = await services.GetRequiredService<OuraSyncService>().SyncAsync(
                    new OuraSyncOptions(from, HistoryWindows: int.MaxValue), ct);
                if (asJson)
                {
                    Cli.WriteJson(result);
                    return;
                }

                var changed = result.Written == 0
                    ? "nothing new"
                    : $"{result.Written} {Plural(result.Written, "version")} written"
                      + (result.Voided > 0 ? $", {result.Voided} withdrawn (deleted in the Oura app)" : "");
                Console.WriteLine(
                    $"Synced {Day(result.From)} to {Day(result.To)}: {changed}. "
                    + $"Oura answered {result.Records} {Plural(result.Records, "record")} across {result.Windows} {Plural(result.Windows, "window")}.");
                if (result.HistoryFrom is { } historyFrom)
                {
                    Console.WriteLine(result.HistoryDone
                        ? $"History: all in, back to {Day(historyFrom)}."
                        : $"History: back to {Day(historyFrom)} so far; the next sync reads further.");
                }
            }, cancellationToken);
        });
        return command;
    }

    // ---------------------------------------------------------------------------------
    // steps / workouts / sleep
    // ---------------------------------------------------------------------------------

    private static Option<int> DaysOption(int fallback) => new("--days")
    {
        Description = $"How many days through today (1 to {ActivityReads.MaxRangeDays}). Defaults to {fallback}.",
        DefaultValueFactory = _ => fallback
    };

    private static Command CreateSteps(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var daysOption = DaysOption(DefaultDays);
        var command = new Command(
            "steps", $"Steps by day, against the {ActivityTargets.MinimumSteps:N0} minimum and the {ActivityTargets.GoalSteps:N0} goal.");
        command.Options.Add(daysOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var days = parseResult.GetValue(daysOption);
            return Run(parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                var reads = services.GetRequiredService<ActivityReads>();
                var (from, to) = await reads.LastDaysAsync(days, ct);
                var rows = await reads.GetDaysAsync(from, to, ct);
                if (asJson)
                {
                    Cli.WriteJson(rows.Select(d => new
                    {
                        d.Day,
                        d.Steps,
                        band = d.Steps is { } s ? ActivityTargets.Band(s) : null,
                        d.ActiveCalories,
                        d.TotalCalories,
                        d.WalkingM,
                        d.HighS,
                        d.MediumS,
                        d.LowS,
                        d.NonWearS,
                        d.Score,
                        d.SyncedAt,
                        d.Versions
                    }));
                    return;
                }

                if (rows.Count == 0)
                {
                    Console.WriteLine($"No Oura activity from {Day(from)} to {Day(to)}. Run 'bsk oura sync'.");
                    return;
                }

                Console.WriteLine($"Steps — minimum {ActivityTargets.MinimumSteps:N0}, goal {ActivityTargets.GoalSteps:N0}");
                foreach (var day in rows)
                {
                    var band = day.Steps is { } steps
                        ? ActivityTargets.Band(steps) switch
                        {
                            StepBands.Goal => "goal",
                            StepBands.Minimum => "minimum",
                            _ => "under"
                        }
                        : "";
                    var active = (day.HighS ?? 0) + (day.MediumS ?? 0);
                    Console.WriteLine(
                        $"  {Day(day.Day)} {WeekDay(day.Day)}  {Number(day.Steps),7}  {band,-8} "
                        + $"{(active > 0 ? $"{Minutes(active)} active" : ""),-14}{(day.ActiveCalories is { } cal ? $"{cal:N0} kcal" : "")}");
                }

                var counted = rows.Where(d => d.Steps is not null).ToList();
                if (counted.Count > 0)
                {
                    Console.WriteLine(
                        $"  Average {counted.Average(d => d.Steps!.Value):N0} a day; "
                        + $"{counted.Count(d => d.Steps >= ActivityTargets.GoalSteps)} at the goal, "
                        + $"{counted.Count(d => d.Steps < ActivityTargets.MinimumSteps)} under the minimum.");
                }
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateWorkouts(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var daysOption = DaysOption(30);
        var allOption = new Option<bool>("--all") { Description = "Include workouts that don't count as exercise (walks, Oura's detections), with why." };
        var command = new Command("workouts", "Workouts that count as exercise, newest first, with their notes.");
        command.Options.Add(daysOption);
        command.Options.Add(allOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var days = parseResult.GetValue(daysOption);
            var all = parseResult.GetValue(allOption);
            return Run(parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                var reads = services.GetRequiredService<ActivityReads>();
                var (from, to) = await reads.LastDaysAsync(days, ct);
                var workouts = await reads.GetWorkoutsAsync(from, to, all, ct);
                if (asJson)
                {
                    Cli.WriteJson(workouts.Select(WorkoutJson));
                    return;
                }

                if (workouts.Count == 0)
                {
                    Console.WriteLine(all
                        ? $"No workouts from {Day(from)} to {Day(to)}."
                        : $"No workouts that count from {Day(from)} to {Day(to)}. 'bsk oura workouts --all' shows the rest.");
                    return;
                }

                foreach (var workout in workouts)
                {
                    WriteWorkout(workout);
                }

                var counted = workouts.Where(w => w.Verdict.Counted).ToList();
                Console.WriteLine(
                    $"  {counted.Count} {Plural(counted.Count, "session")} counted, {Minutes(counted.Sum(w => w.Seconds))} in all.");
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateSleep(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var daysOption = DaysOption(DefaultDays);
        var command = new Command("sleep", "Sleep and readiness by day: the scores, the night and any naps.");
        command.Options.Add(daysOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var days = parseResult.GetValue(daysOption);
            return Run(parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                var reads = services.GetRequiredService<ActivityReads>();
                var (from, to) = await reads.LastDaysAsync(days, ct);
                var rows = await reads.GetSleepAsync(from, to, ct);
                if (asJson)
                {
                    Cli.WriteJson(rows);
                    return;
                }

                if (rows.Count == 0)
                {
                    Console.WriteLine($"No Oura sleep from {Day(from)} to {Day(to)}. Run 'bsk oura sync'.");
                    return;
                }

                Console.WriteLine("Sleep — score, time asleep, bed to wake; readiness");
                foreach (var row in rows)
                {
                    var sleep = row.Sleep;
                    var night = sleep?.BedAt is { } bed && sleep.WokeAt is { } woke
                        ? $"{Clock(bed)}–{Clock(woke)}"
                        : "";
                    var naps = sleep is { Naps: > 0 } ? $"  +{sleep.Naps} {Plural(sleep.Naps, "nap")} ({Minutes(sleep.NapS)})" : "";
                    Console.WriteLine(
                        $"  {Day(row.Day)} {WeekDay(row.Day)}  sleep {Number(sleep?.Score),3}  "
                        + $"{(sleep?.AsleepS is { } asleep ? Minutes(asleep) : ""),-10} {night,-11}  "
                        + $"readiness {Number(row.Readiness?.Score),3}{naps}");
                }
            }, cancellationToken);
        });
        return command;
    }

    // ---------------------------------------------------------------------------------
    // note
    // ---------------------------------------------------------------------------------

    private static Command CreateNote(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var workoutArgument = new Argument<string>("workout") { Description = "The workout (its Oura id or first 8 characters)." };
        var textArgument = new Argument<string?>("text")
        {
            Description = $"The note: one line, up to {WorkoutNoteService.MaxLength} characters.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var clearOption = new Option<bool>("--clear") { Description = "Remove the workout's note." };
        var command = new Command("note", "Set or clear a one-line note on a workout. Syncs never touch it.");
        command.Arguments.Add(workoutArgument);
        command.Arguments.Add(textArgument);
        command.Options.Add(clearOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var reference = parseResult.GetValue(workoutArgument) ?? "";
            var text = parseResult.GetValue(textArgument);
            var clear = parseResult.GetValue(clearOption);
            return Run(parseResult, connectionOption, jsonOption, async (services, asJson, ct) =>
            {
                if (clear == (text is not null))
                {
                    throw new ArgumentException("Give the note's text, or --clear to remove it (not both).");
                }

                var notes = services.GetRequiredService<WorkoutNoteService>();
                var workout = clear
                    ? await notes.ClearAsync(reference, ct)
                    : await notes.SetAsync(reference, text, ct);
                if (asJson)
                {
                    Cli.WriteJson(WorkoutJson(workout));
                    return;
                }

                Console.WriteLine(workout.Note is null ? "Note cleared." : "Note saved.");
                WriteWorkout(workout);
            }, cancellationToken);
        });
        return command;
    }

    // ---------------------------------------------------------------------------------
    // Output
    // ---------------------------------------------------------------------------------

    private static Task<int> Run(
        ParseResult parseResult, Option<string?> connectionOption, Option<bool> jsonOption,
        Func<IServiceProvider, bool, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
        var asJson = parseResult.GetValue(jsonOption);

        return Cli.RunAsync(asJson, async () =>
        {
            await using var provider = Cli.BuildServices(connectionString);
            await action(provider, asJson, cancellationToken);
            return 0;
        });
    }

    private static object WorkoutJson(Workout workout) => new
    {
        workout.Id,
        workout.Day,
        workout.Activity,
        workout.Label,
        workout.StartedAt,
        workout.EndedAt,
        workout.Seconds,
        workout.Calories,
        workout.DistanceM,
        workout.Intensity,
        workout.Source,
        counted = workout.Verdict.Counted,
        reason = workout.Verdict.Reason,
        workout.Note,
        workout.NoteAt,
        workout.SyncedAt
    };

    private static void WriteWorkout(Workout workout)
    {
        var name = workout.Label is { Length: > 0 } label ? $"{label} ({workout.Activity})" : workout.Activity;
        var extra = string.Join(", ", new[]
        {
            workout.DistanceM is { } m and > 0 ? $"{m / 1000.0:0.0} km" : null,
            workout.Calories is { } cal and > 0 ? $"{cal:N0} kcal" : null,
            workout.Intensity
        }.Where(s => s is not null));
        var verdict = workout.Verdict.Counted ? "" : $"  — not counted: {workout.Verdict.Reason}";
        Console.WriteLine(
            $"  {Short(workout.Id)}  {Day(workout.Day)} {Clock(workout.StartedAt)}  {name}, {Minutes(workout.Seconds)}"
            + $"{(extra.Length > 0 ? $" ({extra})" : "")}  [{workout.Source}]{verdict}");
        if (workout.Note is { } note)
        {
            Console.WriteLine($"            “{note}”");
        }
    }

    private static void WriteStatus(OuraStatus status, bool asJson)
    {
        if (asJson)
        {
            Cli.WriteJson(status);
            return;
        }

        Console.WriteLine(status.State switch
        {
            OuraStatus.NotConfigured =>
                "Oura isn't set up here: set Oura__ClientId, Oura__ClientSecret and Integrations__TokenKey.",
            OuraStatus.NotConnected => "Oura isn't connected: press Connect Oura on the Activity page.",
            OuraStatus.Reconnect => $"Oura needs reconnecting: {status.ReconnectReason ?? "Oura refused the stored sign-in."}",
            _ => "Oura is connected."
        });

        if (status.ConnectedAt is { } connectedAt)
        {
            Console.WriteLine($"  Connected     {RewardOutput.Stamp(connectedAt)}");
        }

        if (status.ExpiresAt is { } expiresAt)
        {
            Console.WriteLine($"  Token until   {RewardOutput.Stamp(expiresAt)} (refreshed before then)");
        }

        if (status.Scopes.Count > 0)
        {
            Console.WriteLine($"  Scopes        {string.Join(", ", status.Scopes)}");
        }

        if (status.MissingScopes.Count > 0)
        {
            Console.WriteLine($"  Not granted   {string.Join(", ", status.MissingScopes)}: reconnect and allow them");
        }

        if (status.LastSyncedAt is { } syncedAt)
        {
            Console.WriteLine($"  Last sync     {RewardOutput.Stamp(syncedAt)}");
        }
        else if (status.Connected)
        {
            Console.WriteLine("  Last sync     never");
        }

        if (status.HistoryFrom is { } historyFrom)
        {
            Console.WriteLine($"  History       back to {Day(historyFrom)}{(status.HistoryDone ? ", all in" : " so far")}");
        }

        if (status.LastSyncError is { } error)
        {
            Console.WriteLine($"  Sync error    {error}");
        }
    }

    private static DateOnly ParseDay(string text)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new ArgumentException($"'{text}' is not a day (expected YYYY-MM-DD).");

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string WeekDay(DateOnly day) => day.DayOfWeek.ToString()[..3];

    private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Short(string id) => id.Length <= WorkoutNoteService.MinPrefix ? id : id[..WorkoutNoteService.MinPrefix];

    private static string Number(int? value) => value is { } v ? v.ToString("N0", CultureInfo.InvariantCulture) : "—";

    private static string Minutes(int seconds)
    {
        var minutes = (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";
}
