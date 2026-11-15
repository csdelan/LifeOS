using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// <c>GET /api/browse</c> and <c>GET /api/alignment</c> (BROWSE-1, BROWSE-2, code review CR120):
/// Browse counts every type and pages the one looked at on the server, and the Map and
/// <c>/mcp</c>'s alignment read only what they draw. <c>tests/rules/browse.json</c> and
/// <c>alignment.json</c> hold the folds; these hold the SQL under them.
/// </summary>
public sealed class BrowseEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> NewAsync(ApiTestHost host, string type, string title, object? attrs = null, Guid? parent = null)
    {
        var response = await host.Client.PostAsJsonAsync("/api/subjects", new
        {
            type, title, attrs = attrs ?? new Dictionary<string, string>(),
            parent = parent?.ToString(), relation = parent is null ? null : SubjectRelations.Serves,
        }, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task PostOkAsync(ApiTestHost host, string path, object body)
    {
        var response = await host.Client.PostAsJsonAsync(path, body, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static Task<JsonElement> GetAsync(ApiTestHost host, string path)
        => host.Client.GetFromJsonAsync<JsonElement>(path, Ct);

    private static string[] Titles(JsonElement read)
        => [.. read.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("title").GetString()!)];

    private static int Count(JsonElement read, string type)
        => read.GetProperty("types").EnumerateArray()
            .Where(t => t.GetProperty("type").GetString() == type)
            .Select(t => t.GetProperty("count").GetInt32())
            .SingleOrDefault();

    [Fact]
    public async Task Browse_counts_every_type_and_reads_a_page_of_the_one_looked_at()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        await NewAsync(host, SubjectTypes.Task, "Gamma task", new Dictionary<string, string> { ["due"] = "2026-12-01" });
        await NewAsync(host, SubjectTypes.Task, "Alpha task");
        await NewAsync(host, SubjectTypes.Task, "Beta task");
        var old = await NewAsync(host, SubjectTypes.Task, "Old task");
        await PostOkAsync(host, "/api/archive", new { subject = old.ToString() });
        await NewAsync(host, SubjectTypes.Decision, "Pick a stack");
        await NewAsync(host, SubjectTypes.Person, "Sam");

        var page = await GetAsync(host, "/api/browse?type=Task&limit=2");
        Assert.Equal(["Alpha task", "Beta task"], Titles(page));
        Assert.Equal((3, 3), (page.GetProperty("total").GetInt32(), page.GetProperty("matching").GetInt32()));
        Assert.Equal((3, 1, 1), (Count(page, SubjectTypes.Task), Count(page, SubjectTypes.Decision), Count(page, SubjectTypes.Person)));
        // A Task with a due date gives the type its Due column.
        Assert.Contains("due", page.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));

        // The filters narrow what matches; the type's total and the counts stay.
        var beta = await GetAsync(host, "/api/browse?type=Task&text=BETA");
        Assert.Equal(["Beta task"], Titles(beta));
        Assert.Equal((3, 1), (beta.GetProperty("total").GetInt32(), beta.GetProperty("matching").GetInt32()));

        // Archived items count and list only when asked for.
        var archived = await GetAsync(host, "/api/browse?type=Task&archived=true");
        Assert.Equal(["Alpha task", "Beta task", "Gamma task", "Old task"], Titles(archived));
        Assert.Equal(4, Count(archived, SubjectTypes.Task));

        // All types reads every type Browse lists, and its total is theirs.
        var all = await GetAsync(host, "/api/browse?type=all&limit=1000");
        Assert.Superset(new HashSet<string> { "Alpha task", "Pick a stack", "Sam" }, Titles(all).ToHashSet());
        Assert.Equal(
            all.GetProperty("types").EnumerateArray().Sum(t => t.GetProperty("count").GetInt32()),
            all.GetProperty("total").GetInt32());

        foreach (var bad in new[] { "type=Space", "due=soon", "sort=size", "limit=0", "entered=ever" })
        {
            var refused = await host.Client.GetAsync($"/api/browse?{bad}", Ct);
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, $"{bad}: {refused.StatusCode}");
        }
    }

    [Fact]
    public async Task Alignment_reads_the_tree_types_and_what_an_edge_links_to_them()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var value = await NewAsync(host, SubjectTypes.Value, "Keep my word", new Dictionary<string, string> { ["statement"] = "I keep my word." });
        var goal = await NewAsync(host, SubjectTypes.Goal, "Ship the shed", parent: value);
        var task = await NewAsync(host, SubjectTypes.Task, "Buy lumber", parent: goal);
        var oldGoal = await NewAsync(host, SubjectTypes.Goal, "Old goal", parent: value);
        await PostOkAsync(host, "/api/archive", new { subject = oldGoal.ToString() });
        var person = await NewAsync(host, SubjectTypes.Person, "Sam");

        // A habit that serves the identity statement is drawn; one that serves only the archived
        // goal is drawn only with archived items.
        var stretch = await db.Kernel.CreateTrackedHabitAsync("Stretch", """{"start":"2026-01-01","recurrence":{"freq":"daily"}}""", Ct);
        var floss = await db.Kernel.CreateTrackedHabitAsync("Floss", """{"start":"2026-01-01","recurrence":{"freq":"daily"}}""", Ct);
        await PostOkAsync(host, "/api/link", new { from = stretch.Id.ToString(), relation = SubjectRelations.Serves, to = value.ToString() });
        await PostOkAsync(host, "/api/link", new { from = floss.Id.ToString(), relation = SubjectRelations.Serves, to = oldGoal.ToString() });

        var map = await GetAsync(host, "/api/alignment");
        var ids = map.GetProperty("subjects").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Superset(new HashSet<Guid> { value, goal, task, stretch.Id }, ids);
        Assert.DoesNotContain(person, ids);
        Assert.DoesNotContain(oldGoal, ids);
        Assert.DoesNotContain(floss.Id, ids);
        var edges = map.GetProperty("edges").EnumerateArray()
            .Select(e => (e.GetProperty("fromId").GetGuid(), e.GetProperty("toId").GetGuid())).ToHashSet();
        Assert.Superset(new HashSet<(Guid, Guid)> { (goal, value), (task, goal), (stretch.Id, value) }, edges);
        Assert.All(edges, e => Assert.True(ids.Contains(e.Item1) && ids.Contains(e.Item2), $"{e} reaches past what's drawn"));

        var withArchived = await GetAsync(host, "/api/alignment?includeArchived=true");
        var all = withArchived.GetProperty("subjects").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Superset(new HashSet<Guid> { oldGoal, floss.Id }, all);
        Assert.DoesNotContain(person, all);
    }
}
