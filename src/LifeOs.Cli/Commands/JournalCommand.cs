using System.CommandLine;
using LifeOs.Application.Capture;
using LifeOs.Cli.Editor;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

internal static class JournalCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("journal", "Open $EDITOR and capture the buffer as one immutable journal event.");
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);

            return Cli.RunAsync(asJson, async () =>
            {
                var text = await EditorBuffer.EditAsync(EditorLauncher.LaunchAsync, cancellationToken: cancellationToken);

                if (string.IsNullOrWhiteSpace(text))
                {
                    // An empty buffer means the user chose not to write anything.
                    // That is not an error; capture nothing.
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
                var result = await provider.GetRequiredService<CaptureService>()
                    .CaptureJournalAsync(text, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new { id = result.EventId, artifactId = result.ArtifactId, kind = "journal" });
                }
                else
                {
                    Console.WriteLine($"Captured journal {result.EventId}.");
                }

                return 0;
            });
        });

        return command;
    }
}
