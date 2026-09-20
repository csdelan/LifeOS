using System.CommandLine;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk artifact</c> — work with stored artifacts. Today it exposes <c>get</c>, which
/// exports an artifact's binary payload back out byte-for-byte. This is the supported
/// read path for bytes (used by tests and, later, the web API); list views only carry
/// metadata, never the payload.
/// </summary>
internal static class ArtifactCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("artifact", "Work with stored artifacts (export binary payloads).");
        command.Subcommands.Add(CreateGet(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateGet(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var idArgument = new Argument<string>("id")
        {
            Description = "The artifact id (uuid) whose bytes to export."
        };
        var outOption = new Option<string>("--out", "-o")
        {
            Description = "Path to write the bytes to. The original file's bytes are restored exactly.",
            Required = true
        };

        var command = new Command("get", "Export an artifact's binary payload to a file, byte-for-byte.");
        command.Arguments.Add(idArgument);
        command.Options.Add(outOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
            var asJson = parseResult.GetValue(jsonOption);
            var idText = parseResult.GetValue(idArgument)!;
            var outPath = parseResult.GetValue(outOption)!;

            return Cli.RunAsync(asJson, async () =>
            {
                if (!Guid.TryParse(idText, out var artifactId))
                {
                    Cli.WriteError(asJson, $"'{idText}' is not a valid artifact id.");
                    return 1;
                }

                await using var provider = Cli.BuildServices(connectionString);
                var blob = await provider.GetRequiredService<IArtifactBlobStore>()
                    .GetAsync(artifactId, cancellationToken);

                if (blob is null)
                {
                    Cli.WriteError(asJson, $"Artifact {artifactId} has no binary payload.");
                    return 1;
                }

                await File.WriteAllBytesAsync(outPath, blob.Bytes, cancellationToken);

                if (asJson)
                {
                    Cli.WriteJson(new
                    {
                        artifactId = blob.ArtifactId,
                        path = outPath,
                        contentType = blob.ContentType,
                        byteSize = blob.ByteSize,
                        sha256 = blob.Sha256,
                        filename = blob.Filename
                    });
                }
                else
                {
                    Console.WriteLine($"Wrote {blob.ByteSize} bytes to {outPath} (sha256 {blob.Sha256}).");
                }

                return 0;
            });
        });

        return command;
    }
}
