using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Contracts;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Reads cut to what a screen shows (code review CR121, fifth review R16): the shell's running
/// timer and the Tasks page's timers, not every timer ever; a detail's own habit; a series' newest
/// repetitions with their count; and the home's shots from their first, newest and picked photos,
/// not every photo of every space.
/// </summary>
public sealed class BoundedReadsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static Task<JsonElement> GetAsync(HttpClient client, string path)
        => client.GetFromJsonAsync<JsonElement>(path, Ct);

    private static async Task<Guid> NewTaskAsync(HttpClient client, string title)
        => (await PostAsync(client, "/api/subjects", new { type = SubjectTypes.Task, title })).GetProperty("id").GetGuid();

    private static Guid[] Ids(JsonElement timers)
        => [.. timers.EnumerateArray().Select(t => t.GetProperty("subjectId").GetGuid())];

    [Fact]
    public async Task Timers_read_as_the_running_ones_or_those_of_a_status_and_one_Tasks_on_its_own()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var c = host.Client;
        var write = await NewTaskAsync(c, "Write the report");
        var read = await NewTaskAsync(c, "Read the brief");
        var file = await NewTaskAsync(c, "File the taxes");
        var never = await NewTaskAsync(c, "Never timed");

        // Starting one pauses the other: the report runs again, the brief is paused, the taxes timed and done.
        await PostAsync(c, "/api/timer", new { task = write.ToString(), action = "start" });
        await PostAsync(c, "/api/timer", new { task = read.ToString(), action = "start" });
        await PostAsync(c, "/api/timer", new { task = file.ToString(), action = "start" });
        await PostAsync(c, "/api/timer", new { task = file.ToString(), action = "complete" });
        await PostAsync(c, "/api/timer", new { task = write.ToString(), action = "resume" });

        Assert.Equal([write, read], Ids(await GetAsync(c, "/api/timers?active=true")));
        Assert.Equal([write, read], Ids(await GetAsync(c, "/api/timers?status=open")));
        Assert.Equal([file], Ids(await GetAsync(c, "/api/timers?status=closed")));
        Assert.Equal([file], Ids(await GetAsync(c, "/api/timers?status=Completed")));
        Assert.Equal([write, read, file], Ids(await GetAsync(c, "/api/timers")));

        var done = await GetAsync(c, $"/api/subjects/{file}/timer");
        Assert.Equal(("stopped", file), (done.GetProperty("state").GetString(), done.GetProperty("subjectId").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/subjects/{never}/timer", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_habit_reads_on_its_own_archived_or_not_and_nothing_else_does()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var c = host.Client;
        var stretch = await db.Kernel.CreateTrackedHabitAsync("Stretch", """{"start":"2026-01-01","recurrence":{"freq":"daily"}}""", Ct);
        var task = await NewTaskAsync(c, "Buy a mat");

        var habit = await GetAsync(c, $"/api/habits/{stretch.Id}");
        Assert.Equal(("Stretch", false), (habit.GetProperty("name").GetString(), habit.GetProperty("archived").GetBoolean()));

        await PostAsync(c, "/api/archive", new { subject = stretch.Id.ToString() });
        Assert.True((await GetAsync(c, $"/api/habits/{stretch.Id}")).GetProperty("archived").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/habits/{task}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_series_reads_its_newest_repetitions_and_how_many_there_are()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var c = host.Client;
        var today = await db.TodayAsync();
        var series = await NewTaskAsync(c, "Water the plants");
        var set = await PostAsync(c, "/api/recur", new
        {
            subject = series.ToString(),
            recurrence = new { freq = "interval", every = 1, unit = "days", from = "completion" },
            start = today.ToString("yyyy-MM-dd"),
        });
        // Each one done makes the next.
        var next = Assert.Single(set.GetProperty("made").EnumerateArray()).GetProperty("id").GetGuid();
        for (var i = 0; i < 2; i++)
        {
            await PostAsync(c, "/api/status", new { subject = next.ToString(), status = "Completed" });
            var history = await GetAsync(c, $"/api/tasks/series/{series}");
            next = history.GetProperty("repetitions").EnumerateArray()
                .First(r => !r.GetProperty("closed").GetBoolean()).GetProperty("id").GetGuid();
        }

        var all = await GetAsync(c, $"/api/tasks/series/{series}");
        var made = all.GetProperty("repetitions").GetArrayLength();
        Assert.True(made >= 3, $"made {made}");
        Assert.Equal(made, all.GetProperty("repetitionCount").GetInt32());

        var newest = await GetAsync(c, $"/api/tasks/series/{series}?limit=1");
        Assert.Equal(
            all.GetProperty("repetitions")[0].GetProperty("id").GetGuid(),
            Assert.Single(newest.GetProperty("repetitions").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(made, newest.GetProperty("repetitionCount").GetInt32());
        Assert.Equal(made, (await GetAsync(c, $"/api/tasks/series/{series}?limit=50")).GetProperty("repetitionCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/tasks/series/{series}?limit=0", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_home_reads_each_shots_first_newest_and_picked_photos_and_counts_the_rest()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var photos = db.Get<SpacePhotoService>();
        var today = await db.TodayAsync();
        var office = await db.Get<SpaceService>().AddAsync("Office", 5, 5, Ct);
        var first = await photos.AddAsync("Office", FoodPhotoTests.Jpeg(), today.AddDays(-20), cancellationToken: Ct);
        var between = await photos.AddAsync("Office", FoodPhotoTests.Jpeg(), today.AddDays(-15), cancellationToken: Ct);
        var picked = await photos.AddAsync("Office", FoodPhotoTests.Jpeg(), today.AddDays(-10), cancellationToken: Ct);
        var newest = await photos.AddAsync("Office", FoodPhotoTests.Jpeg(), today, cancellationToken: Ct);
        await photos.SetBaselineAsync(picked.Id.ToString(), Ct);

        // The read brings the ends and the pick, and counts all four.
        var ends = await db.Get<ISpaceRepository>().GetShotEndsAsync([picked.Id], Ct);
        Assert.Equal([first.Id, picked.Id, newest.Id], ends.Photos.Select(p => p.Id));
        Assert.DoesNotContain(between.Id, ends.Photos.Select(p => p.Id));
        Assert.Equal(4, ends.Counts[(office.Id, SpaceShot.MainId.ToLowerInvariant())]);

        // The home shows the shot as the fold over every photo did.
        var home = await TestReaders.On(db.ConnectionString).Get<EnvironmentReader>().GetHomeAsync(Ct);
        var shot = Assert.Single(Assert.Single(home.Spaces, s => s.Id == office.Id).Shots);
        Assert.Equal((4, picked.Id, newest.Id), (shot.Photos, shot.Baseline!.Id, shot.Newest!.Id));

        // With no pick, the first is the baseline again.
        await photos.RemoveAsync(picked.Id.ToString(), Ct);
        shot = Assert.Single(Assert.Single((await TestReaders.On(db.ConnectionString).Get<EnvironmentReader>().GetHomeAsync(Ct)).Spaces, s => s.Id == office.Id).Shots);
        Assert.Equal((3, first.Id, newest.Id), (shot.Photos, shot.Baseline!.Id, shot.Newest!.Id));
    }
}
