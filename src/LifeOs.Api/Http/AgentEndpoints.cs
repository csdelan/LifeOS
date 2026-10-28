using LifeOs.Api.Auth;
using LifeOs.Api.Mcp;

namespace LifeOs.Api.Http;

/// <summary>
/// Connected agents (agent-access plan §4.5): the agents Chris allowed in LifeOS and what each one
/// called. Only his Google web session reaches these (<c>AllowlistedUser</c>); an agent's token is
/// 403, so no agent can allow itself or another, or read the log. The consent page allows an agent
/// here before it approves the request at Supabase.
/// </summary>
public static class AgentEndpoints
{
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
    }

    private static async Task<IResult> List(
        AgentAccessStore access, McpOptions mcp, CancellationToken cancellationToken)
        => Results.Ok(new AgentsRead
        {
            McpEnabled = mcp.Enabled,
            Agents = await access.ListAsync(cancellationToken),
        });

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
        string clientId, AllowAgentBody? body, AgentAccessStore access, CancellationToken cancellationToken)
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

        return Results.Ok(await access.AllowAsync(clientId, name, uri, cancellationToken));
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

    /// <summary>What agents may change: <c>off</c> (read-only) until Phase 2 builds writes.</summary>
    public string Writes { get; set; } = "off";

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
}

/// <summary><c>PUT /api/agents/{clientId}/person</c>: the AI-agent Person, or null to unlink.</summary>
public sealed class LinkAgentPersonBody
{
    public Guid? PersonId { get; set; }
}
