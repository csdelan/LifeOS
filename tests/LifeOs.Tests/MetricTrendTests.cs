using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// MEASURE-2 (measurements plan §2.2–§2.3): a metric's day values, its trend and its goal date,
/// as pure functions. The web's TypeScript port (M2) checks the same cases.
/// </summary>
public sealed class MetricTrendTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    [Fact]
    public void A_days_value_is_its_first_reading_by_time()
    {
        var evening = Reading(Start, 19, 193.2m);
        var morning = Reading(Start, 7, 192.4m);
        var next = Reading(Start.AddDays(1), 6, 192.0m);

        var days = MetricTrend.DayValues([evening, next, morning]);

        Assert.Equal(
            [(Start, 192.4m, morning.Id), (Start.AddDays(1), 192.0m, next.Id)],
            days.Select(d => (d.Day, d.Value, d.ReadingId)));
    }

    [Fact]
    public void The_trend_pulls_a_fifth_of_the_way_a_day_and_further_after_a_gap()
    {
        var trend = MetricTrend.Trend(
            [Day(0, 200m), Day(1, 199m), Day(3, 198m)], decimals: 1);

        // 200; 200 + 0.2 × (199 − 200) = 199.8; then a 2-day gap pulls 1 − 0.8² = 0.36 of the way.
        Assert.Equal([200m, 199.8m, 199.15m], trend.Select(p => p.Trend));
        Assert.Equal([200m, 199m, 198m], trend.Select(p => p.Value));
    }

    [Fact]
    public void A_steady_loss_gives_the_date_the_trend_reaches_the_target()
    {
        // 0.2 lb a day for 14 days, from 200: the rate is 1.4 lb a week.
        var days = Enumerable.Range(0, 14).Select(i => Day(i, 200m - 0.2m * i)).ToList();

        var goal = MetricTrend.Goal(days, 185m, MetricBetter.Lower, decimals: 1);

        // The trend lags a steady ramp: 197.4 + 0.8 × (1 − 0.8¹³) = 198.156…, so the target is
        // 13.156 / 0.2 = 65.8 days on, rounded up to 66.
        Assert.Equal(GoalStatuses.Heading, goal.Status);
        Assert.Equal((198.16m, -1.4m), (goal.Now, goal.RatePerWeek));
        Assert.Equal(Start.AddDays(13 + 66), goal.ReachedBy);
        Assert.Equal((14, Start, Start.AddDays(13)), (goal.BasisDays, goal.BasisFrom, goal.BasisTo));
        Assert.Equal(MetricBetter.Lower, goal.Direction);
    }

    [Fact]
    public void The_rate_reads_only_the_newest_28_days()
    {
        // A drop long ago doesn't count: the last four days are flat.
        List<MetricDay> days = [Day(0, 210m), Day(30, 200m), Day(31, 200m), Day(32, 200m), Day(33, 200m)];

        var goal = MetricTrend.Goal(days, 185m, MetricBetter.Lower, decimals: 1);

        Assert.Equal(GoalStatuses.NotHeading, goal.Status);
        Assert.Equal(0m, goal.RatePerWeek);
        Assert.Equal((4, Start.AddDays(30)), (goal.BasisDays, goal.BasisFrom));
        Assert.Null(goal.ReachedBy);
    }

    [Fact]
    public void With_one_day_in_the_last_28_the_rate_reads_the_newest_two()
    {
        List<MetricDay> days = [Day(0, 200m), Day(40, 196m)];

        var goal = MetricTrend.Goal(days, 185m, MetricBetter.Lower, decimals: 1);

        Assert.Equal(GoalStatuses.Heading, goal.Status);
        Assert.Equal(-0.7m, goal.RatePerWeek);
        Assert.Equal((2, Start, Start.AddDays(40)), (goal.BasisDays, goal.BasisFrom, goal.BasisTo));
    }

    [Fact]
    public void Two_days_are_enough_for_a_date_and_one_is_too_few()
    {
        var one = MetricTrend.Goal([Day(0, 195m)], 185m, MetricBetter.Lower, decimals: 1);
        Assert.Equal((GoalStatuses.TooFew, 195m, 1), (one.Status, one.Now, one.BasisDays));
        Assert.Null(one.ReachedBy);

        var two = MetricTrend.Goal([Day(0, 195m), Day(1, 194m)], 185m, MetricBetter.Lower, decimals: 1);
        Assert.Equal(GoalStatuses.Heading, two.Status);
        Assert.NotNull(two.ReachedBy);

        var none = MetricTrend.Goal([Day(0, 195m), Day(1, 194m)], null, MetricBetter.Lower, decimals: 1);
        Assert.Equal(GoalStatuses.NoTarget, none.Status);
    }

    [Fact]
    public void At_or_past_the_target_reads_as_reached_and_a_rise_as_not_heading()
    {
        // The trend: 186, 185.8, 185.44, 184.952 — under 185.
        var reached = MetricTrend.Goal(
            [Day(0, 186m), Day(1, 185m), Day(2, 184m), Day(3, 183m)], 185m, MetricBetter.Lower, decimals: 1);
        Assert.Equal(GoalStatuses.Reached, reached.Status);

        var rising = MetricTrend.Goal([Day(0, 190m), Day(1, 191m)], 185m, MetricBetter.Lower, decimals: 1);
        Assert.Equal((GoalStatuses.NotHeading, 7m), (rising.Status, rising.RatePerWeek));

        // Higher is better: a rise toward 45 is heading.
        var vo2 = MetricTrend.Goal([Day(0, 40m), Day(7, 41m)], 45m, MetricBetter.Higher, decimals: 1);
        Assert.Equal(GoalStatuses.Heading, vo2.Status);
    }

    [Fact]
    public void A_target_more_than_two_years_away_reads_as_far()
    {
        var goal = MetricTrend.Goal([Day(0, 200.0m), Day(27, 199.9m)], 185m, MetricBetter.Lower, decimals: 1);

        Assert.Equal(GoalStatuses.Far, goal.Status);
        Assert.Null(goal.ReachedBy);
        Assert.Equal(-0.03m, goal.RatePerWeek);
    }

    [Fact]
    public void Without_a_direction_the_goal_takes_the_side_the_trend_is_on()
    {
        var goal = MetricTrend.Goal([Day(0, 200m), Day(1, 199m)], 185m, better: null, decimals: 1);

        Assert.Equal((GoalStatuses.Heading, MetricBetter.Lower), (goal.Status, goal.Direction));
    }

    [Fact]
    public void Values_are_checked_against_the_metrics_decimal_places()
    {
        Assert.Equal(192.4m, MetricLimits.CheckValue(192.4m, 1, "Weight"));
        Assert.Equal(192.40m, MetricLimits.CheckValue(192.40m, 1, "Weight"));
        Assert.Contains("Weight takes at most 1 decimal place; got 192.45",
            Assert.Throws<ArgumentException>(() => MetricLimits.CheckValue(192.45m, 1, "Weight")).Message);
        Assert.Contains("takes a whole number",
            Assert.Throws<ArgumentException>(() => MetricLimits.CheckValue(5.5m, 0, "Steps")).Message);
        Assert.Throws<ArgumentException>(() => MetricLimits.CheckValue(1_000_001m, 0, "Weight"));
        Assert.Equal("192.4", MetricLimits.Format(192.4m, 1));
        Assert.Equal("6.00", MetricLimits.Format(6m, 2));
        Assert.Equal("lower", MetricLimits.CheckBetter(" Lower "));
        Assert.Throws<ArgumentException>(() => MetricLimits.CheckBetter("down"));
    }

    private static MetricDay Day(int offset, decimal value) => new(Start.AddDays(offset), value, Guid.NewGuid());

    private static MetricReading Reading(DateOnly day, int hour, decimal value)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0));
        var at = new DateTimeOffset(local, TimeSpan.Zero);
        return new MetricReading(Guid.NewGuid(), Guid.Empty, value, "lb", null, at, day, local, at, at, 1, Guid.NewGuid());
    }
}
