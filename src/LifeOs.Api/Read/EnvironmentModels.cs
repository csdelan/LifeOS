using System.Globalization;
using LifeOs.Application.Home;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// One space (HOME-1): its name, weight (1–10) and pace (1–5), its share of the Home Score (null
/// once archived), and when it was archived.
/// </summary>
public sealed class SpaceRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    public int Weight { get; set; }
    public int Pace { get; set; }
    /// <summary>Its weight's share of the spaces not archived, in percent to one place.</summary>
    public decimal? Share { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    /// <summary>The home-zone day it was archived (<c>yyyy-MM-dd</c>): it leaves the Home Score from then.</summary>
    public string? ArchivedOn { get; set; }
}

/// <summary>One rating (HOME-2): the space, the day (<c>yyyy-MM-dd</c>, home zone), each part 1–5 in halves or null when left out, and the note.</summary>
public sealed class SpaceRatingRow
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string Space { get; set; } = "";
    public string Day { get; set; } = "";
    public decimal? Tidiness { get; set; }
    public decimal? Cleanliness { get; set; }
    public decimal? Aesthetics { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RatedAt { get; set; }
}

/// <summary><c>GET /api/environment/ratings</c>: the ratings from <see cref="From"/> to <see cref="Today"/>, newest first.</summary>
public sealed class SpaceRatingsRead
{
    public string Today { get; set; } = "";
    public string From { get; set; } = "";
    public IReadOnlyList<SpaceRatingRow> Ratings { get; set; } = [];
}

/// <summary>
/// A rating write's answer (<c>POST /api/environment/ratings</c>, <c>…/{id}</c>,
/// <c>…/{id}/delete</c>): the ratings as they now read (none after a delete), any rank-up it
/// paid, and each life domain whose level, rank or bests it moved — as an earning write answers.
/// </summary>
public sealed class SpaceRatingResult
{
    public IReadOnlyList<SpaceRatingRow> Ratings { get; set; } = [];
    public IReadOnlyList<AwardRow> Awards { get; set; } = [];
    public IReadOnlyList<DomainChangeRow> Progress { get; set; } = [];
}

/// <summary><c>POST /api/environment/spaces</c> and <c>…/{id}</c>: a name, and a weight (1–10) and pace (1–5), 3 when left out of a new one.</summary>
public sealed class SpaceBody
{
    public string? Name { get; set; }
    public int? Weight { get; set; }
    public int? Pace { get; set; }
}

/// <summary>
/// <c>POST /api/environment/ratings</c>: the day (<c>yyyy-MM-dd</c>, home zone; today when
/// omitted) and one rating per space — a walk-through saves many at once.
/// </summary>
public sealed class SpaceRatingsBody
{
    public string? Day { get; set; }
    public IReadOnlyList<SpaceRatingBody> Ratings { get; set; } = [];
}

/// <summary>One space's rating: the space (id or name), any parts (1–5 in halves), a note, or <see cref="Same"/> to confirm its last rating.</summary>
public sealed class SpaceRatingBody
{
    public string? Space { get; set; }
    public decimal? Tidiness { get; set; }
    public decimal? Cleanliness { get; set; }
    public decimal? Aesthetics { get; set; }
    public string? Note { get; set; }
    public bool? Same { get; set; }
}

/// <summary><c>POST /api/environment/ratings/{id}</c>: a new day, any parts to replace, and a note (blank removes it; left out keeps it).</summary>
public sealed class SpaceRatingEditBody
{
    public string? Day { get; set; }
    public decimal? Tidiness { get; set; }
    public decimal? Cleanliness { get; set; }
    public decimal? Aesthetics { get; set; }
    public string? Note { get; set; }
}

/// <summary>Maps the home's spaces and ratings onto the API's shapes.</summary>
public static class EnvironmentRows
{
    public static SpaceRow From(Space space, int totalWeight) => new()
    {
        Id = space.Id,
        Urn = space.Urn,
        Name = space.Name,
        Weight = space.Weight,
        Pace = space.Pace,
        Share = space.Archived || totalWeight == 0
            ? null
            : Math.Round(100m * space.Weight / totalWeight, 1, MidpointRounding.AwayFromZero),
        ArchivedAt = space.ArchivedAt,
        ArchivedOn = space.ArchivedOn is { } on ? Day(on) : null
    };

    public static SpaceRatingRow From(SpaceRating rating, IReadOnlyList<Space> spaces) => new()
    {
        Id = rating.Id,
        SpaceId = rating.SpaceId,
        Space = spaces.FirstOrDefault(s => s.Id == rating.SpaceId)?.Name ?? "",
        Day = Day(rating.Day),
        Tidiness = rating.Tidiness,
        Cleanliness = rating.Cleanliness,
        Aesthetics = rating.Aesthetics,
        Note = rating.Note,
        RatedAt = rating.RatedAt
    };

    public static SpaceRatingResult From(RatingWriteResult result, IReadOnlyList<Space> spaces)
    {
        var earned = RewardRows.Earned(result.Earned.Awards, result.Earned.Progress);
        return new SpaceRatingResult
        {
            Ratings = result.Ratings.Select(r => From(r, spaces)).ToList(),
            Awards = earned.Awards,
            Progress = earned.Progress
        };
    }

    /// <summary>The weight of the spaces not archived: what each one's share is of.</summary>
    public static int TotalWeight(IEnumerable<Space> spaces) => spaces.Where(s => !s.Archived).Sum(s => s.Weight);

    internal static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
