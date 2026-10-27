using System.Globalization;

namespace LifeOs.Domain;

/// <summary>
/// A space in the home (HOME-1, D18, environment plan §4.1): a <c>Space</c> subject, read
/// through <c>bsk.v_space</c>. <paramref name="Weight"/> (1–10) is how much it counts in the
/// Home Score, <paramref name="Pace"/> (1–5, 3 normal) how fast it gets messy.
/// <paramref name="ArchivedOn"/> is the home-zone day it was archived: it leaves the Home Score
/// from that day on. <paramref name="StoredShots"/> are the spots it's photographed from as its
/// <c>shots</c> attribute holds them (HOME-4); <see cref="Shots"/> is what they mean.
/// </summary>
public sealed record Space(
    Guid Id,
    string Urn,
    string Name,
    int Weight,
    int Pace,
    DateTimeOffset? ArchivedAt,
    DateOnly? ArchivedOn,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SpaceShot>? StoredShots = null)
{
    /// <summary>Whether the space is archived now.</summary>
    public bool Archived => ArchivedAt is not null;

    /// <summary>
    /// The spots the space is photographed from, in order: the stored ones, or one shot,
    /// <see cref="SpaceShot.MainId"/>, when none are stored.
    /// </summary>
    public IReadOnlyList<SpaceShot> Shots => StoredShots is { Count: > 0 } stored ? stored : [new SpaceShot(SpaceShot.MainId)];

    /// <summary>A shot's name: its own, or the space's while it has none.</summary>
    public string ShotName(SpaceShot shot) => shot.Name ?? Name;
}

/// <summary>
/// A spot a space is photographed from (HOME-4, environment plan §8), like "Desk" or "Counters".
/// <paramref name="Name"/> null reads as the space's name (an unnamed space's only shot).
/// <paramref name="Baseline"/> is the photo Chris picked to compare against; null means the
/// shot's first photo.
/// </summary>
public sealed record SpaceShot(string Id, string? Name = null, Guid? Baseline = null)
{
    /// <summary>The id of the shot every space starts with.</summary>
    public const string MainId = "main";
}

/// <summary>
/// One photo of a space (HOME-4, environment plan §8): the newest version of a
/// <c>space_photo</c> event, read through <c>bsk.v_space_photo</c>. <paramref name="ArtifactId"/>
/// is the photo's bytes; <paramref name="AddedAt"/> is when it was first stored, which orders the
/// photos of one day (a before and an after).
/// </summary>
public sealed record SpacePhoto(
    Guid Id,
    Guid SpaceId,
    string ShotId,
    DateOnly Day,
    Guid ArtifactId,
    DateTimeOffset AddedAt,
    DateTimeOffset ChangedAt,
    Guid EventId);

/// <summary>
/// Which photos a shot compares (HOME-4): its baseline (Then) and its newest photo (Now).
/// </summary>
public static class ShotPhotos
{
    /// <summary>The photos of <paramref name="shot"/> among <paramref name="photos"/>, oldest first.</summary>
    public static IReadOnlyList<SpacePhoto> Of(SpaceShot shot, IEnumerable<SpacePhoto> photos)
        => photos.Where(p => string.Equals(p.ShotId, shot.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Day).ThenBy(p => p.AddedAt).ThenBy(p => p.Id)
            .ToList();

    /// <summary>
    /// The shot's baseline: the photo Chris picked while it's still one of the shot's, else the
    /// shot's first. Null when the shot has no photos.
    /// </summary>
    public static SpacePhoto? Baseline(SpaceShot shot, IEnumerable<SpacePhoto> photos)
    {
        var ofShot = Of(shot, photos);
        return ofShot.FirstOrDefault(p => p.Id == shot.Baseline) ?? ofShot.FirstOrDefault();
    }

    /// <summary>The shot's newest photo, or null when it has none.</summary>
    public static SpacePhoto? Newest(SpaceShot shot, IEnumerable<SpacePhoto> photos)
        => Of(shot, photos).LastOrDefault();
}

/// <summary>
/// One rating of a space (HOME-2, environment plan §4.2): the newest version of a
/// <c>space_rating</c> event, read through <c>bsk.v_space_rating</c>. Each part is 1–5 in halves,
/// or null when this rating left it out. <paramref name="RatedAt"/> is when this version was
/// written.
/// </summary>
public sealed record SpaceRating(
    Guid Id,
    Guid SpaceId,
    DateOnly Day,
    decimal? Tidiness,
    decimal? Cleanliness,
    decimal? Aesthetics,
    string? Note,
    DateTimeOffset RatedAt,
    Guid EventId)
{
    /// <summary>The rating of <paramref name="part"/>, or null when this rating left it out.</summary>
    public decimal? Of(string part) => part switch
    {
        HomeParts.Tidiness => Tidiness,
        HomeParts.Cleanliness => Cleanliness,
        HomeParts.Aesthetics => Aesthetics,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Not a part of a space's rating.")
    };
}

/// <summary>
/// What the Home Score is worked out from (environment plan §4.6): every space, archived ones
/// included, and every live rating, oldest first by day.
/// </summary>
public sealed record HomeInputs(IReadOnlyList<Space> Spaces, IReadOnlyList<SpaceRating> Ratings)
{
    /// <summary>No spaces and no ratings.</summary>
    public static readonly HomeInputs None = new([], []);
}

/// <summary>The three parts a space is rated on (HOME-2), in the order they're shown.</summary>
public static class HomeParts
{
    public const string Tidiness = "tidiness";
    public const string Cleanliness = "cleanliness";
    public const string Aesthetics = "aesthetics";

    public static readonly IReadOnlyList<string> All = [Tidiness, Cleanliness, Aesthetics];

    /// <summary>A part's name in words, e.g. "Tidiness".</summary>
    public static string Label(string part) => part switch
    {
        Tidiness => "Tidiness",
        Cleanliness => "Cleanliness",
        Aesthetics => "Aesthetics",
        _ => part
    };
}

/// <summary>
/// The limits on spaces and ratings (HOME-1, HOME-2, environment plan §4.1–§4.2), shared by the
/// services, the CLI and the API so they refuse the same things in the same words.
/// </summary>
public static class SpaceLimits
{
    /// <summary>The payload key a rating's events carry.</summary>
    public const string RatingKey = "space_rating";

    /// <summary>The attribute holding a space's weight.</summary>
    public const string WeightKey = "weight";

    /// <summary>The attribute holding a space's pace.</summary>
    public const string PaceKey = "pace";

    /// <summary>The attribute holding a space's shots and their baselines (HOME-4).</summary>
    public const string ShotsKey = "shots";

    /// <summary>The payload key a photo's events carry (HOME-4).</summary>
    public const string PhotoKey = "space_photo";

    /// <summary>The longest shot name, in characters.</summary>
    public const int MaxShotName = 40;

    /// <summary>The most shots a space can have.</summary>
    public const int MaxShots = 6;

    public const int MinWeight = 1;
    public const int MaxWeight = 10;
    public const int DefaultWeight = 3;
    public const int MinPace = 1;
    public const int MaxPace = 5;
    public const int DefaultPace = 3;

    /// <summary>The longest space name, in characters.</summary>
    public const int MaxName = 60;

    /// <summary>The longest rating note, in characters.</summary>
    public const int MaxNote = 140;

    public const decimal MinRating = 1m;
    public const decimal MaxRating = 5m;

    // Unicode's own line breaks, which aren't control characters.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>A weight as kept, or refused in words outside 1–10.</summary>
    public static int CheckWeight(int weight)
        => weight is >= MinWeight and <= MaxWeight
            ? weight
            : throw new ArgumentException($"A space's weight is a whole number from {MinWeight} to {MaxWeight}; got {weight}.");

    /// <summary>A pace as kept, or refused in words outside 1–5.</summary>
    public static int CheckPace(int pace)
        => pace is >= MinPace and <= MaxPace
            ? pace
            : throw new ArgumentException($"A space's pace is a whole number from {MinPace} (slow to get messy) to {MaxPace} (fast); got {pace}.");

    /// <summary>A space's name as kept: trimmed, one line, 1–60 characters.</summary>
    public static string CheckName(string? name)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new ArgumentException("Give the space a name, e.g. Office.");
        }

        if (text.Any(IsBreak))
        {
            throw new ArgumentException("A space's name is one line: take out the line break.");
        }

        if (text.Length > MaxName)
        {
            throw new ArgumentException($"A space's name is up to {MaxName} characters; that one is {text.Length}.");
        }

        return text;
    }

    /// <summary>
    /// A part's rating as kept, or refused in words: 1–5 in half points.
    /// <paramref name="part"/> names it in the refusal.
    /// </summary>
    public static decimal CheckRating(decimal rating, string part)
    {
        if (rating < MinRating || rating > MaxRating || rating * 2 != decimal.Truncate(rating * 2))
        {
            throw new ArgumentException(
                $"{HomeParts.Label(part)} is rated 1 to 5 in half points (1, 1.5 … 5); got {rating.ToString(CultureInfo.InvariantCulture)}.");
        }

        return rating;
    }

    /// <summary>A shot's name as kept: trimmed, one line, 1–40 characters.</summary>
    public static string CheckShotName(string? name)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new ArgumentException("Give the shot a name, e.g. Desk.");
        }

        if (text.Any(IsBreak))
        {
            throw new ArgumentException("A shot's name is one line: take out the line break.");
        }

        if (text.Length > MaxShotName)
        {
            throw new ArgumentException($"A shot's name is up to {MaxShotName} characters; that one is {text.Length}.");
        }

        return text;
    }

    /// <summary>A rating's note as kept: trimmed, one line, at most 140 characters; null when blank.</summary>
    public static string? CheckNote(string? note)
    {
        var text = note?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Any(IsBreak))
        {
            throw new ArgumentException("A rating's note is one line: take out the line break.");
        }

        if (text.Length > MaxNote)
        {
            throw new ArgumentException($"A rating's note is up to {MaxNote} characters; that one is {text.Length}.");
        }

        return text;
    }

    private static bool IsBreak(char c) => char.IsControl(c) || c == LineSeparator || c == ParagraphSeparator;
}
