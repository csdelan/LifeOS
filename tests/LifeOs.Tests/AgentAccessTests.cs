using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using LifeOs.Api.Mcp;
using LifeOs.Domain;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Client;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Which agents may call <c>/mcp</c> and what each one called (agent-access plan §4.5, step 1.4,
/// and §4.4 fix 3): LifeOS's own allow list, changed only with Chris's web session, and the
/// access log behind Connected agents.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AgentAccessTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The allow list (fix 3) ----

    [Fact]
    public async Task A_client_not_allowed_in_LifeOS_is_403_and_its_refusal_is_logged_once_a_minute()
    {
        await using var host = await StartAsync();
        var stranger = NewClient("stranger");

        using var first = await host.Client.SendAsync(McpServerTests.Initialize(ApiTestHost.TokenFor(clientId: stranger)), Ct);
        using var second = await host.Client.SendAsync(McpServerTests.Initialize(ApiTestHost.TokenFor(clientId: stranger)), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        var agent = Assert.Single((await AgentsAsync(host)).Agents, a => a.ClientId == stranger);
        Assert.False(agent.Allowed);
        Assert.Null(agent.Name);
        Assert.NotNull(agent.LastRefusedAt);
        Assert.Equal(1, agent.RefusedThisWeek);
        var call = Assert.Single((await CallsAsync(host, stranger)).Calls);
        Assert.Equal(AgentAccessStore.Refused, call.Outcome);
        Assert.Null(call.Tool);
    }

    [Fact]
    public async Task Allowing_lets_an_agent_in_and_revoking_shuts_it_out_on_its_next_call()
    {
        await using var host = await StartAsync();
        var client = NewClient("claude");
        var token = ApiTestHost.TokenFor(clientId: client);

        using var allow = await host.Client.PutAsJsonAsync(
            $"api/agents/{client}", new { name = " Claude ", clientUri = "https://claude.ai" }, Ct);
        var allowed = await ReadAsync<AgentClient>(allow);
        using var before = await host.Client.SendAsync(McpServerTests.Initialize(token), Ct);
        using var revoke = await host.Client.PostAsync($"api/agents/{client}/revoke", content: null, Ct);
        var revoked = await ReadAsync<AgentClient>(revoke);
        using var after = await host.Client.SendAsync(McpServerTests.Initialize(token), Ct);

        Assert.True(allowed.Allowed);
        Assert.Equal("Claude", allowed.Name);
        Assert.Equal("https://claude.ai", allowed.ClientUri);
        Assert.NotNull(allowed.AllowedAt);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.False(revoked.Allowed);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task Allowing_again_after_a_revoke_lets_it_back_in_and_restarts_its_allowed_date()
    {
        await using var host = await StartAsync();
        var client = NewClient("again");

        var first = await AllowAsync(host, client);
        var second = await AllowAsync(host, client);
        await host.Client.PostAsync($"api/agents/{client}/revoke", content: null, Ct);
        var third = await AllowAsync(host, client);

        Assert.Equal(first.AllowedAt, second.AllowedAt);
        Assert.True(third.Allowed);
        Assert.Null(third.RevokedAt);
        Assert.True(third.AllowedAt > first.AllowedAt);
        using var response = await host.Client.SendAsync(McpServerTests.Initialize(ApiTestHost.TokenFor(clientId: client)), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_agents_token_cant_list_allow_revoke_or_read_the_log()
    {
        await using var host = await StartAsync();
        var client = NewClient("self");
        await AllowAsync(host, client);
        var token = ApiTestHost.TokenFor(clientId: client);
        var other = NewClient("friend");

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "api/agents"),
            new HttpRequestMessage(HttpMethod.Get, "api/agents/calls"),
            new HttpRequestMessage(HttpMethod.Put, $"api/agents/{other}") { Content = JsonContent.Create(new { name = "Friend" }) },
            new HttpRequestMessage(HttpMethod.Post, $"api/agents/{client}/revoke"),
            new HttpRequestMessage(HttpMethod.Put, $"api/agents/{client}/person") { Content = JsonContent.Create(new { personId = (Guid?)null }) },
        };
        foreach (var request in requests)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await host.Client.SendAsync(request, Ct);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{request.Method} {request.RequestUri} → {(int)response.StatusCode}");
            request.Dispose();
        }

        Assert.DoesNotContain((await AgentsAsync(host)).Agents, a => a.ClientId == other);
    }

    [Fact]
    public async Task Bad_input_is_400_in_words_and_an_unknown_client_is_404()
    {
        await using var host = await StartAsync();

        using var badId = await host.Client.PutAsJsonAsync("api/agents/bad%20id!", new { name = "x" }, Ct);
        using var noName = await host.Client.PutAsJsonAsync($"api/agents/{NewClient("nameless")}", new { name = "  " }, Ct);
        using var badUri = await host.Client.PutAsJsonAsync($"api/agents/{NewClient("uri")}", new { name = "x", clientUri = "not an address" }, Ct);
        using var scriptUri = await host.Client.PutAsJsonAsync($"api/agents/{NewClient("js")}", new { name = "x", clientUri = "javascript:alert(1)" }, Ct);
        using var badLimit = await host.Client.GetAsync("api/agents/calls?limit=500", Ct);
        using var unknown = await host.Client.PostAsync($"api/agents/{NewClient("ghost")}/revoke", content: null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.Contains("isn't a client id", await badId.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badUri.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, scriptUri.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task An_agent_links_to_an_AI_Person_only()
    {
        await using var host = await StartAsync();
        var client = NewClient("linked");
        await AllowAsync(host, client);
        var ns = Guid.NewGuid().ToString("N")[..8];
        var ai = await PostPersonAsync(host, $"Claude {ns}", "ai");
        var human = await PostPersonAsync(host, $"Sam {ns}", "human");

        using var toHuman = await host.Client.PutAsJsonAsync($"api/agents/{client}/person", new { personId = human }, Ct);
        using var toNobody = await host.Client.PutAsJsonAsync($"api/agents/{client}/person", new { personId = Guid.NewGuid() }, Ct);
        using var toAi = await host.Client.PutAsJsonAsync($"api/agents/{client}/person", new { personId = ai }, Ct);
        var linked = await ReadAsync<AgentClient>(toAi);
        using var unlink = await host.Client.PutAsJsonAsync($"api/agents/{client}/person", new { personId = (Guid?)null }, Ct);
        var unlinked = await ReadAsync<AgentClient>(unlink);
        using var unknown = await host.Client.PutAsJsonAsync($"api/agents/{NewClient("nobody")}/person", new { personId = ai }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, toHuman.StatusCode);
        Assert.Contains("not an AI agent", await toHuman.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, toNobody.StatusCode);
        Assert.Equal(ai, linked.PersonId);
        Assert.Equal($"Claude {ns}", linked.PersonName);
        Assert.Null(unlinked.PersonId);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // ---- Connecting again (migration 0048) ----

    private const string Callback = "https://claude.ai/api/mcp/auth_callback";

    [Fact]
    public async Task Connecting_again_replaces_the_old_clients_carrying_the_Person_over_and_revoking_them()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N")[..8];
        var name = $"Claude {ns}";
        var (older, old, renewed) = (NewClient("older"), NewClient("old"), NewClient("new"));
        var ai = await PostPersonAsync(host, $"Claude {ns}", "ai");
        await PutAgentAsync(host, older, new { name, redirectUri = Callback });
        var first = await ReadAsync<AgentClient>(await PutAgentAsync(host, old, new { name, clientUri = "https://claude.ai", redirectUri = Callback }));
        using var link = await host.Client.PutAsJsonAsync($"api/agents/{old}/person", new { personId = ai }, Ct);
        await ReadAsync<AgentClient>(link);

        var replacing = await ReadAsync<AgentClient>(await PutAgentAsync(
            host, renewed, new { name, clientUri = "https://claude.ai", redirectUri = Callback, replaces = new[] { old, older } }));
        var agents = (await AgentsAsync(host)).Agents;
        using var oldCall = await host.Client.SendAsync(McpServerTests.Initialize(ApiTestHost.TokenFor(clientId: old)), Ct);
        using var newCall = await host.Client.SendAsync(McpServerTests.Initialize(ApiTestHost.TokenFor(clientId: renewed)), Ct);

        Assert.Equal(Callback, first.RedirectUri);
        Assert.True(replacing.Allowed);
        Assert.Equal(Callback, replacing.RedirectUri);
        Assert.Equal(ai, replacing.PersonId);
        Assert.Equal(name, replacing.PersonName);
        Assert.All(agents.Where(a => a.ClientId == old || a.ClientId == older), a =>
        {
            Assert.False(a.Allowed);
            Assert.NotNull(a.RevokedAt);
            Assert.Equal(renewed, a.ReplacedBy);
        });
        Assert.Equal(ai, Assert.Single(agents, a => a.ClientId == old).PersonId);
        Assert.Equal(HttpStatusCode.Forbidden, oldCall.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newCall.StatusCode);

        // Allowed again by hand, a replaced client is just allowed again.
        var back = await ReadAsync<AgentClient>(await PutAgentAsync(host, old, new { name }));
        Assert.True(back.Allowed);
        Assert.Null(back.ReplacedBy);
        Assert.Equal(Callback, back.RedirectUri);
    }

    [Fact]
    public async Task A_client_replaces_only_one_with_the_same_name_and_return_address_or_nothing_changes()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N")[..8];
        var name = $"Claude {ns}";
        var kept = NewClient("kept");
        await ReadAsync<AgentClient>(await PutAgentAsync(host, kept, new { name, redirectUri = Callback }));
        var (otherName, otherAddress, noAddress, fresh) = (NewClient("n"), NewClient("a"), NewClient("x"), NewClient("f"));

        using var byName = await PutAgentAsync(host, otherName, new { name = $"Helper {ns}", redirectUri = Callback, replaces = new[] { kept } });
        using var byAddress = await PutAgentAsync(host, otherAddress, new { name, redirectUri = "claude://claude.ai/mcp-auth-callback/sdk", replaces = new[] { kept } });
        using var withoutAddress = await PutAgentAsync(host, noAddress, new { name, replaces = new[] { kept } });
        using var itself = await PutAgentAsync(host, kept, new { name, redirectUri = Callback, replaces = new[] { kept } });
        using var ghost = await PutAgentAsync(host, fresh, new { name, redirectUri = Callback, replaces = new[] { NewClient("ghost") } });
        using var badId = await PutAgentAsync(host, fresh, new { name, redirectUri = Callback, replaces = new[] { "bad id!" } });
        using var badAddress = await PutAgentAsync(host, fresh, new { name, redirectUri = "not an address" });
        var agents = (await AgentsAsync(host)).Agents;

        Assert.All(new[] { byName, byAddress, withoutAddress, itself, ghost, badId, badAddress },
            r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Contains("same name and return address", await byAddress.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("can't replace itself", await itself.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(Assert.Single(agents, a => a.ClientId == kept).Allowed);
        Assert.DoesNotContain(agents, a => a.ClientId == otherName || a.ClientId == otherAddress || a.ClientId == fresh);
    }

    [Fact]
    public async Task A_replacing_client_keeps_a_Person_of_its_own_and_an_archived_one_isnt_carried()
    {
        await using var host = await StartAsync();
        var ns = Guid.NewGuid().ToString("N")[..8];
        var name = $"Claude {ns}";
        var (old, own, plain) = (NewClient("old"), NewClient("own"), NewClient("plain"));
        var oldPerson = await PostPersonAsync(host, $"Old {ns}", "ai");
        var ownPerson = await PostPersonAsync(host, $"Own {ns}", "ai");
        foreach (var client in new[] { old, own })
        {
            await ReadAsync<AgentClient>(await PutAgentAsync(host, client, new { name, redirectUri = Callback }));
        }

        await ReadAsync<AgentClient>(await host.Client.PutAsJsonAsync($"api/agents/{old}/person", new { personId = oldPerson }, Ct));
        await ReadAsync<AgentClient>(await host.Client.PutAsJsonAsync($"api/agents/{own}/person", new { personId = ownPerson }, Ct));

        var keepsOwn = await ReadAsync<AgentClient>(await PutAgentAsync(host, own, new { name, redirectUri = Callback, replaces = new[] { old } }));
        Assert.Equal(ownPerson, keepsOwn.PersonId);

        // Once its Person is archived, there's no one to carry over.
        using var archive = await host.Client.PostAsJsonAsync("api/archive", new { subject = ownPerson.ToString() }, Ct);
        Assert.True(archive.IsSuccessStatusCode, await archive.Content.ReadAsStringAsync(Ct));
        var nobody = await ReadAsync<AgentClient>(await PutAgentAsync(host, plain, new { name, redirectUri = Callback, replaces = new[] { own } }));
        Assert.Null(nobody.PersonId);
        Assert.Equal(plain, Assert.Single((await AgentsAsync(host)).Agents, a => a.ClientId == own).ReplacedBy);
    }

    [Fact]
    public async Task Without_the_list_or_when_it_cant_be_read_every_agent_is_refused()
    {
        var auth = new SupabaseAuthOptions { AllowedEmails = "owner@example.com" };
        auth.Normalize();
        var mcp = new McpOptions { Enabled = true };
        var unreachable = new AgentAccessStore("Host=127.0.0.1;Port=1;Timeout=2;Pooling=false", "UTC");

        var noList = await HandleAsync(new McpAgentHandler(auth, mcp));
        var cantRead = await HandleAsync(new McpAgentHandler(auth, mcp, unreachable));
        var allowed = await HandleAsync(new McpAgentHandler(auth, mcp, new AgentAccessStore(postgres.ConnectionString, "UTC")), allowFirst: true);

        Assert.True(noList.HasFailed);
        Assert.True(cantRead.HasFailed);
        Assert.True(allowed.HasSucceeded);
    }

    // ---- The access log (§4.5) ----

    [Fact]
    public async Task Tool_calls_are_logged_with_arguments_outcome_items_and_time()
    {
        await using var host = await StartAsync();
        var client = NewClient("logged");
        await AllowAsync(host, client);
        var ns = Guid.NewGuid().ToString("N")[..8];
        using (var capture = await host.Client.PostAsJsonAsync("api/capture", new { text = $"A capture {ns}" }, Ct))
        {
            capture.EnsureSuccessStatusCode();
        }

        await using (var mcp = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: client)))
        {
            var inbox = await mcp.CallToolAsync("inbox", cancellationToken: Ct);
            var missing = await mcp.CallToolAsync("get_item", new Dictionary<string, object?> { ["id"] = $"urn:bsk:task:nope-{ns}" }, cancellationToken: Ct);
            Assert.False(inbox.IsError == true);
            Assert.True(missing.IsError);
        }

        var calls = (await CallsAsync(host, client)).Calls;
        Assert.Equal(new[] { "get_item", "inbox" }, calls.Select(c => c.Tool));
        var error = calls[0];
        Assert.Equal(AgentAccessStore.Error, error.Outcome);
        Assert.Contains($"nope-{ns}", error.Arguments, StringComparison.Ordinal);
        Assert.Null(error.Items);
        var ok = calls[1];
        Assert.Equal(AgentAccessStore.Ok, ok.Outcome);
        Assert.True(ok.Items >= 1, $"inbox logged {ok.Items} items");
        Assert.True(ok.DurationMs >= 0);
        Assert.True(error.CalledAt >= ok.CalledAt);

        var agent = Assert.Single((await AgentsAsync(host)).Agents, a => a.ClientId == client);
        Assert.Equal(2, agent.CallsThisWeek);
        Assert.Equal(error.CalledAt, agent.LastCallAt);
        Assert.Equal(0, agent.RefusedThisWeek);
        Assert.Contains((await CallsAsync(host, clientId: null)).Calls, c => c.ClientId == client);
    }

    [Fact]
    public async Task Long_arguments_are_trimmed_to_1_KB()
    {
        await using var host = await StartAsync();
        var client = NewClient("verbose");
        await AllowAsync(host, client);

        await using (var mcp = await McpServerTests.ConnectAsync(host, ApiTestHost.TokenFor(clientId: client)))
        {
            await mcp.CallToolAsync("find_items", new Dictionary<string, object?> { ["query"] = new string('x', 3000) }, cancellationToken: Ct);
        }

        var call = Assert.Single((await CallsAsync(host, client)).Calls);
        Assert.Equal(AgentAccessStore.MaxArgumentsLength, call.Arguments!.Length);
        Assert.EndsWith("…", call.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Trimming_never_splits_a_surrogate_pair()
    {
        var emoji = "😀";
        var text = new string('a', AgentAccessStore.MaxArgumentsLength - 2) + emoji + "tail";

        var trimmed = AgentAccessStore.Trim(text)!;

        Assert.Equal(AgentAccessStore.MaxArgumentsLength - 1, trimmed.Length);
        Assert.False(char.IsHighSurrogate(trimmed[^2]));
        Assert.Null(AgentAccessStore.Trim(null));
        Assert.Equal("short", AgentAccessStore.Trim("short"));
    }

    [Fact]
    public async Task This_week_starts_Monday_in_the_home_zone_and_old_rows_are_deleted()
    {
        var client = NewClient("weekly");
        await using (var owner = new NpgsqlConnection(postgres.ConnectionString))
        {
            await owner.OpenAsync(Ct);
            await owner.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk_ops.agent_client (client_id, name) VALUES (@client, 'Weekly');
                INSERT INTO bsk_ops.agent_call (called_at, client_id, tool, outcome, duration_ms) VALUES
                    ((date_trunc('week', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC') - interval '1 minute', @client, 'inbox', 'ok', 3),
                    (now() - interval '91 days', @client, 'inbox', 'ok', 3),
                    (now(), @client, 'inbox', 'ok', 3);
                """,
                new { client }, cancellationToken: Ct));
        }

        // A new store cleans up on its first write.
        var store = new AgentAccessStore(postgres.ConnectionString, "UTC");
        var before = Assert.Single(await store.ListAsync(Ct), a => a.ClientId == client);
        await store.RecordCallAsync(new AgentCallRecord(client, "people", null, AgentAccessStore.Ok, 0, 1), Ct);
        var calls = await store.RecentCallsAsync(client, limit: 10, Ct);

        Assert.Equal(1, before.CallsThisWeek);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, c => Assert.True(c.CalledAt > DateTime.UtcNow.AddDays(-8)));
    }

    // ---- Helpers ----

    private static string NewClient(string name) => $"{name}-{Guid.NewGuid():N}"[..24];

    private Task<ApiTestHost> StartAsync()
        => ApiTestHost.StartAsync(postgres.ConnectionString, Ct, new Dictionary<string, string?>
        {
            [McpOptions.EnabledKey] = "true",
            [HomeZone.ConfigurationKey] = "UTC",
        });

    private async Task<AuthorizationHandlerContext> HandleAsync(McpAgentHandler handler, bool allowFirst = false)
    {
        var client = NewClient("unit");
        if (allowFirst)
        {
            await new AgentAccessStore(postgres.ConnectionString, "UTC").AllowAsync(client, "Unit", null, cancellationToken: Ct);
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("email", "owner@example.com"),
                new Claim("app_metadata", """{"provider":"google","providers":["google"]}"""),
                new Claim("amr", """{"method":"oauth_provider/authorization_code","timestamp":1793200000}"""),
                new Claim("client_id", client),
            ],
            authenticationType: "Bearer"));
        var context = new AuthorizationHandlerContext([new McpAgentRequirement()], user, resource: null);
        await handler.HandleAsync(context);
        return context;
    }

    private static async Task<AgentClient> AllowAsync(ApiTestHost host, string client)
    {
        using var response = await host.Client.PutAsJsonAsync($"api/agents/{client}", new { name = "Agent" }, Ct);
        return await ReadAsync<AgentClient>(response);
    }

    private static Task<HttpResponseMessage> PutAgentAsync(ApiTestHost host, string client, object body)
        => host.Client.PutAsJsonAsync($"api/agents/{Uri.EscapeDataString(client)}", body, Ct);

    private static async Task<AgentsRead> AgentsAsync(ApiTestHost host)
    {
        using var response = await host.Client.GetAsync("api/agents", Ct);
        return await ReadAsync<AgentsRead>(response);
    }

    private static async Task<AgentCallsRead> CallsAsync(ApiTestHost host, string? clientId)
    {
        using var response = await host.Client.GetAsync(
            clientId is null ? "api/agents/calls?limit=200" : $"api/agents/calls?clientId={clientId}", Ct);
        return await ReadAsync<AgentCallsRead>(response);
    }

    private static async Task<Guid> PostPersonAsync(ApiTestHost host, string title, string kind)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "api/subjects",
            new { type = SubjectTypes.Person, title, attrs = new Dictionary<string, string> { ["person_kind"] = kind } },
            Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/subjects → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} → {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Web)!;
    }
}
