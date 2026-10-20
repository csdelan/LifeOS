using System.CommandLine;
using System.Globalization;
using LifeOs.Application.Rewards;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk habit</c> — habit settings that aren't plain attributes:
/// <list type="bullet">
/// <item><c>habit target &lt;habit&gt; [--adherence 80%|0.8] [--days 60] | --clear</c> — show
/// or set the establish target (REWARD-2): the habit is suggested for establishing once it has
/// been followed that share of the time over the last that many days (default 80% over 60).
/// Set either part alone and the other keeps what it is; <c>--clear</c> puts both back on the
/// default. A partial day counts half.</item>
/// </list>
/// Record days with <c>bsk adhere</c>, schedule with <c>bsk recur</c>, and price days,
/// streaks and establishing with <c>bsk bounty</c>.
/// </summary>
internal static class HabitCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("habit", "Habit settings: 'habit target' shows or sets when a habit is ready to establish.");
        command.Subcommands.Add(CreateTarget(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateTarget(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var habitArgument = new Argument<string>("habit")
        {
            Description = "The Habit (urn, short id, or title)."
        };
        var adherenceOption = new Option<string?>("--adherence")
        {
            Description = "The share of days to follow: a percentage (80%, or 80) or a fraction (0.8)."
        };
        var daysOption = new Option<int?>("--days")
        {
            Description = $"The window it is measured over, in days (1 to {EstablishTarget.MaxWindowDays})."
        };
        var clearOption = new Option<bool>("--clear")
        {
            Description = "Put the target back on the default, 80% over 60 days."
        };

        var command = new Command("target", "Show or set when a habit is ready to establish.");
        command.Arguments.Add(habitArgument);
        command.Options.Add(adherenceOption);
        command.Options.Add(daysOption);
        command.Options.Add(clearOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var reference = parseResult.GetValue(habitArgument)!;
            var adherence = parseResult.GetValue(adherenceOption);
            var days = parseResult.GetValue(daysOption);
            var clear = parseResult.GetValue(clearOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var setting = adherence is not null || days is not null;
                if (clear && setting)
                {
                    throw new ArgumentException("Give --adherence and/or --days, or --clear — not both.");
                }

                var minAdherence = adherence is null ? (decimal?)null : ParseAdherence(adherence);

                await using var provider = Cli.BuildServices(connectionString);
                var targets = provider.GetRequiredService<HabitTargetService>();
                var view = clear
                    ? await targets.ClearAsync(reference, cancellationToken)
                    : setting
                        ? await targets.SetAsync(reference, minAdherence, days, cancellationToken)
                        : await targets.GetAsync(reference, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        habit = new { urn = view.Habit.Urn, type = view.Habit.Type, title = view.Habit.Title },
                        minAdherence = view.MinAdherence,
                        windowDays = view.WindowDays,
                        adherenceIsCustom = view.AdherenceIsCustom,
                        windowIsCustom = view.WindowIsCustom
                    });
                    return 0;
                }

                Console.WriteLine($"{view.Habit.Title} ({view.Habit.Urn}) is ready to establish when followed {Describe(view)}.");
                return 0;
            });
        });

        return command;
    }

    /// <summary>"80% of the time (default) over the last 30 days".</summary>
    private static string Describe(HabitTargetView view)
    {
        var percent = (view.MinAdherence * 100).ToString("0.##", CultureInfo.InvariantCulture);
        var days = view.WindowDays == 1 ? "day" : $"{view.WindowDays} days";
        return $"{percent}% of the time{(view.AdherenceIsCustom ? "" : " (default)")} " +
               $"over the last {days}{(view.WindowIsCustom ? "" : " (default)")}";
    }

    /// <summary>
    /// "80%" is a percentage; a bare number is passed on as it is, where 1 or less is a
    /// fraction (0.8) and above 1 a percentage (80) — the service's rule.
    /// </summary>
    private static decimal ParseAdherence(string text)
    {
        var trimmed = text.Trim();
        var percent = trimmed.EndsWith('%');
        if (!decimal.TryParse(
                percent ? trimmed[..^1].TrimEnd() : trimmed,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"--adherence takes a percentage (80%) or a fraction (0.8); got '{text}'.");
        }

        if (!percent)
        {
            return value;
        }

        return value is > 0 and <= 100
            ? value / 100
            : throw new ArgumentException($"A percentage is above 0 and up to 100; got '{text}'.");
    }
}
