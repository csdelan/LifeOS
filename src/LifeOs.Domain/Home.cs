namespace LifeOs.Domain;

/// <summary>
/// The Home Score's tunables (HOME-3, D18, environment plan §2.2, §4.4). Points are kept in
/// ten-thousandths (<see cref="Unit"/>), so 100 points is 1,000,000, and every step rounds down,
/// so C# and the web's TypeScript port agree to the digit.
/// </summary>
public static class HomeRules
{
    /// <summary>One point, in the ten-thousandths the Home Score is kept in.</summary>
    public const long Unit = 10_000;

    /// <summary>A perfect score: 100 points.</summary>
    public const long Full = 100 * Unit;

    /// <summary>Half a rating step: (r − 1) × 25 points, so a half point is 12.5 points.</summary>
    public const long HalfStep = 125_000;

    /// <summary>The rating a fade never takes a part below.</summary>
    public const decimal FloorRating = 2m;

    /// <summary>Days after a rating before its fade starts: it slips from the second day.</summary>
    public const int GraceDays = 1;

    /// <summary>Pace divides by this, so pace 3 is a normal fade.</summary>
    public const int PaceDivisor = 3;

    /// <summary>How much each part counts in a space's score, in percent (45 / 35 / 20).</summary>
    public static int Weight(string part) => part switch
    {
        HomeParts.Tidiness => 45,
        HomeParts.Cleanliness => 35,
        HomeParts.Aesthetics => 20,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Not a part of a space's rating.")
    };

    /// <summary>The days a part takes to fade half a point at pace 3 (3 / 7 / 90).</summary>
    public static int FadeDays(string part) => part switch
    {
        HomeParts.Tidiness => 3,
        HomeParts.Cleanliness => 7,
        HomeParts.Aesthetics => 90,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Not a part of a space's rating.")
    };

    /// <summary>Whether a space's pace speeds up or slows down a part's fade (Tidiness and Cleanliness; not Aesthetics).</summary>
    public static bool Paced(string part) => part != HomeParts.Aesthetics;

    /// <summary>A rating (1–5 in halves) as points: (r − 1) × 25, in ten-thousandths.</summary>
    public static long Points(decimal rating) => (long)(2 * rating - 2) * HalfStep;

    /// <summary>The floor a fade stops at: a rating of 2, 25 points.</summary>
    public static long Floor => Points(FloorRating);

    /// <summary>
    /// What <paramref name="fadingDays"/> days of fade take off a part of a space with
    /// <paramref name="pace"/>: ⌊125,000 × f × k / (3 × N)⌋, k the pace for Tidiness and
    /// Cleanliness and 3 for Aesthetics.
    /// </summary>
    public static long Fade(string part, int pace, int fadingDays)
    {
        if (fadingDays <= 0)
        {
            return 0;
        }

        var k = Paced(part) ? pace : PaceDivisor;
        return HalfStep * fadingDays * k / (PaceDivisor * FadeDays(part));
    }

    /// <summary>
    /// A part rated <paramref name="rating"/>, after <paramref name="fadingDays"/> days of fade:
    /// max(min(p₀, floor), p₀ − fade). A rating below the floor doesn't fade.
    /// </summary>
    public static long Value(string part, decimal rating, int pace, int fadingDays)
    {
        var p0 = Points(rating);
        return Math.Max(Math.Min(p0, Floor), p0 - Fade(part, pace, fadingDays));
    }

    /// <summary>A space's score from its three parts' values: ⌊(45 T + 35 C + 20 A) / 100⌋.</summary>
    public static long SpaceScore(long tidiness, long cleanliness, long aesthetics)
        => (Weight(HomeParts.Tidiness) * tidiness
            + Weight(HomeParts.Cleanliness) * cleanliness
            + Weight(HomeParts.Aesthetics) * aesthetics) / 100;

    /// <summary>
    /// The Home Score a part costs (its drag, for Fix next): ⌊w × part weight × (1,000,000 − v) /
    /// (Σw × 100)⌋, with Σw the weight of the spaces counted that day.
    /// </summary>
    public static long Drag(int spaceWeight, string part, long value, long totalWeight)
        => totalWeight <= 0 ? 0 : spaceWeight * Weight(part) * (Full - value) / (totalWeight * 100);
}

/// <summary>
/// One part of a space on one day (HOME-3): the rating it was last given on or before the day
/// and when (null when never rated), its value now after fading (points in ten-thousandths), and
/// the Home Score it costs (<see cref="Drag"/>, 0 while the space doesn't count).
/// </summary>
public sealed record PartValue(string Part, decimal? Rated, DateOnly? RatedOn, long? Value, long Drag);

/// <summary>
/// One space on one day (HOME-3): its score (null until all three parts have a rating) and its
/// three parts, in <see cref="HomeParts.All"/> order.
/// </summary>
public sealed record SpaceDay(Guid SpaceId, long? Score, IReadOnlyList<PartValue> Parts)
{
    /// <summary>Whether the space counts in the day's Home Score: all three parts rated.</summary>
    public bool Counted => Score is not null;

    /// <summary>The part named <paramref name="part"/>.</summary>
    public PartValue Part(string part) => Parts.First(p => p.Part == part);

    /// <summary>The day the space was last rated, any part; null when never.</summary>
    public DateOnly? LastRatedOn => Parts.Max(p => p.RatedOn);
}

/// <summary>
/// The home on one day (HOME-3): the Home Score (null before any space counts) and every space
/// not archived on or before the day, in the order given.
/// </summary>
public sealed record HomeDay(DateOnly Day, long? Score, IReadOnlyList<SpaceDay> Spaces);

/// <summary>
/// The Home Score (HOME-3, D18, environment plan §2.2, §4.4): the only source of Home Score
/// numbers, a pure function of the spaces, their ratings and the exception days.
/// <list type="bullet">
/// <item>Each part takes its newest rating on or before the day (by day, then by when it was
/// written); a rating can leave a part out.</item>
/// <item>A part fades from the second day after it's rated, by <see cref="HomeRules.Fade"/>, not
/// counting exception days, and never below a rating of 2.</item>
/// <item>A space counts once all three parts are rated, until the day it's archived.</item>
/// <item>The Home Score is the weighted average of the counted spaces' scores.</item>
/// </list>
/// Weights and paces apply to all of history, as any retune does.
/// </summary>
public static class HomeScore
{
    /// <summary>
    /// The home on each day from <paramref name="from"/> to <paramref name="to"/>, oldest first.
    /// Walking day by day keeps it O(days × spaces × 3).
    /// </summary>
    public static IReadOnlyList<HomeDay> Days(
        IReadOnlyList<Space> spaces,
        IReadOnlyList<SpaceRating> ratings,
        IEnumerable<DateOnly> exceptionDays,
        DateOnly from,
        DateOnly to)
    {
        var result = new List<HomeDay>();
        if (to < from)
        {
            return result;
        }

        var exceptions = new ExceptionCounter(exceptionDays);
        var tracks = spaces.ToDictionary(
            s => s.Id,
            s => HomeParts.All.Select(part => new PartTrack(part, ratings
                    .Where(r => r.SpaceId == s.Id && r.Of(part) is not null)
                    .OrderBy(r => r.Day).ThenBy(r => r.RatedAt).ThenBy(r => r.Id)
                    .ToList()))
                .ToList());

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var present = spaces.Where(s => s.ArchivedOn is not { } archived || archived > day).ToList();
            var raw = new List<(Space Space, List<(PartTrack Track, SpaceRating? Rating, long? Value)> Parts)>();
            foreach (var space in present)
            {
                var parts = new List<(PartTrack, SpaceRating?, long?)>();
                foreach (var track in tracks[space.Id])
                {
                    var rating = track.At(day);
                    long? value = rating is null
                        ? null
                        : HomeRules.Value(
                            track.Part, rating.Of(track.Part)!.Value, space.Pace,
                            exceptions.FadingDays(rating.Day, day));
                    parts.Add((track, rating, value));
                }

                raw.Add((space, parts));
            }

            var counted = raw.Where(r => r.Parts.All(p => p.Value is not null)).ToList();
            long totalWeight = counted.Sum(r => r.Space.Weight);
            long weighted = 0;
            var spaceDays = new List<SpaceDay>(raw.Count);
            foreach (var (space, parts) in raw)
            {
                var isCounted = parts.All(p => p.Value is not null);
                long? score = isCounted
                    ? HomeRules.SpaceScore(parts[0].Value!.Value, parts[1].Value!.Value, parts[2].Value!.Value)
                    : null;
                if (score is { } s)
                {
                    weighted += space.Weight * s;
                }

                spaceDays.Add(new SpaceDay(
                    space.Id,
                    score,
                    parts.Select(p => new PartValue(
                            p.Track.Part,
                            p.Rating?.Of(p.Track.Part),
                            p.Rating?.Day,
                            p.Value,
                            isCounted ? HomeRules.Drag(space.Weight, p.Track.Part, p.Value!.Value, totalWeight) : 0))
                        .ToList()));
            }

            result.Add(new HomeDay(day, totalWeight > 0 ? weighted / totalWeight : null, spaceDays));
        }

        return result;
    }

    /// <summary>The home on one day.</summary>
    public static HomeDay On(HomeInputs home, IEnumerable<DateOnly> exceptionDays, DateOnly day)
        => Days(home.Spaces, home.Ratings, exceptionDays, day, day)[0];

    /// <summary>
    /// The Home Score on each day from <paramref name="from"/> to <paramref name="to"/> that has
    /// one: what the life-domain fold reads.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, long> Scores(
        HomeInputs home, IEnumerable<DateOnly> exceptionDays, DateOnly from, DateOnly to)
        => home.Ratings.Count == 0
            ? new Dictionary<DateOnly, long>()
            : Days(home.Spaces, home.Ratings, exceptionDays, from, to)
                .Where(d => d.Score is not null)
                .ToDictionary(d => d.Day, d => d.Score!.Value);

    /// <summary>
    /// The parts that cost the Home Score most on <paramref name="day"/>, biggest first: Fix
    /// next. Parts that cost nothing are left out.
    /// </summary>
    public static IReadOnlyList<(Guid SpaceId, PartValue Part)> FixNext(HomeDay day, int count = 3)
        => day.Spaces
            .Where(s => s.Counted)
            .SelectMany(s => s.Parts.Select(p => (s.SpaceId, Part: p)))
            .Where(x => x.Part.Drag > 0)
            .OrderByDescending(x => x.Part.Drag)
            .Take(count)
            .ToList();

    /// <summary>One part of one space: its ratings that rate the part, oldest first, read forward as the days advance.</summary>
    private sealed class PartTrack(string part, IReadOnlyList<SpaceRating> ratings)
    {
        private int next;
        private SpaceRating? current;

        public string Part { get; } = part;

        /// <summary>The newest rating on or before <paramref name="day"/>; days must not go backwards.</summary>
        public SpaceRating? At(DateOnly day)
        {
            while (next < ratings.Count && ratings[next].Day <= day)
            {
                current = ratings[next++];
            }

            return current;
        }
    }

    /// <summary>Counts the days a fade runs: the days after a rating, less exception days and the grace day.</summary>
    private sealed class ExceptionCounter(IEnumerable<DateOnly> exceptionDays)
    {
        private readonly DateOnly[] days = exceptionDays.Distinct().Order().ToArray();

        /// <summary>max(0, non-exception days in (rated, day] − grace).</summary>
        public int FadingDays(DateOnly rated, DateOnly day)
        {
            var elapsed = day.DayNumber - rated.DayNumber;
            if (elapsed <= HomeRules.GraceDays)
            {
                return 0;
            }

            var skipped = CountUpTo(day) - CountUpTo(rated);
            return Math.Max(0, elapsed - skipped - HomeRules.GraceDays);
        }

        /// <summary>The exception days on or before <paramref name="day"/>.</summary>
        private int CountUpTo(DateOnly day)
        {
            var index = Array.BinarySearch(days, day);
            return index >= 0 ? index + 1 : ~index;
        }
    }
}
