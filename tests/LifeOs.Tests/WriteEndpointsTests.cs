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
