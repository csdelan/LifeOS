using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk unlink &lt;subject&gt; &lt;relation&gt; &lt;subject&gt;</c> — remove a directed
/// edge between two subjects (the inverse of <c>bsk link</c>). Alignment edges are
/// structural facts rather than lifecycle events, so removal is a hard delete, not a
/// tombstone. Idempotent: unlinking an edge that is not there reports "no edge" and
/// still exits 0.
/// </summary>
internal static class UnlinkCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var fromArgument = new Argument<string>("from")
        {
            Description = "The source subject (urn, short id, or title)."
        };
        var relationArgument = new Argument<string>("relation")
        {
            Description = $"The subject-to-subject relation: {string.Join(", ", SubjectRelations.All)}."
        };
        relationArgument.AcceptOnlyFromAmong([.. SubjectRelations.All]);
        var toArgument = new Argument<string>("to")
        {
            Description = "The target subject (urn, short id, or title)."
        };

        var command = new Command("unlink", "Remove a subject-to-subject relation edge.");
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
                    .UnlinkAsync(from, relation, to, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        from = new { result.From.Urn, result.From.Type },
                        relation = result.Relation,
                        to = new { result.To.Urn, result.To.Type },
                        removed = result.Removed
                    });
                }
                else if (result.Removed > 0)
                {
                    Console.WriteLine($"Unlinked {result.From.Urn} {result.Relation} {result.To.Urn}.");
                }
                else
                {
                    Console.WriteLine($"No {result.Relation} edge from {result.From.Urn} to {result.To.Urn}.");
                }

                return 0;
            });
        });

        return command;
    }
}
