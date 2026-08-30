using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk link &lt;subject&gt; &lt;relation&gt; &lt;subject&gt;</c> — record a directed
/// edge between two subjects (each named by URN, short id, or title). The leaf-only
/// rule is enforced: <c>X serves Task</c> is rejected with a clear message.
/// </summary>
internal static class LinkCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var fromArgument = new Argument<string>("from")
        {
            Description = "The source subject (urn, short id, or title)."
        };
        var relationArgument = new Argument<string>("relation")
        {
            Description = $"The relation: {string.Join(", ", RelationKinds.All)}."
        };
        relationArgument.AcceptOnlyFromAmong([.. RelationKinds.All]);
        var toArgument = new Argument<string>("to")
        {
            Description = "The target subject (urn, short id, or title)."
        };

        var command = new Command("link", "Link two subjects with one of the seven relations.");
        command.Arguments.Add(fromArgument);
        command.Arguments.Add(relationArgument);
        command.Arguments.Add(toArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var from = parseResult.GetValue(fromArgument)!;
            var relation = parseResult.GetValue(relationArgument)!;
            var to = parseResult.GetValue(toArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<RelationService>()
                    .LinkAsync(from, relation, to, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = result.Id,
                        from = new { result.From.Urn, result.From.Type },
                        relation = result.Relation,
                        to = new { result.To.Urn, result.To.Type }
                    });
                }
                else
                {
                    Console.WriteLine($"Linked {result.From.Urn} {result.Relation} {result.To.Urn}.");
                }

                return 0;
            });
        });

        return command;
    }
}
