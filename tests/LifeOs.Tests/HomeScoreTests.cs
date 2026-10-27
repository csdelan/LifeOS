using LifeOs.Cli.Commands;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// HOME-3, D18 (environment plan §2.2, §4.4): the Home Score as a pure function of the spaces,
/// their ratings and the exception days. The worked examples are to the digit, in
/// ten-thousandths of a point; the web's TypeScript port (<c>web/src/lib/home-score.ts</c>)
/// checks the same numbers.
/// </summary>
public sealed class HomeScoreTests
{
    private static readonly DateOnly D0 = new(2026, 10, 1);

    [Fact]
    public void The_rules_hold_together()
    {
        Assert.Equal(100, HomeParts.All.Sum(HomeRules.Weight));
        Assert.InRange(HomeRules.FloorRating, SpaceLimits.MinRating, SpaceLimits.MaxRating);
        Assert.True(HomeRules.FadeDays(HomeParts.Tidiness) < HomeRules.FadeDays(HomeParts.Cleanliness));
        Assert.True(HomeRules.FadeDays(HomeParts.Cleanliness) < HomeRules.FadeDays(HomeParts.Aesthetics));
        Assert.InRange(HomeRules.PaceDivisor, SpaceLimits.MinPace, SpaceLimits.MaxPace);
        Assert.True(HomeRules.GraceDays >= 0);
        Assert.Equal(0, HomeRules.Points(SpaceLimits.MinRating));
        Assert.Equal(HomeRules.Full, HomeRules.Points(SpaceLimits.MaxRating));
        Assert.Equal(250_000, HomeRules.Floor);
        Assert.Equal(HomeRules.HalfStep, HomeRules.Points(1.5m));
    }

    [Fact]
    public void A_part_fades_by_its_pace_down_to_the_floor()
    {
        // Tidiness at pace 5: half a point every 3 × 3/5 days, so 69,444 a day.
        Assert.Equal(805_556, HomeRules.Value(HomeParts.Tidiness, 4.5m, 5, 1));
        Assert.Equal(847_223, HomeRules.Value(HomeParts.Tidiness, 4.5m, 1, 2));
        // Cleanliness at pace 3: half a point a week.
        Assert.Equal(625_000, HomeRules.Value(HomeParts.Cleanliness, 4m, 3, 7));
        // Aesthetics ignores pace: half a point in 90 days either way.
        Assert.Equal(875_000, HomeRules.Value(HomeParts.Aesthetics, 5m, 5, 90));
        Assert.Equal(875_000, HomeRules.Value(HomeParts.Aesthetics, 5m, 1, 90));
        // Never below a rating of 2, and a rating below 2 doesn't fade.
        Assert.Equal(250_000, HomeRules.Value(HomeParts.Tidiness, 5m, 3, 100));
        Assert.Equal(250_000, HomeRules.Value(HomeParts.Tidiness, 2m, 5, 100));
        Assert.Equal(125_000, HomeRules.Value(HomeParts.Tidiness, 1.5m, 5, 100));
        Assert.Equal(0, HomeRules.Value(HomeParts.Cleanliness, 1m, 5, 100));
        Assert.Equal(1_000_000, HomeRules.Value(HomeParts.Tidiness, 5m, 5, 0));
    }

    [Fact]
    public void The_worked_example_scores_two_spaces_to_the_digit()
    {
        var office = Space("Office", 5, 5);
        var kitchen = Space("Kitchen", 4, 5);
        var ratings = new[]
        {
            Rating(office, D0, 4.5m, 4m, 3.5m),
            Rating(kitchen, D0, 3m, 2.5m, 3m)
        };

        var days = HomeScore.Days([kitchen, office], ratings, [], D0, D0.AddDays(10));
        Assert.Equal(11, days.Count);

        // Day 0: as rated.
        Assert.Equal(781_250, Of(days[0], office).Score);
        Assert.Equal(456_250, Of(days[0], kitchen).Score);
        Assert.Equal(636_805, days[0].Score);
        // Day 1 is the grace day; day 2 is the first fade.
        Assert.Equal(636_805, days[1].Score);
        Assert.Equal(805_556, Of(days[2], office).Part(HomeParts.Tidiness).Value);

        // Day 10: nine days of fade.
        var day10 = days[10];
        Assert.Equal([250_000L, 482_143L, 612_500L], Of(day10, office).Parts.Select(p => p.Value!.Value));
        Assert.Equal([250_000L, 250_000L, 487_500L], Of(day10, kitchen).Parts.Select(p => p.Value!.Value));
        Assert.Equal(403_750, Of(day10, office).Score);
        Assert.Equal(297_500, Of(day10, kitchen).Score);
        Assert.Equal(356_527, day10.Score);
        Assert.Equal([187_500L, 100_694L, 43_055L], Of(day10, office).Parts.Select(p => p.Drag));
        Assert.Equal([150_000L, 116_666L, 45_555L], Of(day10, kitchen).Parts.Select(p => p.Drag));
        Assert.Equal(
            [(office.Id, HomeParts.Tidiness, 187_500L), (kitchen.Id, HomeParts.Tidiness, 150_000L), (kitchen.Id, HomeParts.Cleanliness, 116_666L)],
            HomeScore.FixNext(day10).Select(x => (x.SpaceId, x.Part.Part, x.Part.Drag)));

        // Each part says what it was rated and when.
        var tidiness = Of(day10, office).Part(HomeParts.Tidiness);
        Assert.Equal((4.5m, (DateOnly?)D0), (tidiness.Rated, tidiness.RatedOn));
        Assert.Equal(D0, Of(day10, office).LastRatedOn);
    }

    [Fact]
    public void Exception_days_pause_fades_and_the_rating_day_itself_never_counts()
    {
        var office = Space("Office", 5, 5);
        var ratings = new[] { Rating(office, D0, 4.5m, 4m, 3.5m) };

        // Days 2 and 3 marked: day 3 hasn't faded yet, and day 4 has faded one day.
        var marked = HomeScore.Days([office], ratings, [D0.AddDays(2), D0.AddDays(3)], D0, D0.AddDays(4));
        Assert.Equal(875_000, Of(marked[3], office).Part(HomeParts.Tidiness).Value);
        Assert.Equal(805_556, Of(marked[4], office).Part(HomeParts.Tidiness).Value);

        // An exception on the rating's own day changes nothing.
        var onTheDay = HomeScore.Days([office], ratings, [D0], D0.AddDays(2), D0.AddDays(2));
        Assert.Equal(805_556, Of(onTheDay[0], office).Part(HomeParts.Tidiness).Value);
    }

    [Fact]
    public void A_space_counts_once_all_three_parts_are_rated_and_until_it_is_archived()
    {
        var office = Space("Office", 5, 5);
        var kitchen = Space("Kitchen", 4, 5, archivedOn: D0.AddDays(2));
        var ratings = new[]
        {
            // A quick check leaves Aesthetics out; it's rated the next day.
            Rating(office, D0, 4m, 4m, null),
            Rating(office, D0.AddDays(1), null, null, 3m),
            // On the same day, the rating written later wins.
            Rating(kitchen, D0, 5m, null, null, minute: 2),
            Rating(kitchen, D0, 3m, 3m, 3m, minute: 1)
        };

        var days = HomeScore.Days([kitchen, office], ratings, [], D0, D0.AddDays(2));

        Assert.False(Of(days[0], office).Counted);
        Assert.Null(Of(days[0], office).Score);
        Assert.Equal(0, Of(days[0], office).Part(HomeParts.Tidiness).Drag);
        Assert.Null(Of(days[0], office).Part(HomeParts.Aesthetics).Value);
        Assert.Equal(725_000, Of(days[0], kitchen).Score);
        Assert.Equal(725_000, days[0].Score);

        Assert.Equal(700_000, Of(days[1], office).Score);
        Assert.Equal(711_111, days[1].Score);

        // From the day it's archived, the kitchen leaves.
        Assert.Equal([office.Id], days[2].Spaces.Select(s => s.SpaceId));
        Assert.Equal(658_333, days[2].Score);
    }

    [Fact]
    public void No_ratings_means_no_score()
    {
        var office = Space("Office", 5, 5);
        var day = HomeScore.On(new HomeInputs([office], []), [], D0);
        Assert.Null(day.Score);
        Assert.Null(Of(day, office).LastRatedOn);
        Assert.Empty(HomeScore.FixNext(day));
        Assert.Empty(HomeScore.Scores(new HomeInputs([office], []), [], D0, D0.AddDays(5)));
        Assert.Empty(HomeScore.Days([office], [], [], D0, D0.AddDays(-1)));
    }

    [Fact]
    public void Scores_show_floored_to_a_tenth()
    {
        Assert.Equal("63.6", HomeCommand.Shown(636_805));
        Assert.Equal("10.0", HomeCommand.Shown(100_694));
        Assert.Equal("100.0", HomeCommand.Shown(HomeRules.Full));
        Assert.Equal("0.0", HomeCommand.Shown(0));
    }

    private static Space Space(string name, int weight, int pace, DateOnly? archivedOn = null)
        => new(Guid.NewGuid(), $"urn:bsk:space:{name.ToLowerInvariant()}-abc123", name, weight, pace,
            archivedOn is null ? null : DateTimeOffset.UnixEpoch, archivedOn, DateTimeOffset.UnixEpoch);

    private static SpaceRating Rating(Space space, DateOnly day, decimal? t, decimal? c, decimal? a, int minute = 0)
        => new(Guid.NewGuid(), space.Id, day, t, c, a, null, DateTimeOffset.UnixEpoch.AddMinutes(minute), Guid.NewGuid());

    private static SpaceDay Of(HomeDay day, Space space) => day.Spaces.Single(s => s.SpaceId == space.Id);
}
