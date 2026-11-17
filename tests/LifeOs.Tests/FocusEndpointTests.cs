using System.Net;
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
        Assert.Equal(new[] { later, sooner }.Order(), Ids(focus.GetProperty("activeGoals")).Order());
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
    public async Task Focus_reads_only_the_goals_projects_and_tasks_it_shows()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var today = await db.TodayAsync();
        var goal = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Ship the shed" });
        var project = await NewAsync(host, new { type = SubjectTypes.Project, title = "Shed frame" });
        var task = await NewAsync(host, new { type = SubjectTypes.Task, title = "Buy lumber" });

        // Open by the status filter, since none has a status, but nothing Focus shows (CR119).
        await NewAsync(host, new { type = SubjectTypes.Person, title = "Sam" });
        await NewAsync(host, new { type = SubjectTypes.Area, title = "Home" });
        await NewAsync(host, new { type = SubjectTypes.Value, title = "Finisher", attrs = new Dictionary<string, string> { ["statement"] = "I finish what I start." } });
        await NewAsync(host, new { type = SubjectTypes.Appointment, title = "Dentist", attrs = new Dictionary<string, string> { ["date"] = today.AddDays(30).ToString("yyyy-MM-dd") } });

        var day = await TestReaders.On(db.ConnectionString).Get<LifeOs.Api.Read.FocusReader>().DayAsync(today, cancellationToken: Ct);

        Assert.Equal(new[] { goal, project, task }.Order(), day.Open.Select(s => s.Id).Order());
    }

    [Fact]
    public async Task The_goal_chosen_in_a_review_leads_until_it_ends_and_then_focus_suggests_one()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var today = await db.TodayAsync();
        string Day(int offset) => today.AddDays(offset).ToString("yyyy-MM-dd");
        var later = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Run a marathon", attrs = new Dictionary<string, string> { ["target_date"] = Day(300) } });
        var sooner = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Ship the shed", attrs = new Dictionary<string, string> { ["target_date"] = Day(60) } });
        await StatusAsync(host, later, "Active");
        await StatusAsync(host, sooner, "Active");
        var training = await NewAsync(host, new { type = SubjectTypes.Project, title = "Training block", parent = later.ToString(), relation = SubjectRelations.ResultsIn });
        await StatusAsync(host, training, "Active");
        var review = await host.Client.PostAsJsonAsync("/api/reviews/open", new { kind = "daily" }, Ct);
        var reviewRow = await review.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var reviewId = reviewRow.GetProperty("id").GetGuid();

        // Nothing chosen: the nearest target date leads, as a suggestion.
        var suggested = (await host.Client.GetFromJsonAsync<JsonElement>("/api/focus", Ct)).GetProperty("primaryFocus");
        Assert.Equal((sooner.ToString(), false), (suggested.GetProperty("subjectId").GetString(), suggested.GetProperty("chosen").GetBoolean()));

        // Chosen in the review: the marathon leads, with its project, whatever its date.
        var choice = await host.Client.PostAsJsonAsync("/api/focus/primary", new { goalId = later, reviewId }, Ct);
        Assert.True(choice.IsSuccessStatusCode, await choice.Content.ReadAsStringAsync(Ct));
        var chosenRow = await choice.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((later, true, true), (chosenRow.GetProperty("goalId").GetGuid(), chosenRow.GetProperty("holds").GetBoolean(), chosenRow.GetProperty("written").GetBoolean()));
        var focus = await host.Client.GetFromJsonAsync<JsonElement>("/api/focus", Ct);
        var chosen = focus.GetProperty("primaryFocus");
        Assert.Equal((later.ToString(), true, reviewRow.GetProperty("title").GetString()), (
            chosen.GetProperty("subjectId").GetString(), chosen.GetProperty("chosen").GetBoolean(), chosen.GetProperty("chosenIn").GetString()));
        Assert.Equal([training], Ids(focus.GetProperty("objectives")));
        Assert.Equal(JsonValueKind.Null, chosen.GetProperty("lapsedTitle").ValueKind);
        var reviews = await host.Client.GetFromJsonAsync<JsonElement>("/api/reviews", Ct);
        Assert.False(reviews.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == reviewId).GetProperty("keepFocus").GetBoolean());

        // The marathon is done: focus suggests the shed again, and names what lapsed.
        await StatusAsync(host, later, "Completed");
        var lapsed = (await host.Client.GetFromJsonAsync<JsonElement>("/api/focus", Ct)).GetProperty("primaryFocus");
        Assert.Equal((sooner.ToString(), false, "Run a marathon", "Completed"), (
            lapsed.GetProperty("subjectId").GetString(), lapsed.GetProperty("chosen").GetBoolean(),
            lapsed.GetProperty("lapsedTitle").GetString(), lapsed.GetProperty("lapsedAs").GetString()));
    }

    [Fact]
    public async Task Only_an_active_goal_can_be_chosen_as_the_focus()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var draft = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Learn Spanish" });

        var refused = await host.Client.PostAsJsonAsync("/api/focus/primary", new { goalId = draft }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Active Goal", await refused.Content.ReadAsStringAsync(Ct));

        var missing = await host.Client.PostAsJsonAsync("/api/focus/primary", new { goalId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Clearing a focus never chosen writes nothing.
        var none = await host.Client.PostAsJsonAsync("/api/focus/primary", new { goalId = (Guid?)null }, Ct);
        var row = await none.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((false, JsonValueKind.Null), (row.GetProperty("written").GetBoolean(), row.GetProperty("chosenAt").ValueKind));
    }

    [Fact]
    public async Task The_focus_goals_sub_goals_are_its_milestones_and_their_projects_its_objectives()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var today = await db.TodayAsync();
        Dictionary<string, string> By(int offset) => new() { ["target_date"] = today.AddDays(offset).ToString("yyyy-MM-dd") };

        // Sub-goals made under their parent, with no relation named: a Goal under a Goal serves it.
        var half = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Run a half-marathon", attrs = By(200) });
        var tenK = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Run a 10K", parent = half.ToString(), attrs = By(90) });
        var fiveK = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Run a 5K", parent = half.ToString(), attrs = By(30) });
        var trail = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Run a trail race", parent = half.ToString(), attrs = By(150) });
        var pace = await NewAsync(host, new { type = SubjectTypes.Goal, title = "Hold a 5:30 pace", parent = tenK.ToString(), attrs = By(60) });
        foreach (var goal in new[] { half, tenK, fiveK, pace })
        {
            await StatusAsync(host, goal, "Active");
        }

        var couch = await NewAsync(host, new { type = SubjectTypes.Project, title = "Couch to 5K", parent = fiveK.ToString() });
        var intervals = await NewAsync(host, new { type = SubjectTypes.Project, title = "Interval sessions", parent = pace.ToString() });
        var shoes = await NewAsync(host, new { type = SubjectTypes.Project, title = "Trail shoes", parent = trail.ToString() });
        var fence = await NewAsync(host, new { type = SubjectTypes.Project, title = "Paint the fence" });
        foreach (var project in new[] { couch, intervals, shoes, fence })
        {
            await StatusAsync(host, project, "Active");
        }

        // The 5K's date is nearest, so it would be the suggestion: Chris chooses the half-marathon.
        await PostOkAsync(host, "/api/focus/primary", new { goalId = half });
        var focus = await host.Client.GetFromJsonAsync<JsonElement>("/api/focus", Ct);

        Assert.Equal(half.ToString(), focus.GetProperty("primaryFocus").GetProperty("subjectId").GetString());
        // Its Active sub-goals, soonest first; the trail race isn't Active, and the pace is the 10K's.
        Assert.Equal([fiveK, tenK], Ids(focus.GetProperty("milestones")));
        Assert.Equal(today.AddDays(30).ToString("yyyy-MM-dd"), focus.GetProperty("milestones")[0].GetProperty("targetDate").GetString());
        // The projects under it at any depth through Active sub-goals; not the New one's, nor the fence.
        Assert.Equal([couch, intervals], Ids(focus.GetProperty("objectives")));

        // A Goal's relations say each sub-goal's status and target date, for its page.
        var relations = await host.Client.GetFromJsonAsync<JsonElement>($"/api/subjects/{half}/relations", Ct);
        var tenKRow = Assert.Single(relations.GetProperty("children").EnumerateArray(), c => c.GetProperty("subjectId").GetGuid() == tenK);
        Assert.Equal("serves", tenKRow.GetProperty("relation").GetString());
        Assert.Equal("Active", tenKRow.GetProperty("status").GetString());
        Assert.Equal(today.AddDays(90).ToString("yyyy-MM-dd"), tenKRow.GetProperty("targetDate").GetString());
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
