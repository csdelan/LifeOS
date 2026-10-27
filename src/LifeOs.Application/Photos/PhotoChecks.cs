namespace LifeOs.Application.Photos;

/// <summary>
/// What a photo upload must be, for the Food Log (FOOD-5) and the home's spaces (HOME-4) alike, so
/// both refuse the same things in the same words:
/// <list type="bullet">
/// <item>JPEG, PNG or WebP, known by its first bytes, whatever the upload claims;</item>
/// <item>not empty, and at most <see cref="MaxBytes"/> (the web app sends a 1,200-pixel JPEG of
/// about 150 KB).</item>
/// </list>
/// A host that must not keep photos in its database (staging and production on Supabase's free
/// plan, without a Storage key) refuses every upload with <see cref="NotSetUp"/>.
/// </summary>
public static class PhotoChecks
{
    /// <summary>The largest photo accepted: 5 MiB.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>What a photo can be, known by its first bytes (<see cref="Sniff"/>).</summary>
    public static readonly IReadOnlySet<string> PhotoTypes = new HashSet<string>(["image/jpeg", "image/png", "image/webp"]);

    /// <summary>The refusal when photos aren't set up on this host.</summary>
    public const string NotSetUp =
        "Photos aren't set up on this server yet: it needs a Supabase Storage key (Storage__SupabaseSecretKey).";

    /// <summary>
    /// The image type of <paramref name="bytes"/>, or refused in words: empty, over
    /// <see cref="MaxBytes"/>, or not a JPEG, PNG or WebP.
    /// </summary>
    public static string Check(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("That photo is empty.");
        }

        if (bytes.Length > MaxBytes)
        {
            throw new ArgumentException(TooBig(bytes.Length));
        }

        return Sniff(bytes) ?? throw new ArgumentException("That isn't a photo: send a JPEG, PNG or WebP image.");
    }

    /// <summary>The refusal for a photo of <paramref name="length"/> bytes, over the limit.</summary>
    public static string TooBig(long length)
        => $"That photo is {length / (1024.0 * 1024.0):0.0} MB; a photo can be at most {MaxBytes / (1024 * 1024)} MB.";

    /// <summary>The image type the bytes begin with: JPEG, PNG or WebP, else null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>A file name as kept, for export only: its last path part, printable, at most 200 characters.</summary>
    public static string? CleanName(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        var name = filename.Replace('\\', '/').Split('/')[^1].Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        return name.Length == 0 ? null : name.Length > 200 ? name[..200] : name;
    }
}
