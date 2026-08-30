using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk decide "title"</c> — record a Decision as a closed historical fact
/// carrying a prediction. It may result in a subject (or nothing) and may supersede
/// a prior Decision. A Decision carries a prediction, not an obligation.
/// </summary>
internal static class DecideCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var titleArgument = new Argument<string>("title") { Description = "What was decided." };

        var alternativesOption = new Option<string[]>("--alternatives")
        {
            Description = "Alternatives that were considered (repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        var rationaleOption = new Option<string?>("--rationale") { Description = "Why this was decided." };
        var expectedOption = new Option<string?>("--expected-outcome")
        {
            Description = "The outcome predicted — what would make this decision right."
        };
        var confidenceOption = new Option<string?>("--confidence")
        {
            Description = "Confidence in the prediction, e.g. 0.7 or high."
        };
        var reviewAtOption = new Option<string?>("--review-at")
        {
            Description = "When to review whether the prediction held (ISO-8601)."
        };
        var resultsInOption = new Option<string?>("--results-in")
        {
            Description = "A subject this decision results in (urn, short id, or title). Optional."
        };
        var supersedeOption = new Option<string?>("--supersede")
        {
            Description = "A prior Decision this one reverses (urn, short id, or title)."
        };

        var command = new Command("decide", "Record a Decision (a prediction), optionally resulting in or superseding.");
        command.Arguments.Add(titleArgument);
        foreach (var option in new Option[]
                 {
                     alternativesOption, rationaleOption, expectedOption, confidenceOption,
                     reviewAtOption, resultsInOption, supersedeOption
                 })
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            var input = new DecisionInput(
                Title: parseResult.GetValue(titleArgument)!,
                Alternatives: parseResult.GetValue(alternativesOption),
                Rationale: parseResult.GetValue(rationaleOption),
                ExpectedOutcome: parseResult.GetValue(expectedOption),
                Confidence: parseResult.GetValue(confidenceOption),
                ReviewAt: parseResult.GetValue(reviewAtOption),
                ResultsIn: parseResult.GetValue(resultsInOption),
                Supersedes: parseResult.GetValue(supersedeOption));

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<DecisionService>()
                    .DecideAsync(input, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = result.Decision.Id,
                        urn = result.Decision.Urn,
                        title = result.Decision.Title,
                        resultsInSubjectId = result.ResultsInSubjectId,
                        supersedesSubjectId = result.SupersedesSubjectId
                    });
                }
                else
                {
                    Console.WriteLine($"Recorded Decision {result.Decision.Urn}.");
                }

                return 0;
            });
        });

        return command;
    }
}
