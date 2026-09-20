namespace LifeOs.Application.Capture;

/// <summary>
/// A tiny extension → MIME map for binary captures, so a caller that does not supply
/// a content type still gets a useful one for the common voice / document formats.
/// Deliberately small: an unknown extension falls back to <see cref="Default"/> rather
/// than pretending to recognise every format. The caller's explicit type always wins.
/// </summary>
public static class ContentTypes
{
    /// <summary>The fallback when nothing better is known.</summary>
    public const string Default = "application/octet-stream";

    private static readonly IReadOnlyDictionary<string, string> ByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Documents / images (CAP-4)
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".heic"] = "image/heic",
            // Audio (CAP-2 voice)
            [".wav"] = "audio/wav",
            [".mp3"] = "audio/mpeg",
            [".m4a"] = "audio/mp4",
            [".aac"] = "audio/aac",
            [".ogg"] = "audio/ogg",
            [".opus"] = "audio/opus",
            [".flac"] = "audio/flac",
            [".webm"] = "audio/webm",
        };

    /// <summary>
    /// Guesses a content type from a filename's extension, or returns
    /// <see cref="Default"/> when the name is empty or the extension is unknown.
    /// </summary>
    public static string Guess(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return Default;
        }

        var extension = Path.GetExtension(filename);
        return ByExtension.TryGetValue(extension, out var contentType) ? contentType : Default;
    }
}
