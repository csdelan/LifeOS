using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Food;

/// <summary>
/// Food entry photos (FOOD-5, food-log plan §3.8). A photo is an artifact with bytes, stored
/// through <see cref="IArtifactBlobStore"/> (Supabase Storage when the host has it, else the
/// database), written by an <c>observation</c> event whose payload names the entry
/// (<c>food_photo</c>: {meal}); the entry then gets a new version pointing at it
/// (<see cref="FoodLogService.SetPhotoAsync"/>). The event has no subject and no triage flag,
/// so it shows nowhere but on its entry.
/// <list type="bullet">
/// <item>Only JPEG, PNG and WebP, known by their first bytes, whatever the upload claims.</item>
/// <item>At most <see cref="MaxBytes"/>. The web app sends a 1,200-pixel JPEG of about 150 KB.</item>
/// <item>The same bytes again (from this source) reuse the artifact already stored.</item>
/// </list>
/// A host that must not keep photos in its database (staging and production on Supabase's free
/// plan, without a Storage key) passes <paramref name="enabled"/> false: every upload is refused
/// in words and nothing is stored. Removing a photo still works.
/// </summary>
public sealed class FoodPhotoService(
    IArtifactBlobStore blobs, FoodLogService food, IClock clock, string sourceId, bool enabled = true)
{
    /// <summary>The largest photo accepted: 5 MiB.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>The refusal when photos aren't set up on this host.</summary>
    public const string NotSetUp =
        "Photos aren't set up on this server yet: it needs a Supabase Storage key (Storage__SupabaseSecretKey).";

    /// <summary>True when this host takes photos.</summary>
    public bool Enabled => enabled;

    /// <summary>
    /// Stores <paramref name="bytes"/> as the photo of the entry named by
    /// <paramref name="reference"/>, replacing any photo it had. The entry is checked before
    /// anything is stored.
    /// </summary>
    public async Task<FoodWriteResult> AttachAsync(
        string reference, byte[] bytes, string? filename = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!enabled)
        {
            throw new InvalidOperationException(NotSetUp);
        }

        var meal = await food.ResolveMealAsync(reference, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("That photo is empty.");
        }

        if (bytes.Length > MaxBytes)
        {
            throw new ArgumentException(
                $"That photo is {bytes.Length / (1024.0 * 1024.0):0.0} MB; a photo can be at most {MaxBytes / (1024 * 1024)} MB.");
        }

        var contentType = Sniff(bytes)
            ?? throw new ArgumentException("That isn't a photo: send a JPEG, PNG or WebP image.");

        var now = clock.UtcNow;
        var stored = await blobs.PutAsync(new NewBinaryCapture(
            Kind: EventKinds.Observation,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: sourceId,
            Sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Bytes: bytes,
            ContentType: contentType,
            ByteSize: bytes.Length,
            Filename: CleanName(filename),
            PayloadJson: JsonSerializer.Serialize(new { food_photo = new { meal = meal.Id } })), cancellationToken);

        return await food.SetPhotoAsync(meal.Id.ToString(), stored.ArtifactId, cancellationToken);
    }

    /// <summary>Removes the photo of the entry named by <paramref name="reference"/>; the bytes stay in the record.</summary>
    public Task<FoodWriteResult> RemoveAsync(string reference, CancellationToken cancellationToken = default)
        => food.SetPhotoAsync(reference, null, cancellationToken);

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

    // A file name is kept for export only: its last path part, printable, at most 200 characters.
    private static string? CleanName(string? filename)
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
