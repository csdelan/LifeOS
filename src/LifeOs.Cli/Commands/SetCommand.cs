using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk set &lt;subject&gt; &lt;key=value&gt;…</c> — update a subject's attributes in
/// place (e.g. <c>due=2026-09-02</c>, <c>next_review_at=2026-09-15</c>,
/// <c>expected_cadence=weekly</c>). An empty value (<c>key=</c>) removes the key.
/// Status is not settable here — a status moves only by <c>bsk status</c>.
/// </summary>
internal static class SetCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The subject to update (urn, short id, or title)."
        };
        var assignmentsArgument = new Argument<string[]>("assignments")
        {
            Description = "One or more key=value pairs; an empty value (key=) removes the key.",
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command("set", "Update a subject's attributes (due date, review date, cadence, …).");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(assignmentsArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;
            var assignments = parseResult.GetValue(assignmentsArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                var parsed = ParseAssignments(assignments);

                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<AttributeService>()
                    .SetAsync(subject, parsed, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        urn = result.Subject.Urn,
                        type = result.Subject.Type,
                        set = result.SetKeys,
                        removed = result.RemovedKeys
                    });
                }
                else
                {
                    var parts = new List<string>();
                    if (result.SetKeys.Count > 0)
                    {
                        parts.Add($"set {string.Join(", ", result.SetKeys)}");
                    }

                    if (result.RemovedKeys.Count > 0)
                    {
                        parts.Add($"removed {string.Join(", ", result.RemovedKeys)}");
                    }

                    Console.WriteLine($"Updated {result.Subject.Urn}: {string.Join("; ", parts)}.");
                }

                return 0;
            });
        });

        return command;
    }

    private static List<AttributeAssignment> ParseAssignments(IEnumerable<string> pairs)
    {
        var assignments = new List<AttributeAssignment>();
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                throw new ArgumentException($"set expects key=value; got '{pair}'.");
            }

            var key = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();
            assignments.Add(new AttributeAssignment(key, value.Length == 0 ? null : value));
        }

        return assignments;
    }
}
