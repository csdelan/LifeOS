using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// The home's spaces, ratings and photos (D18).

/// <summary>A rating event to append and the space its <c>concerns</c> edge points at (HOME-2).</summary>
public sealed record SpaceRatingWrite(NewEvent Event, Guid SpaceId);

/// <summary>A photo version to append and the space its <c>concerns</c> edge points at (HOME-4).</summary>
public sealed record SpacePhotoWrite(NewEvent Event, Guid SpaceId);

/// <summary>
/// Reads the home's spaces, their ratings and their photos (HOME-1, HOME-2, HOME-4, environment
/// plan §4.1–§4.3, §8) through <c>bsk.v_space</c>, <c>bsk.v_space_rating</c> and
/// <c>bsk.v_space_photo</c>, and appends ratings and photo versions. Spaces are written as
/// subjects (<see cref="ISubjectRepository"/>); ratings are append-only <c>measurement</c>
/// events and photos <c>observation</c> events, each with a <c>concerns</c> edge to its space.
/// A photo's first version is written with its bytes, through <see cref="IArtifactBlobStore"/>.
/// </summary>
public interface ISpaceRepository
{
    /// <summary>Every space, archived ones included, by name.</summary>
    Task<IReadOnlyList<Space>> GetSpacesAsync(CancellationToken cancellationToken = default);

    /// <summary>The space with that id, or null.</summary>
    Task<Space?> GetSpaceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live ratings, oldest first by day then by when they were written: of
    /// <paramref name="spaceId"/> only when given, and on or after <paramref name="from"/> when given.
    /// </summary>
    Task<IReadOnlyList<SpaceRating>> GetRatingsAsync(
        Guid? spaceId = null, DateOnly? from = null, CancellationToken cancellationToken = default);

    /// <summary>The live rating with that id, or null (never written, or deleted).</summary>
    Task<SpaceRating?> GetRatingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live ratings whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<SpaceRating>> FindRatingsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Noon on <paramref name="day"/> in the database's home zone: when a backdated rating is filed.</summary>
    Task<DateTimeOffset> NoonAsync(DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends rating events and their <c>concerns</c> edges as one transaction. Returns the
    /// new event ids, in order.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendRatingsAsync(
        IReadOnlyList<SpaceRatingWrite> writes, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live photos (HOME-4, <c>bsk.v_space_photo</c>), oldest first by day then by when each
    /// was added: of <paramref name="spaceId"/> only when given.
    /// </summary>
    Task<IReadOnlyList<SpacePhoto>> GetPhotosAsync(Guid? spaceId = null, CancellationToken cancellationToken = default);

    /// <summary>The live photo with that id, or null (never added, or removed).</summary>
    Task<SpacePhoto?> GetPhotoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live photos whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<SpacePhoto>> FindPhotosAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends photo versions (a new day or shot, a removal, or a first version for bytes stored
    /// before) and their <c>concerns</c> edges as one transaction. Returns the new event ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendPhotoVersionsAsync(
        IReadOnlyList<SpacePhotoWrite> writes, CancellationToken cancellationToken = default);
}
