using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Repeating Tasks over HTTP (TASKS-3, D16, recurring-tasks plan RT3): <c>POST /api/recur</c>
/// with a start and the Task-only shapes, the series fields on list items and the detail, the
/// series kept out of the Task lists, <c>GET /api/tasks/series</c> and its history, and
/// <c>POST /api/tasks/settle</c>. The host reads today in UTC, as the database does here, and
/// each test stops its series when it's done.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TaskEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Weekday(DateOnly date) => date.DayOfWeek.ToString()[..3].ToLowerInvariant();

    private Task<ApiTestHost> StartAsync() => ApiTestHost.StartAsync(
        postgres.ConnectionString, Ct,
        new Dictionary<string, string?> { [HomeZone.ConfigurationKey] = PostgresFixture.HabitViewZone });

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"GET {path} → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<Guid> NewTaskAsync(HttpClient client, string title, object? attrs = null)
        => (await PostAsync(client, "/api/subjects", new { type = "Task", title, attrs })).GetProperty("id").GetGuid();

    private static JsonElement? Find(JsonElement list, Guid id)
        => list.EnumerateArray().Where(e => e.GetProperty("id").GetGuid() == id).Cast<JsonElement?>().FirstOrDefault();

    [Fact]
    public async Task A_repeating_Task_reads_as_its_repetitions_with_the_series_alongside()
    {
        await using var host = await StartAsync();
        var c = host.Client;
        var series = await NewTaskAsync(c, Unique("Take out trash"));
        await PostAsync(c, "/api/bounty", new
        {
            subject = series.ToString(), trigger = "completed", amounts = new Dictionary<string, long> { ["freedom"] = 0, ["xp"] = 4 }
        });

        var set = await PostAsync(c, "/api/recur", new
        {
            subject = series.ToString(),
            recurrence = new { freq = "weekly", on = new[] { Weekday(Today) } },
            start = Iso(Today)
        });
        var made = Assert.Single(set.GetProperty("made").EnumerateArray());
        var repetition = made.GetProperty("id").GetGuid();

        // The Task list shows the repetition, not the series.
        var tasks = await GetAsync(c, "/api/subjects?type=Task");
        Assert.Null(Find(tasks, series));
        var row = Find(tasks, repetition)!.Value;
        Assert.False(row.GetProperty("isSeries").GetBoolean());
        Assert.Equal(series, row.GetProperty("seriesId").GetGuid());
        Assert.Equal(Iso(Today), row.GetProperty("occurrence").GetString());
        Assert.Equal(Iso(Today), row.GetProperty("due").GetString());
        Assert.Contains("\"weekly\"", row.GetProperty("repeat").GetString());
        Assert.Equal(4, row.GetProperty("bounty").GetProperty("xp").GetInt64());

        // Asked for, the series is there, marked.
        var withSeries = await GetAsync(c, "/api/subjects?type=Task&includeSeries=true");
        var seriesRow = Find(withSeries, series)!.Value;
        Assert.True(seriesRow.GetProperty("isSeries").GetBoolean());
        Assert.Equal(JsonValueKind.Null, seriesRow.GetProperty("seriesId").ValueKind);
        Assert.Contains("\"weekly\"", seriesRow.GetProperty("repeat").GetString());
        Assert.Null(Find(await GetAsync(c, "/api/subjects"), series));

        var listItem = await GetAsync(c, $"/api/subjects/{repetition}/list-item");
        Assert.Equal(series, listItem.GetProperty("seriesId").GetGuid());

        // The repetition's detail: the series, its counts, and the series' price.
        var detail = await GetAsync(c, $"/api/subjects/{repetition}");
        var repeating = detail.GetProperty("repeating");
        Assert.False(repeating.GetProperty("isSeries").GetBoolean());
        Assert.Equal(series, repeating.GetProperty("seriesId").GetGuid());
        Assert.Equal(Iso(Today), repeating.GetProperty("occurrence").GetString());
        Assert.Equal(Iso(Today), repeating.GetProperty("start").GetString());
        Assert.Equal(1, repeating.GetProperty("openCount").GetInt64());
        Assert.True(detail.GetProperty("bountyFromSeries").GetBoolean());
        var completedLine = detail.GetProperty("bounty").EnumerateArray().Single(l => l.GetProperty("trigger").GetString() == "completed");
        Assert.Equal(4, completedLine.GetProperty("amounts").GetProperty("xp").GetInt64());

        var seriesDetail = await GetAsync(c, $"/api/subjects/{series}");
        Assert.True(seriesDetail.GetProperty("repeating").GetProperty("isSeries").GetBoolean());
        Assert.False(seriesDetail.GetProperty("bountyFromSeries").GetBoolean());

        // Done: it pays the series' price and next week's is made.
        var done = await PostAsync(c, "/api/status", new { subject = repetition.ToString(), status = "Completed" });
        Assert.Equal(4, done.GetProperty("awards").EnumerateArray().Single().GetProperty("amounts").GetProperty("xp").GetInt64());

        var history = await GetAsync(c, $"/api/tasks/series/{series}");
        Assert.Equal(1, history.GetProperty("series").GetProperty("completedCount").GetInt64());
        Assert.Equal(Iso(Today), history.GetProperty("series").GetProperty("lastCompletedOn").GetString());
        Assert.Equal(Iso(Today.AddDays(7)), history.GetProperty("series").GetProperty("nextDue").GetString());
        var repetitions = history.GetProperty("repetitions").EnumerateArray().ToList();
        Assert.Equal([Iso(Today.AddDays(7)), Iso(Today)], repetitions.Select(r => r.GetProperty("occurrence").GetString()));
        Assert.True(repetitions[1].GetProperty("closed").GetBoolean());
        Assert.Equal("Completed", repetitions[1].GetProperty("status").GetString());

        var all = await GetAsync(c, "/api/tasks/series");
        Assert.Equal(1, Find(all, series)!.Value.GetProperty("openCount").GetInt64());
        Assert.Equal(4, Find(all, series)!.Value.GetProperty("bounty").GetProperty("xp").GetInt64());

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/tasks/series/{repetition}", Ct)).StatusCode);

        await PostAsync(c, "/api/status", new { subject = series.ToString(), status = "Cancelled" });
        Assert.True((await GetAsync(c, $"/api/tasks/series/{series}")).GetProperty("series").GetProperty("stopped").GetBoolean());
    }

    [Fact]
    public async Task Recur_takes_the_Task_only_shapes_and_a_start()
    {
        await using var host = await StartAsync();
        var c = host.Client;

        var weekly = await NewTaskAsync(c, Unique("Clean bathroom"));
        var each = await PostAsync(c, "/api/recur", new
        {
            subject = weekly.ToString(), recurrence = new { freq = "each", period = "week" }
        });
        Assert.Equal("""{"freq":"each","period":"week"}""", each.GetProperty("recurrence").GetString());
        var week = Find(await GetAsync(c, "/api/subjects?type=Task"), Assert.Single(each.GetProperty("made").EnumerateArray()).GetProperty("id").GetGuid())!.Value;
        var monday = TaskSeries.WeekOf(Today);
        Assert.Equal(Iso(monday), week.GetProperty("scheduled").GetString());
        Assert.Equal(Iso(monday.AddDays(6)), week.GetProperty("due").GetString());

        var fridge = await NewTaskAsync(c, Unique("Clean fridge"));
        var after = await PostAsync(c, "/api/recur", new
        {
            subject = fridge.ToString(),
            recurrence = new { freq = "interval", every = 30, unit = "days", from = "completion" },
            start = Iso(Today.AddDays(3))
        });
        var first = Assert.Single(after.GetProperty("made").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(Iso(Today.AddDays(3)), (await GetAsync(c, $"/api/subjects/{first}/list-item")).GetProperty("due").GetString());

        // A cue isn't a Task pattern, and a series is never completed itself.
        var cue = await c.PostAsJsonAsync("/api/recur", new
        {
            subject = (await NewTaskAsync(c, Unique("Cue"))).ToString(), recurrence = new { freq = "trigger", cue = "after dinner" }
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, cue.StatusCode);
        var completing = await c.PostAsJsonAsync("/api/status", new { subject = fridge.ToString(), status = "Completed" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, completing.StatusCode);

        // Once each week is for Tasks.
        var habit = (await PostAsync(c, "/api/subjects", new { type = "Habit", title = Unique("Floss") })).GetProperty("id").GetGuid();
        var refused = await c.PostAsJsonAsync("/api/recur", new
        {
            subject = habit.ToString(), recurrence = new { freq = "each", period = "week" }
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        foreach (var id in new[] { weekly, fridge })
        {
            await PostAsync(c, "/api/status", new { subject = id.ToString(), status = "Cancelled" });
        }
    }

    [Fact]
    public async Task A_Task_made_with_a_pattern_starts_repeating_and_settle_answers()
    {
        await using var host = await StartAsync();
        var c = host.Client;

        var series = await NewTaskAsync(c, Unique("Feed fish"), new Dictionary<string, string>
        {
            ["recurrence"] = """{"freq":"daily"}""",
            ["start"] = Iso(Today.AddDays(-2))
        });
        var history = await GetAsync(c, $"/api/tasks/series/{series}");
        Assert.Equal(
            [Iso(Today), Iso(Today.AddDays(-1)), Iso(Today.AddDays(-2))],
            history.GetProperty("repetitions").EnumerateArray().Select(r => r.GetProperty("occurrence").GetString()));

        var cue = await c.PostAsJsonAsync("/api/subjects", new
        {
            type = "Task", title = Unique("Cue task"), attrs = new Dictionary<string, string> { ["recurrence"] = """{"freq":"trigger","cue":"after dinner"}""" }
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, cue.StatusCode);

        var settle = await PostAsync(c, "/api/tasks/settle", new { });
        Assert.True(settle.GetProperty("series").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.Array, settle.GetProperty("made").ValueKind);

        await PostAsync(c, "/api/status", new { subject = series.ToString(), status = "Cancelled" });
    }
}
