using System.CommandLine;
using LifeOs.Application.Focus;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// The Primary focus (TODAY-1, D6, focus plan G1):
/// <list type="bullet">
/// <item><c>bsk focus</c> (or <c>bsk focus show</c>) says the focus last chosen, and whether that Goal
/// is still Active.</item>
/// <item><c>bsk focus set &lt;goal&gt; [--review R]</c> makes an Active Goal the focus. The goal is
/// an id, URN, short id or title; <c>R</c> is the review it's chosen in.</item>
/// <item><c>bsk focus clear</c> chooses No focus, so Focus suggests the Active Goal with the
/// nearest target date.</item>
/// </list>
/// With <c>--json</c> each prints what <c>POST /api/focus/primary</c> answers.
/// </summary>
internal static class FocusCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("focus", "The Primary focus: the Active Goal you chose in a review.");
        var show = CreateShow(connectionOption, jsonOption);
        command.Subcommands.Add(show);
        command.Subcommands.Add(CreateSet(connectionOption, jsonOption));
        command.Subcommands.Add(CreateClear(connectionOption, jsonOption));
        command.SetAction((parseResult, cancellationToken) => Show(parseResult, connectionOption, jsonOption, cancellationToken));
        return command;
    }

    private static Command CreateShow(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("show", "The focus last chosen, and whether that Goal is still Active.");
        command.SetAction((parseResult, cancellationToken) => Show(parseResult, connectionOption, jsonOption, cancellationToken));
        return command;
    }

    private static Task<int> Show(
        ParseResult parseResult, Option<string?> connectionOption, Option<bool> jsonOption, CancellationToken cancellationToken)
    {
        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
        var asJson = parseResult.GetValue(jsonOption);
        return Cli.RunAsync(asJson, async () =>
        {
            await using var provider = Cli.BuildServices(connectionString);
            var focus = await provider.GetRequiredService<FocusService>().GetAsync(cancellationToken);
            Write(FocusChoiceRow.From(focus), asJson, verb: null);
            return 0;
        });
    }

    private static Command CreateSet(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var goalArgument = new Argument<string>("goal") { Description = "An Active Goal: its id, URN, short id or title." };
        var reviewOption = new Option<string?>("--review") { Description = "The review it's chosen in (its id); turns that review's Keep focus off." };
        var command = new Command("set", "Make an Active Goal the Primary focus.");
        command.Arguments.Add(goalArgument);
        command.Options.Add(reviewOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var review = ReviewId(parseResult.GetValue(reviewOption));
                var change = await provider.GetRequiredService<FocusService>()
                    .SetAsync(parseResult.GetValue(goalArgument), review, cancellationToken);
                Write(FocusChoiceRow.From(change.Focus, change.Written), asJson, change.Written ? "Focus set" : "Already the focus");
                return 0;
            });
        });
        return command;
    }

    private static Command CreateClear(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("clear", "Choose No focus: Focus then suggests the Active Goal with the nearest target date.");
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var change = await provider.GetRequiredService<FocusService>().SetAsync(null, cancellationToken: cancellationToken);
                Write(FocusChoiceRow.From(change.Focus, change.Written), asJson, change.Written ? "Focus cleared" : "No focus already");
                return 0;
            });
        });
        return command;
    }

    private static Guid? ReviewId(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : Guid.TryParse(text.Trim(), out var id)
                ? id
                : throw new ArgumentException($"'{text.Trim()}' isn't a review's id.");

    private static void Write(FocusChoiceRow row, bool asJson, string? verb)
    {
        if (asJson)
        {
            Cli.WriteJson(row);
            return;
        }

        var prefix = verb is null ? "" : $"{verb}. ";
        if (row.ChosenAt is null)
        {
            Console.WriteLine($"{prefix}No Primary focus chosen yet: Focus suggests the Active Goal with the nearest target date.");
            return;
        }

        if (row.GoalId is null)
        {
            Console.WriteLine($"{prefix}No focus: Focus suggests the Active Goal with the nearest target date.");
            return;
        }

        var where = row.ReviewTitle is null ? "with bsk" : $"in the {row.ReviewTitle}";
        var state = row.Holds ? "" : $" It's {row.LapsedAs} now, so Focus suggests another until you choose again.";
        Console.WriteLine($"{prefix}Primary focus: {row.GoalTitle}, chosen {where}. {row.GoalUrn}{state}");
    }
}
