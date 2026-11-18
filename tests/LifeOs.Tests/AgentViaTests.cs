using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Api.Mcp;
using LifeOs.Api.Read;
using LifeOs.Domain;
using static LifeOs.Tests.McpWriteTests;

namespace LifeOs.Tests;

/// <summary>
/// "via Claude" (agent-access plan §5.6 step 2.4): what an agent wrote through <c>/mcp</c> says
/// which agent in the web's reads, by the AI-agent Person it acts as: an item's history (status
/// changes, renames and field changes from and to), its journal, the Inbox and the Inbox's
/// resolved list. What Chris wrote says nothing. A capture or journal entry keeps the Person the
/// agent acted as when it wrote; the rest follow the client's Person now, or its own name.
/// </summary>
public sealed class AgentViaTests(PostgresFixture postgres)
{
    private const string Writer = "agent-via-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agents_writes_say_via_its_Person_and_Chriss_say_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var ns = Guid.NewGuid().ToString("N");
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Fix the gate {ns}");
        var area = await PostSubjectAsync(host, new { type = SubjectTypes.Area, title = $"Home {ns}" });
        var person = await ActAsAiPersonAsync(host, Writer);
        var name = await TitleAsync(host, person);
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));

        await PostOkAsync(host, "api/status", new { subject = taskUrn, status = "In progress" });
        await PostOkAsync(host, "api/journal", new { subject = taskUrn, text = $"Chris looked {ns}" });
        await PostOkAsync(host, "api/capture", new { text = $"Chris's thought {ns}" });
        await CallAsync(client, "set_status", new() { ["item"] = taskUrn, ["status"] = "Completed" });
        await CallAsync(client, "update_item", new()
        {
            ["item"] = taskUrn, ["title"] = $"Fix the side gate {ns}", ["due"] = "2026-12-01", ["area"] = area.ToString(),
        });
        await CallAsync(client, "add_journal_entry", new() { ["item"] = taskUrn, ["text"] = $"Claude measured {ns}" });
        var capture = await CallAsync(client, "capture", new() { ["text"] = $"Claude's thought {ns}" });
        var dismissed = await CallAsync(client, "capture", new() { ["text"] = $"Claude's spare {ns}" });
        await CallAsync(client, "dismiss", new() { ["item_id"] = dismissed.GetProperty("capture_id").GetString() });

        var history = await GetAsync(host, $"api/subjects/{taskId}/history");
        var agent = new Via(name, person);
        Assert.Equal(
            new[]
            {
                ("fields", "title", (string?)$"Fix the gate {ns}", (string?)$"Fix the side gate {ns}", agent),
                ("fields", "area", null, $"Home {ns}", agent),
                ("fields", "due", null, "2026-12-01", agent),
                ("status", "Completed", null, null, agent),
                ("status", "In progress", null, null, (Via?)null),
            },
            Rows(history).OrderBy(r => r.Index).Select(r => (r.Kind, r.What, r.From, r.To, r.Via)));

        var journal = (await GetAsync(host, $"api/subjects/{taskId}/journal")).EnumerateArray()
            .Select(e => (e.GetProperty("content").GetString(), ViaOf(e))).ToList();
        Assert.Equal([($"Chris looked {ns}", null), ($"Claude measured {ns}", agent)], journal);

        var inbox = (await GetAsync(host, "api/inbox")).EnumerateArray()
            .ToDictionary(i => i.GetProperty("eventContent").GetString()!, ViaOf);
        Assert.Equal(agent, inbox[$"Claude's thought {ns}"]);
        Assert.Null(inbox[$"Chris's thought {ns}"]);
        Assert.False(inbox.ContainsKey($"Claude's spare {ns}"));
        Assert.Equal(capture.GetProperty("capture_id").GetGuid(), (await GetAsync(host, "api/inbox")).EnumerateArray()
            .Single(i => ViaOf(i) is not null).GetProperty("itemId").GetGuid());

        var resolved = Assert.Single((await GetAsync(host, "api/inbox/resolved")).EnumerateArray().ToList());
        Assert.Equal(("dismissed", agent), (resolved.GetProperty("resolution").GetString(), ViaOf(resolved)));
    }

    [Fact]
    public async Task Via_names_the_Person_now_a_capture_keeps_the_Person_it_was_made_as_and_an_unlinked_client_its_own_name()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var ns = Guid.NewGuid().ToString("N");
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Clean the gutters {ns}");
        var first = await ActAsAiPersonAsync(host, Writer);
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));
        await CallAsync(client, "set_status", new() { ["item"] = taskUrn, ["status"] = "In progress" });
        await CallAsync(client, "add_journal_entry", new() { ["item"] = taskUrn, ["text"] = $"On the ladder {ns}" });

        // Chris renames the Person: what it wrote says the name it has now.
        await PostOkAsync(host, "api/rename", new { subject = first.ToString(), title = $"Claude at home {ns}" });
        Assert.Equal(new Via($"Claude at home {ns}", first), StatusVia(await GetAsync(host, $"api/subjects/{taskId}/history")));

        // Linked to another Person, the client's status change follows it; the journal entry keeps the first.
        var second = await PostSubjectAsync(host, new
        {
            type = SubjectTypes.Person, title = $"Claude at work {ns}", attrs = new Dictionary<string, string> { ["person_kind"] = "ai" },
        });
        await PutOkAsync(host, $"api/agents/{Writer}/person", new { personId = second });
        Assert.Equal(new Via($"Claude at work {ns}", second), StatusVia(await GetAsync(host, $"api/subjects/{taskId}/history")));
        Assert.Equal(new Via($"Claude at home {ns}", first), ViaOf(Assert.Single((await GetAsync(host, $"api/subjects/{taskId}/journal")).EnumerateArray().ToList())));

        // Unlinked, it's the client by the name the consent page showed.
        await PutOkAsync(host, $"api/agents/{Writer}/person", new { personId = (Guid?)null });
        Assert.Equal(new Via("Test agent", null), StatusVia(await GetAsync(host, $"api/subjects/{taskId}/history")));
    }

    [Fact]
    public async Task An_items_history_says_each_field_change_plainly_and_a_pattern_only_as_changed()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var ns = Guid.NewGuid().ToString("N");
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Sweep the porch {ns}");
        var description = new string('a', SubjectReader.MaxHistoryValueLength + 20);

        await PostOkAsync(host, "api/attributes", new { subject = taskUrn, attrs = new Dictionary<string, string> { ["description"] = description, ["priority"] = "High" } });
        await PostOkAsync(host, "api/attributes", new { subject = taskUrn, attrs = new Dictionary<string, string> { ["priority"] = "" } });
        await PostOkAsync(host, "api/recur", new { subject = taskUrn, recurrence = new { freq = "weekly", on = new[] { "sat" } } });

        var rows = Rows(await GetAsync(host, $"api/subjects/{taskId}/history")).ToList();
        var recurrence = rows.Single(r => r.What == "recurrence");
        Assert.False(recurrence.Plain);
        Assert.Equal((null, null), (recurrence.From, recurrence.To));
        Assert.Equal(("High", (string?)null), rows.Where(r => r.What == "priority").Select(r => (r.From, r.To)).First());
        var cut = rows.Single(r => r.What == "description").To!;
        Assert.Equal(SubjectReader.MaxHistoryValueLength, cut.Length);
        Assert.EndsWith("a…", cut, StringComparison.Ordinal);
        Assert.All(rows, r => Assert.Null(r.Via));

        // A limit cuts the history, newest first.
        var newest = Assert.Single((await GetAsync(host, $"api/subjects/{taskId}/history?limit=1")).EnumerateArray().ToList());
        Assert.Equal(rows[0].Id, newest.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task An_agents_row_read_without_its_client_still_says_an_agent_wrote_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await StartAsync(db.ConnectionString);
        var (taskId, taskUrn) = await PostTaskAsync(host, $"Oil the hinge {Guid.NewGuid():N}");
        var person = await ActAsAiPersonAsync(host, Writer);
        await using var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: Writer));
        await CallAsync(client, "set_status", new() { ["item"] = taskUrn, ["status"] = "In progress" });
        await CallAsync(client, "add_journal_entry", new() { ["item"] = taskUrn, ["text"] = "Oiled." });

        var reader = TestReaders.Subjects(db.ConnectionString);
        var history = await reader.GetHistoryAsync(taskId, cancellationToken: Ct);
        var journal = await reader.GetJournalAsync(taskId, cancellationToken: Ct);

        Assert.Equal(AgentSources.Unnamed, history.First(h => h.Status == "In progress").Via?.Name);
        Assert.Equal(person, Assert.Single(journal).Via?.PersonId);
    }

    private sealed record Via(string Name, Guid? PersonId);

    private sealed record Row(int Index, Guid Id, string Kind, string What, string? From, string? To, bool Plain, Via? Via);

    /// <summary>The history's rows, one per status change and one per field changed, in the order they came.</summary>
    private static IEnumerable<Row> Rows(JsonElement history)
    {
        var index = 0;
        foreach (var entry in history.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetGuid();
            var via = ViaOf(entry);
            if (entry.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            {
                yield return new Row(index++, id, "status", status.GetString()!, null, null, true, via);
                continue;
            }

            foreach (var change in entry.GetProperty("changes").EnumerateArray().OrderBy(c => c.GetProperty("field").GetString() == "title" ? 0 : 1)
                         .ThenBy(c => c.GetProperty("field").GetString(), StringComparer.Ordinal))
            {
                yield return new Row(
                    index++, id, "fields", change.GetProperty("field").GetString()!, Str(change, "from"), Str(change, "to"),
                    change.GetProperty("plain").GetBoolean(), via);
            }
        }
    }

    private static Via? StatusVia(JsonElement history)
        => Rows(history).First(r => r.Kind == "status" && r.Via is not null).Via;

    private static Via? ViaOf(JsonElement row)
        => row.TryGetProperty("via", out var via) && via.ValueKind == JsonValueKind.Object
            ? new Via(via.GetProperty("name").GetString()!, Str(via, "personId") is { } id ? Guid.Parse(id) : null)
            : null;

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<JsonElement> GetAsync(ApiTestHost host, string path)
    {
        using var response = await host.Client.GetAsync(path, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"GET {path} → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> TitleAsync(ApiTestHost host, Guid id)
        => (await GetAsync(host, $"api/subjects/{id}")).GetProperty("title").GetString()!;

    private static async Task PutOkAsync(ApiTestHost host, string path, object body)
    {
        using var response = await host.Client.PutAsJsonAsync(path, body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"PUT {path} → {(int)response.StatusCode}: {text}");
    }

    private static Task<ApiTestHost> StartAsync(string connectionString)
        => ApiTestHost.StartAsync(connectionString, Ct, new Dictionary<string, string?>
        {
            [McpOptions.EnabledKey] = "true",
            [HomeZone.ConfigurationKey] = DomainTestDatabase.Zone,
            [McpOptions.WritesKey] = McpWrites.Full,
            [McpOptions.WriteClientsKey] = $"{Writer}:full",
        });
}
