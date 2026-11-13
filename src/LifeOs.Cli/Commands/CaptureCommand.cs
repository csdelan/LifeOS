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

        var typeOption = new Option<string?>("--type")
        {
            Description = "Note (the default) stays a capture; Idea or Problem makes one from the text."
        };

        var command = new Command("capture", "Capture free text as one immutable note event, flagged in the Inbox.");
        command.Arguments.Add(textArgument);
        command.Options.Add(typeOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var text = parseResult.GetValue(textArgument)!;
            var type = parseResult.GetValue(typeOption);

            return Cli.RunAsync(asJson, async () =>
            {
                await using var provider = Cli.BuildServices(connectionString);
                // Every capture lands in the Inbox for triage (INBOX-1 flag-on-capture).
                var result = await provider.GetRequiredService<InboxFilingService>()
                    .CaptureAsync(text, type, cancellationToken: cancellationToken);
                var item = result.Item;

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        id = result.EventId,
                        artifactId = result.ArtifactId,
                        kind = "note",
                        item = item is null ? null : new { id = item.Id, urn = item.Urn, type = item.Type, title = item.Title }
                    });
                }
                else if (item is null)
                {
                    Console.WriteLine($"Captured note {result.EventId}.");
                }
                else
                {
                    Console.WriteLine($"Captured note {result.EventId} as {item.Type} {item.Urn}: {item.Title}");
                }

                return 0;
            });
        });

        return command;
    }
}
