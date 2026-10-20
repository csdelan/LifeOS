using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// Earning in the UI over HTTP (rewards plan R6): the type defaults a create form
/// pre-fills, <c>POST /api/bounty</c> (the twin of <c>bsk bounty</c>), the bounty on list
/// and detail reads, and a habit's status and "Ready to establish" progress on
/// <c>GET /api/habits</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BountyEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private Task<ApiTestHost> StartAsync() => ApiTestHost.StartAsync(postgres.ConnectionString, Ct);

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<string> RefusedAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/bounty", body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return json.GetProperty("error").GetString() ?? "";
    }

    private static Task<JsonElement> GetAsync(HttpClient client, string path)
        => client.GetFromJsonAsync<JsonElement>(path, Ct);

    private static async Task<Guid> NewAsync(HttpClient client, string type)
        => (await PostAsync(client, "/api/subjects", new { type, title = Unique($"priced {type}") }))
            .GetProperty("id").GetGuid();

    private static Dictionary<string, long> Amounts(JsonElement amounts)
        => amounts.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64());

    private static JsonElement Line(JsonElement lines, string trigger)
        => lines.EnumerateArray().Single(l => l.GetProperty("trigger").GetString() == trigger);

    [Fact]
    public async Task The_defaults_list_every_earning_type_and_its_triggers_in_order()
    {
        await using var host = await StartAsync();

        var defaults = await GetAsync(host.Client, "/api/bounty/defaults");

        var byType = defaults.EnumerateArray().ToDictionary(
            t => t.GetProperty("type").GetString()!, t => t.GetProperty("lines"));
        Assert.Equal(
            [SubjectTypes.Goal, SubjectTypes.Habit, SubjectTypes.Project, SubjectTypes.Task],
            byType.Keys.Order());
        var task = Assert.Single(byType[SubjectTypes.Task].EnumerateArray());
        Assert.Equal(BountyTriggers.Completed, task.GetProperty("trigger").GetString());
        Assert.Equal(
            new Dictionary<string, long> { [Currencies.Coins] = 2, [Currencies.Freedom] = 10, [Currencies.Xp] = 10 },
            Amounts(task.GetProperty("amounts")));
        Assert.False(task.GetProperty("isCustom").GetBoolean());
        Assert.Equal(
            ["followed", "streak_7", "streak_30", "streak_100", "established"],
            byType[SubjectTypes.Habit].EnumerateArray().Select(l => l.GetProperty("trigger").GetString()));
    }

    [Fact]
    public async Task Pricing_an_item_shows_on_its_reads_and_is_what_completing_pays()
    {
        await using var host = await StartAsync();
        var task = await NewAsync(host.Client, SubjectTypes.Task);

        // Every currency named: the whole price, as the web's editor sends it.
        var priced = await PostAsync(host.Client, "/api/bounty", new
        {
            subject = task.ToString(),
            amounts = new Dictionary<string, long>
            {
                [Currencies.Coins] = 0, [Currencies.Freedom] = 20, [Currencies.Gems] = 0,
                [Currencies.Karma] = 3, [Currencies.Xp] = 15, [Currencies.Resolve] = 0
            }
        });
        var expected = new Dictionary<string, long>
        {
            [Currencies.Freedom] = 20, [Currencies.Karma] = 3, [Currencies.Xp] = 15
        };
        var line = Line(priced.GetProperty("lines"), BountyTriggers.Completed);
        Assert.Equal(expected, Amounts(line.GetProperty("amounts")));
        Assert.True(line.GetProperty("isCustom").GetBoolean());
        Assert.Equal(2, Amounts(line.GetProperty("default"))[Currencies.Coins]);

        var item = await GetAsync(host.Client, $"/api/subjects/{task}/list-item");
        Assert.Equal(expected, Amounts(item.GetProperty("bounty")));
        var listed = (await GetAsync(host.Client, "/api/subjects?type=Task")).EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == task);
        Assert.Equal(expected, Amounts(listed.GetProperty("bounty")));
        var detail = await GetAsync(host.Client, $"/api/subjects/{task}");
        Assert.True(Line(detail.GetProperty("bounty"), BountyTriggers.Completed).GetProperty("isCustom").GetBoolean());

        var completed = await PostAsync(host.Client, "/api/status", new { subject = task.ToString(), status = "Completed" });
        var award = Assert.Single(completed.GetProperty("awards").EnumerateArray());
        Assert.Equal(expected, Amounts(award.GetProperty("amounts")));
    }

    [Fact]
    public async Task Nothing_pays_nothing_and_clear_puts_the_default_back()
    {
        await using var host = await StartAsync();
        var project = await NewAsync(host.Client, SubjectTypes.Project);

        var nothing = await PostAsync(host.Client, "/api/bounty", new { subject = project.ToString(), nothing = true });
        var line = Line(nothing.GetProperty("lines"), BountyTriggers.Completed);
        Assert.Empty(line.GetProperty("amounts").EnumerateObject());
        Assert.True(line.GetProperty("isCustom").GetBoolean());

        var cleared = await PostAsync(host.Client, "/api/bounty", new { subject = project.ToString(), clear = true });
        line = Line(cleared.GetProperty("lines"), BountyTriggers.Completed);
        Assert.False(line.GetProperty("isCustom").GetBoolean());
        Assert.Equal(50, Amounts(line.GetProperty("amounts"))[Currencies.Xp]);

        // The list read carries no bounty for a type that doesn't earn.
        var idea = await NewAsync(host.Client, SubjectTypes.Idea);
        var ideaItem = await GetAsync(host.Client, $"/api/subjects/{idea}/list-item");
        Assert.Equal(JsonValueKind.Null, ideaItem.GetProperty("bounty").ValueKind);
    }

    [Fact]
    public async Task A_bounty_the_kernel_refuses_is_a_400_with_its_reason()
    {
        await using var host = await StartAsync();
        var habit = await NewAsync(host.Client, SubjectTypes.Habit);
        var idea = await NewAsync(host.Client, SubjectTypes.Idea);

        Assert.Contains("Say which trigger",
            await RefusedAsync(host.Client, new { subject = habit.ToString(), amounts = new { xp = 5 } }));
        Assert.Contains("half of followed",
            await RefusedAsync(host.Client, new { subject = habit.ToString(), trigger = "partial", amounts = new { xp = 5 } }));
        Assert.Contains("Unknown currency 'gold'",
            await RefusedAsync(host.Client, new { subject = habit.ToString(), trigger = "followed", amounts = new { gold = 5 } }));
        Assert.Contains("only",
            await RefusedAsync(host.Client, new { subject = idea.ToString(), amounts = new { xp = 5 } }));
        Assert.Contains("one of amounts, nothing or clear",
            await RefusedAsync(host.Client, new { subject = habit.ToString(), trigger = "followed", nothing = true, clear = true }));

        var streak = await PostAsync(host.Client, "/api/bounty", new
        {
            subject = habit.ToString(), trigger = "streak_7", amounts = new { resolve = 2 }
        });
        // Setting a currency starts from what the trigger pays now, its default.
        Assert.Equal(
            new Dictionary<string, long>
            {
                [Currencies.Coins] = 5, [Currencies.Freedom] = 15, [Currencies.Xp] = 25, [Currencies.Resolve] = 2
            },
            Amounts(Line(streak.GetProperty("lines"), "streak_7").GetProperty("amounts")));
    }

    [Fact]
    public async Task The_habits_read_carries_status_bounty_and_ready_to_establish()
    {
        await using var host = await StartAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var kernel = new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, Unique("ready habit"),
            JsonSerializer.Serialize(new
            {
                start = today.AddDays(-3).ToString("yyyy-MM-dd"),
                recurrence = new { freq = "daily" },
                establish_target = new { min_adherence = 0.5, window_days = 2 }
            }),
            cancellationToken: Ct);
        await PostAsync(host.Client, "/api/adhere", new
        {
            habit = habit.Id.ToString(), state = "followed", on = today.AddDays(-1).ToString("yyyy-MM-dd")
        });

        var row = await HabitAsync(host.Client, habit.Id);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("status").ValueKind);
        Assert.Equal(5, Amounts(row.GetProperty("bounty"))[Currencies.Xp]);
        var progress = row.GetProperty("establishment");
        Assert.True(progress.GetProperty("targetMet").GetBoolean());
        Assert.Equal(2, progress.GetProperty("windowDays").GetInt32());
        Assert.Equal(0.5m, progress.GetProperty("minAdherence").GetDecimal());
        Assert.Equal(1m, progress.GetProperty("adherence").GetDecimal());

        // Confirming is an ordinary status change: it pays and graduates.
        var established = await PostAsync(host.Client, "/api/status", new { subject = habit.Id.ToString(), status = "Established" });
        Assert.Contains(established.GetProperty("awards").EnumerateArray(),
            a => a.GetProperty("trigger").GetString() == BountyTriggers.Established);

        row = await HabitAsync(host.Client, habit.Id);
        Assert.Equal("Established", row.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("establishment").ValueKind);
    }

    private static async Task<JsonElement> HabitAsync(HttpClient client, Guid id)
        => (await GetAsync(client, "/api/habits")).EnumerateArray()
            .Single(h => h.GetProperty("id").GetGuid() == id);
}
