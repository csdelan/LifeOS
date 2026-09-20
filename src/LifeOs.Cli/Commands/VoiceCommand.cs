using System.CommandLine;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk voice</c> — capture a voice note (CAP-2): one immutable <c>voice</c> event
/// whose artifact carries the audio bytes, with an optional transcript kept as the
/// artifact's text sidecar. This closes the previously unreachable <c>voice</c> event
/// kind. Transcription itself is app-side; the kernel stores the audio and whatever
/// transcript it is given. The capture lands in the Inbox for triage (INBOX-1).
/// </summary>
internal static class VoiceCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var audioOption = new Option<string>("--audio", "-a")
        {
            Description = "Path to the audio recording. Its bytes are copied into the managed store.",
            Required = true
        };
        var transcriptOption = new Option<string?>("--transcript", "-t")
        {
            Description = "Transcript text. Optional (transcript-only may fail app-side)."
        };
        var transcriptFileOption = new Option<string?>("--transcript-file", "-T")
        {
            Description = "Read the transcript from this file instead of --transcript."
        };
        var contentTypeOption = new Option<string?>("--content-type")
        {
            Description = "MIME type of the audio. Inferred from the extension when omitted."
        };

        var command = new Command("voice", "Capture a voice note as one voice event with an audio artifact.");
        command.Options.Add(audioOption);
        command.Options.Add(transcriptOption);
        command.Options.Add(transcriptFileOption);
        command.Options.Add(contentTypeOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var path = parseResult.GetValue(audioOption)!;
            var transcriptText = parseResult.GetValue(transcriptOption);
            var transcriptFile = parseResult.GetValue(transcriptFileOption);
            var contentType = parseResult.GetValue(contentTypeOption);

            return Cli.RunAsync(asJson, async () =>
            {
                if (!File.Exists(path))
                {
                    Cli.WriteError(asJson, $"Audio file not found: {path}");
                    return 1;
                }

                if (transcriptText is not null && transcriptFile is not null)
                {
                    Cli.WriteError(asJson, "Use either --transcript or --transcript-file, not both.");
                    return 1;
                }

                var transcript = transcriptText;
                if (transcriptFile is not null)
                {
                    if (!File.Exists(transcriptFile))
                    {
                        Cli.WriteError(asJson, $"Transcript file not found: {transcriptFile}");
                        return 1;
                    }

                    transcript = await File.ReadAllTextAsync(transcriptFile, cancellationToken);
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var filename = Path.GetFileName(path);

                await using var provider = Cli.BuildServices(connectionString);
                var result = await provider.GetRequiredService<AttachmentService>()
                    .CaptureVoiceAsync(bytes, filename, contentType, transcript, cancellationToken);

                if (!result.Deduplicated)
                {
                    await provider.GetRequiredService<TriageService>()
                        .FlagItemAsync(isEvent: true, result.EventId, cancellationToken);
                }

                CaptureOutput.Write(asJson, result, filename, "Recorded voice note");
                return 0;
            });
        });

        return command;
    }
}
