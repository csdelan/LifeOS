using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using LifeOs.Api.Hosting;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The HTTP write door against a real database. The SPA names subjects by id
/// (a uuid), while the Application services resolve urn / short id / title — so
/// these tests send exactly what the web client sends and check the edge lands.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WriteEndpointsTests(PostgresFixture postgres)
{
    private const string Issuer = "https://test.supabase.co/auth/v1";
    private const string Audience = "authenticated";
    private const string Secret = "unit-test-hmac-secret-32bytes-min!";
    private const string Allowed = "owner@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Goal_created_under_a_value_named_by_id_is_linked_to_it()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N");

        var value = await PostSubjectAsync(host.Client, new
        {
            type = SubjectTypes.Value,
            title = $"value {ns}",
            attrs = new Dictionary<string, string> { ["statement"] = "I am someone who ships." },
        });

        var goal = await PostSubjectAsync(host.Client, new
        {
            type = SubjectTypes.Goal,
            title = $"goal {ns}",
            parent = value.Id.ToString(),
            relation = SubjectRelations.Serves,
            attrs = new Dictionary<string, string> { ["target_date"] = "2027-01-01" },
        });

        Assert.True(await EdgeExistsAsync(goal.Id, SubjectRelations.Serves, value.Id));
    }

    [Fact]
    public async Task Habit_created_under_a_goal_named_by_id_serves_it()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N");

        var goal = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Goal, title = $"goal {ns}" });
        var habit = await PostSubjectAsync(host.Client, new
        {
            type = SubjectTypes.Habit,
            title = $"habit {ns}",
            parent = goal.Id.ToString(),
            relation = SubjectRelations.Serves,
        });

        Assert.True(await EdgeExistsAsync(habit.Id, SubjectRelations.Serves, goal.Id));
    }

    [Fact]
    public async Task Unknown_parent_id_is_a_404_with_a_readable_error()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/subjects", new
        {
            type = SubjectTypes.Goal,
            title = $"orphan {Guid.NewGuid():N}",
            parent = Guid.NewGuid().ToString(),
        }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task A_captured_note_shows_its_tags_and_comes_back_from_resolved()
    {
        // The web Inbox's round trip: capture → tag (read back on the inbox row) →
        // dismiss (now under /inbox/resolved, tags intact) → flag (Back to Inbox).
        await using var host = await StartAsync();
        var text = $"a note to come back to {Guid.NewGuid():N}";

        await PostOkAsync(host.Client, "/api/capture", new { text });
        var row = await FindAsync(host.Client, "/api/inbox", text);
        var id = row.GetProperty("itemId").GetString()!;

        await PostOkAsync(host.Client, "/api/tag", new { item = id, add = new[] { "Errand", "home" } });
        row = await FindAsync(host.Client, "/api/inbox", text);
        Assert.Equal(["errand", "home"], Tags(row));

        await PostOkAsync(host.Client, "/api/dismiss", new { item = id });
        Assert.Null(await FindOrNullAsync(host.Client, "/api/inbox", text));
        var resolved = await FindAsync(host.Client, "/api/inbox/resolved", text);
        Assert.Equal("dismissed", resolved.GetProperty("resolution").GetString());
        Assert.False(resolved.GetProperty("subjectStatusTerminal").GetBoolean());
        Assert.Equal(["errand", "home"], Tags(resolved));

        await PostOkAsync(host.Client, "/api/flag", new { item = id });
        Assert.NotNull(await FindOrNullAsync(host.Client, "/api/inbox", text));
        Assert.Null(await FindOrNullAsync(host.Client, "/api/inbox/resolved", text));
    }

    [Fact]
    public async Task A_subject_renamed_by_id_reads_back_with_its_new_title_and_old_urn()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Goal, title = $"get fit {ns}" });

        var response = await host.Client.PostAsJsonAsync(
            "/api/rename", new { subject = goal.Id.ToString(), title = $"  run a   marathon {ns} " }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal($"run a marathon {ns}", renamed.GetProperty("title").GetString());
        Assert.Equal($"get fit {ns}", renamed.GetProperty("previousTitle").GetString());
        Assert.True(renamed.GetProperty("changed").GetBoolean());

        var detail = await host.Client.GetFromJsonAsync<JsonElement>($"/api/subjects/{goal.Id}", Ct);
        Assert.Equal($"run a marathon {ns}", detail.GetProperty("title").GetString());
        Assert.Equal(goal.Urn, detail.GetProperty("urn").GetString());
    }

    [Fact]
    public async Task Renaming_a_problem_onto_another_problems_title_is_a_409()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N");
        await PostSubjectAsync(host.Client, new { type = SubjectTypes.Problem, title = $"sleep {ns}" });
        var other = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Problem, title = $"energy {ns}" });

        var response = await host.Client.PostAsJsonAsync(
            "/api/rename", new { subject = other.Id.ToString(), title = $"sleep {ns}" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task A_renamed_tag_reads_back_under_its_new_name()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N")[..8];
        var task = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Task, title = $"tagged {ns}" });
        await PostOkAsync(host.Client, "/api/tag", new { item = task.Id.ToString(), add = new[] { $"old-{ns}" } });

        var response = await host.Client.PostAsJsonAsync("/api/rename-tag", new { from = $"old-{ns}", to = $"New-{ns}" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal($"new-{ns}", renamed.GetProperty("to").GetString());
        Assert.Equal(1, renamed.GetProperty("renamed").GetInt32());

        var tags = await host.Client.GetFromJsonAsync<string[]>($"/api/subjects/{task.Id}/tags", Ct) ?? [];
        Assert.Equal([$"new-{ns}"], tags);

        // A tag nothing carries is a readable 400, not a silent success.
        var missing = await host.Client.PostAsJsonAsync("/api/rename-tag", new { from = $"old-{ns}", to = "x" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Timer_actions_on_a_task_named_by_id_show_up_in_the_timer_list()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N");
        var task = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Task, title = $"timed {ns}" });

        var started = await PostTimerAsync(host.Client, task.Id, "start");
        Assert.Equal("running", started.GetProperty("timer").GetProperty("state").GetString());
        Assert.Equal(task.Id, started.GetProperty("timer").GetProperty("subjectId").GetGuid());

        var timers = await host.Client.GetFromJsonAsync<JsonElement>("/api/timers", Ct);
        var row = timers.EnumerateArray().Single(t => t.GetProperty("subjectId").GetGuid() == task.Id);
        Assert.Equal("running", row.GetProperty("state").GetString());
        Assert.Equal($"timed {ns}", row.GetProperty("subjectTitle").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("runningSince").ValueKind);

        var done = await PostTimerAsync(host.Client, task.Id, "complete");
        Assert.True(done.GetProperty("completed").GetBoolean());
        Assert.Equal("stopped", done.GetProperty("timer").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Starting_a_second_timer_reports_the_one_it_paused()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N");
        var first = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Task, title = $"first {ns}" });
        var second = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Task, title = $"second {ns}" });

        await PostTimerAsync(host.Client, first.Id, "start");
        var switched = await PostTimerAsync(host.Client, second.Id, "start");

        var paused = switched.GetProperty("paused").EnumerateArray().Select(p => p.GetProperty("id").GetGuid());
        Assert.Contains(first.Id, paused);

        await PostTimerAsync(host.Client, second.Id, "stop");
        await PostTimerAsync(host.Client, first.Id, "stop");
    }

    [Fact]
    public async Task A_timer_action_that_does_not_fit_is_a_400_with_the_reason()
    {
        await using var host = await StartAsync();
        var task = await PostSubjectAsync(host.Client, new { type = SubjectTypes.Task, title = $"idle {Guid.NewGuid():N}" });

        var response = await host.Client.PostAsJsonAsync("/api/timer", new { task = task.Id.ToString(), action = "pause" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains("not running", error.GetProperty("error").GetString());
    }

    private static async Task<JsonElement> PostTimerAsync(HttpClient client, Guid task, string action)
    {
        var response = await client.PostAsJsonAsync("/api/timer", new { task = task.ToString(), action }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/timer {action} → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static string[] Tags(JsonElement row)
        => row.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();

    private static async Task PostOkAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
    }

    private static async Task<JsonElement> FindAsync(HttpClient client, string path, string content)
    {
        var row = await FindOrNullAsync(client, path, content);
        Assert.True(row.HasValue, $"No row in {path} with content '{content}'.");
        return row.Value;
    }

    private static async Task<JsonElement?> FindOrNullAsync(HttpClient client, string path, string content)
    {
        // The test database is shared, so ask for the widest page of resolved rows.
        var url = path.EndsWith("/resolved", StringComparison.Ordinal) ? $"{path}?limit=200" : path;
        var rows = await client.GetFromJsonAsync<JsonElement>(url, Ct);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("eventContent").GetString() == content)
            {
                return row;
            }
        }

        return null;
    }

    private sealed record Created(Guid Id, string Urn);

    private static async Task<Created> PostSubjectAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/subjects", body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/subjects → {(int)response.StatusCode}: {text}");
        var json = JsonDocument.Parse(text).RootElement;
        return new Created(json.GetProperty("id").GetGuid(), json.GetProperty("urn").GetString()!);
    }

    private async Task<bool> EdgeExistsAsync(Guid from, string relation, Guid to)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM bsk.subject_relation
                WHERE from_subject = @from AND relation = @relation AND to_subject = @to);
            """,
            new { from, relation, to }, cancellationToken: Ct));
    }

    private async Task<TestHost> StartAsync()
    {
        // Program.Main sets this before building the host; the read endpoints need it
        // to map snake_case columns (item_id -> ItemId).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = "LifeOs.Api.WriteEndpointsTests",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Issuer"] = Issuer,
            ["Auth:Audience"] = Audience,
            ["Auth:JwtSecret"] = Secret,
            ["ALLOWED_EMAILS"] = Allowed,
            ["ConnectionStrings:Owner"] = postgres.ConnectionString,
            ["ConnectionStrings:Reader"] = BskReader.ConnectionStringFrom(postgres.ConnectionString),
            ["VITE_SUPABASE_URL"] = "https://placeholder.supabase.co",
            ["VITE_SUPABASE_ANON_KEY"] = "placeholder-anon-key",
        });

        ApiHost.ConfigureServices(builder);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        ApiHost.ConfigurePipeline(app);
        await app.StartAsync(Ct);

        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single().TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        return new TestHost(app, client);
    }

    private static string Token()
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["email"] = Allowed,
                ["email_verified"] = true,
                ["role"] = "authenticated",
            },
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256),
        });

    private sealed class TestHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
