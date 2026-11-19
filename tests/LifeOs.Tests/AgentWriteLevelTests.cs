using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Api.Http;
using LifeOs.Api.Mcp;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Each agent's write level, kept in LifeOS beside the Person it acts as (migration 0076,
/// agent-access plan §5.1, decision 7 as amended 2026-11-19): Chris gives it on Connected agents,
/// connecting again carries it over, <c>Mcp:WriteClients</c> still names a level for a client with
/// none stored, and <c>Mcp:Writes</c> holds every agent to its ceiling. Connected agents and
/// <c>/mcp</c> agree on each step.
/// </summary>
public sealed class AgentWriteLevelTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_level_given_on_connected_agents_lets_an_agent_write_with_no_setting_naming_it()
    {
        var agent = NewClient("given");
        var log = new LogCapture();
        await using var host = await StartAsync(McpWrites.Full, writeClients: null, log);
        await McpWriteTests.ActAsAiPersonAsync(host, agent);

        var before = await AgentAsync(host, agent);
        Assert.Null(before.GivenLevel);
        Assert.False(before.MayWrite);
        Assert.Equal("it has no write level (Connected agents)", before.ReadOnlyBecause);

        var full = await SetLevelAsync(host, agent, McpWrites.Full);
        Assert.Equal(McpWrites.Full, full.GivenLevel);
        Assert.Equal(McpWrites.Full, full.WriteLevel);
        Assert.True(full.MayWrite);
        Assert.Equal(McpWrites.Full, (await AgentAsync(host, agent)).WriteLevel);
        await using (var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: agent)))
        {
            Assert.Equal(McpWriteAccess.ToolsAt(McpWrites.Full).Order(), (await McpWriteTests.NamesAsync(client)).Intersect(McpWriteAccess.Tools).Order());
            await McpWriteTests.CallAsync(client, "capture", new() { ["text"] = "Given full on Connected agents" });
        }

        Assert.Contains($"MCP write tools listed for {agent} at full", log.Messages("LifeOs.Api.Mcp"));

        // Read only, set here, takes the writes away on the next call.
        var off = await SetLevelAsync(host, agent, McpWrites.Off);
        Assert.Equal(McpWrites.Off, off.GivenLevel);
        Assert.Null(off.WriteLevel);
        Assert.Equal("its write level is read only (Connected agents)", off.ReadOnlyBecause);
        await using (var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: agent)))
        {
            Assert.Empty((await McpWriteTests.NamesAsync(client)).Intersect(McpWriteAccess.Tools));
            var refused = await client.CallToolAsync("capture", new Dictionary<string, object?> { ["text"] = "Hello" }, cancellationToken: Ct);
            Assert.True(refused.IsError);
            Assert.Contains("Connected agents", McpWriteTests.Text(refused), StringComparison.Ordinal);
        }

        // Cleared, it's back to what the settings say, which here is nothing.
        var cleared = await SetLevelAsync(host, agent, null);
        Assert.Null(cleared.GivenLevel);
        Assert.False(cleared.MayWrite);
    }

    [Fact]
    public async Task A_level_set_here_wins_over_the_setting_and_mcp_writes_still_holds_it()
    {
        var agent = NewClient("held");
        await using var host = await StartAsync(McpWrites.Capture, writeClients: agent);
        await McpWriteTests.ActAsAiPersonAsync(host, agent);

        // Named at capture in the settings, with nothing stored.
        Assert.Equal(McpWrites.Capture, (await AgentAsync(host, agent)).GivenLevel);

        // Given full here, it still writes at capture while Mcp:Writes is capture.
        var full = await SetLevelAsync(host, agent, McpWrites.Full);
        Assert.Equal(McpWrites.Full, full.GivenLevel);
        Assert.Equal(McpWrites.Capture, full.WriteLevel);
        await using (var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: agent)))
        {
            Assert.Equal(McpWriteAccess.ToolsAt(McpWrites.Capture).Order(), (await McpWriteTests.NamesAsync(client)).Intersect(McpWriteAccess.Tools).Order());
        }

        // Read only here beats the setting that names it.
        var off = await SetLevelAsync(host, agent, McpWrites.Off);
        Assert.False(off.MayWrite);
        await using (var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: agent)))
        {
            Assert.Empty((await McpWriteTests.NamesAsync(client)).Intersect(McpWriteAccess.Tools));
        }
    }

    // The point of migration 0076: Claude registers a new client each time it connects again, and
    // its writes used to stop until Mcp:WriteClients named the new id.
    [Fact]
    public async Task Connecting_again_carries_the_level_over_with_the_person()
    {
        var name = $"Claude {Guid.NewGuid():N}";
        var (first, second, third) = (NewClient("first"), NewClient("second"), NewClient("third"));
        await using var host = await StartAsync(McpWrites.Full, writeClients: $"{first}:full");
        await ReadAsync<AgentClient>(await PutAgentAsync(host, first, new { name, clientUri = "https://claude.ai", redirectUri = Callback }));
        var person = await McpWriteTests.PostSubjectAsync(host, new
        {
            type = SubjectTypes.Person,
            title = name,
            attrs = new Dictionary<string, string> { ["person_kind"] = "ai" },
        });
        await ReadAsync<AgentClient>(await host.Client.PutAsJsonAsync($"api/agents/{first}/person", new { personId = person }, Ct));

        // The setting names the first client at full; the client that replaces it takes full.
        var renewed = await ReadAsync<AgentClient>(await PutAgentAsync(
            host, second, new { name, clientUri = "https://claude.ai", redirectUri = Callback, replaces = new[] { first } }));
        Assert.Equal(person, renewed.PersonId);
        Assert.Equal(McpWrites.Full, renewed.GivenLevel);
        Assert.Equal(McpWrites.Full, renewed.WriteLevel);
        Assert.True(renewed.MayWrite);
        await using (var client = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: second)))
        {
            Assert.Equal(McpWriteAccess.ToolsAt(McpWrites.Full).Order(), (await McpWriteTests.NamesAsync(client)).Intersect(McpWriteAccess.Tools).Order());
        }

        // A level set here is the one carried next time.
        await SetLevelAsync(host, second, McpWrites.Capture);
        var again = await ReadAsync<AgentClient>(await PutAgentAsync(
            host, third, new { name, clientUri = "https://claude.ai", redirectUri = Callback, replaces = new[] { second } }));
        Assert.Equal(McpWrites.Capture, again.GivenLevel);
        Assert.Equal(McpWrites.Capture, again.WriteLevel);

        var agents = (await AgentsAsync(host)).Agents;
        Assert.False(Assert.Single(agents, a => a.ClientId == second).Allowed);
        Assert.Equal(McpWrites.Capture, Assert.Single(agents, a => a.ClientId == third).WriteLevel);
    }

    [Fact]
    public async Task A_client_allowed_without_replacing_another_starts_with_no_level()
    {
        var name = $"Claude {Guid.NewGuid():N}";
        var (old, fresh) = (NewClient("old"), NewClient("fresh"));
        await using var host = await StartAsync(McpWrites.Full, writeClients: $"{old}:full");
        await ReadAsync<AgentClient>(await PutAgentAsync(host, old, new { name, redirectUri = Callback }));

        var unticked = await ReadAsync<AgentClient>(await PutAgentAsync(host, fresh, new { name, redirectUri = Callback }));

        Assert.Null(unticked.GivenLevel);
        Assert.False(unticked.MayWrite);
    }

    [Fact]
    public async Task Setting_a_level_refuses_a_bad_level_an_unknown_client_and_an_agent()
    {
        var agent = NewClient("refuse");
        await using var host = await StartAsync(McpWrites.Full, writeClients: null);
        await McpWriteTests.ActAsAiPersonAsync(host, agent);

        using var bad = await host.Client.PutAsJsonAsync($"api/agents/{agent}/level", new { level = "admin" }, Ct);
        using var unknown = await host.Client.PutAsJsonAsync($"api/agents/{NewClient("ghost")}/level", new { level = "full" }, Ct);
        using var badId = await host.Client.PutAsJsonAsync("api/agents/bad%20id!/level", new { level = "full" }, Ct);
        using var asAgent = new HttpRequestMessage(HttpMethod.Put, $"api/agents/{agent}/level") { Content = JsonContent.Create(new { level = "full" }) };
        asAgent.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.TokenFor(clientId: agent));
        using var byAgent = await host.Client.SendAsync(asAgent, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("isn't a write level", await bad.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Null((await AgentAsync(host, agent)).GivenLevel);
    }

    private static string NewClient(string name) => $"{name}-{Guid.NewGuid():N}"[..24];

    private Task<ApiTestHost> StartAsync(string writes, string? writeClients, LogCapture? log = null)
        => ApiTestHost.StartAsync(classDb.ConnectionString, Ct, new Dictionary<string, string?>
        {
            [McpOptions.EnabledKey] = "true",
            [HomeZone.ConfigurationKey] = DomainTestDatabase.Zone,
            [McpOptions.WritesKey] = writes,
            [McpOptions.WriteClientsKey] = writeClients,
        }, log is null ? null : log.AddTo);

    private static async Task<AgentClient> SetLevelAsync(ApiTestHost host, string clientId, string? level)
        => await ReadAsync<AgentClient>(await host.Client.PutAsJsonAsync($"api/agents/{clientId}/level", new { level }, Ct));

    private static Task<HttpResponseMessage> PutAgentAsync(ApiTestHost host, string client, object body)
        => host.Client.PutAsJsonAsync($"api/agents/{Uri.EscapeDataString(client)}", body, Ct);

    private static async Task<AgentClient> AgentAsync(ApiTestHost host, string clientId)
        => Assert.Single((await AgentsAsync(host)).Agents, a => a.ClientId == clientId);

    private static async Task<AgentsRead> AgentsAsync(ApiTestHost host)
    {
        using var response = await host.Client.GetAsync("api/agents", Ct);
        return await ReadAsync<AgentsRead>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} → {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Web)!;
    }
}
