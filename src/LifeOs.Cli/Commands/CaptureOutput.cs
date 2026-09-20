using LifeOs.Application.Capture;

namespace LifeOs.Cli.Commands;

/// <summary>
/// Shared result formatting for the binary-capture verbs (<c>attach</c>, <c>voice</c>),
/// so both surface the same fields — ids, content hash, size, and whether the capture
/// was deduplicated against an identical earlier one.
/// </summary>
internal static class CaptureOutput
{
    public static void Write(bool asJson, AttachmentResult result, string filename, string verb)
    {
        if (asJson)
        {
            Cli.WriteJson(new
            {
                id = result.EventId,
                artifactId = result.ArtifactId,
                kind = result.Kind,
                filename,
                contentType = result.ContentType,
                byteSize = result.ByteSize,
                sha256 = result.Sha256,
                deduplicated = result.Deduplicated
            });
        }
        else if (result.Deduplicated)
        {
            Console.WriteLine(
                $"{verb} {filename} — identical to an earlier capture; reused event {result.EventId} (no new bytes stored).");
        }
        else
        {
            Console.WriteLine($"{verb} {filename} as {result.Kind} event {result.EventId} ({result.ByteSize} bytes).");
        }
    }
}
