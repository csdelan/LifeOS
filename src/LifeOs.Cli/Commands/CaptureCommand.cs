using System.CommandLine;
using LifeOs.Application.Capture;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

internal static class CaptureCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var textArgument = new Argument<string>("text")
        {
            Description = "The note text to capture."
        };

        var command = new Command("capture", "Capture free text as one immutable note event.");
        command.Arguments.Add(textArgument);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var text = parseResult.GetValue(textArgument)!;

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<CaptureService>()
                    .CaptureNoteAsync(text, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new { id = result.EventId, artifactId = result.ArtifactId, kind = "note" });
                }
                else
                {
                    Console.WriteLine($"Captured note {result.EventId}.");
                }

                return 0;
            });
        });

        return command;
    }
}
