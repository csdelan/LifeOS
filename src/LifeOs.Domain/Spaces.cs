using System.Globalization;

namespace LifeOs.Domain;

/// <summary>
/// A space in the home (HOME-1, D18, environment plan §4.1): a <c>Space</c> subject, read
/// through <c>bsk.v_space</c>. <paramref name="Weight"/> (1–10) is how much it counts in the
/// Home Score, <paramref name="Pace"/> (1–5, 3 normal) how fast it gets messy.
/// <paramref name="ArchivedOn"/> is the home-zone day it was archived: it leaves the Home Score
/// from that day on.
/// </summary>
public sealed record Space(
    Guid Id,
    string Urn,
    string Name,
    int Weight,
    int Pace,
    DateTimeOffset? ArchivedAt,
    DateOnly? ArchivedOn,
    DateTimeOffset CreatedAt)
{
    /// <summary>Whether the space is archived now.</summary>
    public bool Archived => ArchivedAt is not null;
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
