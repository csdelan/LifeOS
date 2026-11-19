using LifeOs.Api.Auth;
using LifeOs.Api.Mcp;

namespace LifeOs.Api.Http;

/// <summary>
/// Connected agents (agent-access plan §4.5): the agents Chris allowed in LifeOS and what each one
/// called. Only his Google web session reaches these (<c>AllowlistedUser</c>); an agent's token is
/// 403, so no agent can allow itself or another, or read the log. The consent page allows an agent
/// here before it approves the request at Supabase, and replaces the client an agent used before it
/// connected again, carrying its Person and its write level over. Chris gives each agent its level
/// here too (migration 0076), which <c>Mcp:Writes</c> still holds it to.
/// </summary>
public static class AgentEndpoints
{
    /// <summary>The most clients one approval replaces.</summary>
    public const int MaxReplaces = 20;

    public static void MapAgentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/agents")
            .WithTags("Agents")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("", List).WithName("GetAgents")
            .Produces<AgentsRead>();
        api.MapGet("/calls", Calls).WithName("GetAgentCalls")
            .Produces<AgentCallsRead>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPut("/{clientId}", Allow).WithName("AllowAgent")
            .Produces<AgentClient>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);
        api.MapPost("/{clientId}/revoke", Revoke).WithName("RevokeAgent")
            .Produces<AgentClient>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPut("/{clientId}/person", LinkPerson).WithName("LinkAgentPerson")
            .Produces<AgentClient>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
        api.MapPut("/{clientId}/level", SetLevel).WithName("SetAgentLevel")
            .Produces<AgentClient>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> List(
        AgentAccessStore access, McpOptions mcp, CancellationToken cancellationToken)
    {
        var agents = await access.ListAsync(cancellationToken);
        foreach (var agent in agents)
        {
            Describe(mcp, agent);
        }

        // Writes and each agent's say come from the settings /mcp runs on, so the page can't call
        // agents read-only while they write (code review CR64).
        return Results.Ok(new AgentsRead { McpEnabled = mcp.Enabled, Writes = mcp.Writes, Agents = agents });
    }

    /// <summary>Fills in what <c>/mcp</c> lets <paramref name="agent"/> write, by the rule each call is checked against.</summary>
    private static AgentClient Describe(McpOptions mcp, AgentClient agent)
    {
        agent.GivenLevel = agent.Name is null ? null : mcp.GivenLevel(agent.ClientId, agent.StoredLevel);
        agent.ReadOnlyBecause = McpWriteAccess.ReadOnlyBecause(mcp, agent);
        agent.MayWrite = agent.ReadOnlyBecause is null;
        agent.WriteLevel = McpWriteAccess.LevelOf(mcp, agent);
        return agent;
    }

    private static async Task<IResult> SetLevel(
        string clientId, SetAgentLevelBody? body, AgentAccessStore access, McpOptions mcp, CancellationToken cancellationToken)
    {
        if (!AgentAccessStore.IsClientId(clientId))
        {
            return BadClientId(clientId);
        }

        var level = body?.Level?.Trim();
        if (level is not null && !McpWrites.Levels.Contains(level, StringComparer.Ordinal))
        {
            return Results.BadRequest(new ApiError($"'{level}' isn't a write level. Use off, capture or full."));
        }

        return await access.SetLevelAsync(clientId, level, cancellationToken) is { } client
            ? Results.Ok(Describe(mcp, client))
            : NotAllowed(clientId);
    }

    private static async Task<IResult> Calls(
        AgentAccessStore access, string? clientId, int? limit, CancellationToken cancellationToken)
    {
        if (clientId is not null && !AgentAccessStore.IsClientId(clientId))
        {
            return BadClientId(clientId);
        }

        if (limit is < 1 or > AgentAccessStore.MaxCallLimit)
        {
            return Results.BadRequest(new ApiError($"limit runs from 1 to {AgentAccessStore.MaxCallLimit}; got {limit}."));
        }

        return Results.Ok(new AgentCallsRead { Calls = await access.RecentCallsAsync(clientId, limit, cancellationToken) });
    }

    private static async Task<IResult> Allow(
        string clientId, AllowAgentBody? body, AgentAccessStore access, McpOptions mcp, CancellationToken cancellationToken)
    {
        if (!AgentAccessStore.IsClientId(clientId))
        {
            return BadClientId(clientId);
        }

        var name = body?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Results.BadRequest(new ApiError("Allowing an agent takes its name, as the consent page showed it (up to 200 characters)."));
        }

        // The website a client registered with is the client's own say, so only a web address is kept.
        var uri = string.IsNullOrWhiteSpace(body?.ClientUri) ? null : body.ClientUri.Trim();
        if (uri is not null
            && (uri.Length > 500
                || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)))
        {
            return Results.BadRequest(new ApiError("clientUri must be an http or https address, up to 500 characters."));
        }

        // Where the consent page sent the browser back to. It's only compared, never followed, so
        // an app's own scheme (claude://…) is kept as well as a web address.
        var redirect = string.IsNullOrWhiteSpace(body?.RedirectUri) ? null : body.RedirectUri.Trim();
        if (redirect is not null && (redirect.Length > 500 || !Uri.TryCreate(redirect, UriKind.Absolute, out _)))
        {
            return Results.BadRequest(new ApiError("redirectUri must be an absolute address, up to 500 characters."));
        }

        var replaces = body?.Replaces?.Select(r => r?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (replaces.Count > MaxReplaces)
        {
            return Results.BadRequest(new ApiError($"A client replaces at most {MaxReplaces} others at once."));
        }

        if (replaces.FirstOrDefault(r => !AgentAccessStore.IsClientId(r)) is { } bad)
        {
            return BadClientId(bad);
        }

        if (replaces.Count > 0 && redirect is null)
        {
            return Results.BadRequest(new ApiError("Replacing a client takes the redirectUri both share."));
        }

        try
        {
            // A replacing client takes the level Mcp:WriteClients gave the one it replaces, when that
            // one has none of its own stored (migration 0076).
            return Results.Ok(Describe(mcp, await access.AllowAsync(
                clientId, name, uri, redirect, replaces, cancellationToken, settingsLevels: mcp.WriteClients)));
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(new ApiError(e.Message));
        }
    }

    private static async Task<IResult> Revoke(
        string clientId, AgentAccessStore access, CancellationToken cancellationToken)
    {
        if (!AgentAccessStore.IsClientId(clientId))
        {
            return BadClientId(clientId);
        }

        return await access.RevokeAsync(clientId, cancellationToken) is { } client
            ? Results.Ok(client)
            : NotAllowed(clientId);
    }

    private static async Task<IResult> LinkPerson(
        string clientId, LinkAgentPersonBody? body, AgentAccessStore access, CancellationToken cancellationToken)
    {
        if (!AgentAccessStore.IsClientId(clientId))
        {
            return BadClientId(clientId);
        }

        try
        {
            return await access.LinkPersonAsync(clientId, body?.PersonId, cancellationToken) is { } client
                ? Results.Ok(client)
                : NotAllowed(clientId);
        }
        catch (ArgumentException e)
        {
            return Results.BadRequest(new ApiError(e.Message));
        }
    }

    private static IResult BadClientId(string clientId)
        => Results.BadRequest(new ApiError($"'{clientId}' isn't a client id."));

    private static IResult NotAllowed(string clientId)
        => Results.NotFound(new ApiError($"No agent with client id {clientId} was ever allowed in LifeOS."));
}

/// <summary><c>GET /api/agents</c>.</summary>
public sealed class AgentsRead
{
    /// <summary>Whether <c>/mcp</c> is on (<c>Mcp:Enabled</c>); off, no agent can call.</summary>
    public bool McpEnabled { get; set; }

    /// <summary>
    /// The most any agent may change (<c>Mcp:Writes</c>): <c>off</c>, read-only, <c>capture</c>, the
    /// Inbox and journal writes of agent-access plan §5.2, or <c>full</c>, which also changes items.
    /// Each agent whose <c>MayWrite</c> is true writes at its own <c>WriteLevel</c>, at most this.
    /// </summary>
    public string Writes { get; set; } = McpWrites.Off;

    public IReadOnlyList<AgentClient> Agents { get; set; } = [];
}

/// <summary><c>GET /api/agents/calls</c>.</summary>
public sealed class AgentCallsRead
{
    public IReadOnlyList<AgentCall> Calls { get; set; } = [];
}

/// <summary><c>PUT /api/agents/{clientId}</c>: the client as the consent page showed it.</summary>
public sealed class AllowAgentBody
{
    public string? Name { get; set; }
    public string? ClientUri { get; set; }

    /// <summary>Where the consent page sends the browser back to; with the name, it says which agent this is.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// Clients this one takes over from: on the list with the same name and redirect address.
    /// This one acts as their Person and they're revoked (migration 0048).
    /// </summary>
    public IReadOnlyList<string>? Replaces { get; set; }
}

/// <summary><c>PUT /api/agents/{clientId}/person</c>: the AI-agent Person, or null to unlink.</summary>
public sealed class LinkAgentPersonBody
{
    public Guid? PersonId { get; set; }
}

/// <summary>
/// <c>PUT /api/agents/{clientId}/level</c>: the level Chris gives an agent (migration 0076),
/// <c>off</c> (it only reads), <c>capture</c> or <c>full</c>, or null to leave it to
/// <c>Mcp:WriteClients</c>. <c>Mcp:Writes</c> still holds every agent to its level.
/// </summary>
public sealed class SetAgentLevelBody
{
    public string? Level { get; set; }
}
