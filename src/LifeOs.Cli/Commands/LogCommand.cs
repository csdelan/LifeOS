using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk log activity "text" [--evidences &lt;commitment&gt;] [--violates
/// &lt;commitment&gt;]</c> — record an activity event and mark it, in the event
/// payload, as evidence for or a breach of a Commitment.
/// </summary>
internal static class LogCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var log = new Command("log", "Log events such as activities.");
        log.Subcommands.Add(CreateActivity(connectionOption, jsonOption));
        return log;
    }

    private static Command CreateActivity(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var textArgument = new Argument<string>("text") { Description = "What happened." };

        var evidencesOption = new Option<string[]>("--evidences")
        {
            Description = "A Commitment this activity is evidence for (urn, short id, or title; repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        var violatesOption = new Option<string[]>("--violates")
        {
            Description = "A Commitment this activity breaches (urn, short id, or title; repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };

        var command = new Command("activity", "Log an activity, optionally evidencing or violating a Commitment.");
        command.Arguments.Add(textArgument);
        command.Options.Add(evidencesOption);
        command.Options.Add(violatesOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            var input = new ActivityInput(
                Text: parseResult.GetValue(textArgument)!,
                Evidences: parseResult.GetValue(evidencesOption),
                Violates: parseResult.GetValue(violatesOption));

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<ActivityService>()
                    .LogAsync(input, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        eventId = result.EventId,
                        evidences = result.EvidencesCommitmentIds,
                        violates = result.ViolatesCommitmentIds
                    });
                }
                else
                {
                    Console.WriteLine($"Logged activity {result.EventId}.");
                }

                return 0;
            });
        });

        return command;
    }
}
