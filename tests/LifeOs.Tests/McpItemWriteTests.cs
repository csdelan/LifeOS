using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Api.Mcp;
using LifeOs.Domain;
using ModelContextProtocol.Client;
using Npgsql;
using static LifeOs.Tests.McpWriteTests;

namespace LifeOs.Tests;

/// <summary>
/// The write tools at the <c>full</c> level (agent-access plan §5.2, step 2.3 of §5.6):
/// <c>set_status</c>, <c>update_item</c>, <c>create_item</c>, <c>tag</c> and <c>link</c>. Each
/// writes through the API's own services as the agent's source, says what changed, refuses in
/// words, writes nothing when told what's already so, and leaves a repeating Task's series, a
/// Review and the items of a switched-off area alone.
/// </summary>
public sealed class McpItemWriteTests(PostgresFixture postgres)
{
    private const string Writer = "agent-full-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Set_status_moves_an_item_as_the_agent_pays_as_Chris_would_and_says_it_again_for_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Fix the gate {Guid.NewGuid():N}");
        await using var client = await ConnectAsync(host);

        var started = await CallAsync(client, "set_status", new() { ["item"] = taskUrn, ["status"] = "in progress" });
        var done = await CallAsync(client, "set_status", new() { ["item"] = taskId.ToString(), ["status"] = "Completed" });
        var paid = await AgentLedgerEntriesAsync(db);
        var again = await CallAsync(client, "set_status", new() { ["item"] = taskUrn, ["status"] = "completed" });

        Assert.False(started.TryGetProperty("from", out _));
        Assert.Equal("In progress", started.GetProperty("to").GetString());
        Assert.Equal(("In progress", "Completed"), (done.GetProperty("from").GetString(), done.GetProperty("to").GetString()));
        Assert.Equal("earn", Assert.Single(done.GetProperty("earned").EnumerateArray().ToList()).GetProperty("entry").GetString());
        Assert.True(again.GetProperty("unchanged").GetBoolean());
        Assert.False(again.TryGetProperty("earned", out _));

        await using var owner = new NpgsqlConnection(db.ConnectionString);
        var changes = (await owner.QueryAsync<(string Status, string Source, string Provenance)>(new CommandDefinition(
            """
            SELECT payload->>'status', source_id, provenance FROM bsk.event
            WHERE kind = 'state_change' AND payload->>'subject_id' = @id ORDER BY recorded_at;
            """,
            new { id = taskId.ToString() }, cancellationToken: Ct))).ToList();
        Assert.Equal(
            new[] { ("In progress", $"mcp:{Writer}", Provenances.Declared), ("Completed", $"mcp:{Writer}", Provenances.Declared) },
            changes);
        // It paid as the agent, and saying Completed again paid nothing more.
        Assert.True(paid > 0);
        Assert.Equal(paid, await AgentLedgerEntriesAsync(db));

        var refused = await client.CallToolAsync("set_status", new Dictionary<string, object?> { ["item"] = taskUrn, ["status"] = "Done" }, cancellationToken: Ct);
        Assert.True(refused.IsError);
        Assert.Contains("A Task's status is one of: Not started, In progress", Text(refused), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repeating_tasks_series_and_a_review_stay_Chris_and_an_item_is_named_by_id_or_urn_only()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var title = $"Water the plants {Guid.NewGuid():N}";
        var (seriesId, seriesUrn) = await PostTaskAsync(host, title);
        await PostOkAsync(host, "api/recur", new { subject = seriesUrn, recurrence = new { freq = "weekly", on = new[] { "wed" } } });
        var review = await OpenReviewAsync(host);
        await using var client = await ConnectAsync(host);

        await RefusedAsync(client, "set_status", new() { ["item"] = seriesUrn, ["status"] = "Cancelled" }, "is a repeating Task");
        await RefusedAsync(client, "update_item", new() { ["item"] = seriesUrn, ["due"] = "2026-12-01" }, "is a repeating Task");
        await RefusedAsync(client, "set_status", new() { ["item"] = review.ToString(), ["status"] = "Completed" }, "is a Review, which is Chris's");
        await RefusedAsync(client, "tag", new() { ["item"] = review.ToString(), ["add"] = new[] { "done" } }, "is a Review, which is Chris's");
        await RefusedAsync(client, "set_status", new() { ["item"] = title, ["status"] = "Cancelled" }, "no item has the id or URN");

        // A field its repetitions copy is fine on the series.
        var described = await CallAsync(client, "update_item", new() { ["item"] = seriesId.ToString(), ["description"] = "The ferns too." });
        Assert.Equal("description", Assert.Single(described.GetProperty("changes").EnumerateArray().ToList()).GetProperty("field").GetString());
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        Assert.Equal(0, await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'state_change' AND source_id LIKE 'mcp:%';", cancellationToken: Ct)));
    }

    [Fact]
    public async Task Update_item_writes_only_what_differs_says_each_change_from_and_to_and_refuses_in_words()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var ns = Guid.NewGuid().ToString("N")[..8];
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Paint the shed {ns}");
        var area = await PostSubjectAsync(host, new { type = SubjectTypes.Area, title = $"Home {ns}" });
        var areaUrn = await UrnAsync(db, area);
        await using var client = await ConnectAsync(host);
        var edit = new Dictionary<string, object?>
        {
            ["item"] = taskUrn, ["title"] = $"Paint the shed green {ns}", ["description"] = "Two coats.",
            ["due"] = "2026-12-01", ["priority"] = "high", ["estimated_duration"] = "1h 30m", ["area"] = area.ToString(),
        };

        var changed = await CallAsync(client, "update_item", edit);
        var again = await CallAsync(client, "update_item", edit);
        var cleared = await CallAsync(client, "update_item", new() { ["item"] = taskUrn, ["due"] = "" });

        Assert.Equal(
            new (string, string?, string?)[]
            {
                ("title", $"Paint the shed {ns}", $"Paint the shed green {ns}"), ("description", null, "Two coats."),
                ("due", null, "2026-12-01"), ("priority", null, "High"), ("estimated_duration", null, "1h 30m"), ("area", null, areaUrn),
            },
            Changes(changed));
        Assert.Empty(Changes(again));
        Assert.Equal(new (string, string?, string?)[] { ("due", "2026-12-01", null) }, Changes(cleared));

        // Each change is an attribute_change the agent wrote, from and to.
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        var written = (await owner.QueryAsync<(string Source, string Payload)>(new CommandDefinition(
            """
            SELECT source_id, payload::text FROM bsk.event
            WHERE kind = 'attribute_change' AND payload->>'subject_id' = @id ORDER BY recorded_at;
            """,
            new { id = taskId.ToString() }, cancellationToken: Ct))).ToList();
        Assert.Equal(3, written.Count);
        Assert.All(written, w => Assert.Equal($"mcp:{Writer}", w.Source));
        var due = JsonDocument.Parse(written[^1].Payload).RootElement.GetProperty("attributes").GetProperty("due");
        Assert.Equal("2026-12-01", due.GetProperty("from").GetString());

        await RefusedAsync(client, "update_item", new() { ["item"] = taskUrn }, "Send a new title, or at least one field");
        await RefusedAsync(client, "update_item", new() { ["item"] = taskUrn, ["target_date"] = "2027-01-01" }, "A Task has no target_date. Its fields are: title, description, due");
        await RefusedAsync(client, "update_item", new() { ["item"] = taskUrn, ["due"] = "next Tuesday" }, "due is a day as yyyy-MM-dd");
        await RefusedAsync(client, "update_item", new() { ["item"] = taskUrn, ["priority"] = "urgent" }, "priority is Low, Medium, High");
        await RefusedAsync(client, "update_item", new() { ["item"] = taskUrn, ["area"] = taskId.ToString() }, "is a Task, not an Area");

        // A refusal from the kernel says so in its words, and what was written before it.
        var first = $"Leaky tap {ns}";
        await PostSubjectAsync(host, new { type = SubjectTypes.Problem, title = first });
        var other = await PostSubjectAsync(host, new { type = SubjectTypes.Problem, title = $"Dripping tap {ns}" });
        var refused = await client.CallToolAsync(
            "update_item", new Dictionary<string, object?> { ["item"] = other.ToString(), ["title"] = first, ["description"] = "Kitchen." }, cancellationToken: Ct);
        Assert.True(refused.IsError);
        Assert.Contains("Set description, but not the title: A Problem subject titled", Text(refused), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_item_makes_an_item_under_its_parent_once_for_a_request_id()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var ns = Guid.NewGuid().ToString("N")[..8];
        var project = await PostSubjectAsync(host, new { type = SubjectTypes.Project, title = $"Shed {ns}" });
        await using var client = await ConnectAsync(host);
        var key = Guid.NewGuid().ToString();
        var make = new Dictionary<string, object?>
        {
            ["type"] = "task", ["title"] = $"Buy paint {ns}", ["parent"] = project.ToString(), ["due"] = "2026-12-05", ["request_id"] = key,
        };

        var made = await CallAsync(client, "create_item", make);
        var again = await CallAsync(client, "create_item", make);
        var problem = await CallAsync(client, "create_item", new() { ["type"] = "Problem", ["title"] = $"Shed door sticks {ns}" });
        var sameProblem = await CallAsync(client, "create_item", new() { ["type"] = "Problem", ["title"] = $"Shed door sticks {ns}" });

        var task = made.GetProperty("item");
        Assert.Equal(SubjectTypes.Task, task.GetProperty("type").GetString());
        var link = made.GetProperty("link");
        Assert.Equal((SubjectRelations.Serves, project), (link.GetProperty("relation").GetString(), link.GetProperty("to").GetProperty("id").GetGuid()));
        Assert.True(again.GetProperty("already_done").GetBoolean());
        Assert.Equal(task.GetProperty("id").GetGuid(), again.GetProperty("item").GetProperty("id").GetGuid());
        Assert.True(sameProblem.GetProperty("reused").GetBoolean());
        Assert.Equal(problem.GetProperty("item").GetProperty("id").GetGuid(), sameProblem.GetProperty("item").GetProperty("id").GetGuid());

        await using var owner = new NpgsqlConnection(db.ConnectionString);
        var id = task.GetProperty("id").GetGuid();
        Assert.Equal("2026-12-05", await owner.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT attributes->>'due' FROM bsk.subject WHERE id = @id;", new { id }, cancellationToken: Ct)));
        Assert.Equal(1, await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @id AND to_subject = @project;", new { id, project }, cancellationToken: Ct)));
        // Born at its first status, which carries the request id under the agent's source.
        var born = await owner.QuerySingleAsync<(string Source, string Key, string Status)>(new CommandDefinition(
            "SELECT source_id, external_id, payload->>'status' FROM bsk.event WHERE kind = 'state_change' AND payload->>'subject_id' = @id;",
            new { id = id.ToString() }, cancellationToken: Ct));
        Assert.Equal(($"mcp:{Writer}", key, "Not started"), born);
        // The Problem waits in the Inbox for Chris; the Task doesn't.
        var inbox = (await CallAsync(client, "inbox", new() { ["limit"] = 200 })).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("item_id").GetGuid()).ToList();
        Assert.Contains(problem.GetProperty("item").GetProperty("id").GetGuid(), inbox);
        Assert.DoesNotContain(id, inbox);

        await RefusedAsync(client, "create_item", new() { ["type"] = "Goal", ["title"] = "Be fit" }, "create_item makes a Task, Project, Problem, Idea or Decision");
        await RefusedAsync(client, "create_item", new() { ["type"] = "Task", ["title"] = "x", ["relation"] = "serves" }, "relation goes with parent");
        await RefusedAsync(client, "create_item", new() { ["type"] = "Project", ["title"] = "x", ["due"] = "2026-12-01" }, "A Project has no due");
    }

    [Fact]
    public async Task Tag_and_link_change_an_item_say_what_changed_and_leave_what_is_already_so()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var ns = Guid.NewGuid().ToString("N")[..8];
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Oil the hinges {ns}");
        var project = await PostSubjectAsync(host, new { type = SubjectTypes.Project, title = $"Gate {ns}" });
        await using var client = await ConnectAsync(host);

        var tagged = await CallAsync(client, "tag", new() { ["item"] = taskUrn, ["add"] = new[] { "Home", "quick" } });
        var tagAgain = await CallAsync(client, "tag", new() { ["item"] = taskUrn, ["add"] = new[] { "home" } });
        var untagged = await CallAsync(client, "tag", new() { ["item"] = taskUrn, ["remove"] = new[] { "quick" } });
        var linked = await CallAsync(client, "link", new() { ["from"] = taskUrn, ["relation"] = "serves", ["to"] = project.ToString() });
        var linkAgain = await CallAsync(client, "link", new() { ["from"] = taskId.ToString(), ["relation"] = "Serves", ["to"] = project.ToString() });
        var unlinked = await CallAsync(client, "link", new() { ["from"] = taskUrn, ["relation"] = "serves", ["to"] = project.ToString(), ["remove"] = true });
        var unlinkAgain = await CallAsync(client, "link", new() { ["from"] = taskUrn, ["relation"] = "serves", ["to"] = project.ToString(), ["remove"] = true });

        Assert.Equal(2, tagged.GetProperty("added").GetInt32());
        Assert.Equal(0, tagAgain.GetProperty("added").GetInt32());
        Assert.Equal(1, untagged.GetProperty("removed").GetInt32());
        Assert.True(linked.GetProperty("linked").GetBoolean());
        Assert.True(linkAgain.GetProperty("linked").GetBoolean());
        Assert.True(unlinked.GetProperty("removed").GetBoolean());
        Assert.False(unlinkAgain.GetProperty("removed").GetBoolean());

        await using var owner = new NpgsqlConnection(db.ConnectionString);
        Assert.Equal(new[] { "home" }, (await owner.QueryAsync<string>(new CommandDefinition(
            "SELECT tag FROM bsk.item_tag WHERE subject_id = @taskId;", new { taskId }, cancellationToken: Ct))).ToArray());
        Assert.Equal(0, await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @taskId;", new { taskId }, cancellationToken: Ct)));

        await RefusedAsync(client, "link", new() { ["from"] = project.ToString(), ["relation"] = "serves", ["to"] = taskUrn }, "Nothing may 'serves' a Task");
        await RefusedAsync(client, "link", new() { ["from"] = taskUrn, ["relation"] = "parent", ["to"] = project.ToString() }, "relation is serves, results_in, supersedes");
        await RefusedAsync(client, "tag", new() { ["item"] = taskUrn }, "Send tags to add, to remove, or both");
    }

    [Fact]
    public async Task With_an_area_off_its_items_are_not_found_and_a_status_change_keeps_its_earnings_to_itself()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var settings = Settings();
        settings[McpOptions.DomainsKey] = "health,food,home";
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, settings);
        var reward = await PostSubjectAsync(host, new { type = SubjectTypes.Reward, title = $"A new book {Guid.NewGuid():N}" });
        var (_, taskUrn) = await PostTaskAsync(host, $"Sweep the porch {Guid.NewGuid():N}");
        await using var client = await ConnectAsync(host);

        await RefusedAsync(client, "update_item", new() { ["item"] = reward.ToString(), ["description"] = "Fiction." }, "no item has the id or URN");
        await RefusedAsync(client, "tag", new() { ["item"] = reward.ToString(), ["add"] = new[] { "fun" } }, "no item has the id or URN");
        var done = await CallAsync(client, "set_status", new() { ["item"] = taskUrn, ["status"] = "Completed" });

        Assert.Equal("Completed", done.GetProperty("to").GetString());
        Assert.False(done.TryGetProperty("earned", out _));
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        [McpOptions.EnabledKey] = "true",
        [HomeZone.ConfigurationKey] = DomainTestDatabase.Zone,
        [McpOptions.WritesKey] = McpWrites.Full,
        [McpOptions.WriteClientsKey] = $"{Writer}:full",
    };

    private static Task<ApiTestHost> StartAsync(string connectionString)
        => ApiTestHost.StartAsync(connectionString, Ct, Settings());

    private static async Task<McpClient> ConnectAsync(ApiTestHost host)
    {
        await ActAsAiPersonAsync(host, Writer);
        return await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));
    }

    private static async Task<Guid> OpenReviewAsync(ApiTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync("api/reviews/open", new { kind = "daily" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/reviews/open → {(int)response.StatusCode}: {text}");
        var json = JsonDocument.Parse(text).RootElement;
        return (json.TryGetProperty("review", out var review) ? review : json).GetProperty("id").GetGuid();
    }

    private static async Task<int> AgentLedgerEntriesAsync(DomainTestDatabase db)
    {
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        return await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'ledger' AND source_id = @source;",
            new { source = $"mcp:{Writer}" }, cancellationToken: Ct));
    }

    private static async Task<string> UrnAsync(DomainTestDatabase db, Guid id)
    {
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        return await owner.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT urn FROM bsk.subject WHERE id = @id;", new { id }, cancellationToken: Ct)) ?? "";
    }

    private static List<(string Field, string? From, string? To)> Changes(JsonElement answer)
        => answer.GetProperty("changes").EnumerateArray()
            .Select(c => (
                c.GetProperty("field").GetString()!,
                c.TryGetProperty("from", out var from) ? from.GetString() : null,
                c.TryGetProperty("to", out var to) ? to.GetString() : null))
            .ToList();

    /// <summary>The call is refused, in words that include <paramref name="words"/>.</summary>
    private static async Task RefusedAsync(McpClient client, string tool, Dictionary<string, object?> arguments, string words)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        Assert.True(result.IsError == true, $"{tool} wasn't refused: {Text(result)}");
        Assert.Contains(words, Text(result), StringComparison.Ordinal);
    }
}
