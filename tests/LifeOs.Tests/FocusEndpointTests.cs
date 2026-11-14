using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// <c>GET /api/focus</c> (code review CR106): the Focus page, worked out by the API's
/// <c>FocusReader</c>, as <c>life_snapshot</c> works out today, where the web had put it together
/// from five reads of its own.
/// </summary>
public sealed class FocusEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> NewAsync(ApiTestHost host, object body)
    {
        var response = await host.Client.PostAsJsonAsync("/api/subjects", body, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task PostOkAsync(ApiTestHost host, string path, object body)
    {
        var response = await host.Client.PostAsJsonAsync(path, body, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static Task StatusAsync(ApiTestHost host, Guid id, string status)
        => PostOkAsync(host, "/api/status", new { subject = id.ToString(), status });

    private static Guid[] Ids(JsonElement rows, string key = "id")
        => [.. rows.EnumerateArray().Select(r => r.GetProperty(key).GetGuid())];

    private static JsonElement Habit(JsonElement rows, Guid id)
        => Assert.Single(rows.EnumerateArray(), r => r.GetProperty("habitId").GetGuid() == id);

    [Fact]
    public async Task Focus_leads_with_the_nearest_goal_and_lists_todays_tasks_habits_and_inbox()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var today = await db.TodayAsync();
        string Day(int offset) => today.AddDays(offset).ToString("yyyy-MM-dd");

        // Two Active goals: the one with the nearer target date leads.
        var later = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Run a marathon", attrs = new Dictionary<string, string> { ["target_date"] = Day(300) } });
        var sooner = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Ship the shed", attrs = new Dictionary<string, string> { ["target_date"] = Day(60) } });
        await StatusAsync(host, later, "Active");
        await StatusAsync(host, sooner, "Active");
        var frame = await NewAsync(host, new { type = SubjectTypes.Project, title = "Shed frame", parent = sooner.ToString(), relation = SubjectRelations.ResultsIn });
        var training = await NewAsync(host, new { type = SubjectTypes.Project, title = "Training block", parent = later.ToString(), relation = SubjectRelations.ResultsIn });
        var roof = await NewAsync(host, new { type = SubjectTypes.Project, title = "Shed roof", parent = sooner.ToString(), relation = SubjectRelations.ResultsIn });
        await StatusAsync(host, frame, "Active");
        await StatusAsync(host, training, "Active");

        var overdue = await NewAsync(host, new { type = SubjectTypes.Task, title = "Buy lumber", attrs = new Dictionary<string, string> { ["due"] = Day(-2) } });
        var scheduled = await NewAsync(host, new { type = SubjectTypes.Task, title = "Pour footings", attrs = new Dictionary<string, string> { ["scheduled"] = Day(0) } });
        var underway = await NewAsync(host, new { type = SubjectTypes.Task, title = "Draw plans", attrs = new Dictionary<string, string> { ["due"] = Day(5) } });
        await StatusAsync(host, underway, "In progress");
        var done = await NewAsync(host, new { type = SubjectTypes.Task, title = "Pick a site", attrs = new Dictionary<string, string> { ["due"] = Day(-1) } });
        await StatusAsync(host, done, "Completed");
        await PostOkAsync(host, "/api/capture", new { text = "Call the lumber yard" });

        // A daily habit is due today; one kept on tomorrow's weekday only isn't, but is still shown,
        // to log a session done anyway; a graduated one is maintained.
        var tomorrow = today.AddDays(1).DayOfWeek.ToString()[..3].ToLowerInvariant();
        var daily = await db.Kernel.CreateTrackedHabitAsync("Stretch", JsonSerializer.Serialize(new { start = Day(-3), recurrence = new { freq = "daily" } }), Ct);
        var weekly = await db.Kernel.CreateTrackedHabitAsync("Long run", JsonSerializer.Serialize(new { start = Day(-3), recurrence = new { freq = "weekly", on = new[] { tomorrow } } }), Ct);
        var graduated = await db.Kernel.CreateTrackedHabitAsync("Floss", JsonSerializer.Serialize(new { start = Day(-3), recurrence = new { freq = "daily" } }), Ct);
        await StatusAsync(host, graduated.Id, "Established");

        var focus = await host.Client.GetFromJsonAsync<JsonElement>("/api/focus", Ct);

        var primary = focus.GetProperty("primaryFocus");
        Assert.Equal(("Ship the shed", sooner.ToString(), "goal"), (
            primary.GetProperty("title").GetString(), primary.GetProperty("subjectId").GetString(), primary.GetProperty("kind").GetString()));
        // Only the Active project that results in the goal; the roof has no status yet.
        Assert.Equal([frame], Ids(focus.GetProperty("objectives")));
        Assert.Equal([frame], Ids(focus.GetProperty("objectives"), "subjectId"));
        Assert.Equal([overdue, scheduled], Ids(focus.GetProperty("dueAndOverdue")));
        Assert.Equal([underway], Ids(focus.GetProperty("nextActions")));
        Assert.Equal(1, focus.GetProperty("inboxCount").GetInt32());
        Assert.Equal([later, sooner], Ids(focus.GetProperty("activeGoals")).Order());
        Assert.DoesNotContain(roof, Ids(focus.GetProperty("activeProjects")));

        var todayHabits = focus.GetProperty("todayHabits");
        Assert.True(Habit(todayHabits, daily.Id).GetProperty("listed").GetBoolean());
        var offDay = Habit(todayHabits, weekly.Id);
        Assert.Equal((false, "unrecorded", Day(0)), (
            offDay.GetProperty("listed").GetBoolean(), offDay.GetProperty("state").GetString(), offDay.GetProperty("occurrenceDate").GetString()));
        Assert.DoesNotContain(graduated.Id, Ids(todayHabits, "habitId"));
        Assert.False(Habit(focus.GetProperty("maintainingHabits"), graduated.Id).GetProperty("listed").GetBoolean());
    }

    [Fact]
    public async Task With_no_active_goal_focus_says_so_and_offers_any_active_projects()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var project = await NewAsync(host, new { type = SubjectTypes.Project, title = "Tidy the garage" });
        await StatusAsync(host, project, "Active");

        var focus = await host.Client.GetFromJsonAsync<JsonElement>("/api/focus", Ct);

        var primary = focus.GetProperty("primaryFocus");
        Assert.Equal(("No active goal yet", ""), (primary.GetProperty("title").GetString(), primary.GetProperty("subjectId").GetString()));
        Assert.Equal([project], Ids(focus.GetProperty("objectives")));
    }
}
