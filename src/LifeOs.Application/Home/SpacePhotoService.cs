using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Photos;
using LifeOs.Domain;

namespace LifeOs.Application.Home;

/// <summary>The photos of one space (HOME-4): the space and its live photos, newest first.</summary>
public sealed record SpacePhotoList(Space Space, IReadOnlyList<SpacePhoto> Photos);

/// <summary>
/// Photos of the home's spaces (HOME-4, D18, environment plan §8), behind <c>bsk space photo</c>
/// and <c>/api/environment/…/photos</c>. A photo belongs to a space and one of its shots on a
/// day, not to a rating, so one can be added without rating and a deleted rating keeps its
/// photos. It is a <c>declared</c> <c>observation</c> event,
/// <c>{space_photo: {id, space, shot, day, photo?}}</c>, with a <c>concerns</c> edge to the space:
/// the first version is the event that stores the bytes (its artifact is the photo); a new day or
/// shot writes a newer version carrying <c>photo</c>; removing writes <c>{id, voided: true}</c>
/// and the bytes stay in the record.
/// <para>
/// A shot's baseline is its first photo until Chris picks another (<see cref="SetBaselineAsync"/>).
/// Photos never change the Home Score, the level or the ledger, so no write here folds the
/// domains. The bytes are checked by <see cref="PhotoChecks"/>, as the Food Log's are, and a host
/// without photo storage (<paramref name="enabled"/> false) refuses every upload in words.
/// Days are today or earlier in the home zone.
/// </para>
/// </summary>
public sealed class SpacePhotoService(
    IArtifactBlobStore blobs,
    ISpaceRepository spaces,
    SpaceService spaceService,
    ISubjectEventRepository edges,
    IHomeCalendar calendar,
    IClock clock,
    string sourceId,
    bool enabled = true)
{
    /// <summary>True when this host takes photos.</summary>
    public bool Enabled => enabled;

    /// <summary>
    /// Adds <paramref name="bytes"/> as a photo of <paramref name="space"/>, from
    /// <paramref name="shot"/> (its first shot when null) on <paramref name="day"/> (today in the
    /// home zone when null). The same bytes already on that shot stay as they are and come back.
    /// Refused, with nothing stored, for an archived or unknown space, an unknown shot, a day
    /// after today, or bytes that aren't a photo.
    /// </summary>
    public async Task<SpacePhoto> AddAsync(
        string space, byte[] bytes, DateOnly? day = null, string? shot = null, string? filename = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!enabled)
        {
            throw new InvalidOperationException(PhotoChecks.NotSetUp);
        }

        var target = await spaceService.ResolveAsync(space, cancellationToken);
        if (target.Archived)
        {
            throw new InvalidOperationException($"'{target.Name}' is archived: bring it back before adding photos.");
        }

        var onShot = SpaceService.ResolveShot(target, shot);
        var now = clock.UtcNow;
        var today = await calendar.DateAtAsync(now, cancellationToken);
        var on = CheckDay(day ?? today, today);
        var contentType = PhotoChecks.Check(bytes);

        var id = Guid.NewGuid();
        var occurredAt = await OccurredAtAsync(on, today, now, cancellationToken);
        var stored = await blobs.PutAsync(new NewBinaryCapture(
            Kind: EventKinds.Observation,
            Provenance: Provenances.Declared,
            OccurredAt: occurredAt,
            RecordedAt: now,
            SourceId: sourceId,
            Sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Bytes: bytes,
            ContentType: contentType,
            ByteSize: bytes.Length,
            Filename: PhotoChecks.CleanName(filename),
            PayloadJson: Payload(id, target.Id, onShot.Id, on, photo: null)), cancellationToken);

        if (!stored.Deduplicated)
        {
            await edges.CreateEdgeAsync(
                stored.EventId, target.Id, SubjectEventRelations.Concerns, Provenances.Declared, cancellationToken);
            return await ReadAsync(id, cancellationToken);
        }

        // The same bytes were stored before. Already a photo of this shot: nothing to add.
        // Otherwise they become a new photo, pointing at the bytes already stored.
        var existing = (await spaces.GetPhotosAsync(target.Id, cancellationToken))
            .FirstOrDefault(p => p.ArtifactId == stored.ArtifactId && Same(p.ShotId, onShot.Id));
        if (existing is not null)
        {
            return existing;
        }

        await spaces.AppendPhotoVersionsAsync(
            [new SpacePhotoWrite(Declared(occurredAt, now, Payload(id, target.Id, onShot.Id, on, stored.ArtifactId)), target.Id)],
            cancellationToken);
        return await ReadAsync(id, cancellationToken);
    }

    /// <summary>
    /// Moves a photo to another day or shot. Changing nothing writes nothing. Refused for a day
    /// after today, a shot the space doesn't have, or a photo that doesn't exist.
    /// </summary>
    public async Task<SpacePhoto> ChangeAsync(
        string reference, DateOnly? day = null, string? shot = null, CancellationToken cancellationToken = default)
    {
        if (day is null && string.IsNullOrWhiteSpace(shot))
        {
            throw new ArgumentException("Say what to change: a day or a shot.");
        }

        var photo = await ResolveAsync(reference, cancellationToken);
        var space = await SpaceOfAsync(photo, cancellationToken);
        var now = clock.UtcNow;
        var today = await calendar.DateAtAsync(now, cancellationToken);
        var on = CheckDay(day ?? photo.Day, today);
        var shotId = string.IsNullOrWhiteSpace(shot) ? photo.ShotId : SpaceService.ResolveShot(space, shot).Id;
        if (on == photo.Day && Same(shotId, photo.ShotId))
        {
            return photo;
        }

        var occurredAt = await OccurredAtAsync(on, today, now, cancellationToken);
        await spaces.AppendPhotoVersionsAsync(
            [new SpacePhotoWrite(Declared(occurredAt, now, Payload(photo.Id, space.Id, shotId, on, photo.ArtifactId)), space.Id)],
            cancellationToken);
        return await ReadAsync(photo.Id, cancellationToken);
    }

    /// <summary>
    /// Removes a photo: a voided version, so the record keeps it (and its bytes). When it was a
    /// shot's baseline, the shot's first photo left takes over. Returns the photo as it was.
    /// </summary>
    public async Task<SpacePhoto> RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var photo = await ResolveAsync(reference, cancellationToken);
        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [SpaceLimits.PhotoKey] = new Dictionary<string, object> { ["id"] = photo.Id.ToString(), ["voided"] = true }
        });
        await spaces.AppendPhotoVersionsAsync([new SpacePhotoWrite(Declared(now, now, payload), photo.SpaceId)], cancellationToken);
        return photo;
    }

    /// <summary>
    /// Makes a photo its shot's baseline: the Then its newest photo is compared against. Refused
    /// when the photo's shot was removed (move it to a shot first).
    /// </summary>
    public async Task<SpacePhoto> SetBaselineAsync(string reference, CancellationToken cancellationToken = default)
    {
        var photo = await ResolveAsync(reference, cancellationToken);
        var space = await SpaceOfAsync(photo, cancellationToken);
        var shot = space.Shots.FirstOrDefault(s => Same(s.Id, photo.ShotId))
                   ?? throw new InvalidOperationException(
                       $"That photo's shot was removed from {space.Name}: move it to one of its shots first.");
        await spaceService.SetBaselineAsync(space, shot.Id, photo.Id, cancellationToken);
        return photo;
    }

    /// <summary>
    /// The photos of <paramref name="space"/>, of <paramref name="shot"/> only when given, newest
    /// first. Photos of a shot that was removed are left out.
    /// </summary>
    public async Task<SpacePhotoList> ListAsync(
        string space, string? shot = null, CancellationToken cancellationToken = default)
    {
        var target = await spaceService.ResolveAsync(space, cancellationToken);
        var shots = string.IsNullOrWhiteSpace(shot) ? target.Shots : [SpaceService.ResolveShot(target, shot)];
        var photos = (await spaces.GetPhotosAsync(target.Id, cancellationToken))
            .Where(p => shots.Any(s => Same(s.Id, p.ShotId)))
            .OrderByDescending(p => p.Day).ThenByDescending(p => p.AddedAt).ThenByDescending(p => p.Id)
            .ToList();
        return new SpacePhotoList(target, photos);
    }

    /// <summary>A live photo named by its id or the start of it (at least 4 hex digits).</summary>
    public async Task<SpacePhoto> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var wanted = reference?.Trim() ?? "";
        if (Guid.TryParse(wanted, out var id))
        {
            return await spaces.GetPhotoAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No photo has the id {id} (it may have been removed).");
        }

        if (wanted.Length < 4 || !wanted.All(ch => Uri.IsHexDigit(ch) || ch == '-'))
        {
            throw new ArgumentException($"'{reference}' isn't a photo's id: give at least its first 4 hex digits.");
        }

        var matches = await spaces.FindPhotosAsync(wanted, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No photo's id starts with '{wanted}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException($"'{wanted}' starts {matches.Count} photos' ids: give more of the id.")
        };
    }

    private async Task<Space> SpaceOfAsync(SpacePhoto photo, CancellationToken cancellationToken)
        => await spaces.GetSpaceAsync(photo.SpaceId, cancellationToken)
           ?? throw new InvalidOperationException("That photo's space no longer exists.");

    private async Task<SpacePhoto> ReadAsync(Guid id, CancellationToken cancellationToken)
        => await spaces.GetPhotoAsync(id, cancellationToken)
           ?? throw new InvalidOperationException("The photo was stored but can't be read back.");

    private static DateOnly CheckDay(DateOnly day, DateOnly today)
        => day <= today
            ? day
            : throw new ArgumentException(
                $"A photo is for today or earlier; {Text(day)} hasn't happened yet (today is {Text(today)}).");

    private async Task<DateTimeOffset> OccurredAtAsync(
        DateOnly on, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
        => on == today ? now : await spaces.NoonAsync(on, cancellationToken);

    private NewEvent Declared(DateTimeOffset occurredAt, DateTimeOffset now, string payload)
        => new(EventKinds.Observation, Provenances.Declared, occurredAt, now, sourceId, PayloadJson: payload);

    /// <summary>A photo version's payload; <paramref name="photo"/> left out when the event stores the bytes itself.</summary>
    private static string Payload(Guid id, Guid space, string shot, DateOnly day, Guid? photo)
    {
        var version = new Dictionary<string, object>
        {
            ["id"] = id.ToString(),
            ["space"] = space.ToString(),
            ["shot"] = shot,
            ["day"] = Text(day)
        };
        if (photo is { } p)
        {
            version["photo"] = p.ToString();
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { [SpaceLimits.PhotoKey] = version });
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
