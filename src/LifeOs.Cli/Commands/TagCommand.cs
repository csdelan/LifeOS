using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk tag &lt;item&gt; --add x y --remove z</c> — add and remove tags on an item
/// (GEN-1). The item is a subject (urn / short id / title) or an event (its id — a
/// bare uuid that names an existing event). Tags are a classification space kept
/// deliberately separate from relations, so this is its own verb, not part of
/// <c>relate</c> / <c>link</c>. Tags are normalized (lowercased, trimmed).
/// </summary>
internal static class TagCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var itemArgument = new Argument<string>("item")
        {
            Description = "The item to tag: a subject (urn, short id, or title) or an event id."
        };
        var addOption = new Option<string[]>("--add")
        {
            Description = "Tags to add (repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };
        var removeOption = new Option<string[]>("--remove")
        {
            Description = "Tags to remove (repeatable).",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };

        var command = new Command("tag", "Add or remove tags on a subject or event (classification, not relations).");
        command.Arguments.Add(itemArgument);
        command.Options.Add(addOption);
        command.Options.Add(removeOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var item = parseResult.GetValue(itemArgument)!;
            var add = parseResult.GetValue(addOption) ?? [];
            var remove = parseResult.GetValue(removeOption) ?? [];

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<TagService>()
                    .ApplyAsync(item, add, remove, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        item = new { kind = result.Item.IsEvent ? "event" : "subject", id = result.Item.Id },
                        added = result.Added,
                        removed = result.Removed,
                        addedCount = result.AddedCount,
                        removedCount = result.RemovedCount
                    });
                }
                else
                {
                    var parts = new List<string>();
                    if (result.AddedCount > 0)
                    {
                        parts.Add($"added {result.AddedCount}");
                    }

                    if (result.RemovedCount > 0)
                    {
                        parts.Add($"removed {result.RemovedCount}");
                    }

                    var summary = parts.Count > 0 ? string.Join(", ", parts) : "no change";
                    Console.WriteLine($"Tagged {result.Item.Label}: {summary}.");
                }

                return 0;
            });
        });

        return command;
    }
}
