using System.CommandLine;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk rename &lt;subject&gt; "&lt;new title&gt;"</c> — change a subject's title in
/// place. The URN is deliberately left unchanged: it is an immutable handle, and
/// edges and tags reference the subject by id, so a rename never breaks a link.
/// Renaming a reuse-by-title subject (e.g. a Problem) onto a title that already
/// exists is rejected. Title is a first-class column, so it is not settable through
/// <c>bsk set</c> (attributes only) — this is its own verb.
/// </summary>
internal static class RenameCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var subjectArgument = new Argument<string>("subject")
        {
            Description = "The subject to rename (urn, short id, or title)."
        };
        var titleArgument = new Argument<string>("title")
        {
            Description = "The new title."
        };

        var command = new Command("rename", "Change a subject's title (the URN is unchanged).");
        command.Arguments.Add(subjectArgument);
        command.Arguments.Add(titleArgument);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var subject = parseResult.GetValue(subjectArgument)!;
            var title = parseResult.GetValue(titleArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<RenameService>()
                    .RenameAsync(subject, title, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        urn = result.Subject.Urn,
                        type = result.Subject.Type,
                        title = result.Subject.Title,
                        previousTitle = result.PreviousTitle,
                        changed = result.Changed
                    });
                }
                else if (result.Changed)
                {
                    Console.WriteLine(
                        $"Renamed {result.Subject.Urn}: \"{result.PreviousTitle}\" → \"{result.Subject.Title}\".");
                }
                else
                {
                    Console.WriteLine($"{result.Subject.Urn} already has that title; nothing changed.");
                }

                return 0;
            });
        });

        return command;
    }
}
