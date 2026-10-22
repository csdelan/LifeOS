using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The Activity page's API (ACTIVITY-2..4, activity plan A3, §3.6): the days with their step
/// bands and the thresholds, workouts with whether they count and why not, one day in full,
/// this week against last, the note (LifeOS's one write), and the page's sync with its
/// 5-minute throttle. Oura is played by <see cref="FakeOuraClient"/>, connected over HTTP, so
/// the first sync puts its records in. Each test has a database of its own, in UTC, with the
/// clock at noon on Thursday 2026-10-22.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ActivityEndpointTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = OuraHarness.Today;
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string?> WithTokenKey = new(DomainTestDatabase.ApiZone)
    {
        [TokenCipher.KeyKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Day(int offset) => Today.AddDays(offset);

    private sealed class Site(DomainTestDatabase db, ApiTestHost host, FakeOuraClient oura, TimerTestClock clock) : IAsyncDisposable
    {
        public DomainTestDatabase Db { get; } = db;
        public HttpClient Client { get; } = host.Client;
        public FakeOuraClient Oura { get; } = oura;
        public TimerTestClock Clock { get; } = clock;

        public async ValueTask DisposeAsync()
        {
            await host.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    /// <summary>A new database and API host; Oura holds what <paramref name="seed"/> adds and is connected unless told not to be.</summary>
    private async Task<Site> StartAsync(Action<FakeOuraClient>? seed = null, bool connect = true)
    {
        var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var clock = new TimerTestClock(Noon);
        var oura = new FakeOuraClient(clock);
        seed?.Invoke(oura);
        var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, WithTokenKey, services =>
        {
            services.AddSingleton<IOuraClient>(oura);
            services.AddSingleton<IClock>(clock);
        });
        var site = new Site(db, host, oura, clock);
        if (connect)
        {
            await JsonAsync(await site.Client.PostAsJsonAsync(
                "/api/oura/authorize", new { redirectUri = OuraHarness.Redirect.AbsoluteUri }, Ct));
            var connected = await JsonAsync(await site.Client.PostAsJsonAsync(
                "/api/oura/connect", new { code = "code-1", state = oura.AuthorizedState }, Ct));
            Assert.Equal("connected", connected.GetProperty("state").GetString());
            Assert.Null(connected.GetProperty("lastSyncError").GetString());
        }

        return site;
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        Assert.DoesNotContain("access-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-", body, StringComparison.Ordinal);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<string?> RefusedAsync(HttpResponseMessage response, HttpStatusCode expected)
        => (await JsonAsync(response, expected)).GetProperty("error").GetString();

    private static int? Int(JsonElement element, string name)
        => element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetInt32();

    private static List<string?> Strings(JsonElement array, string name)
        => array.EnumerateArray().Select(e => e.GetProperty(name).GetString()).ToList();

    [Fact]
    public async Task The_days_answer_every_day_with_its_band_sleep_and_readiness_and_the_thresholds()
    {
        await using var site = await StartAsync(oura =>
        {
            oura.ActivityDays.AddRange([
                OuraHarness.Steps(Day(-2), 4_100), OuraHarness.Steps(Day(-1), 6_200), OuraHarness.Steps(Today, 8_123)]);
            oura.SleepScores.Add(OuraHarness.SleepScore(Today, 84));
            oura.SleepPeriods.Add(OuraHarness.Night("s-night", Today, 7 * 60));
            oura.Readiness.Add(OuraHarness.Ready(Today, 77));
        });

        var read = await JsonAsync(await site.Client.GetAsync("/api/activity/days?from=2026-10-19&to=2026-10-22", Ct));

        Assert.Equal(("2026-10-19", "2026-10-22", "2026-10-22"),
            (read.GetProperty("from").GetString(), read.GetProperty("to").GetString(), read.GetProperty("today").GetString()));
        Assert.Equal((5_000, 7_500), (read.GetProperty("minimumSteps").GetInt32(), read.GetProperty("goalSteps").GetInt32()));
        var days = read.GetProperty("days");
        Assert.Equal(["2026-10-19", "2026-10-20", "2026-10-21", "2026-10-22"], Strings(days, "day"));
        Assert.Equal([null, 4_100, 6_200, 8_123], days.EnumerateArray().Select(d => Int(d, "steps")));
        Assert.Equal([null, StepBands.Under, StepBands.Minimum, StepBands.Goal], Strings(days, "band"));

        // A day with nothing is still there, all null, so the chart shows the gap.
        var empty = days[0];
        Assert.Null(Int(empty, "sleepScore"));
        Assert.Null(Int(empty, "activeCalories"));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("bedAt").ValueKind);

        var today = days[3];
        Assert.Equal((350, 2_450, 6_200), (Int(today, "activeCalories"), Int(today, "totalCalories"), Int(today, "walkingM")));
        Assert.Equal((600, 1_500, 9_000, 900, 81),
            (Int(today, "highS"), Int(today, "mediumS"), Int(today, "lowS"), Int(today, "nonWearS"), Int(today, "activityScore")));
        Assert.Equal((84, 77), (Int(today, "sleepScore"), Int(today, "readinessScore")));
        Assert.Equal((7 * 3_600, 440 * 60), (Int(today, "asleepS"), Int(today, "inBedS")));
        Assert.Equal(OuraHarness.At(Day(-1), 23), today.GetProperty("bedAt").GetDateTimeOffset());
        Assert.Equal((0, 0), (today.GetProperty("napCount").GetInt32(), today.GetProperty("napAsleepS").GetInt32()));

        // No range: the 30 days through today.
        var recent = await JsonAsync(await site.Client.GetAsync("/api/activity/days", Ct));
        Assert.Equal(("2026-09-23", "2026-10-22"), (recent.GetProperty("from").GetString(), recent.GetProperty("to").GetString()));
        Assert.Equal(30, recent.GetProperty("days").GetArrayLength());

        // Only Chris.
        using var anonymous = new HttpClient { BaseAddress = site.Client.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activity/days", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/activity/sync", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_range_of_more_than_731_days_or_one_running_backwards_is_refused_in_words()
    {
        await using var site = await StartAsync(connect: false);

        var longest = await JsonAsync(await site.Client.GetAsync("/api/activity/days?from=2024-10-22&to=2026-10-22", Ct));
        Assert.Equal(731, longest.GetProperty("days").GetArrayLength());

        Assert.Contains("up to 731 days", await RefusedAsync(
            await site.Client.GetAsync("/api/activity/days?from=2024-10-21&to=2026-10-22", Ct), HttpStatusCode.BadRequest));
        Assert.Contains("up to 731 days", await RefusedAsync(
            await site.Client.GetAsync("/api/activity/workouts?from=2024-10-21&to=2026-10-22", Ct), HttpStatusCode.BadRequest));
        Assert.Contains("before it starts", await RefusedAsync(
            await site.Client.GetAsync("/api/activity/days?from=2026-10-22&to=2026-10-21", Ct), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Workouts_say_whether_they_count_and_why_not()
    {
        await using var site = await StartAsync(oura => oura.Workouts.AddRange([
            OuraHarness.Workout("w-run", Day(-1)),
            OuraHarness.Workout("w-walk", Day(-2), "walking"),
            OuraHarness.Workout("w-detected", Day(-3), source: "autodetected"),
            OuraHarness.Workout("w-garmin", Day(-4), source: "garmin_import"),
        ]));

        var read = await JsonAsync(await site.Client.GetAsync("/api/activity/workouts", Ct));

        // No range: twelve whole weeks, Monday to Sunday, through today.
        Assert.Equal(("2026-08-03", "2026-10-22"), (read.GetProperty("from").GetString(), read.GetProperty("to").GetString()));
        var workouts = read.GetProperty("workouts");
        Assert.Equal(["w-run", "w-walk", "w-detected", "w-garmin"], Strings(workouts, "id"));
        Assert.Equal([true, false, false, false], workouts.EnumerateArray().Select(w => w.GetProperty("counted").GetBoolean()));
        Assert.Equal(
            [null, ExerciseRules.WalkReason, ExerciseRules.DetectedReason, ExerciseRules.UnknownSourceReason],
            Strings(workouts, "reason"));

        var run = workouts[0];
        Assert.Equal(("2026-10-21", "running", "manual", "moderate"),
            (run.GetProperty("day").GetString(), run.GetProperty("activity").GetString(),
             run.GetProperty("source").GetString(), run.GetProperty("intensity").GetString()));
        Assert.Equal((2_400, 420, 5_100), (run.GetProperty("seconds").GetInt32(), Int(run, "calories"), Int(run, "distanceM")));
        Assert.Equal(OuraHarness.At(Day(-1), 7), run.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("note").ValueKind);

        var lastTwoDays = await JsonAsync(await site.Client.GetAsync("/api/activity/workouts?from=2026-10-20&to=2026-10-22", Ct));
        Assert.Equal(["w-run", "w-walk"], Strings(lastTwoDays.GetProperty("workouts"), "id"));
    }

    [Fact]
    public async Task A_note_is_set_and_cleared_and_its_refusals_are_400s_in_words()
    {
        await using var site = await StartAsync(oura => oura.Workouts.Add(OuraHarness.Workout("w-run-0001", Day(-1))));
        const string url = "/api/activity/workouts/w-run-0001/note";

        var noted = await JsonAsync(await site.Client.PostAsJsonAsync(url, new { text = "  Hill repeats  " }, Ct));
        Assert.Equal(("w-run-0001", "Hill repeats"), (noted.GetProperty("id").GetString(), noted.GetProperty("note").GetString()));
        Assert.Equal(Noon, noted.GetProperty("noteAt").GetDateTimeOffset());
        Assert.True(noted.GetProperty("counted").GetBoolean());

        var listed = await JsonAsync(await site.Client.GetAsync("/api/activity/workouts", Ct));
        Assert.Equal("Hill repeats", listed.GetProperty("workouts")[0].GetProperty("note").GetString());

        Assert.Contains("one line", await RefusedAsync(
            await site.Client.PostAsJsonAsync(url, new { text = "Hills\nthen flats" }, Ct), HttpStatusCode.BadRequest));
        Assert.Contains("up to 140 characters", await RefusedAsync(
            await site.Client.PostAsJsonAsync(url, new { text = new string('x', 141) }, Ct), HttpStatusCode.BadRequest));
        Assert.Contains("an empty text clears it", await RefusedAsync(
            await site.Client.PostAsJsonAsync(url, new { }, Ct), HttpStatusCode.BadRequest));

        // The page names a workout by its whole id: no workout, and no short ids, here.
        Assert.Contains("no workout 'w-gone'", await RefusedAsync(
            await site.Client.PostAsJsonAsync("/api/activity/workouts/w-gone/note", new { text = "x" }, Ct),
            HttpStatusCode.NotFound));
        Assert.Contains("no workout 'w-run-00'", await RefusedAsync(
            await site.Client.PostAsJsonAsync("/api/activity/workouts/w-run-00/note", new { text = "x" }, Ct),
            HttpStatusCode.NotFound));

        site.Clock.Advance(TimeSpan.FromMinutes(1));
        var cleared = await JsonAsync(await site.Client.PostAsJsonAsync(url, new { text = "" }, Ct));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("note").ValueKind);

        // Set and cleared: two notes written, nothing for the refusals.
        await using var connection = new NpgsqlConnection(site.Db.ConnectionString);
        Assert.Equal(2, await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM bsk.event WHERE jsonb_exists(payload, 'workout_note');"));
    }

    [Fact]
    public async Task A_day_in_full_has_both_scores_contributors_the_night_s_stages_its_naps_and_its_workouts()
    {
        await using var site = await StartAsync(oura =>
        {
            oura.ActivityDays.Add(OuraHarness.Steps(Today, 8_123));
            oura.SleepScores.Add(OuraHarness.SleepScore(Today, 84));
            oura.Readiness.Add(OuraHarness.Ready(Today, 77));
            oura.SleepPeriods.AddRange([
                OuraHarness.Night("s-night", Today, 7 * 60),
                OuraHarness.Period("s-nap", Today, SleepTypes.Nap, 25, OuraHarness.At(Today, 13))]);
            oura.Workouts.AddRange([
                OuraHarness.Workout("w-run", Today),
                new OuraWorkout("w-yoga", Today, "yoga", "Morning flow", OuraHarness.At(Today, 6),
                    OuraHarness.At(Today, 6).AddMinutes(30), null, null, "easy", "manual")]);
        });

        var day = await JsonAsync(await site.Client.GetAsync("/api/activity/day?date=2026-10-22", Ct));

        Assert.Equal(("2026-10-22", "2026-10-22"), (day.GetProperty("day").GetString(), day.GetProperty("today").GetString()));
        var summary = day.GetProperty("summary");
        Assert.Equal((8_123, 84, 77), (Int(summary, "steps"), Int(summary, "sleepScore"), Int(summary, "readinessScore")));
        Assert.Equal((1, 25 * 60), (summary.GetProperty("napCount").GetInt32(), summary.GetProperty("napAsleepS").GetInt32()));

        var sleep = day.GetProperty("sleepContributors");
        Assert.Equal(90, sleep.GetProperty("deep_sleep").GetInt32());
        Assert.Equal(JsonValueKind.Null, sleep.GetProperty("timing").ValueKind);
        Assert.Equal(70, day.GetProperty("readinessContributors").GetProperty("hrv_balance").GetInt32());
        Assert.Equal((-0.12, 0.05),
            (day.GetProperty("temperatureDeviation").GetDouble(), day.GetProperty("temperatureTrendDeviation").GetDouble()));

        var night = day.GetProperty("night");
        Assert.Equal(("s-night", SleepTypes.LongSleep), (night.GetProperty("id").GetString(), night.GetProperty("type").GetString()));
        Assert.Equal((420 * 12, 420 * 30, 420 * 18, 1_200, 600),
            (Int(night, "deepS"), Int(night, "lightS"), Int(night, "remS"), Int(night, "awakeS"), Int(night, "latencyS")));
        Assert.Equal((92, 48, 47), (Int(night, "efficiency"), Int(night, "averageHrv"), Int(night, "lowestHr")));
        Assert.Equal(54.2, night.GetProperty("averageHr").GetDouble());
        Assert.Equal(OuraHarness.At(Day(-1), 23), night.GetProperty("startedAt").GetDateTimeOffset());

        var nap = Assert.Single(day.GetProperty("naps").EnumerateArray());
        Assert.Equal(("s-nap", 25 * 60), (nap.GetProperty("id").GetString(), Int(nap, "asleepS")));

        // The day's workouts read earliest first.
        Assert.Equal(["w-yoga", "w-run"], Strings(day.GetProperty("workouts"), "id"));
        Assert.Equal("Morning flow", day.GetProperty("workouts")[0].GetProperty("label").GetString());

        // A day with nothing, and no date (today).
        var nothing = await JsonAsync(await site.Client.GetAsync("/api/activity/day?date=2026-10-20", Ct));
        Assert.Null(Int(nothing.GetProperty("summary"), "steps"));
        Assert.Equal(JsonValueKind.Null, nothing.GetProperty("night").ValueKind);
        Assert.Empty(nothing.GetProperty("naps").EnumerateArray());
        Assert.Empty(nothing.GetProperty("sleepContributors").EnumerateObject());
        Assert.Equal("2026-10-22", (await JsonAsync(await site.Client.GetAsync("/api/activity/day", Ct))).GetProperty("day").GetString());
    }

    [Fact]
    public async Task The_summary_has_today_so_far_this_week_against_last_and_the_connection()
    {
        // Thursday 2026-10-22: this week is Monday 19th to Sunday 25th, last week 12th to 18th.
        await using var site = await StartAsync(oura =>
        {
            oura.ActivityDays.AddRange([
                OuraHarness.Steps(new(2026, 10, 12), 9_000),
                OuraHarness.Steps(new(2026, 10, 13), 7_500),
                OuraHarness.Steps(new(2026, 10, 14), 4_999),
                OuraHarness.Steps(new(2026, 10, 19), 8_000),
                OuraHarness.Steps(new(2026, 10, 20), 4_000),
                OuraHarness.Steps(new(2026, 10, 21), 6_000),
                OuraHarness.Steps(Today, 7_600)]);
            oura.Workouts.AddRange([
                OuraHarness.Workout("w-ride", new(2026, 10, 13), "cycling", minutes: 60, calories: 500),
                OuraHarness.Workout("w-auto", new(2026, 10, 15), source: "autodetected"),
                OuraHarness.Workout("w-walk", new(2026, 10, 20), "walking"),
                OuraHarness.Workout("w-run", new(2026, 10, 21)),
                OuraHarness.Workout("w-yoga", Today, "yoga", minutes: 30, calories: null)]);
            oura.SleepScores.AddRange([OuraHarness.SleepScore(new(2026, 10, 20), 80), OuraHarness.SleepScore(Today, 85)]);
            oura.Readiness.Add(OuraHarness.Ready(Today, 76));
        });

        var summary = await JsonAsync(await site.Client.GetAsync("/api/activity/summary", Ct));

        Assert.Equal("2026-10-22", summary.GetProperty("today").GetString());
        Assert.Equal((5_000, 7_500), (summary.GetProperty("minimumSteps").GetInt32(), summary.GetProperty("goalSteps").GetInt32()));

        // Today so far: its steps, and last night's sleep and readiness.
        var today = summary.GetProperty("todaySoFar");
        Assert.Equal((7_600, 85, 76), (Int(today, "steps"), Int(today, "sleepScore"), Int(today, "readinessScore")));
        Assert.Equal(StepBands.Goal, today.GetProperty("band").GetString());
        Assert.Equal(["w-yoga"], Strings(summary.GetProperty("todayWorkouts"), "id"));

        // This week: Monday to Wednesday averaged (today is still going), today at the goal
        // already, two sessions that count (not the walk), sleep averaged over the days with one.
        var week = summary.GetProperty("thisWeek");
        Assert.Equal(("2026-10-19", "2026-10-25"), (week.GetProperty("from").GetString(), week.GetProperty("to").GetString()));
        Assert.Equal((6_000, 3, 2, 1), (Int(week, "averageSteps"), Int(week, "stepDays"), Int(week, "goalDays"), Int(week, "underDays")));
        Assert.Equal((2, 70 * 60, 420), (Int(week, "sessions"), Int(week, "exerciseSeconds"), Int(week, "exerciseCalories")));
        Assert.Equal((83, 76), (Int(week, "averageSleep"), Int(week, "averageReadiness")));

        var last = summary.GetProperty("lastWeek");
        Assert.Equal(("2026-10-12", "2026-10-18"), (last.GetProperty("from").GetString(), last.GetProperty("to").GetString()));
        Assert.Equal((7_166, 3, 2, 1), (Int(last, "averageSteps"), Int(last, "stepDays"), Int(last, "goalDays"), Int(last, "underDays")));
        Assert.Equal((1, 3_600, 500), (Int(last, "sessions"), Int(last, "exerciseSeconds"), Int(last, "exerciseCalories")));
        Assert.Null(Int(last, "averageSleep"));
        Assert.Null(Int(last, "averageReadiness"));

        var oura = summary.GetProperty("oura");
        Assert.Equal("connected", oura.GetProperty("state").GetString());
        Assert.Equal(Noon, oura.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_page_s_sync_is_skipped_under_five_minutes_and_sync_now_always_runs()
    {
        await using var site = await StartAsync(oura => oura.ActivityDays.Add(OuraHarness.Steps(Today, 4_000)));
        var reads = site.Oura.Reads.Count;

        // Opening the page right after connecting: the sync that connecting ran is enough.
        var skipped = await JsonAsync(await site.Client.PostAsync("/api/activity/sync", null, Ct));
        Assert.False(skipped.GetProperty("ran").GetBoolean());
        Assert.False(skipped.GetProperty("changed").GetBoolean());
        Assert.Equal(Noon, skipped.GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, skipped.GetProperty("from").ValueKind);

        site.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        var stillSkipped = await JsonAsync(await site.Client.PostAsJsonAsync("/api/activity/sync", new { force = false }, Ct));
        Assert.False(stillSkipped.GetProperty("ran").GetBoolean());
        Assert.Equal(reads, site.Oura.Reads.Count);

        // Sync now always runs; nothing changed at Oura, so nothing is written.
        var forced = await JsonAsync(await site.Client.PostAsJsonAsync("/api/activity/sync", new { force = true }, Ct));
        Assert.True(forced.GetProperty("ran").GetBoolean());
        Assert.Equal((false, 0, 1), (forced.GetProperty("changed").GetBoolean(), forced.GetProperty("written").GetInt32(),
            forced.GetProperty("records").GetInt32()));
        Assert.Equal(("2026-10-08", "2026-10-23"), (forced.GetProperty("from").GetString(), forced.GetProperty("to").GetString()));
        Assert.Equal(site.Clock.UtcNow, forced.GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.True(site.Oura.Reads.Count > reads);

        // Five minutes on, opening the page syncs again and picks up what changed.
        site.Oura.ActivityDays[0] = OuraHarness.Steps(Today, 6_500);
        site.Oura.Workouts.Add(OuraHarness.Workout("w-run", Today));
        site.Clock.Advance(TimeSpan.FromMinutes(5));
        var again = await JsonAsync(await site.Client.PostAsync("/api/activity/sync", null, Ct));
        Assert.True(again.GetProperty("ran").GetBoolean());
        Assert.Equal((true, 2, 0), (again.GetProperty("changed").GetBoolean(), again.GetProperty("written").GetInt32(),
            again.GetProperty("voided").GetInt32()));
        Assert.Equal(site.Clock.UtcNow, again.GetProperty("lastSyncedAt").GetDateTimeOffset());

        var day = await JsonAsync(await site.Client.GetAsync("/api/activity/day", Ct));
        Assert.Equal(6_500, Int(day.GetProperty("summary"), "steps"));
        Assert.Equal(["w-run"], Strings(day.GetProperty("workouts"), "id"));
    }

    [Fact]
    public async Task Oura_s_errors_reach_the_page_in_words_and_what_LifeOS_holds_still_reads()
    {
        await using var site = await StartAsync(oura => oura.ActivityDays.Add(OuraHarness.Steps(Today, 4_000)), connect: false);

        Assert.Equal("Oura isn't connected: press Connect Oura on the Activity page.", await RefusedAsync(
            await site.Client.PostAsync("/api/activity/sync", null, Ct), HttpStatusCode.Conflict));
        var unconnected = await JsonAsync(await site.Client.GetAsync("/api/activity/summary", Ct));
        Assert.Equal("not_connected", unconnected.GetProperty("oura").GetProperty("state").GetString());

        await JsonAsync(await site.Client.PostAsJsonAsync(
            "/api/oura/authorize", new { redirectUri = OuraHarness.Redirect.AbsoluteUri }, Ct));
        await JsonAsync(await site.Client.PostAsJsonAsync(
            "/api/oura/connect", new { code = "code-1", state = site.Oura.AuthorizedState }, Ct));

        // Oura down: a 502, kept on the connection, and the page still reads what LifeOS has.
        site.Oura.FailReads = new OuraUnavailableException("Oura couldn't be reached.");
        Assert.Equal("Oura couldn't be reached.", await RefusedAsync(
            await site.Client.PostAsJsonAsync("/api/activity/sync", new { force = true }, Ct), HttpStatusCode.BadGateway));
        var down = await JsonAsync(await site.Client.GetAsync("/api/activity/summary", Ct));
        Assert.Equal("Oura couldn't be reached.", down.GetProperty("oura").GetProperty("lastSyncError").GetString());
        Assert.Equal(4_000, Int(down.GetProperty("todaySoFar"), "steps"));

        // A lapsed membership or a missing scope: a 409 passing Oura's words on.
        site.Oura.FailReads = new OuraAccessDeniedException(403, "Oura refused access to daily_activity (Membership required).");
        Assert.Contains("Membership required", await RefusedAsync(
            await site.Client.PostAsJsonAsync("/api/activity/sync", new { force = true }, Ct), HttpStatusCode.Conflict));

        // A token refused even after refreshing: reconnect, and the throttle doesn't hide it.
        site.Oura.FailReads = null;
        site.Oura.RefuseEveryToken = true;
        Assert.Contains("Reconnect Oura", await RefusedAsync(
            await site.Client.PostAsJsonAsync("/api/activity/sync", new { force = true }, Ct), HttpStatusCode.Conflict));
        Assert.Equal("reconnect", (await JsonAsync(await site.Client.GetAsync("/api/activity/summary", Ct)))
            .GetProperty("oura").GetProperty("state").GetString());
        Assert.Contains("Reconnect Oura", await RefusedAsync(
            await site.Client.PostAsync("/api/activity/sync", null, Ct), HttpStatusCode.Conflict));
    }
}

/// <summary>
/// The week rules behind the summary (activity plan §3.6), on <see cref="ActivityReads"/>
/// directly: today counts toward the goal once it's there, but isn't averaged or counted under
/// the minimum until it's over.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ActivitySummaryTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Today_is_not_under_the_minimum_until_it_is_over()
    {
        await using var h = await OuraHarness.CreateAsync(postgres, Ct);
        h.Oura.ActivityDays.AddRange([
            OuraHarness.Steps(new(2026, 10, 19), 5_000),
            OuraHarness.Steps(OuraHarness.Today, 3_000)]);
        await h.Sync.SyncAsync(new OuraSyncOptions(HistoryWindows: 0), Ct);

        var thursday = (await h.Reads.GetSummaryAsync(Ct)).ThisWeek;
        Assert.Equal((5_000, 1, 0, 0), (thursday.AverageSteps, thursday.StepDays, thursday.GoalDays, thursday.UnderDays));

        // The next Monday: nothing to average yet, and last week's Thursday is over, and under.
        h.Clock.Advance(TimeSpan.FromDays(4));
        var monday = await h.Reads.GetSummaryAsync(Ct);
        Assert.Equal(new DateOnly(2026, 10, 26), monday.Today);
        Assert.Null(monday.ThisWeek.AverageSteps);
        Assert.Equal(0, monday.ThisWeek.StepDays);
        Assert.Equal((4_000, 2, 0, 1),
            (monday.LastWeek.AverageSteps, monday.LastWeek.StepDays, monday.LastWeek.GoalDays, monday.LastWeek.UnderDays));
    }
}
