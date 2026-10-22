using System.Text.Json;
using LifeOs.Application.Activity;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// The activity rules in code (ACTIVITY-2, ACTIVITY-3, ACTIVITY-6, activity plan §2, §3.4, §3.9):
/// which workouts count as exercise and why not, the step bands, how a tag from the Oura app
/// reads, and the payloads a sync writes — the same record always making the same JSON and hash
/// (D14, §3.2).
/// </summary>
public sealed class ActivityRulesTests
{
    [Theory]
    [InlineData("manual", "running", true, null)]
    [InlineData("workout_heart_rate", "cycling", true, null)]
    [InlineData("live_oura_heart_rate", "strength_training", true, null)]
    [InlineData("live_third_party_heart_rate", "rowing", true, null)]
    [InlineData(" MANUAL ", "Running", true, null)]
    [InlineData("manual", "walking", false, ExerciseRules.WalkReason)]
    [InlineData("workout_heart_rate", "Walking", false, ExerciseRules.WalkReason)]
    [InlineData("autodetected", "walking", false, ExerciseRules.WalkReason)]
    [InlineData("autodetected", "running", false, ExerciseRules.DetectedReason)]
    [InlineData("confirmed", "cycling", false, ExerciseRules.DetectedReason)]
    [InlineData("garmin_import", "running", false, ExerciseRules.UnknownSourceReason)]
    [InlineData("unknown", "padel_v2", false, ExerciseRules.UnknownSourceReason)]
    [InlineData(null, "yoga", false, ExerciseRules.UnknownSourceReason)]
    [InlineData("manual", null, true, null)]
    public void Only_workouts_Chris_records_count_and_never_a_walk(string? source, string? activity, bool counted, string? reason)
    {
        var verdict = ExerciseRules.Counts(source, activity);

        Assert.Equal(counted, verdict.Counted);
        Assert.Equal(reason, verdict.Reason);
    }

    [Theory]
    [InlineData(0, StepBands.Under)]
    [InlineData(4_999, StepBands.Under)]
    [InlineData(5_000, StepBands.Minimum)]
    [InlineData(7_499, StepBands.Minimum)]
    [InlineData(7_500, StepBands.Goal)]
    [InlineData(23_000, StepBands.Goal)]
    public void Steps_fall_under_the_minimum_at_it_or_at_the_goal(int steps, string band)
        => Assert.Equal(band, ActivityTargets.Band(steps));

    private static readonly DateOnly Day = new(2026, 10, 21);

    private static OuraActivityDay Activity(int steps, string? ouraId = "oura-1")
        => new(Day, ouraId, new DateTimeOffset(2026, 10, 21, 4, 0, 0, TimeSpan.FromHours(-7)), steps,
            512, 2650, 7400, 600, 2400, 12000, 30000, 25000, 1800, 82);

    [Fact]
    public void A_daily_record_hashes_the_same_every_time_and_leaves_oura_s_id_out_of_the_hash()
    {
        var first = OuraPayloads.Activity(Activity(9234));
        var again = OuraPayloads.Activity(Activity(9234));
        var renamed = OuraPayloads.Activity(Activity(9234, ouraId: "oura-2"));
        var grown = OuraPayloads.Activity(Activity(9300));

        Assert.Equal((first.Json, first.Hash), (again.Json, again.Hash));
        Assert.Equal(first.Hash, renamed.Hash);
        Assert.NotEqual(first.Json, renamed.Json);
        Assert.NotEqual(first.Hash, grown.Hash);
        Assert.Matches("^[0-9a-f]{16}$", first.Hash);

        Assert.Equal("2026-10-21", first.Key);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 4, 0, 0, TimeSpan.FromHours(-7)), first.OccurredAt);
        Assert.Equal(
            """{"day":"2026-10-21","oura_id":"oura-1","steps":9234,"active_calories":512,"total_calories":2650,"walking_m":7400,"high_s":600,"medium_s":2400,"low_s":12000,"sedentary_s":30000,"resting_s":25000,"non_wear_s":1800,"score":82}""",
            first.Json);
        Assert.Equal($$"""{"oura_activity":{{first.Json}}}""", first.PayloadJson);
    }

    [Fact]
    public void A_daily_record_without_a_timestamp_is_dated_at_midnight_utc()
    {
        var record = OuraPayloads.SleepScore(new OuraSleepScore(Day, null, null, 80, new Dictionary<string, int?>()));

        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), record.OccurredAt);
        Assert.Equal("""{"day":"2026-10-21","oura_id":null,"score":80,"contributors":{}}""", record.Json);
    }

    [Fact]
    public void Contributors_are_written_in_name_order_whatever_order_they_came_in()
    {
        var a = OuraPayloads.Readiness(new OuraReadiness(Day, "r", null, 76, -0.1, 0.05,
            new Dictionary<string, int?> { ["sleep_balance"] = 75, ["hrv_balance"] = 70, ["activity_balance"] = null }));
        var b = OuraPayloads.Readiness(new OuraReadiness(Day, "r", null, 76, -0.1, 0.05,
            new Dictionary<string, int?> { ["activity_balance"] = null, ["hrv_balance"] = 70, ["sleep_balance"] = 75 }));

        Assert.Equal(a.Hash, b.Hash);
        Assert.Contains("""
            "temperature_deviation":-0.1,"temperature_trend_deviation":0.05,"contributors":{"activity_balance":null,"hrv_balance":70,"sleep_balance":75}
            """, a.Json);
    }

    [Fact]
    public void A_workout_keeps_oura_s_offset_and_its_id_is_its_key()
    {
        var start = new DateTimeOffset(2026, 10, 21, 7, 2, 0, TimeSpan.FromHours(-7));
        var record = OuraPayloads.Workout(new OuraWorkout(
            "w-1", Day, "running", null, start, start.AddMinutes(39), 420, 5100, "moderate", "manual"));

        Assert.Equal(("w-1", start), (record.Key, record.OccurredAt));
        using var json = JsonDocument.Parse(record.PayloadJson);
        var workout = json.RootElement.GetProperty("oura_workout");
        Assert.Equal("2026-10-21T07:02:00-07:00", workout.GetProperty("start").GetString());
        Assert.Equal("2026-10-21T07:41:00-07:00", workout.GetProperty("end").GetString());
        Assert.Equal(JsonValueKind.Null, workout.GetProperty("label").ValueKind);
        Assert.Equal("manual", workout.GetProperty("source").GetString());
    }

    [Fact]
    public void A_tag_keeps_what_oura_gives_and_its_id_is_its_key()
    {
        var start = new DateTimeOffset(2026, 10, 21, 21, 0, 0, TimeSpan.FromHours(-7));
        var tub = OuraPayloads.Tag(new OuraTag(
            "t-1", Day, Day, "custom", "Hot tub", start, start.AddMinutes(25), "With the neighbours"));

        Assert.Equal(("t-1", start, Day), (tub.Key, tub.OccurredAt, tub.Day));
        Assert.Equal(OuraCollections.Tag, tub.Collection);
        Assert.Equal(EventKinds.Observation, tub.Collection.Kind);
        Assert.Equal(
            """{"oura_tag":{"id":"t-1","day":"2026-10-21","end_day":"2026-10-21","type_code":"custom","custom_name":"Hot tub","start":"2026-10-21T21:00:00-07:00","end":"2026-10-21T21:25:00-07:00","comment":"With the neighbours"}}""",
            tub.PayloadJson);

        // A moment: no end and no end day, written as nulls, so the hash is the same every time.
        var happy = OuraPayloads.Tag(new OuraTag("t-2", Day, null, "generic_happy", null, start, null, null));
        Assert.Equal(
            """{"id":"t-2","day":"2026-10-21","end_day":null,"type_code":"generic_happy","custom_name":null,"start":"2026-10-21T21:00:00-07:00","end":null,"comment":null}""",
            happy.Json);
        Assert.Equal(happy.Hash, OuraPayloads.Tag(new OuraTag("t-2", Day, null, "generic_happy", null, start, null, null)).Hash);
        Assert.NotEqual(happy.Hash, OuraPayloads.Tag(new OuraTag("t-2", Day, null, "generic_happy", null, start, null, "Good day")).Hash);
    }

    [Theory]
    [InlineData("generic_happy", null, "Happy")]
    [InlineData("generic_excited", null, "Excited")]
    [InlineData("tag_generic_late_meal", null, "Late meal")]
    [InlineData("tag_generic_alcohol", null, "Alcohol")]
    [InlineData("tag_sauna", null, "Sauna")]
    [InlineData("tableTennis", null, "Table tennis")]
    [InlineData("generic_HIIT_class", null, "Hiit class")]
    [InlineData("  generic_happy  ", null, "Happy")]
    [InlineData("custom", "Hot tub", "Hot tub")]
    [InlineData("CUSTOM", "  Hot tub ", "Hot tub")]
    [InlineData("custom", null, OuraTags.UnnamedCustom)]
    [InlineData("custom", "  ", OuraTags.UnnamedCustom)]
    [InlineData(null, null, OuraTags.TextOnly)]
    [InlineData("", "Ignored", OuraTags.TextOnly)]
    [InlineData("generic_", null, "Generic")]
    [InlineData("tag_", null, "Tag")]
    public void A_tag_reads_as_its_code_made_readable_its_own_name_or_a_note(string? code, string? customName, string expected)
        => Assert.Equal(expected, OuraTags.Name(code, customName));

    [Fact]
    public void A_held_tag_knows_its_length()
    {
        var start = new DateTimeOffset(2026, 10, 21, 21, 0, 0, TimeSpan.Zero);
        ActivityTag Held(DateTimeOffset? end) => new(
            "t-1", Day, null, "tag_sauna", null, start, end, null, start, Guid.Empty);

        Assert.Null(Held(null).Seconds);
        Assert.Equal(25 * 60, Held(start.AddMinutes(25)).Seconds);
        Assert.Equal(0, Held(start.AddMinutes(-5)).Seconds);
        Assert.Equal("Sauna", Held(null).Name);
    }

    [Fact]
    public void An_external_id_names_the_collection_key_hash_and_the_version_it_replaces()
    {
        var record = OuraPayloads.Activity(Activity(9234));
        var previous = Guid.Parse("8f1c2a3b-0000-4000-8000-000000000001");

        Assert.Equal($"daily_activity:2026-10-21:{record.Hash}:first", OuraPayloads.ExternalId(record, null));
        Assert.Equal($"daily_activity:2026-10-21:{record.Hash}:{previous}", OuraPayloads.ExternalId(record, previous));
    }

    [Fact]
    public void Only_workouts_sleep_periods_and_tags_can_be_withdrawn()
    {
        var voided = OuraPayloads.Voided(OuraCollections.Workout, "w-1", Day, DateTimeOffset.UnixEpoch);

        Assert.Equal("""{"id":"w-1","day":"2026-10-21","voided":true}""", voided.Json);
        Assert.Equal("""{"oura_workout":{"id":"w-1","day":"2026-10-21","voided":true}}""", voided.PayloadJson);
        Assert.NotNull(OuraPayloads.Voided(OuraCollections.Sleep, "s-1", Day, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => OuraPayloads.Voided(OuraCollections.DailyActivity, "2026-10-21", Day, DateTimeOffset.UnixEpoch));
        Assert.Equal(
            """{"oura_tag":{"id":"t-1","day":"2026-10-21","voided":true}}""",
            OuraPayloads.Voided(OuraCollections.Tag, "t-1", Day, DateTimeOffset.UnixEpoch).PayloadJson);
        Assert.Equal(
            ["workout", "sleep", "enhanced_tag"],
            OuraCollections.All.Where(c => c.Voidable).Select(c => c.Name));
    }

    [Theory]
    [InlineData("  Hill repeats  ", "Hill repeats")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void A_note_is_trimmed_and_blank_clears(string? text, string expected)
        => Assert.Equal(expected, WorkoutNoteService.Normalize(text));

    [Theory]
    [InlineData("Hills\nthen flats")]
    [InlineData("Hills\rthen flats")]
    [InlineData("Hills\tthen flats")]
    [InlineData("Hills\u2029then flats")]
    public void A_note_is_one_line(string text)
        => Assert.Throws<ArgumentException>(() => WorkoutNoteService.Normalize(text));
}
