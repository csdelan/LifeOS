using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>POST /api/recur</c> (recurring-tasks plan RT1): the web's way to set a subject's
/// recurrence, the twin of <c>bsk recur</c>. It stores the object the expander reads, and a
/// recurrence sent through <c>POST /api/subjects</c> as text is stored as the object too.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RecurEndpointTests(PostgresFixture postgres)
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

    private async Task<string?> StoredAsync(Guid id)
    {
        await using var c = new NpgsqlConnection(postgres.ConnectionString);
        await c.OpenAsync(Ct);
        return await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT CASE WHEN jsonb_typeof(attributes->'recurrence') = 'object'
                        THEN (attributes->'recurrence')::text END
            FROM bsk.subject WHERE id = @id;
            """,
            new { id }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Recur_sets_checks_and_clears_a_recurrence()
    {
        await using var host = await StartAsync();
        var habit = (await PostAsync(host.Client, "/api/subjects", new { type = "Habit", title = Unique("stretch") }))
            .GetProperty("id").GetGuid();

        var set = await PostAsync(host.Client, "/api/recur", new
        {
            subject = habit.ToString(),
            recurrence = new { freq = "weekly", on = new[] { "fri", "mon" } }
        });
        Assert.Equal("""{"freq":"weekly","on":["mon","fri"]}""", set.GetProperty("recurrence").GetString());
        Assert.Equal("""{"on": ["mon", "fri"], "freq": "weekly"}""", await StoredAsync(habit));

        await PostAsync(host.Client, "/api/recur", new
        {
            subject = habit.ToString(),
            recurrence = new { freq = "monthly", on = new[] { "last" } }
        });
        Assert.Equal("""{"on": "last", "freq": "monthly"}""", await StoredAsync(habit));

        var refused = await host.Client.PostAsJsonAsync("/api/recur", new
        {
            subject = habit.ToString(),
            recurrence = new { freq = "weekly", on = new[] { "funday" } }
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var cleared = await PostAsync(host.Client, "/api/recur", new { subject = habit.ToString() });
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("recurrence").ValueKind);
        Assert.Null(await StoredAsync(habit));
    }

    [Fact]
    public async Task A_recurrence_sent_with_a_new_subject_is_stored_as_the_object()
    {
        await using var host = await StartAsync();

        var created = await PostAsync(host.Client, "/api/subjects", new
        {
            type = "Appointment",
            title = Unique("standup"),
            attrs = new Dictionary<string, string> { ["recurrence"] = """{"freq":"daily"}""" }
        });

        Assert.Equal("""{"freq": "daily"}""", await StoredAsync(created.GetProperty("id").GetGuid()));
    }
}
