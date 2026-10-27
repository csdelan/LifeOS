using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Photos;
using LifeOs.Domain;

namespace LifeOs.Application.Food;

/// <summary>
/// Food entry photos (FOOD-5, food-log plan §3.8). A photo is an artifact with bytes, stored
/// through <see cref="IArtifactBlobStore"/> (Supabase Storage when the host has it, else the
/// database), written by an <c>observation</c> event whose payload names the entry
/// (<c>food_photo</c>: {meal}); the entry then gets a new version pointing at it
/// (<see cref="FoodLogService.SetPhotoAsync"/>). The event has no subject and no triage flag,
/// so it shows nowhere but on its entry. What a photo can be is <see cref="PhotoChecks"/>'s,
/// shared with the home's spaces; the same bytes again (from this source) reuse the artifact
/// already stored.
/// <para>
/// A host that must not keep photos in its database passes <paramref name="enabled"/> false:
/// every upload is refused in words and nothing is stored. Removing a photo still works.
/// </para>
/// </summary>
public sealed class FoodPhotoService(
    IArtifactBlobStore blobs, FoodLogService food, IClock clock, string sourceId, bool enabled = true)
{
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
            throw new InvalidOperationException(PhotoChecks.NotSetUp);
        }

        var meal = await food.ResolveMealAsync(reference, cancellationToken);
        var contentType = PhotoChecks.Check(bytes);

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
            Filename: PhotoChecks.CleanName(filename),
            PayloadJson: JsonSerializer.Serialize(new { food_photo = new { meal = meal.Id } })), cancellationToken);

        return await food.SetPhotoAsync(meal.Id.ToString(), stored.ArtifactId, cancellationToken);
    }

    /// <summary>Removes the photo of the entry named by <paramref name="reference"/>; the bytes stay in the record.</summary>
    public Task<FoodWriteResult> RemoveAsync(string reference, CancellationToken cancellationToken = default)
        => food.SetPhotoAsync(reference, null, cancellationToken);
}
