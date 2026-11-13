using System.CommandLine;
using LifeOs.Application.Capture;
using LifeOs.Cli.Editor;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk journal &lt;item&gt; [--text | --file]</c>: adds an entry to an item's journal (JOURNAL-1)
/// through <see cref="JournalService"/>, the way the web and an agent do, so the journal event and
/// its <c>concerns</c> edge are written in one transaction and an entry never lands about nothing
/// (code review CR44, CR63). The entry is <c>--text</c>, else <c>--file</c>'s contents, else stdin
/// when it's redirected, else what's written in <c>$EDITOR</c>; an empty editor buffer writes nothing.
/// </summary>
internal static class JournalCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var itemArgument = new Argument<string>("item")
        {
            Description = "The item the entry is about (urn, short id, or title)."
        };
        var textOption = new Option<string?>("--text", "-t")
        {
            Description = "The entry. Without it or --file, bsk reads stdin when it's redirected, else opens $EDITOR."
        };
        var fileOption = new Option<string?>("--file", "-f")
        {
            Description = "Read the entry from this file."
        };

        var command = new Command("journal", "Add an entry to an item's journal: one journal event concerning the item.");
        command.Arguments.Add(itemArgument);
        command.Options.Add(textOption);
        command.Options.Add(fileOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var item = parseResult.GetValue(itemArgument)!;
            var text = parseResult.GetValue(textOption);
            var file = parseResult.GetValue(fileOption);

            return Cli.RunAsync(asJson, async () =>
            {
                if (text is not null && file is not null)
                {
                    Cli.WriteError(asJson, "Use either --text or --file, not both.");
                    return 1;
                }

                var fromEditor = text is null && file is null && !Console.IsInputRedirected;
                var entry = text
                    ?? (file is not null ? await File.ReadAllTextAsync(file, cancellationToken)
                        : Console.IsInputRedirected ? await Console.In.ReadToEndAsync(cancellationToken)
                        : await EditorBuffer.EditAsync(EditorLauncher.LaunchAsync, cancellationToken: cancellationToken));

                if (fromEditor && string.IsNullOrWhiteSpace(entry))
                {
                    // An empty buffer means the user chose not to write anything: not an error.
                    if (asJson)
                    {
                        Cli.WriteJson(new { captured = false, reason = "empty" });
                    }
                    else
                    {
                        Console.WriteLine("Nothing captured (empty buffer).");
                    }

                    return 0;
                }

                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<JournalService>()
                    .AppendAsync(item, entry, cancellationToken: cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = result.EventId,
                        artifactId = result.ArtifactId,
                        kind = "journal",
                        subject = result.Subject.Urn
                    });
                }
                else
                {
                    Console.WriteLine($"Added journal {result.EventId} to {result.Subject.Title}.");
                }

                return 0;
            });
        });

        return command;
    }
}
