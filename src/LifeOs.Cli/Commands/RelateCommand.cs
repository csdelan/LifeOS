using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk relate &lt;event-id&gt; &lt;subject&gt; [--as concerns]</c> — attach an existing
/// captured event to a subject with an <c>event → subject</c> edge (default
/// <c>concerns</c>). This is how a note or journal is filed against the Project,
/// Person, or Goal it is about; idempotent, so relating twice is harmless.
/// </summary>
internal static class RelateCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var eventArgument = new Argument<Guid>("event-id")
        {
            Description = "The captured event to attach."
        };
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The subject it concerns (urn, short id, or title)."
        };
        var asOption = new Option<string>("--as")
        {
            Description = $"The edge relation: {string.Join(", ", SubjectEventRelations.All)}.",
            DefaultValueFactory = _ => SubjectEventRelations.Concerns
        };
        asOption.AcceptOnlyFromAmong([.. SubjectEventRelations.All]);

        var command = new Command("relate", "Attach a captured event to a subject (concerns/evidences/violates).");
        command.Arguments.Add(eventArgument);
        command.Arguments.Add(subjectArgument);
        command.Options.Add(asOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var eventId = parseResult.GetValue(eventArgument);
            var subject = parseResult.GetValue(subjectArgument)!;
            var relation = parseResult.GetValue(asOption)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<RelateService>()
                    .RelateAsync(eventId, subject, relation, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        eventId = result.EventId,
                        subject = new { result.Subject.Urn, result.Subject.Type },
                        relation = result.Relation,
                        created = result.Created
                    });
                }
                else
                {
                    var verb = result.Created ? "Related" : "Already related";
                    Console.WriteLine($"{verb} event {result.EventId} {result.Relation} {result.Subject.Urn}.");
                }

                return 0;
            });
        });

        return command;
    }
}
