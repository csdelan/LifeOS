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

/// <summary>
/// <c>GET /api/environment</c> (HOME-3): today, the last settled day, today's Home Score (points
/// 0–100, exact to four places; null before any space counts), each space not archived with its
/// parts, and Fix next — the three parts that cost the score most.
/// </summary>
public sealed class EnvironmentRead
{
    public string Today { get; set; } = "";
    public string SettledThrough { get; set; } = "";
    public decimal? HomeScore { get; set; }
    public IReadOnlyList<HomeSpaceRow> Spaces { get; set; } = [];
    public IReadOnlyList<FixNextRow> FixNext { get; set; } = [];
}

/// <summary>
/// One space today (HOME-3): its weight, pace and share, its score (null until all three parts
/// are rated), the day it was last rated, and each part.
/// </summary>
public sealed class HomeSpaceRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Weight { get; set; }
    public int Pace { get; set; }
    public decimal? Share { get; set; }
    public decimal? Score { get; set; }
    public string? LastRatedOn { get; set; }
    public HomePartsRow Parts { get; set; } = new();
    /// <summary>The spots it's photographed from (HOME-4), in order, each with Then and Now.</summary>
    public IReadOnlyList<SpaceShotRow> Shots { get; set; } = [];
}

/// <summary>
/// One shot of a space (HOME-4): a spot it's photographed from, its name (the space's own for an
/// unnamed shot), how many photos it has, its baseline (Then: the photo Chris picked, else its
/// first) and its newest photo (Now). With one photo, both are that photo; with none, both are
/// null.
/// </summary>
public sealed class SpaceShotRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Photos { get; set; }
    public SpacePhotoRow? Baseline { get; set; }
    public SpacePhotoRow? Newest { get; set; }
}

/// <summary>
/// One photo of a space (HOME-4): its space and shot, its day (<c>yyyy-MM-dd</c>, home zone), the
/// artifact whose bytes <c>GET /api/artifacts/{id}/content</c> serves, when it was added, whether
/// it's its shot's baseline, and the space's score and parts on its day (null while not all three
/// parts were rated by then). <see cref="Shot"/> is null when its shot was removed.
/// </summary>
public sealed class SpacePhotoRow
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string Space { get; set; } = "";
    public string ShotId { get; set; } = "";
    public string? Shot { get; set; }
    public string Day { get; set; } = "";
    public Guid ArtifactId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public bool Baseline { get; set; }
    public decimal? Score { get; set; }
    public HomePartsRow Parts { get; set; } = new();
}

/// <summary>
/// <c>GET /api/environment/spaces/{id}/photos</c> (HOME-4): the space, its shots with Then and
/// Now, and its photos, newest first (of one shot when asked; a removed shot's photos left out).
/// </summary>
public sealed class SpacePhotosRead
{
    public Guid SpaceId { get; set; }
    public string Space { get; set; } = "";
    public IReadOnlyList<SpaceShotRow> Shots { get; set; } = [];
    public IReadOnlyList<SpacePhotoRow> Photos { get; set; } = [];
}

/// <summary>
/// A photo or shot write's answer (HOME-4): the photo as it now reads (null after a removal or a
/// shot change) and the space's shots with their Then and Now. Photos never move a level, so
/// there's no <c>progress</c>.
/// </summary>
public sealed class SpacePhotoResult
{
    public SpacePhotoRow? Photo { get; set; }
    public Guid SpaceId { get; set; }
    public IReadOnlyList<SpaceShotRow> Shots { get; set; } = [];
}

/// <summary><c>POST /api/environment/photos/{id}</c>: a new day (<c>yyyy-MM-dd</c>) and a shot (its id or name); either can be left out.</summary>
public sealed class SpacePhotoChangeBody
{
    public string? Day { get; set; }
    public string? Shot { get; set; }
}

/// <summary><c>POST /api/environment/spaces/{id}/shots</c> and <c>…/shots/{shot}</c>: the shot's name.</summary>
public sealed class SpaceShotBody
{
    public string? Name { get; set; }
}

/// <summary>A space's three parts.</summary>
public sealed class HomePartsRow
{
    public HomePartRow Tidiness { get; set; } = new();
    public HomePartRow Cleanliness { get; set; } = new();
    public HomePartRow Aesthetics { get; set; } = new();
}

/// <summary>
/// One part of a space today: the rating it was last given and when (null when never rated),
/// its points now after fading (0–100), and the Home Score points it costs (<see cref="Drag"/>,
/// 0 while the space doesn't count).
/// </summary>
public sealed class HomePartRow
{
    public decimal? Rated { get; set; }
    public string? RatedOn { get; set; }
    public decimal? Now { get; set; }
    public decimal Drag { get; set; }
}

/// <summary>One part to fix next: the space, the part and the Home Score points it costs.</summary>
public sealed class FixNextRow
{
    public Guid SpaceId { get; set; }
    public string Space { get; set; } = "";
    public string Part { get; set; } = "";
    public decimal Drag { get; set; }
}

/// <summary>
/// <c>GET /api/environment/days</c> (HOME-3): the Home Score on each day from <see cref="From"/>
/// to <see cref="Today"/>, oldest first, with each space's score and the exception days, and
/// every space (archived ones too) to name them.
/// </summary>
public sealed class HomeDaysRead
{
    public string Today { get; set; } = "";
    public string SettledThrough { get; set; } = "";
    public string From { get; set; } = "";
    public IReadOnlyList<SpaceRow> Spaces { get; set; } = [];
    public IReadOnlyList<HomeDayRow> Days { get; set; } = [];
}

/// <summary>One day: the Home Score (null before any space counts), the exception comment when marked, and each space's score.</summary>
public sealed class HomeDayRow
{
    public string Day { get; set; } = "";
    public decimal? HomeScore { get; set; }
    public string? Exception { get; set; }
    public IReadOnlyList<HomeDaySpaceRow> Spaces { get; set; } = [];
}

/// <summary>One space on one day: its score, null while not all three parts are rated.</summary>
public sealed class HomeDaySpaceRow
{
    public Guid SpaceId { get; set; }
    public decimal? Score { get; set; }
}

/// <summary>Maps the home's spaces and ratings onto the API's shapes.</summary>
public static class EnvironmentRows
{
    /// <summary>Ten-thousandths of a point as points, exact.</summary>
    public static decimal Points(long value) => value / (decimal)HomeRules.Unit;

    public static decimal? Points(long? value) => value is { } v ? Points(v) : null;

    /// <summary>
    /// The home today. <paramref name="spaces"/> carry the shots (the fold's read leaves them
    /// out) and <paramref name="photos"/> are every live photo; without them each space reads
    /// with its one unnamed shot and no photos.
    /// </summary>
    public static EnvironmentRead From(
        HomeReading home, IReadOnlyList<Space>? spaces = null, IReadOnlyList<SpacePhoto>? photos = null)
    {
        var total = TotalWeight(home.Spaces);
        var names = home.Spaces.ToDictionary(s => s.Id, s => s.Name);
        var on = Days(home);
        return new EnvironmentRead
        {
            Today = Day(home.Today),
            SettledThrough = Day(home.SettledThrough),
            HomeScore = Points(home.Score),
            Spaces = home.Spaces.Select(space =>
            {
                var day = home.Of(space.Id);
                return new HomeSpaceRow
                {
                    Id = space.Id,
                    Name = space.Name,
                    Weight = space.Weight,
                    Pace = space.Pace,
                    Share = From(space, total).Share,
                    Score = Points(day?.Score),
                    LastRatedOn = day?.LastRatedOn is { } last ? Day(last) : null,
                    Parts = Parts(day),
                    Shots = Shots(spaces?.FirstOrDefault(s => s.Id == space.Id) ?? space, photos ?? [], on)
                };
            }).ToList(),
            FixNext = home.FixNext.Select(x => new FixNextRow
            {
                SpaceId = x.SpaceId,
                Space = names.GetValueOrDefault(x.SpaceId, ""),
                Part = x.Part.Part,
                Drag = Points(x.Part.Drag)
            }).ToList()
        };
    }

    public static HomeDaysRead From(HomeSeries series)
    {
        var total = TotalWeight(series.Spaces);
        return new HomeDaysRead
        {
            Today = Day(series.Today),
            SettledThrough = Day(series.SettledThrough),
            From = Day(series.Today.AddDays(1 - series.Count)),
            Spaces = series.Spaces.Select(s => From(s, total)).ToList(),
            Days = series.Days.Select(d => new HomeDayRow
            {
                Day = Day(d.Day),
                HomeScore = Points(d.Score),
                Exception = series.Exceptions.GetValueOrDefault(d.Day),
                Spaces = d.Spaces.Select(s => new HomeDaySpaceRow { SpaceId = s.SpaceId, Score = Points(s.Score) }).ToList()
            }).ToList()
        };
    }

    /// <summary>
    /// <paramref name="space"/>'s shots, each with its photo count, baseline and newest photo,
    /// read on their days through <paramref name="on"/>.
    /// </summary>
    public static IReadOnlyList<SpaceShotRow> Shots(
        Space space, IReadOnlyList<SpacePhoto> photos, Func<DateOnly, HomeDay> on)
    {
        var mine = photos.Where(p => p.SpaceId == space.Id).ToList();
        return space.Shots.Select(shot =>
        {
            var baseline = ShotPhotos.Baseline(shot, mine);
            var newest = ShotPhotos.Newest(shot, mine);
            return new SpaceShotRow
            {
                Id = shot.Id,
                Name = space.ShotName(shot),
                Photos = ShotPhotos.Of(shot, mine).Count,
                Baseline = baseline is null ? null : Photo(baseline, space, baseline.Id, on),
                Newest = newest is null ? null : Photo(newest, space, baseline?.Id, on)
            };
        }).ToList();
    }

    /// <summary>
    /// One photo, with its shot's name, whether it's <paramref name="baseline"/>, and the space's
    /// score and parts on its day.
    /// </summary>
    public static SpacePhotoRow Photo(SpacePhoto photo, Space space, Guid? baseline, Func<DateOnly, HomeDay> on)
    {
        var shot = space.Shots.FirstOrDefault(s => string.Equals(s.Id, photo.ShotId, StringComparison.OrdinalIgnoreCase));
        var day = on(photo.Day).Spaces.FirstOrDefault(s => s.SpaceId == space.Id);
        return new SpacePhotoRow
        {
            Id = photo.Id,
            SpaceId = space.Id,
            Space = space.Name,
            ShotId = photo.ShotId,
            Shot = shot is null ? null : space.ShotName(shot),
            Day = Day(photo.Day),
            ArtifactId = photo.ArtifactId,
            AddedAt = photo.AddedAt,
            Baseline = photo.Id == baseline,
            Score = Points(day?.Score),
            Parts = Parts(day)
        };
    }

    /// <summary>The home on any day of <paramref name="home"/>'s inputs, each day worked out once.</summary>
    public static Func<DateOnly, HomeDay> Days(HomeReading home)
    {
        var read = new Dictionary<DateOnly, HomeDay>();
        return day => read.TryGetValue(day, out var known) ? known : read[day] = home.On(day);
    }

    private static HomePartsRow Parts(SpaceDay? day) => new()
    {
        Tidiness = Part(day?.Part(HomeParts.Tidiness)),
        Cleanliness = Part(day?.Part(HomeParts.Cleanliness)),
        Aesthetics = Part(day?.Part(HomeParts.Aesthetics))
    };

    private static HomePartRow Part(PartValue? part) => part is null
        ? new HomePartRow()
        : new HomePartRow
        {
            Rated = part.Rated,
            RatedOn = part.RatedOn is { } on ? Day(on) : null,
            Now = Points(part.Value),
            Drag = Points(part.Drag)
        };

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
