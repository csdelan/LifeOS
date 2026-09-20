using System.CommandLine;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk attach</c> — capture a document/attachment (CAP-4 / D4): one immutable
/// <c>note</c> event whose artifact carries the file's bytes, copied into the managed
/// store. An optional description is kept as the artifact's text sidecar. The capture
/// lands in the Inbox for triage, like every other capture (INBOX-1).
/// </summary>
internal static class AttachCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var fileOption = new Option<string>("--file", "-f")
        {
            Description = "Path to the file to attach. Its bytes are copied into the managed store.",
            Required = true
        };
        var contentTypeOption = new Option<string?>("--content-type")
        {
            Description = "MIME type of the file. Inferred from the extension when omitted."
        };
        var descriptionOption = new Option<string?>("--description", "-d")
        {
            Description = "A text description of the attachment's relevance (stored with it)."
        };

        var command = new Command("attach", "Capture a document/attachment as one note event with a binary artifact.");
        command.Options.Add(fileOption);
        command.Options.Add(contentTypeOption);
        command.Options.Add(descriptionOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var path = parseResult.GetValue(fileOption)!;
            var contentType = parseResult.GetValue(contentTypeOption);
            var description = parseResult.GetValue(descriptionOption);

            return Cli.RunAsync(asJson, async () =>
            {
                if (!File.Exists(path))
                {
                    Cli.WriteError(asJson, $"File not found: {path}");
                    return 1;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var filename = Path.GetFileName(path);

                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<AttachmentService>()
                    .CaptureAttachmentAsync(bytes, filename, contentType, description, cancellationToken);

                // A newly captured attachment enters the inbox; a dedup hit is already there.
                if (!result.Deduplicated)
                {
                    await provider.GetRequiredService<TriageService>()
                        .FlagItemAsync(isEvent: true, result.EventId, cancellationToken);
                }

                CaptureOutput.Write(asJson, result, filename, "Attached");
                return 0;
            });
        });

        return command;
    }
}
