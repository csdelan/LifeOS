using System.Security.Cryptography;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Diagnostics;
using LifeOs.Infrastructure.Integrations;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A connected Oura on a database of its own (activity plan A2): Oura's records are keyed by
/// day and id, and the event stream is append-only, so these tests can't share the fixture's
/// database. Its home zone is UTC; the clock starts at noon on 2026-10-22.
/// </summary>
internal sealed class OuraHarness : IAsyncDisposable
{
    public static readonly Uri Redirect = new("https://lifeos.example/activity/connected");
    public static readonly DateOnly Today = new(2026, 10, 22);

    private OuraHarness(DomainTestDatabase db, TimerTestClock clock, FakeOuraClient oura, IOuraClient? client)
    {
        Db = db;
        Clock = clock;
        Oura = oura;
        Integrations = new NpgsqlIntegrationRepository(db.ConnectionString, new TokenCipher(RandomNumberGenerator.GetBytes(32)));
        Connection = new OuraConnectionService(Integrations, client, clock);
        Activity = new NpgsqlActivityRepository(db.ConnectionString);
        Sync = new OuraSyncService(Connection, client, Activity, Integrations, db.Get<IHomeCalendar>(), clock);
        Notes = new WorkoutNoteService(Activity, db.Get<IEventStore>(), clock, KernelSources.Cli);
        Reads = new ActivityReads(Activity, db.Get<IHomeCalendar>(), clock);
    }

    public DomainTestDatabase Db { get; }
    public TimerTestClock Clock { get; }
    public FakeOuraClient Oura { get; }
    public NpgsqlIntegrationRepository Integrations { get; }
    public OuraConnectionService Connection { get; }
    public NpgsqlActivityRepository Activity { get; }
    public OuraSyncService Sync { get; }
    public WorkoutNoteService Notes { get; }
    public ActivityReads Reads { get; }

    /// <summary>A new database with Oura connected (unless <paramref name="connect"/> is false).</summary>
    public static async Task<OuraHarness> CreateAsync(
        PostgresFixture postgres, CancellationToken ct, bool connect = true, bool configured = true)
    {
        var db = await DomainTestDatabase.CreateAsync(postgres, ct);
        var clock = new TimerTestClock(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        var oura = new FakeOuraClient(clock);
        var harness = new OuraHarness(db, clock, oura, configured ? oura : null);
        if (connect)
        {
            await harness.Connection.BeginConnectAsync(Redirect, ct);
            await harness.Connection.ConnectAsync("code-1", oura.AuthorizedState!, ct);
        }

        return harness;
    }

    public async Task<long> CountAsync(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(sql, args);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return (await connection.QueryAsync<T>(sql, args)).AsList();
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    // ---------------------------------------------------------------------------------
    // Oura's records
    // ---------------------------------------------------------------------------------

    private static readonly TimeSpan Pacific = TimeSpan.FromHours(-7);

    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0)
        => new(day.ToDateTime(new TimeOnly(hour, minute)), Pacific);

    public static OuraActivityDay Steps(DateOnly day, int steps, string? ouraId = null)
        => new(day, ouraId ?? $"activity-{day:yyyyMMdd}", At(day, 4), steps, 350, 2450, 6200, 600, 1500, 9000, 28000, 25000, 900, 81);

    public static OuraWorkout Workout(
        string id, DateOnly day, string activity = "running", string source = "manual", int minutes = 40, int? calories = 420)
        => new(id, day, activity, null, At(day, 7), At(day, 7).AddMinutes(minutes), calories, 5100, "moderate", source);

    public static OuraSleepScore SleepScore(DateOnly day, int score)
        => new(day, $"sleep-day-{day:yyyyMMdd}", At(day, 0), score,
            new Dictionary<string, int?> { ["deep_sleep"] = 90, ["total_sleep"] = 80, ["timing"] = null });

    public static OuraReadiness Ready(DateOnly day, int score)
        => new(day, $"ready-{day:yyyyMMdd}", At(day, 0), score, -0.12, 0.05,
            new Dictionary<string, int?> { ["hrv_balance"] = 70, ["resting_heart_rate"] = 90 });

    /// <summary>A sleep period ending on <paramref name="day"/>: <paramref name="asleepMinutes"/> asleep, starting at <paramref name="start"/>.</summary>
    public static OuraSleepPeriod Period(string id, DateOnly day, string type, int asleepMinutes, DateTimeOffset start)
        => new(id, day, type, start, start.AddMinutes(asleepMinutes + 20), (asleepMinutes + 20) * 60, asleepMinutes * 60,
            asleepMinutes * 12, asleepMinutes * 30, asleepMinutes * 18, 1200, 600, 92, 48, 54.2, 47);

    public static OuraSleepPeriod Night(string id, DateOnly day, int asleepMinutes)
        => Period(id, day, SleepTypes.LongSleep, asleepMinutes, At(day.AddDays(-1), 23));
}

/// <summary>
/// Syncing Oura into LifeOS (ACTIVITY-1..4, D14, activity plan A2, §3.2, §3.3): every record an
/// observed <c>oura</c> event, a new version only when it changed, the newest winning, deleted
/// workouts and sleep periods withdrawn and restored, one sync at a time, the history read back
/// a window at a time, tokens refreshed once when refused, and the events kept out of every view
/// about items.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OuraSyncTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = OuraHarness.Today;
    private static readonly OuraSyncOptions Recent = new(HistoryWindows: 0);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<OuraHarness> CreateAsync(bool connect = true, bool configured = true)
        => OuraHarness.CreateAsync(postgres, Ct, connect, configured);

    private static void Replace<T>(List<T> records, Func<T, bool> match, T with)
        => records[records.FindIndex(r => match(r))] = with;

    [Fact]
    public async Task A_first_sync_writes_each_record_once_as_an_observed_oura_event()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.AddRange([OuraHarness.Steps(Today.AddDays(-1), 8100), OuraHarness.Steps(Today, 4200)]);
        h.Oura.Workouts.Add(OuraHarness.Workout("w-run", Today.AddDays(-1)));
        h.Oura.SleepScores.Add(OuraHarness.SleepScore(Today, 82));
        h.Oura.Readiness.Add(OuraHarness.Ready(Today, 76));
        h.Oura.SleepPeriods.Add(OuraHarness.Night("s-night", Today, 7 * 60));

        var result = await h.Sync.SyncAsync(Recent, Ct);

        Assert.Equal((Today.AddDays(-14), Today.AddDays(1)), (result.From, result.To));
        Assert.Equal((1, 6, 6, 0), (result.Windows, result.Records, result.Written, result.Voided));

        var events = await h.QueryAsync<(string Kind, string Provenance, string ExternalId)>(
            "SELECT kind, provenance, external_id FROM bsk.event WHERE source_id = 'oura' ORDER BY external_id;");
        Assert.Equal(6, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(Provenances.Observed, e.Provenance);
            Assert.Matches("^[a-z_]+:[^:]+:[0-9a-f]{16}:first$", e.ExternalId);
        });
        Assert.Equal(EventKinds.Activity, Assert.Single(events, e => e.ExternalId.StartsWith("workout:w-run:")).Kind);
        Assert.Equal(5, events.Count(e => e.Kind == EventKinds.Measurement));
        Assert.Contains(events, e => e.ExternalId.StartsWith("daily_activity:2026-10-21:"));

        // Dated as Oura dates them: a day's activity at 4 a.m., a workout at its start.
        var occurred = await h.QueryAsync<DateTime>(
            "SELECT occurred_at FROM bsk.event WHERE external_id LIKE 'workout:w-run:%' OR external_id LIKE 'daily_activity:2026-10-22:%' ORDER BY external_id;");
        Assert.Equal(
            [OuraHarness.At(Today, 4).UtcDateTime, OuraHarness.At(Today.AddDays(-1), 7).UtcDateTime],
            occurred.Select(o => DateTime.SpecifyKind(o, DateTimeKind.Utc)));

        var days = await h.Activity.GetActivityDaysAsync(Today.AddDays(-1), Today, Ct);
        Assert.Equal([8100, 4200], days.Select(d => d.Steps));
        Assert.All(days, d => Assert.Equal(1, d.Versions));
        Assert.Equal((350, 2450, 6200, 81), (days[1].ActiveCalories, days[1].TotalCalories, days[1].WalkingM, days[1].Score));

        var workout = Assert.Single(await h.Activity.GetWorkoutsAsync(Today.AddDays(-7), Today, Ct));
        Assert.Equal(("w-run", "running", "manual", 40 * 60), (workout.Id, workout.Activity, workout.Source, workout.Seconds));
        Assert.Equal(OuraHarness.At(Today.AddDays(-1), 7), workout.StartedAt);
        Assert.True(workout.Verdict.Counted);
        Assert.Null(workout.Note);

        var sleep = Assert.Single(await h.Activity.GetSleepDaysAsync(Today.AddDays(-1), Today, Ct));
        Assert.Equal((82, 7 * 3600, 0), (sleep.Score, sleep.AsleepS, sleep.Naps));
        Assert.Equal(90, sleep.Contributors["deep_sleep"]);
        Assert.Null(sleep.Contributors["timing"]);
        Assert.Equal(OuraHarness.At(Today.AddDays(-1), 23), sleep.BedAt);

        var readiness = Assert.Single(await h.Activity.GetReadinessDaysAsync(Today, Today, Ct));
        Assert.Equal(76, readiness.Score);
        Assert.Equal(-0.12, readiness.TemperatureDeviation);
        Assert.Equal(0.05, readiness.TemperatureTrendDeviation);

        var status = await h.Connection.GetStatusAsync(Ct);
        Assert.Equal(h.Clock.UtcNow, status.LastSyncedAt);
        Assert.Null(status.LastSyncError);
        Assert.Equal(Today.AddDays(-14), status.HistoryFrom);
    }

    [Fact]
    public async Task The_window_is_read_two_days_wider_and_filtered_on_day()
    {
        await using var h = await CreateAsync();
        h.Oura.Workouts.AddRange([
            OuraHarness.Workout("w-before", Today.AddDays(-15)),
            OuraHarness.Workout("w-first", Today.AddDays(-14)),
            OuraHarness.Workout("w-tomorrow", Today.AddDays(1))
        ]);

        var result = await h.Sync.SyncAsync(Recent, Ct);

        Assert.Equal(5, h.Oura.Reads.Count);
        Assert.All(h.Oura.Reads, r => Assert.Equal((Today.AddDays(-16), Today.AddDays(3)), (r.Start, r.End)));
        Assert.Equal(["access-1"], h.Oura.Reads.Select(r => r.Token).Distinct());
        Assert.Equal((2, 2), (result.Records, result.Written));
        Assert.Null(await h.Activity.GetWorkoutAsync("w-before", Ct));
    }

    [Fact]
    public async Task An_unchanged_resync_writes_nothing_and_a_new_oura_id_alone_is_no_change()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today, 4200));
        h.Oura.Workouts.Add(OuraHarness.Workout("w-run", Today));
        h.Oura.SleepScores.Add(OuraHarness.SleepScore(Today, 82));
        h.Oura.Readiness.Add(OuraHarness.Ready(Today, 76));
        h.Oura.SleepPeriods.Add(OuraHarness.Night("s-night", Today, 400));
        await h.Sync.SyncAsync(Recent, Ct);

        var again = await h.Sync.SyncAsync(Recent, Ct);
        Replace(h.Oura.ActivityDays, d => d.Day == Today, OuraHarness.Steps(Today, 4200, ouraId: "a-new-id"));
        var renamed = await h.Sync.SyncAsync(Recent, Ct);

        Assert.Equal((5, 0), (again.Records, again.Written));
        Assert.Equal(0, renamed.Written);
        Assert.Equal(5, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE source_id = 'oura';"));
    }

    [Fact]
    public async Task Growing_steps_write_versions_and_the_newest_wins()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today, 1000));
        await h.Sync.SyncAsync(Recent, Ct);
        var first = Assert.Single(await h.Activity.GetActivityDaysAsync(Today, Today, Ct));

        Replace(h.Oura.ActivityDays, d => d.Day == Today, OuraHarness.Steps(Today, 2500));
        var grown = await h.Sync.SyncAsync(Recent, Ct);

        Assert.Equal(1, grown.Written);
        var day = Assert.Single(await h.Activity.GetActivityDaysAsync(Today, Today, Ct));
        Assert.Equal((2500, 2), (day.Steps, day.Versions));
        Assert.NotEqual(first.EventId, day.EventId);
        Assert.True(day.SyncedAt > first.SyncedAt);

        // The new version names the one it replaces.
        var externalId = Assert.Single(await h.QueryAsync<string>(
            "SELECT external_id FROM bsk.event WHERE id = @id;", new { id = day.EventId }));
        Assert.EndsWith($":{first.EventId}", externalId);
    }

    [Fact]
    public async Task A_value_that_flips_back_still_records()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today, 1000));
        foreach (var steps in new[] { 1000, 2000, 1000 })
        {
            Replace(h.Oura.ActivityDays, d => d.Day == Today, OuraHarness.Steps(Today, steps));
            Assert.Equal(1, (await h.Sync.SyncAsync(Recent, Ct)).Written);
        }

        var day = Assert.Single(await h.Activity.GetActivityDaysAsync(Today, Today, Ct));
        Assert.Equal((1000, 3), (day.Steps, day.Versions));
    }

    [Fact]
    public async Task A_deleted_workout_is_voided_and_can_come_back_while_daily_records_stay()
    {
        await using var h = await CreateAsync();
        var run = OuraHarness.Workout("w-run", Today.AddDays(-2));
        h.Oura.Workouts.Add(run);
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today.AddDays(-2), 9000));
        await h.Sync.SyncAsync(Recent, Ct);

        h.Oura.Workouts.Clear();
        h.Oura.ActivityDays.Clear();
        var deleted = await h.Sync.SyncAsync(Recent, Ct);

        Assert.Equal((1, 1), (deleted.Written, deleted.Voided));
        Assert.Null(await h.Activity.GetWorkoutAsync("w-run", Ct));
        Assert.Empty(await h.Activity.GetWorkoutsAsync(Today.AddDays(-14), Today, Ct));
        Assert.Equal(9000, Assert.Single(await h.Activity.GetActivityDaysAsync(Today.AddDays(-2), Today, Ct)).Steps);
        var voided = await h.QueryAsync<string?>(
            "SELECT payload->'oura_workout'->>'voided' FROM bsk.event WHERE external_id LIKE 'workout:w-run:%' ORDER BY recorded_at;");
        Assert.Equal([null, "true"], voided);

        // Still gone: nothing more to write. Back in the Oura app: restored.
        Assert.Equal(0, (await h.Sync.SyncAsync(Recent, Ct)).Written);
        h.Oura.Workouts.Add(run);
        var back = await h.Sync.SyncAsync(Recent, Ct);
        Assert.Equal((1, 0), (back.Written, back.Voided));
        Assert.Equal(40 * 60, (await h.Activity.GetWorkoutAsync("w-run", Ct))!.Seconds);
        Assert.Equal(3, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE external_id LIKE 'workout:w-run:%';"));
    }

    [Fact]
    public async Task Only_records_on_the_windows_days_are_withdrawn()
    {
        await using var h = await CreateAsync();
        h.Oura.Workouts.AddRange([
            OuraHarness.Workout("w-old", Today.AddDays(-40)),
            OuraHarness.Workout("w-recent", Today.AddDays(-3))
        ]);
        h.Oura.SleepPeriods.Add(OuraHarness.Night("s-old", Today.AddDays(-40), 420));
        await h.Sync.SyncAsync(new OuraSyncOptions(From: Today.AddDays(-60)), Ct);
        Assert.NotNull(await h.Activity.GetWorkoutAsync("w-old", Ct));

        h.Oura.Workouts.Clear();
        h.Oura.SleepPeriods.Clear();
        var recent = await h.Sync.SyncAsync(Recent, Ct);

        // The recent window can't say anything about a day it didn't read.
        Assert.Equal(1, recent.Voided);
        Assert.NotNull(await h.Activity.GetWorkoutAsync("w-old", Ct));
        Assert.Null(await h.Activity.GetWorkoutAsync("w-recent", Ct));
        Assert.Single(await h.Activity.GetSleepDaysAsync(Today.AddDays(-40), Today.AddDays(-40), Ct));

        // A sync that reads that day can.
        var wide = await h.Sync.SyncAsync(new OuraSyncOptions(From: Today.AddDays(-60)), Ct);
        Assert.Equal(2, wide.Voided);
        Assert.Null(await h.Activity.GetWorkoutAsync("w-old", Ct));
        Assert.Empty(await h.Activity.GetSleepDaysAsync(Today.AddDays(-40), Today.AddDays(-40), Ct));
    }

    [Fact]
    public async Task Two_racing_syncs_write_once()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.AddRange(Enumerable.Range(0, 10).Select(i => OuraHarness.Steps(Today.AddDays(-i), 3000 + i)));
        h.Oura.Workouts.Add(OuraHarness.Workout("w-run", Today));
        h.Oura.ReadDelay = TimeSpan.FromMilliseconds(50);

        var results = await Task.WhenAll(h.Sync.SyncAsync(Recent, Ct), h.Sync.SyncAsync(Recent, Ct));

        Assert.Equal(11, results.Sum(r => r.Written));
        Assert.Contains(results, r => r.Written == 0);
        Assert.Equal(11, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE source_id = 'oura';"));
    }

    [Fact]
    public async Task The_night_is_the_longest_long_sleep_and_everything_else_but_rest_and_deleted_is_a_nap()
    {
        await using var h = await CreateAsync();
        var day = Today.AddDays(-1);
        h.Oura.SleepScores.Add(OuraHarness.SleepScore(day, 81));
        h.Oura.SleepPeriods.AddRange([
            OuraHarness.Night("s-night", day, 6 * 60),
            OuraHarness.Period("s-second", day, SleepTypes.LongSleep, 60, OuraHarness.At(day, 14)),
            OuraHarness.Period("s-nap", day, SleepTypes.Nap, 30, OuraHarness.At(day, 13)),
            OuraHarness.Period("s-late", day, SleepTypes.LateNap, 20, OuraHarness.At(day, 19)),
            OuraHarness.Period("s-new", day, "siesta_v2", 10, OuraHarness.At(day, 16)),
            OuraHarness.Period("s-rest", day, SleepTypes.Rest, 15, OuraHarness.At(day, 10)),
            OuraHarness.Period("s-deleted", day, SleepTypes.Deleted, 9 * 60, OuraHarness.At(day.AddDays(-1), 21)),
            OuraHarness.Period("s-only-nap", Today.AddDays(-2), SleepTypes.Nap, 25, OuraHarness.At(Today.AddDays(-2), 13))
        ]);

        await h.Sync.SyncAsync(Recent, Ct);

        // Every period is kept; rest and deleted ones just aren't counted.
        Assert.Equal(8, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE external_id LIKE 'sleep:%';"));
        Assert.Equal(6, await h.CountAsync("SELECT count(*) FROM bsk.v_sleep_period;"));
        Assert.Equal("siesta_v2", Assert.Single(await h.QueryAsync<string>(
            "SELECT type FROM bsk.v_sleep_period WHERE period_id = 's-new';")));

        var sleep = await h.Activity.GetSleepDaysAsync(Today.AddDays(-2), day, Ct);
        Assert.Equal(2, sleep.Count);
        var napOnly = sleep[0];
        Assert.Null(napOnly.Score);
        Assert.Null(napOnly.BedAt);
        Assert.Null(napOnly.AsleepS);
        Assert.Equal((1, 25 * 60), (napOnly.Naps, napOnly.NapS));
        var full = sleep[1];
        Assert.Equal((81, 6 * 3600), (full.Score, full.AsleepS));
        Assert.Equal(OuraHarness.At(day.AddDays(-1), 23), full.BedAt);
        Assert.Equal((4, (60 + 30 + 20 + 10) * 60), (full.Naps, full.NapS));

        // The night deleted in the Oura app: the other long_sleep is the night now.
        Replace(h.Oura.SleepPeriods, p => p.Id == "s-night", OuraHarness.Night("s-night", day, 6 * 60) with { Type = SleepTypes.Deleted });
        await h.Sync.SyncAsync(Recent, Ct);
        var after = Assert.Single(await h.Activity.GetSleepDaysAsync(day, day, Ct));
        Assert.Equal((3600, 3, (30 + 20 + 10) * 60), (after.AsleepS, after.Naps, after.NapS));
        Assert.Equal(OuraHarness.At(day, 14), after.BedAt);
    }

    [Fact]
    public async Task Walks_and_detections_are_kept_but_not_counted_and_unknown_values_survive()
    {
        await using var h = await CreateAsync();
        var day = Today.AddDays(-1);
        h.Oura.Workouts.AddRange([
            OuraHarness.Workout("w-walk", day, "walking", "manual"),
            OuraHarness.Workout("w-detected", day, "running", "autodetected"),
            OuraHarness.Workout("w-confirmed", day, "cycling", "confirmed"),
            OuraHarness.Workout("w-strength", day, "strength_training", "workout_heart_rate"),
            OuraHarness.Workout("w-padel", day, "padel_v2", "garmin_import", calories: null) with { Label = "Club night" }
        ]);

        await h.Sync.SyncAsync(Recent, Ct);

        var all = await h.Reads.GetWorkoutsAsync(day, day, all: true, Ct);
        Assert.Equal(5, all.Count);
        var verdicts = all.ToDictionary(w => w.Id, w => w.Verdict);
        Assert.Equal(ExerciseRules.WalkReason, verdicts["w-walk"].Reason);
        Assert.Equal(ExerciseRules.DetectedReason, verdicts["w-detected"].Reason);
        Assert.Equal(ExerciseRules.DetectedReason, verdicts["w-confirmed"].Reason);
        Assert.Equal(ExerciseRules.UnknownSourceReason, verdicts["w-padel"].Reason);
        Assert.True(verdicts["w-strength"].Counted);

        var padel = all.Single(w => w.Id == "w-padel");
        Assert.Equal(("padel_v2", "garmin_import", "Club night"), (padel.Activity, padel.Source, padel.Label));
        Assert.Null(padel.Calories);

        Assert.Equal("w-strength", Assert.Single(await h.Reads.GetWorkoutsAsync(day, day, all: false, Ct)).Id);
    }

    [Fact]
    public async Task The_history_is_read_back_a_window_at_a_time_until_a_window_is_empty()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.AddRange([
            OuraHarness.Steps(Today, 5000),
            OuraHarness.Steps(Today.AddDays(-100), 6000),
            OuraHarness.Steps(Today.AddDays(-300), 7000)
        ]);
        var one = new OuraSyncOptions(HistoryWindows: 1);

        var first = await h.Sync.SyncAsync(one, Ct);
        Assert.Equal((2, 2, Today.AddDays(-194), false), (first.Windows, first.Records, first.HistoryFrom, first.HistoryDone));
        Assert.Contains(h.Oura.Reads, r => (r.Start, r.End) == (Today.AddDays(-196), Today.AddDays(-13)));

        var second = await h.Sync.SyncAsync(one, Ct);
        Assert.Equal((2, 2, Today.AddDays(-374), false), (second.Windows, second.Records, second.HistoryFrom, second.HistoryDone));

        var third = await h.Sync.SyncAsync(one, Ct);
        Assert.Equal((2, 1, Today.AddDays(-554), true), (third.Windows, third.Records, third.HistoryFrom, third.HistoryDone));

        // Done: a sync reads the recent window only.
        var fourth = await h.Sync.SyncAsync(cancellationToken: Ct);
        Assert.Equal((1, Today.AddDays(-554), true), (fourth.Windows, fourth.HistoryFrom, fourth.HistoryDone));

        var status = await h.Connection.GetStatusAsync(Ct);
        Assert.Equal(Today.AddDays(-554), status.HistoryFrom);
        Assert.True(status.HistoryDone);
        Assert.Equal(
            [7000, 6000, 5000],
            (await h.Activity.GetActivityDaysAsync(Today.AddDays(-400), Today, Ct)).Select(d => d.Steps));
    }

    [Fact]
    public async Task The_history_stops_at_the_floor()
    {
        await using var h = await CreateAsync();
        for (var day = Today; day >= OuraSyncService.HistoryFloor; day = day.AddDays(-150))
        {
            h.Oura.ActivityDays.Add(OuraHarness.Steps(day, 4000));
        }

        var result = await h.Sync.SyncAsync(new OuraSyncOptions(HistoryWindows: int.MaxValue), Ct);

        Assert.Equal((OuraSyncService.HistoryFloor, true), (result.HistoryFrom, result.HistoryDone));
        Assert.Equal(h.Oura.ActivityDays.Count, result.Written);
        Assert.DoesNotContain(h.Oura.Reads, r => r.Start < OuraSyncService.HistoryFloor.AddDays(-OuraSyncService.PadDays));
    }

    [Fact]
    public async Task Syncing_from_a_day_reads_every_window_since_then()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.AddRange([OuraHarness.Steps(Today.AddDays(-400), 1), OuraHarness.Steps(Today.AddDays(-1), 2)]);

        var result = await h.Sync.SyncAsync(new OuraSyncOptions(From: Today.AddDays(-400)), Ct);

        Assert.Equal((Today.AddDays(-400), 3, 2, 2), (result.From, result.Windows, result.Records, result.Written));
        Assert.Equal((Today.AddDays(-400), false), (result.HistoryFrom, result.HistoryDone));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Sync.SyncAsync(new OuraSyncOptions(From: new DateOnly(2014, 12, 31)), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Sync.SyncAsync(new OuraSyncOptions(From: Today.AddDays(1)), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Sync.SyncAsync(new OuraSyncOptions(HistoryWindows: -1), Ct));
    }

    [Fact]
    public async Task A_refused_token_is_refreshed_once_and_the_read_tried_again()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today, 4200));
        h.Oura.RefusedTokens.Add("access-1");

        var result = await h.Sync.SyncAsync(Recent, Ct);

        Assert.Equal(1, result.Written);
        Assert.Equal(1, h.Oura.Refreshes);
        Assert.Equal(["access-1", "access-2"], h.Oura.Reads.Select(r => r.Token).Distinct());
        Assert.Equal(OuraStatus.ConnectedState, (await h.Connection.GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task A_token_refused_after_refreshing_needs_reconnecting()
    {
        await using var h = await CreateAsync();
        h.Oura.RefuseEveryToken = true;

        var ex = await Assert.ThrowsAsync<OuraConnectionException>(() => h.Sync.SyncAsync(Recent, Ct));

        Assert.Contains("Reconnect Oura", ex.Message);
        Assert.Equal(1, h.Oura.Refreshes);
        var status = await h.Connection.GetStatusAsync(Ct);
        Assert.Equal(OuraStatus.Reconnect, status.State);
        Assert.Contains("even after refreshing", status.ReconnectReason);
        Assert.Equal(ex.Message, status.LastSyncError);
        Assert.Null(status.LastSyncedAt);

        // Nothing calls Oura until it's connected again.
        var reads = h.Oura.Reads.Count;
        await Assert.ThrowsAsync<OuraConnectionException>(() => h.Sync.SyncAsync(Recent, Ct));
        Assert.Equal(reads, h.Oura.Reads.Count);
    }

    [Fact]
    public async Task Oura_down_or_refusing_access_keeps_what_LifeOS_has_and_says_why()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today, 4200));
        await h.Sync.SyncAsync(Recent, Ct);
        var synced = h.Clock.UtcNow;
        h.Clock.Advance(TimeSpan.FromHours(1));

        h.Oura.FailReads = new OuraUnavailableException("Oura couldn't be reached.");
        await Assert.ThrowsAsync<OuraUnavailableException>(() => h.Sync.SyncAsync(Recent, Ct));
        var down = await h.Connection.GetStatusAsync(Ct);
        Assert.Equal(synced, down.LastSyncedAt);
        Assert.Equal("Oura couldn't be reached.", down.LastSyncError);
        Assert.Equal(4200, Assert.Single(await h.Activity.GetActivityDaysAsync(Today, Today, Ct)).Steps);

        h.Oura.FailReads = new OuraAccessDeniedException(403, "Oura refused access to daily_activity (Membership required).");
        await Assert.ThrowsAsync<OuraAccessDeniedException>(() => h.Sync.SyncAsync(Recent, Ct));
        var denied = await h.Connection.GetStatusAsync(Ct);
        Assert.Equal(OuraStatus.ConnectedState, denied.State);
        Assert.Contains("Membership required", denied.LastSyncError);

        h.Oura.FailReads = null;
        await h.Sync.SyncAsync(Recent, Ct);
        var back = await h.Connection.GetStatusAsync(Ct);
        Assert.Equal(h.Clock.UtcNow, back.LastSyncedAt);
        Assert.Null(back.LastSyncError);
    }

    [Fact]
    public async Task A_sync_needs_oura_set_up_and_connected()
    {
        await using var unconnected = await CreateAsync(connect: false);
        Assert.Contains("isn't connected",
            (await Assert.ThrowsAsync<OuraConnectionException>(() => unconnected.Sync.SyncAsync(Recent, Ct))).Message);
        Assert.Empty(unconnected.Oura.Reads);

        await using var unconfigured = await CreateAsync(connect: false, configured: false);
        Assert.Equal(OuraConnectionService.NotSetUp,
            (await Assert.ThrowsAsync<OuraConnectionException>(() => unconfigured.Sync.SyncAsync(Recent, Ct))).Message);
    }

    [Fact]
    public async Task Connecting_runs_the_first_sync_and_a_failed_one_leaves_it_connected()
    {
        await using var h = await CreateAsync(connect: false);
        h.Oura.ActivityDays.AddRange([OuraHarness.Steps(Today, 4200), OuraHarness.Steps(Today.AddDays(-100), 9000)]);
        await h.Connection.BeginConnectAsync(OuraHarness.Redirect, Ct);

        var status = await h.Sync.ConnectAsync("code-1", h.Oura.AuthorizedState!, Ct);

        Assert.Equal(OuraStatus.ConnectedState, status.State);
        Assert.Equal(h.Clock.UtcNow, status.LastSyncedAt);
        Assert.Equal(2, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE source_id = 'oura';"));
        // The recent window, the history window holding the day 100 days back, and the empty
        // one before it that ends the backfill.
        Assert.Equal(3 * OuraCollections.All.Count, h.Oura.Reads.Count);
        Assert.True(status.HistoryDone);

        await using var failing = await CreateAsync(connect: false);
        failing.Oura.FailReads = new OuraUnavailableException("Oura couldn't be reached.");
        await failing.Connection.BeginConnectAsync(OuraHarness.Redirect, Ct);
        var failed = await failing.Sync.ConnectAsync("code-1", failing.Oura.AuthorizedState!, Ct);
        Assert.Equal(OuraStatus.ConnectedState, failed.State);
        Assert.Null(failed.LastSyncedAt);
        Assert.Equal("Oura couldn't be reached.", failed.LastSyncError);
    }

    [Fact]
    public async Task Oura_events_and_notes_stay_out_of_every_view_about_items_and_the_reader_can_read_them()
    {
        await using var h = await CreateAsync();
        h.Oura.ActivityDays.Add(OuraHarness.Steps(Today, 4200));
        h.Oura.Workouts.Add(OuraHarness.Workout("w-run-0001", Today));
        h.Oura.SleepScores.Add(OuraHarness.SleepScore(Today, 82));
        h.Oura.Readiness.Add(OuraHarness.Ready(Today, 76));
        h.Oura.SleepPeriods.Add(OuraHarness.Night("s-night", Today, 400));
        await h.Sync.SyncAsync(Recent, Ct);
        await h.Notes.SetAsync("w-run-0001", "Hill repeats", Ct);

        Assert.Equal(5, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE source_id = 'oura';"));
        Assert.Equal(1, await h.CountAsync("SELECT count(*) FROM bsk.event WHERE kind = 'note';"));
        Assert.Equal(0, await h.CountAsync("SELECT count(*) FROM bsk.v_inbox;"));
        Assert.Equal(0, await h.CountAsync("SELECT count(*) FROM bsk.v_inbox_resolved;"));
        Assert.Equal(0, await h.CountAsync("SELECT count(*) FROM bsk.subject_event;"));
        Assert.Equal(0, await h.CountAsync("SELECT count(*) FROM bsk.v_ledger;"));
        Assert.Equal(0, await h.CountAsync("SELECT count(*) FROM bsk.v_task_timer;"));
        Assert.Equal(0, await h.CountAsync("SELECT count(*) FROM bsk.v_meal;"));
        Assert.Equal(0, (await new DiagnosticRunner(h.Db.ConnectionString).RunAsync(cancellationToken: Ct)).FindingCount);

        await using var reader = new NpgsqlConnection(BskReader.ConnectionStringFrom(h.Db.ConnectionString));
        await reader.OpenAsync(Ct);
        foreach (var view in new[] { "bsk.v_activity_day", "bsk.v_workout", "bsk.v_sleep_period", "bsk.v_sleep_day", "bsk.v_readiness_day" })
        {
            Assert.Equal(1, await reader.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT count(*) FROM {view};", cancellationToken: Ct)));
        }

        Assert.Equal("Hill repeats", await reader.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT note FROM bsk.v_workout;", cancellationToken: Ct)));
    }
}

/// <summary>
/// Chris's note on a workout (ACTIVITY-3, activity plan A2, §3.2): set, changed and cleared with
/// the newest winning, untouched by syncs, back with a workout that comes back, and refused in
/// words with nothing written.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkoutNoteTests(PostgresFixture postgres)
{
    private const string RunId = "0a1b2c3d-4e5f-run";
    private static readonly DateOnly Today = OuraHarness.Today;
    private static readonly OuraSyncOptions Recent = new(HistoryWindows: 0);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<OuraHarness> WithRunAsync()
    {
        var h = await OuraHarness.CreateAsync(postgres, Ct);
        h.Oura.Workouts.Add(OuraHarness.Workout(RunId, Today.AddDays(-1)));
        await h.Sync.SyncAsync(Recent, Ct);
        return h;
    }

    private static Task<long> NotesAsync(OuraHarness h)
        => h.CountAsync("SELECT count(*) FROM bsk.event WHERE payload ? 'workout_note';");

    [Fact]
    public async Task A_note_is_set_changed_and_cleared_and_the_newest_wins()
    {
        await using var h = await WithRunAsync();

        var noted = await h.Notes.SetAsync("0a1b2c3d", "Hill repeats, left knee fine", Ct);
        Assert.Equal((RunId, "Hill repeats, left knee fine", h.Clock.UtcNow), (noted.Id, noted.Note, noted.NoteAt));

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("Easy 5k", (await h.Notes.SetAsync(RunId, "  Easy 5k  ", Ct)).Note);

        // The same note again writes nothing.
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.Notes.SetAsync(RunId, "Easy 5k", Ct);
        Assert.Equal(2, await NotesAsync(h));

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var cleared = await h.Notes.ClearAsync(RunId, Ct);
        Assert.Null(cleared.Note);
        Assert.Null(cleared.NoteAt);
        Assert.Equal(3, await NotesAsync(h));

        // Clearing what's already clear writes nothing; a blank text clears too.
        await h.Notes.SetAsync(RunId, "   ", Ct);
        Assert.Equal(3, await NotesAsync(h));

        var events = await h.QueryAsync<(string Kind, string Provenance, string Source, string? Text)>(
            "SELECT kind, provenance, source_id, payload->'workout_note'->>'text' FROM bsk.event WHERE payload ? 'workout_note' ORDER BY recorded_at;");
        Assert.All(events, e => Assert.Equal((EventKinds.Note, Provenances.Declared, KernelSources.Cli), (e.Kind, e.Provenance, e.Source)));
        Assert.Equal(["Hill repeats, left knee fine", "Easy 5k", ""], events.Select(e => e.Text));

        // A note of exactly the most characters is fine.
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var longest = new string('x', WorkoutNoteService.MaxLength);
        Assert.Equal(longest, (await h.Notes.SetAsync(RunId, longest, Ct)).Note);
    }

    [Fact]
    public async Task Syncs_leave_the_note_alone_and_a_workout_that_comes_back_brings_it()
    {
        await using var h = await WithRunAsync();
        await h.Notes.SetAsync(RunId, "Hill repeats", Ct);

        // Edited in the Oura app: a new version, the note still on it.
        Replace(h, OuraHarness.Workout(RunId, Today.AddDays(-1), calories: 510));
        Assert.Equal(1, (await h.Sync.SyncAsync(Recent, Ct)).Written);
        var edited = (await h.Activity.GetWorkoutAsync(RunId, Ct))!;
        Assert.Equal((510, "Hill repeats"), (edited.Calories, edited.Note));

        // Deleted: gone, and a note can't go on it. Back: so is its note.
        h.Oura.Workouts.Clear();
        await h.Sync.SyncAsync(Recent, Ct);
        Assert.Null(await h.Activity.GetWorkoutAsync(RunId, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Notes.SetAsync(RunId, "Lost", Ct));

        h.Oura.Workouts.Add(OuraHarness.Workout(RunId, Today.AddDays(-1), calories: 510));
        await h.Sync.SyncAsync(Recent, Ct);
        Assert.Equal("Hill repeats", (await h.Activity.GetWorkoutAsync(RunId, Ct))!.Note);
        Assert.Equal(1, await NotesAsync(h));
    }

    [Fact]
    public async Task A_note_that_is_too_long_or_more_than_a_line_or_on_no_known_workout_is_refused()
    {
        await using var h = await WithRunAsync();
        h.Oura.Workouts.AddRange([
            OuraHarness.Workout("abcdefgh-1", Today),
            OuraHarness.Workout("abcdefgh-2", Today)
        ]);
        await h.Sync.SyncAsync(Recent, Ct);

        var tooLong = await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Notes.SetAsync(RunId, new string('x', WorkoutNoteService.MaxLength + 1), Ct));
        Assert.Contains("140", tooLong.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Notes.SetAsync(RunId, "Hills\nthen flats", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Notes.SetAsync(RunId, "Hills\r\nthen flats", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Notes.SetAsync(RunId, "Hills\u2028then flats", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Notes.SetAsync(RunId, "Hills\tthen flats", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Notes.SetAsync("", "Hills", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Notes.SetAsync("0a1b", "Hills", Ct));
        Assert.Contains("no workout",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => h.Notes.SetAsync("ffffffff", "Hills", Ct))).Message);
        Assert.Contains("starts 2 workouts",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => h.Notes.SetAsync("abcdefgh", "Hills", Ct))).Message);

        Assert.Equal(0, await NotesAsync(h));
        Assert.Equal("abcdefgh-2", (await h.Notes.SetAsync("abcdefgh-2", "Fine", Ct)).Id);
    }

    private static void Replace(OuraHarness h, OuraWorkout with)
        => h.Oura.Workouts[h.Oura.Workouts.FindIndex(w => w.Id == with.Id)] = with;
}
