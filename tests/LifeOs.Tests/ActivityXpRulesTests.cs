using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// ACTIVITY-5 (activity plan §3.8): the activity XP rules and their drift test. The rules live
/// in code, so these checks keep a retune honest: the parts add up to the cap, the curves and
/// tiers rise, and the plan's worked examples still hold. The web's
/// <c>lib/activity-score.test.ts</c> runs the same table.
/// </summary>
public sealed class ActivityXpRulesTests
{
    private static readonly ActivityRules Rules = ActivityRules.Default;

    private static readonly DateTimeOffset Morning = new(2026, 10, 22, 7, 0, 0, TimeSpan.FromHours(-7));

    /// <summary>A workout starting <paramref name="at"/> minutes after 7 a.m. and lasting <paramref name="minutes"/>.</summary>
    private static Workout Workout(int at, int minutes, string activity = "running", string source = "manual")
    {
        var start = Morning.AddMinutes(at);
        return new Workout(
            $"w-{at}-{minutes}-{activity}-{source}", DateOnly.FromDateTime(start.Date), activity, null,
            start, start.AddMinutes(minutes), minutes * 60, null, null, null, source, null, null, start, Guid.NewGuid());
    }

    [Fact]
    public void The_parts_add_up_to_the_cap_and_the_curves_and_tiers_rise()
    {
        Assert.Equal(Currencies.HealthXp, Rules.Currency);
        Assert.True(Currencies.Find(Rules.Currency)!.LifeDomain);
        Assert.Equal((25, 35, 20), (Rules.StepsMax, Rules.ExerciseMax, Rules.SleepMax));
        Assert.Equal(80, Rules.Cap);
        Assert.Equal(Rules.Cap, Rules.StepsMax + Rules.ExerciseMax + Rules.SleepMax);

        Assert.Equal(new CurvePoint(0, 0), Rules.StepsCurve[0]);
        Assert.Equal(0, Rules.SleepCurve[0].Xp);
        foreach (var curve in new[] { Rules.StepsCurve, Rules.SleepCurve })
        {
            Assert.All(curve.Zip(curve.Skip(1)), pair =>
            {
                Assert.True(pair.First.At < pair.Second.At, "each point is further along");
                Assert.True(pair.First.Xp < pair.Second.Xp, "each point pays more");
            });
        }

        Assert.True(Rules.ExerciseTiers[0] is { FromMinutes: > 0, Xp: > 0 }, "the first tier needs some exercise and pays something");
        Assert.All(Rules.ExerciseTiers.Zip(Rules.ExerciseTiers.Skip(1)), pair =>
        {
            Assert.True(pair.First.FromMinutes < pair.Second.FromMinutes, "each tier needs more minutes");
            Assert.True(pair.First.Xp < pair.Second.Xp, "each tier pays more");
        });

        Assert.Equal(new DateOnly(2026, 10, 22), Rules.XpFrom);
    }

    /// <summary>The plan's worked examples (§3.8) and the planning notes' example day.</summary>
    [Fact]
    public void The_worked_examples_hold()
    {
        Assert.Equal((15.2m, 15L), (Rules.StepsPart(6_000), Rules.StepsXp(6_000)));
        Assert.Equal((8.5m, 9L), (Rules.StepsPart(3_750), Rules.StepsXp(3_750)));
        Assert.Equal((12.5m, 13L), (Rules.SleepPart(75), Rules.SleepXp(75)));
        Assert.Equal((17m, 17L), (Rules.SleepPart(82), Rules.SleepXp(82)));

        // Two runs of 12 and 20 minutes make 32 minutes, which pays 25.
        var seconds = ActivityRules.ExerciseSeconds([Workout(0, 12), Workout(120, 20)]);
        Assert.Equal(32 * 60, seconds);
        Assert.Equal(25, Rules.ExerciseXp(seconds));

        // 7,500 steps, a 30-minute workout and a sleep score of 80: 20 + 25 + 15 = 60.
        var day = Rules.Score(new ActivityDayFacts(7_500, 30 * 60, 80));
        Assert.Equal((20L, 25L, 15L, 60L), (day.Steps!.Value, day.Exercise, day.Sleep!.Value, day.Total));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(1_250, 3)]
    [InlineData(2_500, 5)]
    [InlineData(3_750, 9)]
    [InlineData(4_999, 12)]
    [InlineData(5_000, 12)]
    [InlineData(6_000, 15)]
    [InlineData(7_499, 20)]
    [InlineData(7_500, 20)]
    [InlineData(8_750, 23)]
    [InlineData(10_000, 25)]
    [InlineData(10_001, 25)]
    [InlineData(30_000, 25)]
    public void Steps_pay_along_a_straight_line_up_to_25(int steps, long xp)
        => Assert.Equal(xp, Rules.StepsXp(steps));

    [Theory]
    [InlineData(1_250, 2.5)]
    [InlineData(4_999, 11.9972)]
    [InlineData(6_250, 16)]
    [InlineData(8_750, 22.5)]
    public void Between_two_points_the_steps_part_is_on_the_line(int steps, double part)
        => Assert.Equal((decimal)part, Rules.StepsPart(steps));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 0)]
    [InlineData(51, 1)]
    [InlineData(55, 3)]
    [InlineData(60, 5)]
    [InlineData(65, 8)]
    [InlineData(70, 10)]
    [InlineData(75, 13)]
    [InlineData(80, 15)]
    [InlineData(82, 17)]
    [InlineData(84, 19)]
    [InlineData(85, 20)]
    [InlineData(100, 20)]
    public void The_sleep_score_pays_from_50_up_to_20_at_85(int score, long xp)
        => Assert.Equal(xp, Rules.SleepXp(score));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(599, 0)]
    [InlineData(600, 8)]
    [InlineData(1_199, 8)]
    [InlineData(1_200, 15)]
    [InlineData(1_799, 15)]
    [InlineData(1_800, 25)]
    [InlineData(2_699, 25)]
    [InlineData(2_700, 35)]
    [InlineData(4 * 3600, 35)]
    public void Exercise_pays_by_tier_counted_to_the_second(long seconds, long xp)
        => Assert.Equal(xp, Rules.ExerciseXp(seconds));

    [Fact]
    public void A_day_s_workouts_add_up_and_overlaps_count_once()
    {
        // Two separate runs: 20 + 15.
        Assert.Equal(35 * 60, ActivityRules.ExerciseSeconds([Workout(0, 20), Workout(60, 15)]));

        // Overlapping: 0–30 and 20–40 cover 40 minutes, not 50.
        Assert.Equal(40 * 60, ActivityRules.ExerciseSeconds([Workout(20, 20, "cycling"), Workout(0, 30)]));

        // One inside another counts once; back to back joins up.
        Assert.Equal(45 * 60, ActivityRules.ExerciseSeconds([Workout(0, 45), Workout(10, 5, "strengthTraining")]));
        Assert.Equal(30 * 60, ActivityRules.ExerciseSeconds([Workout(0, 15), Workout(15, 15)]));
    }

    [Fact]
    public void Splitting_a_workout_pays_no_more_than_the_whole()
    {
        var whole = Rules.ExerciseXp(ActivityRules.ExerciseSeconds([Workout(0, 40)]));
        var split = Rules.ExerciseXp(ActivityRules.ExerciseSeconds([Workout(0, 20), Workout(20, 20)]));
        var doubled = Rules.ExerciseXp(ActivityRules.ExerciseSeconds([Workout(0, 40), Workout(0, 40)]));

        Assert.Equal(25, whole);
        Assert.Equal(whole, split);
        Assert.Equal(whole, doubled);
    }

    [Fact]
    public void Walks_detections_and_unknown_sources_add_nothing()
    {
        Workout[] workouts =
        [
            Workout(0, 60, "walking"),
            Workout(0, 60, "running", "autodetected"),
            Workout(0, 60, "cycling", "confirmed"),
            Workout(0, 60, "running", "garmin_import"),
        ];

        Assert.Equal(0, ActivityRules.ExerciseSeconds(workouts));
        Assert.Equal(15 * 60, ActivityRules.ExerciseSeconds([.. workouts, Workout(90, 15, "rowing", "workout_heart_rate")]));
    }

    [Fact]
    public void A_workout_that_ends_before_it_starts_covers_nothing()
    {
        var start = Morning;
        Assert.Equal(0, ActivityRules.MergedSeconds([(start, start), (start, start.AddMinutes(-5))]));
        Assert.Equal(0, ActivityRules.MergedSeconds([]));
    }

    [Fact]
    public void Missing_data_pays_nothing_and_is_not_read_as_zero()
    {
        var unknown = Rules.Score(new ActivityDayFacts(null, 0, null));
        Assert.Null(unknown.Steps);
        Assert.Null(unknown.Sleep);
        Assert.Equal((0L, 0L), (unknown.Exercise, unknown.Total));

        // Oura measured 0 steps and a poor night: those are scores of 0, not missing.
        var measured = Rules.Score(new ActivityDayFacts(0, 0, 40));
        Assert.Equal(0L, measured.Steps);
        Assert.Equal(0L, measured.Sleep);
    }

    [Fact]
    public void Readiness_and_oura_s_activity_score_are_not_read()
        => Assert.Equal(
            ["Steps", "ExerciseSeconds", "SleepScore"],
            typeof(ActivityDayFacts).GetProperties().Select(p => p.Name));

    [Fact]
    public void Days_before_2026_10_22_never_pay()
    {
        Assert.False(Rules.Pays(new DateOnly(2026, 10, 21)));
        Assert.True(Rules.Pays(new DateOnly(2026, 10, 22)));
        Assert.True(Rules.Pays(new DateOnly(2027, 1, 1)));
    }
}
