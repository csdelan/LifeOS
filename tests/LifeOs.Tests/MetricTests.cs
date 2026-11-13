using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Measurements;
using LifeOs.Application.Subjects;
using LifeOs.Cli.Commands;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// MEASURE-1 to MEASURE-3, D20 (measurements plan M1, M3, §4.1–§4.8, migrations 0050 and 0051):
/// metrics, their normal ranges and their readings. A metric is a <c>Metric</c> subject read through <c>bsk.v_metric</c>, with attributes
/// only <see cref="MetricService"/> writes; a reading is a versioned <c>measurement</c> event read
/// through <c>bsk.v_metric_reading</c>, newest version per id, voided to delete. Each test runs in
/// a database of its own (<see cref="DomainTestDatabase"/>, home zone UTC), since a metric's name is
/// unique.
/// </summary>
public sealed class MetricTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Weight_comes_from_the_starter_list_and_is_renamed_archived_and_restored()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<MetricService>();

        var weight = await service.AddAsync(" weight ", target: 185m, cancellationToken: Ct);
        Assert.Equal(("weight", "lb", 1, 185m, "lower", "weight"),
            (weight.Name, weight.Unit, weight.Decimals, weight.Target, weight.Better, weight.Catalog));

        // A name is unique among the metrics not archived, in any case.
        Assert.Contains("already have a metric called 'weight'",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync("Weight", cancellationToken: Ct))).Message);

        // A rename keeps the starter-list key, so the app still knows it's weight.
        var renamed = await service.SetAsync("WEIGHT", new MetricChange(Name: "Body weight", Target: 180m), Ct);
        Assert.Equal(("Body weight", 180m, "weight"), (renamed.Name, renamed.Target, renamed.Catalog));
        Assert.Equal(renamed.Id, (await service.ResolveAsync(renamed.Urn[^6..], Ct)).Id);
        var cleared = await service.SetAsync("Body weight", new MetricChange(ClearTarget: true), Ct);
        Assert.Null(cleared.Target);

        var neck = await service.AddAsync("Neck", "in", cancellationToken: Ct);
        Assert.Equal(("in", 1, null, null, null, null), (neck.Unit, neck.Decimals, neck.Better, neck.Catalog, neck.Low, neck.High));

        var archived = await service.ArchiveAsync("Body weight", Ct);
        Assert.Equal((true, await db.TodayAsync()), (archived.Archived, archived.ArchivedOn));
        Assert.Equal(["Neck"], (await service.ListAsync(cancellationToken: Ct)).Select(m => m.Name));

        // An archived metric's name is free again; restoring it while it's taken is refused,
        // whichever route restores it.
        var other = await service.AddAsync("Body weight", "kg", cancellationToken: Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(archived.Id.ToString(), Ct));
        Assert.Contains("already have a metric called 'Body weight'",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => db.Get<ArchiveService>().RestoreAsync(archived.Urn, Ct))).Message);
        await service.SetAsync(other.Id.ToString(), new MetricChange(Name: "Weight (kg)"), Ct);
        Assert.False((await service.RestoreAsync(archived.Id.ToString(), Ct)).Archived);
    }

    [Fact]
    public async Task Bad_metrics_are_refused_and_bsk_set_leaves_their_attributes_alone()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<MetricService>();

        Assert.Contains("Give the metric a unit",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Neck", cancellationToken: Ct))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("  ", "lb", cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("two\nlines", "lb", cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Neck", "in", decimals: 4, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Neck", "in", better: "down", cancellationToken: Ct));
        Assert.Contains("The target takes at most 1 decimal place",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Weight", target: 185.25m, cancellationToken: Ct))).Message);
        Assert.Empty(await service.ListAsync(true, Ct));

        var weight = await service.AddAsync("Weight", target: 185.5m, cancellationToken: Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Weight", new MetricChange(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Weight", new MetricChange(Target: 180m, ClearTarget: true), Ct));
        // Fewer decimal places mustn't strand the target.
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Weight", new MetricChange(Decimals: 0), Ct));

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => db.Get<AttributeService>().SetAsync(
            weight.Urn, [new AttributeAssignment("target", "170")], Ct));
        Assert.Contains("bsk metric set", refused.Message);

        // The unit can change until there's a reading in it.
        Assert.Equal("kg", (await service.SetAsync("Weight", new MetricChange(Unit: "kg"), Ct)).Unit);
        await service.SetAsync("Weight", new MetricChange(Unit: "lb"), Ct);
        await db.Get<MetricReadingService>().LogAsync("Weight", 192.4m, cancellationToken: Ct);
        Assert.Contains("Weight has 1 reading in lb, so its unit stays",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync("Weight", new MetricChange(Unit: "kg"), Ct))).Message);
        Assert.Equal(185.5m, (await service.ResolveAsync("Weight", Ct)).Target);
    }

    [Fact]
    public async Task A_reading_is_logged_now_or_backdated_edited_and_deleted_as_versions()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.Get<MetricService>().AddAsync("Weight", target: 185m, cancellationToken: Ct);
        var readings = db.Get<MetricReadingService>();

        var now = await readings.LogAsync("weight", 192.4m, note: "  after a run ", cancellationToken: Ct);
        Assert.Equal((192.4m, "lb", "after a run", 1, await db.TodayAsync()),
            (now.Value, now.Unit, now.Note, now.Versions, now.TakenOn));
        Assert.True(DateTimeOffset.UtcNow - now.TakenAt < TimeSpan.FromMinutes(1));

        // A past weigh-in keeps its own time and day.
        var lastWeek = DateTimeOffset.UtcNow.AddDays(-7);
        var backdated = await readings.LogAsync("Weight", 194m, lastWeek, cancellationToken: Ct);
        Assert.Equal(DateOnly.FromDateTime(lastWeek.UtcDateTime), backdated.TakenOn);
        Assert.Equal(lastWeek.ToUnixTimeMilliseconds(), backdated.TakenAt.ToUnixTimeMilliseconds());

        Assert.Contains("not in the future",
            (await Assert.ThrowsAsync<ArgumentException>(() => readings.LogAsync("Weight", 190m, DateTimeOffset.UtcNow.AddHours(1), cancellationToken: Ct))).Message);
        Assert.Contains("Weight takes at most 1 decimal place",
            (await Assert.ThrowsAsync<ArgumentException>(() => readings.LogAsync("Weight", 190.25m, cancellationToken: Ct))).Message);
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => readings.LogAsync("Waist", 34m, cancellationToken: Ct));

        // An edit is a newer version; changing nothing writes nothing.
        var edited = await readings.EditAsync(backdated.Id.ToString()[..8], new ReadingEdit(Value: 193.8m, Note: "scale on carpet"), Ct);
        Assert.Equal((193.8m, "scale on carpet", 2, backdated.TakenAt), (edited.Value, edited.Note, edited.Versions, edited.TakenAt));
        Assert.Equal(2, (await readings.EditAsync(edited.Id.ToString(), new ReadingEdit(Value: 193.8m), Ct)).Versions);
        var moved = await readings.EditAsync(edited.Id.ToString(), new ReadingEdit(TakenAt: lastWeek.AddDays(-1), Note: ""), Ct);
        Assert.Equal((DateOnly.FromDateTime(lastWeek.UtcDateTime).AddDays(-1), null, 3), (moved.TakenOn, moved.Note, moved.Versions));

        var list = await readings.ListAsync("Weight", cancellationToken: Ct);
        Assert.Equal([now.Id, moved.Id], list.Readings.Select(r => r.Id));

        await readings.DeleteAsync(moved.Id.ToString(), Ct);
        Assert.Equal([now.Id], (await readings.ListAsync(cancellationToken: Ct)).Readings.Select(r => r.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readings.ResolveAsync(moved.Id.ToString(), Ct));

        // Every version is still in the stream, the voided one included, each concerning the metric.
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var versions = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT count(*) FROM bsk.event e
            JOIN bsk.subject_event se ON se.event_id = e.id AND se.relation = 'concerns'
            JOIN bsk.subject s ON s.id = se.subject_id AND s.type = 'Metric'
            WHERE e.kind = 'measurement' AND e.payload->'metric_reading'->>'id' = @id
            """, new { id = moved.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(4, versions);

        // An archived metric takes no new readings, but its old ones can still be changed.
        await db.Get<MetricService>().ArchiveAsync("Weight", Ct);
        Assert.Contains("archived",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => readings.LogAsync("Weight", 191m, cancellationToken: Ct))).Message);
        Assert.Equal(192.2m, (await readings.EditAsync(now.Id.ToString(), new ReadingEdit(Value: 192.2m), Ct)).Value);
    }

    [Fact]
    public async Task Bsk_measure_reads_a_date_alone_as_noon_and_takes_the_food_logs_times()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var readings = db.Get<MetricReadingService>();
        var today = await db.TodayAsync();
        DateTimeOffset At(DateOnly day, int hour, int minute = 0)
            => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

        Assert.Equal(At(new DateOnly(2026, 11, 1), 12), await MeasureCommand.ParseAtAsync("2026-11-01", readings, Ct));
        Assert.Equal(At(today.AddDays(-1), 12), await MeasureCommand.ParseAtAsync(" Yesterday ", readings, Ct));
        Assert.Equal(At(new DateOnly(2026, 11, 1), 7, 10), await MeasureCommand.ParseAtAsync("2026-11-01 07:10", readings, Ct));
        Assert.Equal(At(today.AddDays(-1), 7), await MeasureCommand.ParseAtAsync("yesterday 7:00", readings, Ct));
        Assert.Null(await MeasureCommand.ParseAtAsync(null, readings, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => MeasureCommand.ParseAtAsync("this morning", readings, Ct));

        // --and takes name=value, split at the last '='.
        Assert.Equal(new ReadingEntry("Diastolic BP", 78m), MeasureCommand.ParseEntry(" Diastolic BP = 78 "));
        Assert.Contains("--and takes name=value",
            Assert.Throws<ArgumentException>(() => MeasureCommand.ParseEntry("Diastolic BP 78")).Message);
    }

    [Fact]
    public async Task Starter_metrics_bring_their_normal_range_which_can_change_and_flags_readings_outside_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<MetricService>();

        // Blood pressure comes with the American Heart Association's normal; body fat with none.
        var systolic = await service.AddAsync("systolic bp", cancellationToken: Ct);
        Assert.Equal(("mmHg", 0, "lower", "bp_systolic", 90m, 119m),
            (systolic.Unit, systolic.Decimals, systolic.Better, systolic.Catalog, systolic.Low, systolic.High));
        Assert.Equal("90–119 mmHg", systolic.RangeText());
        var bodyFat = await service.AddAsync("Body fat", cancellationToken: Ct);
        Assert.Equal(("%", 1, "lower", null, null), (bodyFat.Unit, bodyFat.Decimals, bodyFat.Better, bodyFat.Low, bodyFat.High));
        var vo2 = await service.AddAsync("VO2 max (lab)", cancellationToken: Ct);
        Assert.Equal(("mL/kg/min", 1, "higher", "vo2max_lab"), (vo2.Unit, vo2.Decimals, vo2.Better, vo2.Catalog));

        // The bounds are normal, as on a lab report; past them is high or low.
        Assert.Equal(new string?[] { null, null, MetricFlags.High, MetricFlags.Low },
            new[] { 119m, 90m, 120m, 89m }.Select(systolic.Flag));

        // One end alone; then the other; then one cleared.
        var waist = await service.AddAsync("Waist", high: 40m, cancellationToken: Ct);
        Assert.Equal(("in", null, 40m, "up to 40.0 in"), (waist.Unit, waist.Low, waist.High, waist.RangeText()));
        var both = await service.SetAsync("Waist", new MetricChange(Low: 30m), Ct);
        Assert.Equal((30m, 40m), (both.Low, both.High));
        var noHigh = await service.SetAsync("Waist", new MetricChange(ClearHigh: true), Ct);
        Assert.Equal((30m, null, "at least 30.0 in"), (noHigh.Low, noHigh.High, noHigh.RangeText()));
        Assert.Null(noHigh.Flag(45m));

        // Refusals, in words, with nothing written: both ends upside down, or one new end below
        // the kept one.
        Assert.Contains("The normal range's low end (35.0) is above its high end (32.0)",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Waist", new MetricChange(Low: 35m, High: 32m), Ct))).Message);
        Assert.Contains("low end (30.0) is above its high end (25.0)",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Waist", new MetricChange(High: 25m), Ct))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Waist", new MetricChange(Low: 30m, ClearLow: true), Ct));
        Assert.Contains("The normal range's low end takes a whole number",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Systolic BP", new MetricChange(Low: 90.5m), Ct))).Message);
        Assert.Contains("The normal range's low end (120) is above its high end (110)",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Grip", "lb", decimals: 0, low: 120m, high: 110m, cancellationToken: Ct))).Message);
        // Fewer decimal places mustn't strand an end of the range.
        await service.SetAsync("Waist", new MetricChange(Low: 30.5m), Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Waist", new MetricChange(Decimals: 0), Ct));
        var kept = await service.ResolveAsync("Waist", Ct);
        Assert.Equal((30.5m, (decimal?)null), (kept.Low, kept.High));

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => db.Get<AttributeService>().SetAsync(
            systolic.Urn, [new AttributeAssignment("high", "140")], Ct));
        Assert.Contains("bsk metric set", refused.Message);
    }

    [Fact]
    public async Task Readings_logged_together_share_their_time_and_note_and_are_written_all_or_none()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<MetricService>();
        await service.AddAsync("Systolic BP", cancellationToken: Ct);
        await service.AddAsync("Diastolic BP", cancellationToken: Ct);
        await service.AddAsync("Weight", cancellationToken: Ct);
        var readings = db.Get<MetricReadingService>();
        var at = DateTimeOffset.UtcNow.AddHours(-2);

        var pair = await readings.LogManyAsync(
            [new ReadingEntry("systolic bp", 122m), new ReadingEntry("Diastolic BP", 78m)], at, " after coffee ", Ct);
        Assert.Equal([(122m, "mmHg", "after coffee"), (78m, "mmHg", "after coffee")], pair.Select(r => (r.Value, r.Unit, r.Note)));
        Assert.Equal(at.ToUnixTimeMilliseconds(), pair[0].TakenAt.ToUnixTimeMilliseconds());
        Assert.Equal(pair[0].TakenAt, pair[1].TakenAt);

        // Any refusal writes none of them.
        async Task Refused<T>(IReadOnlyList<ReadingEntry> entries, string words) where T : Exception
        {
            var refusal = await Assert.ThrowsAsync<T>(() => readings.LogManyAsync(entries, cancellationToken: Ct));
            Assert.Contains(words, refusal.Message);
            Assert.Equal(2, (await readings.ListAsync(cancellationToken: Ct)).Readings.Count);
        }

        await Refused<ArgumentException>([new("Systolic BP", 130m), new("Diastolic BP", 80.5m)], "Diastolic BP takes a whole number");
        await Refused<ArgumentException>([new("Systolic BP", 130m), new("systolic bp", 131m)], "Systolic BP is given twice");
        await Refused<ArgumentException>([], "Give at least one reading");
        await Refused<SubjectNotFoundException>([new("Systolic BP", 130m), new("Pulse", 64m)], "Pulse");
        await Refused<ArgumentException>(
            Enumerable.Range(0, MetricReadingService.MaxTogether + 1).Select(_ => new ReadingEntry("Weight", 190m)).ToList(),
            "Log up to 50 readings at once");
        await service.ArchiveAsync("Diastolic BP", Ct);
        await Refused<InvalidOperationException>([new("Systolic BP", 130m), new("Diastolic BP", 80m)], "'Diastolic BP' is archived");
    }

    [Fact]
    public async Task A_metrics_read_has_the_first_weigh_in_of_each_day_its_trend_and_goal_date()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await db.Get<MetricService>().AddAsync("Weight", target: 185m, cancellationToken: Ct);
        var readings = db.Get<MetricReadingService>();
        var today = await db.TodayAsync();
        var morning = await readings.InstantAtAsync(today.AddDays(-2).ToDateTime(new TimeOnly(7, 0)), Ct);

        await readings.LogAsync("Weight", 196m, morning.AddHours(12), cancellationToken: Ct);
        var first = await readings.LogAsync("Weight", 195m, morning, cancellationToken: Ct);
        var yesterday = await readings.LogAsync("Weight", 194m, morning.AddDays(1), cancellationToken: Ct);
        await readings.LogAsync("Weight", 199m, morning.AddDays(-40), cancellationToken: Ct);

        var read = await db.Get<MetricReads>().ReadAsync("weight", days: 7, Ct);
        Assert.Equal(today.AddDays(-6), read.From);
        // The evening weigh-in is a reading, but the day's value is the morning's.
        Assert.Equal(3, read.Readings.Count);
        Assert.Equal([(today.AddDays(-2), 195m, first.Id), (today.AddDays(-1), 194m, yesterday.Id)],
            read.Days.Select(d => (d.Day, d.Value, d.ReadingId)));

        // The trend runs over all history, outside the range too: 199, then 40 days on almost
        // all the way to 195 (195.0005), then a fifth of the way to 194.
        Assert.Equal(194.8m, read.Days[^1].Trend);

        // The rate reads the newest 28 days (195, 194): 7 lb a week down toward 185.
        Assert.Equal(GoalStatuses.Heading, read.Goal.Status);
        Assert.Equal((-7m, 2), (read.Goal.RatePerWeek, read.Goal.BasisDays));
        Assert.NotNull(read.Goal.ReachedBy);

        var summaries = await db.Get<MetricReads>().ListAsync(cancellationToken: Ct);
        var weight = Assert.Single(summaries);
        Assert.Equal((4, 194m), (weight.Readings, weight.Last!.Value));
    }

    [Fact]
    public async Task The_api_adds_weight_logs_readings_and_reads_the_goal()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var metric = await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/metrics", new { name = "Weight", target = 185 }, Ct));
        Assert.Equal(("lb", 1, "lower", "weight", 0),
            (metric.GetProperty("unit").GetString(), metric.GetProperty("decimals").GetInt32(),
                metric.GetProperty("better").GetString(), metric.GetProperty("catalog").GetString(),
                metric.GetProperty("readings").GetInt32()));
        var id = metric.GetProperty("id").GetString();

        var twoDaysAgo = DateTimeOffset.UtcNow.AddDays(-2);
        var logged = await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/readings",
            new { metric = id, value = 194.6, takenAt = twoDaysAgo, note = "first" }, Ct));
        var readingId = logged.GetProperty("reading").GetProperty("id").GetString();
        Assert.Equal(194.6m, logged.GetProperty("reading").GetProperty("value").GetDecimal());
        await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/readings", new { metric = "weight", value = 193.9 }, Ct));

        var detail = await JsonAsync(await host.Client.GetAsync("/api/measurements/metrics/Weight?days=30", Ct));
        Assert.Equal(2, detail.GetProperty("readings").GetArrayLength());
        Assert.Equal(2, detail.GetProperty("days").GetArrayLength());
        Assert.Equal(2, detail.GetProperty("metric").GetProperty("readings").GetInt32());
        var goal = detail.GetProperty("goal");
        Assert.Equal(("heading", 185m, 2), (goal.GetProperty("status").GetString(), goal.GetProperty("target").GetDecimal(),
            goal.GetProperty("basis").GetProperty("days").GetInt32()));
        Assert.True(DateOnly.TryParseExact(goal.GetProperty("reachedBy").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _));

        // Refusals are 400s in words, with nothing written.
        var future = await host.Client.PostAsJsonAsync("/api/measurements/readings",
            new { metric = "Weight", value = 190, takenAt = DateTimeOffset.UtcNow.AddDays(1) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Contains("not in the future", await future.Content.ReadAsStringAsync(Ct));
        var noValue = await host.Client.PostAsJsonAsync("/api/measurements/readings", new { metric = "Weight" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noValue.StatusCode);

        var edited = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/measurements/readings/{readingId}", new { value = 194.4 }, Ct));
        Assert.Equal((194.4m, 2), (edited.GetProperty("reading").GetProperty("value").GetDecimal(),
            edited.GetProperty("reading").GetProperty("versions").GetInt32()));
        var deleted = await JsonAsync(await host.Client.PostAsync($"/api/measurements/readings/{readingId}/delete", null, Ct));
        Assert.Equal(JsonValueKind.Null, deleted.GetProperty("reading").ValueKind);

        var set = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/measurements/metrics/{id}", new { clearTarget = true }, Ct));
        Assert.Equal(JsonValueKind.Null, set.GetProperty("target").ValueKind);
        var list = await JsonAsync(await host.Client.GetAsync("/api/measurements/metrics", Ct));
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal((1, 193.9m), (row.GetProperty("readings").GetInt32(), row.GetProperty("lastReading").GetProperty("value").GetDecimal()));
        var noGoal = await JsonAsync(await host.Client.GetAsync($"/api/measurements/metrics/{id}", Ct));
        Assert.Equal("no_target", noGoal.GetProperty("goal").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, noGoal.GetProperty("from").ValueKind);
    }

    [Fact]
    public async Task The_api_keeps_a_normal_range_flags_readings_and_logs_blood_pressure_together()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var systolic = await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/metrics", new { name = "Systolic BP" }, Ct));
        Assert.Equal((90m, 119m, "bp_systolic"), (systolic.GetProperty("low").GetDecimal(), systolic.GetProperty("high").GetDecimal(),
            systolic.GetProperty("catalog").GetString()));
        await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/metrics", new { name = "Diastolic BP" }, Ct));
        var own = await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/metrics",
            new { name = "Resting HR", unit = "bpm", decimals = 0, better = "lower", low = 50, high = 70 }, Ct));
        Assert.Equal((50m, 70m), (own.GetProperty("low").GetDecimal(), own.GetProperty("high").GetDecimal()));

        var pair = await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/readings/batch",
            new { readings = new[] { new { metric = "Systolic BP", value = 132 }, new { metric = "Diastolic BP", value = 78 } }, note = "after coffee" }, Ct));
        var logged = pair.GetProperty("readings").EnumerateArray().ToList();
        Assert.Equal(new (string?, string?)[] { ("high", "after coffee"), (null, "after coffee") },
            logged.Select(r => (r.GetProperty("flag").GetString(), r.GetProperty("note").GetString())));
        Assert.Equal(logged[0].GetProperty("takenAt").GetDateTimeOffset(), logged[1].GetProperty("takenAt").GetDateTimeOffset());

        // A refusal of one writes none; a value left out is a 400 too.
        var bad = await host.Client.PostAsJsonAsync("/api/measurements/readings/batch",
            new { readings = new[] { new { metric = "Systolic BP", value = 130m }, new { metric = "Diastolic BP", value = 80.5m } } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Diastolic BP takes a whole number", await bad.Content.ReadAsStringAsync(Ct));
        var noValue = await host.Client.PostAsJsonAsync("/api/measurements/readings/batch",
            new { readings = new object[] { new { metric = "Systolic BP", value = 130 }, new { metric = "Diastolic BP" } } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noValue.StatusCode);
        Assert.Contains("Give Diastolic BP's value", await noValue.Content.ReadAsStringAsync(Ct));

        var detail = await JsonAsync(await host.Client.GetAsync("/api/measurements/metrics/Systolic BP", Ct));
        var only = Assert.Single(detail.GetProperty("readings").EnumerateArray());
        Assert.Equal("high", only.GetProperty("flag").GetString());
        var list = await JsonAsync(await host.Client.GetAsync("/api/measurements/metrics", Ct));
        var row = list.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Systolic BP");
        Assert.Equal((1, "high"), (row.GetProperty("readings").GetInt32(), row.GetProperty("lastReading").GetProperty("flag").GetString()));

        // A wider range re-flags the readings it has; clearing an end leaves the other.
        var id = systolic.GetProperty("id").GetString();
        var wider = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/measurements/metrics/{id}", new { high = 139 }, Ct));
        Assert.Equal(139m, wider.GetProperty("high").GetDecimal());
        Assert.Equal(JsonValueKind.Null, wider.GetProperty("lastReading").GetProperty("flag").ValueKind);
        var lowCleared = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/measurements/metrics/{id}", new { clearLow = true }, Ct));
        Assert.Equal((JsonValueKind.Null, 139m), (lowCleared.GetProperty("low").ValueKind, lowCleared.GetProperty("high").GetDecimal()));
        var upsideDown = await host.Client.PostAsJsonAsync($"/api/measurements/metrics/{id}", new { low = 150 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, upsideDown.StatusCode);
        Assert.Contains("above its high end", await upsideDown.Content.ReadAsStringAsync(Ct));
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
