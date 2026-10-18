using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk rename-tag &lt;old&gt; &lt;new&gt;</c> — rename a tag on every item that
/// carries it (GEN-1). A tag is identified only by its text, so this relabels each use;
/// renaming onto a tag that already exists merges the two. Its own verb rather than a
/// <c>bsk tag</c> option, because <c>tag</c> acts on one item and this acts on the tag.
/// </summary>
internal static class RenameTagCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var oldArgument = new Argument<string>("old")
        {
            Description = "The tag to rename."
        };
        var newArgument = new Argument<string>("new")
        {
            Description = "The new tag name. If it is already in use, the two tags merge."
        };

        var command = new Command("rename-tag", "Rename a tag everywhere it is used (merges into an existing tag).");
        command.Arguments.Add(oldArgument);
        command.Arguments.Add(newArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var oldTag = parseResult.GetValue(oldArgument)!;
            var newTag = parseResult.GetValue(newArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<TagService>()
                    .RenameAsync(oldTag, newTag, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        from = result.From,
                        to = result.To,
                        renamed = result.Renamed,
                        merged = result.Merged
                    });
                }
                else if (result.Renamed == 0 && result.Merged == 0)
                {
                    Console.WriteLine($"'{result.From}' is already named that; nothing changed.");
                }
                else
                {
                    var merged = result.Merged > 0 ? $", {result.Merged} merged into an existing '{result.To}'" : "";
                    Console.WriteLine($"Renamed tag '{result.From}' → '{result.To}': {result.Renamed} item(s){merged}.");
                }

                return 0;
            });
        });

        return command;
    }
}
