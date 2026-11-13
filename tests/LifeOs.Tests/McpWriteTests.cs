using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Api.Mcp;
using LifeOs.Domain;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Writes through <c>/mcp</c> (agent-access plan §5.5, notes plan N7): who sees the write tools
/// and may call them, what they write and as whom, that a retried request writes once, and that
/// <c>dismiss</c> clears raw captures only.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class McpWriteTests(PostgresFixture postgres)
{
    private const string Writer = "agent-writer-1";
    private const string Other = "agent-other-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_writes_off_no_write_tool_is_listed_and_a_call_is_refused()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString, writes: null);
        await ActAsAiPersonAsync(host, Writer);
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));

        Assert.Empty((await NamesAsync(client)).Intersect(McpWriteAccess.Tools));
        Assert.DoesNotContain("request_id", client.ServerInstructions ?? "", StringComparison.Ordinal);
        Assert.True(await RefusedAsync(client, "capture", new() { ["text"] = "Hello" }));
        Assert.Equal(0, await AgentEventsAsync(db));
    }

    [Fact]
    public async Task At_capture_a_writer_is_listed_the_capture_tools_and_nothing_else_that_writes()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        await ActAsAiPersonAsync(host, Writer);
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        var writing = tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint != true).Select(t => t.Name).Order();
        Assert.Equal(McpWriteAccess.Tools.Order(), writing);
        Assert.All(
            tools.Where(t => McpWriteAccess.Tools.Contains(t.Name)),
            t => Assert.False(t.ProtocolTool.Annotations?.DestructiveHint ?? true, $"{t.Name} is marked destructive"));
        Assert.Contains("request_id", client.ServerInstructions ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_not_named_to_write_or_acting_as_no_AI_person_stays_read_only()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        // Other acts as an AI Person but isn't named in Mcp:WriteClients; Writer is named but acts as no one.
        await ActAsAiPersonAsync(host, Other);
        await McpServerTests.AllowAsync(host, Writer);

        foreach (var clientId in new[] { Other, Writer })
        {
            await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: clientId));
            Assert.Empty((await NamesAsync(client)).Intersect(McpWriteAccess.Tools));
            var refused = await client.CallToolAsync("capture", new Dictionary<string, object?> { ["text"] = "Hello" }, cancellationToken: Ct);
            Assert.True(refused.IsError, $"{clientId} could capture");
            Assert.Contains("can't write", Text(refused), StringComparison.Ordinal);
            Assert.NotEmpty((await CallAsync(client, "inbox")).GetProperty("as_of").GetString()!);
        }

        Assert.Equal(0, await AgentEventsAsync(db));
    }

    [Fact]
    public async Task An_agent_token_still_cannot_reach_the_api_with_writes_on()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        await ActAsAiPersonAsync(host, Writer);

        using var write = new HttpRequestMessage(HttpMethod.Post, "api/capture") { Content = JsonContent.Create(new { text = "From an agent" }) };
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.TokenFor(clientId: Writer));

        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(write, Ct)).StatusCode);
    }

    [Fact]
    public async Task Writes_carry_the_agents_source_provenance_and_person()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var person = await ActAsAiPersonAsync(host, Writer);
        var ns = Guid.NewGuid().ToString("N")[..8];
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Build the shed {ns}");
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));

        var note = await CallAsync(client, "capture", new() { ["text"] = $"Call the lumber yard {ns}", ["concerning"] = taskUrn });
        var idea = await CallAsync(client, "capture", new() { ["text"] = $"A loft over the shed {ns}\nWith a ladder.", ["kind"] = "Idea" });
        var entry = await CallAsync(client, "add_journal_entry", new() { ["item"] = taskId.ToString(), ["text"] = $"Measured the site {ns}" });

        Assert.Equal("note", note.GetProperty("kind").GetString());
        Assert.Equal(taskId, note.GetProperty("concerns")[0].GetProperty("id").GetGuid());
        var made = idea.GetProperty("item");
        Assert.Equal((SubjectTypes.Idea, $"A loft over the shed {ns}"), (made.GetProperty("type").GetString(), made.GetProperty("title").GetString()));
        Assert.Equal(taskId, entry.GetProperty("item").GetProperty("id").GetGuid());

        await using var owner = new NpgsqlConnection(db.ConnectionString);
        var written = (await owner.QueryAsync<(Guid Id, string Kind, string Source, string Provenance, string? Person)>(new CommandDefinition(
            """
            SELECT id, kind, source_id, provenance, payload->>'agent_person_id'
            FROM bsk.event WHERE id = ANY(@ids);
            """,
            new
            {
                ids = new[]
                {
                    note.GetProperty("capture_id").GetGuid(), idea.GetProperty("capture_id").GetGuid(), entry.GetProperty("entry_id").GetGuid(),
                },
            },
            cancellationToken: Ct))).ToList();
        Assert.Equal(new[] { EventKinds.Journal, EventKinds.Note, EventKinds.Note }, written.Select(w => w.Kind).Order());
        Assert.All(written, w => Assert.Equal(($"mcp:{Writer}", Provenances.Declared, person.ToString()), (w.Source, w.Provenance, w.Person)));

        // Everything else the agent wrote is its own too: the two Inbox flags.
        var others = (await owner.QueryAsync<(string Kind, string Source)>(new CommandDefinition(
            "SELECT kind, source_id FROM bsk.event WHERE source_id LIKE 'mcp:%' AND NOT (id = ANY(@ids));",
            new { ids = written.Select(w => w.Id).ToArray() },
            cancellationToken: Ct))).ToList();
        Assert.Equal(new[] { (EventKinds.Triage, $"mcp:{Writer}"), (EventKinds.Triage, $"mcp:{Writer}") }, others);

        // The note and the Idea wait in the Inbox; the journal entry is on the Task.
        var inbox = (await CallAsync(client, "inbox", new() { ["limit"] = 200 })).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(inbox, i => i.GetProperty("item_id").GetGuid() == note.GetProperty("capture_id").GetGuid()
                                    && i.GetProperty("item_kind").GetString() == "event");
        Assert.Contains(inbox, i => i.GetProperty("item_id").GetGuid() == made.GetProperty("id").GetGuid());
        var concerns = await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_event WHERE subject_id = @taskId AND relation = 'concerns';",
            new { taskId },
            cancellationToken: Ct));
        Assert.Equal(2, concerns);
    }

    [Fact]
    public async Task A_retried_request_id_writes_once()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        await ActAsAiPersonAsync(host, Writer);
        var ns = Guid.NewGuid().ToString("N")[..8];
        var (taskId, _) = await PostTaskAsync(host, $"Paint the fence {ns}");
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));
        string Key() => Guid.NewGuid().ToString();

        var noteKey = Key();
        var note = await CallAsync(client, "capture", new() { ["text"] = $"Buy paint {ns}", ["request_id"] = noteKey });
        var noteAgain = await CallAsync(client, "capture", new() { ["text"] = $"Buy paint {ns}", ["request_id"] = noteKey });
        Assert.Equal(note.GetProperty("capture_id").GetGuid(), noteAgain.GetProperty("capture_id").GetGuid());
        Assert.False(note.TryGetProperty("already_done", out _));
        Assert.True(noteAgain.GetProperty("already_done").GetBoolean());

        var ideaKey = Key();
        var idea = await CallAsync(client, "capture", new() { ["text"] = $"Stain it instead {ns}", ["kind"] = "idea", ["request_id"] = ideaKey });
        var ideaAgain = await CallAsync(client, "capture", new() { ["text"] = $"Stain it instead {ns}", ["kind"] = "idea", ["request_id"] = ideaKey });
        Assert.Contains(ideaAgain.GetProperty("concerns").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == idea.GetProperty("item").GetProperty("id").GetGuid());

        var entryKey = Key();
        var entry = await CallAsync(client, "add_journal_entry", new() { ["item"] = taskId.ToString(), ["text"] = $"First coat {ns}", ["request_id"] = entryKey });
        var entryAgain = await CallAsync(client, "add_journal_entry", new() { ["item"] = taskId.ToString(), ["text"] = $"First coat {ns}", ["request_id"] = entryKey });
        Assert.Equal(entry.GetProperty("entry_id").GetGuid(), entryAgain.GetProperty("entry_id").GetGuid());
        Assert.Equal(taskId, entryAgain.GetProperty("item").GetProperty("id").GetGuid());

        await using var owner = new NpgsqlConnection(db.ConnectionString);
        foreach (var key in new[] { noteKey, ideaKey, entryKey })
        {
            Assert.Equal(1, await owner.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM bsk.event WHERE source_id = @source AND external_id = @key;",
                new { source = $"mcp:{Writer}", key },
                cancellationToken: Ct)));
        }

        Assert.Equal(1, await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE type = 'Idea' AND title = @title;",
            new { title = $"Stain it instead {ns}" },
            cancellationToken: Ct)));
        var inbox = (await CallAsync(client, "inbox", new() { ["limit"] = 200 })).GetProperty("items").EnumerateArray();
        Assert.Single(inbox, i => i.TryGetProperty("text", out var t) && t.GetString() == $"Buy paint {ns}");

        // The same request id for another kind of write is refused, in words.
        var misused = await client.CallToolAsync("add_journal_entry",
            new Dictionary<string, object?> { ["item"] = taskId.ToString(), ["text"] = "Again", ["request_id"] = noteKey }, cancellationToken: Ct);
        Assert.True(misused.IsError);
        Assert.Contains("different write", Text(misused), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dismiss_clears_a_raw_capture_only_and_flagging_brings_it_back()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        await ActAsAiPersonAsync(host, Writer);
        var ns = Guid.NewGuid().ToString("N")[..8];
        await PostOkAsync(host, "api/capture", new { text = $"A reflection to file {ns}" });
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));
        var captureId = Assert.Single(await InboxAsync(client), i => (i.TryGetProperty("text", out var t) ? t.GetString() : null) == $"A reflection to file {ns}")
            .GetProperty("item_id").GetGuid();

        var dismissed = await CallAsync(client, "dismiss", new() { ["item_id"] = captureId.ToString() });
        var again = await CallAsync(client, "dismiss", new() { ["item_id"] = captureId.ToString() });

        Assert.True(dismissed.GetProperty("dismissed").GetBoolean());
        Assert.False(again.GetProperty("dismissed").GetBoolean());
        Assert.DoesNotContain(await InboxAsync(client), i => i.GetProperty("item_id").GetGuid() == captureId);
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        var markers = (await owner.QueryAsync<(string Source, string State)>(new CommandDefinition(
            "SELECT source_id, payload->>'state' FROM bsk.event WHERE kind = 'triage' AND payload->>'item_id' = @id ORDER BY recorded_at;",
            new { id = captureId.ToString() },
            cancellationToken: Ct))).ToList();
        Assert.Equal(new[] { ("api", TriageStates.Flagged), ($"mcp:{Writer}", TriageStates.Dismissed) }, markers);

        // Chris flags it again, and it's back.
        await PostOkAsync(host, "api/flag", new { item = captureId.ToString() });
        Assert.Contains(await InboxAsync(client), i => i.GetProperty("item_id").GetGuid() == captureId);

        // An Idea in the Inbox is Chris's to triage, and a made-up id isn't a capture.
        var idea = (await CallAsync(client, "capture", new() { ["text"] = $"Paint the shed red {ns}", ["kind"] = "idea" }))
            .GetProperty("item").GetProperty("id").GetGuid();
        var refused = await client.CallToolAsync("dismiss", new Dictionary<string, object?> { ["item_id"] = idea.ToString() }, cancellationToken: Ct);
        var bad = await client.CallToolAsync("dismiss", new Dictionary<string, object?> { ["item_id"] = "the shed" }, cancellationToken: Ct);
        Assert.True(refused.IsError);
        Assert.Contains("an Idea", Text(refused), StringComparison.Ordinal);
        Assert.Contains(await InboxAsync(client), i => i.GetProperty("item_id").GetGuid() == idea);
        Assert.True(bad.IsError);
    }

    [Fact]
    public async Task A_write_names_an_item_the_agent_can_see_or_writes_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var settings = Settings(McpWrites.Capture);
        settings[McpOptions.DomainsKey] = "none";
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, settings);
        await ActAsAiPersonAsync(host, Writer);
        // A Reward belongs to the rewards area, switched off here.
        var reward = await PostSubjectAsync(host, new { type = SubjectTypes.Reward, title = $"Movie night {Guid.NewGuid():N}" });
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));

        var hidden = await client.CallToolAsync("add_journal_entry",
            new Dictionary<string, object?> { ["item"] = reward.ToString(), ["text"] = "Picked a film" }, cancellationToken: Ct);
        var unknown = await client.CallToolAsync("capture",
            new Dictionary<string, object?> { ["text"] = "About nothing", ["concerning"] = "urn:bsk:task:nope-000000" }, cancellationToken: Ct);
        var empty = await client.CallToolAsync("capture", new Dictionary<string, object?> { ["text"] = "   " }, cancellationToken: Ct);
        var badKind = await client.CallToolAsync("capture", new Dictionary<string, object?> { ["text"] = "Hi", ["kind"] = "task" }, cancellationToken: Ct);

        Assert.All(new[] { hidden, unknown, empty, badKind }, r => Assert.True(r.IsError));
        Assert.Contains("no item has", Text(hidden), StringComparison.Ordinal);
        Assert.Contains("note, idea or problem", Text(badKind), StringComparison.Ordinal);
        Assert.Equal(0, await AgentEventsAsync(db));
    }

    // ---- Helpers ----

    private static Dictionary<string, string?> Settings(string? writes) => new()
    {
        [McpOptions.EnabledKey] = "true",
        [HomeZone.ConfigurationKey] = DomainTestDatabase.Zone,
        [McpOptions.WritesKey] = writes,
        [McpOptions.WriteClientsKey] = Writer,
    };

    private static Task<ApiTestHost> StartAsync(string connectionString, string? writes = McpWrites.Capture)
        => ApiTestHost.StartAsync(connectionString, Ct, Settings(writes));

    /// <summary>Allows <paramref name="clientId"/> and links it to a new AI-agent Person, as Connected agents does.</summary>
    private static async Task<Guid> ActAsAiPersonAsync(ApiTestHost host, string clientId)
    {
        await McpServerTests.AllowAsync(host, clientId);
        var person = await PostSubjectAsync(host, new
        {
            type = SubjectTypes.Person,
            title = $"Claude {Guid.NewGuid():N}",
            attrs = new Dictionary<string, string> { ["person_kind"] = "ai" },
        });
        using var link = await host.Client.PutAsJsonAsync($"api/agents/{clientId}/person", new { personId = person }, Ct);
        Assert.True(link.IsSuccessStatusCode, $"Linking {clientId}: {(int)link.StatusCode}");
        return person;
    }

    private static async Task<(Guid Id, string Urn)> PostTaskAsync(ApiTestHost host, string title)
    {
        var response = await host.Client.PostAsJsonAsync("api/subjects", new { type = SubjectTypes.Task, title }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/subjects → {(int)response.StatusCode}: {text}");
        var json = JsonDocument.Parse(text).RootElement;
        return (json.GetProperty("id").GetGuid(), json.GetProperty("urn").GetString()!);
    }

    private static async Task<Guid> PostSubjectAsync(ApiTestHost host, object body)
    {
        var response = await host.Client.PostAsJsonAsync("api/subjects", body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/subjects → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task PostOkAsync(ApiTestHost host, string path, object body)
    {
        var response = await host.Client.PostAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(McpClient client)
        => (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();

    /// <summary>Whether a call is refused: an error answer, or no such tool at all.</summary>
    private static async Task<bool> RefusedAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        try
        {
            return (await client.CallToolAsync(tool, arguments, cancellationToken: Ct)).IsError == true;
        }
        catch (McpException)
        {
            return true;
        }
    }

    private static async Task<List<JsonElement>> InboxAsync(McpClient client)
        => (await CallAsync(client, "inbox", new() { ["limit"] = 200 })).GetProperty("items").EnumerateArray().ToList();

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        var text = Text(result);
        Assert.False(result.IsError == true, $"{tool} failed: {text}");
        var json = text[(text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string Text(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private static async Task<int> AgentEventsAsync(DomainTestDatabase db)
    {
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        return await owner.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE source_id LIKE 'mcp:%';", cancellationToken: Ct));
    }
}
