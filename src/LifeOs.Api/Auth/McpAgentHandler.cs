using System.Security.Claims;
using LifeOs.Api.Mcp;
using Microsoft.AspNetCore.Authorization;

namespace LifeOs.Api.Auth;

/// <summary>
/// An outside agent's token (agent-access plan §4.4): a Supabase JWT for Chris that Supabase's
/// OAuth server issued to a client, so it carries the client's <c>client_id</c>. A web session's
/// token has none.
/// </summary>
public static class AgentClaims
{
    public const string ClientIdClaim = "client_id";

    /// <summary>The agent's client id, or null for a token that isn't an agent's.</summary>
    public static string? ClientId(ClaimsPrincipal user)
        => user.FindFirst(ClientIdClaim)?.Value is { } id && !string.IsNullOrWhiteSpace(id) ? id.Trim() : null;
}

public sealed class McpAgentRequirement : IAuthorizationRequirement;

/// <summary>
/// The <c>McpAgent</c> policy that <c>/mcp</c> requires: an allowlisted email on a Google
/// account, a <c>client_id</c>, a session Supabase's OAuth server started for an agent (its
/// <c>amr</c>), a client that isn't in <c>Mcp:BlockedClients</c>, and a client Chris allowed in
/// LifeOS (fix 3, the allow list in <c>bsk_ops.agent_client</c>). Everyone else is 403.
/// </summary>
/// <remarks>
/// Supabase alone can't be trusted with which clients may connect: any of Chris's tokens, an
/// agent's included, can approve a new client at Supabase. LifeOS's list is changed only through
/// <c>/api/agents</c>, which agents' tokens can't reach. Without the list (a host that didn't
/// register <see cref="AgentAccessStore"/>), or when it can't be read, every agent is refused.
/// </remarks>
public sealed class McpAgentHandler(
    SupabaseAuthOptions auth,
    McpOptions mcp,
    AgentAccessStore? access = null,
    ILogger<McpAgentHandler>? logger = null)
    : AuthorizationHandler<McpAgentRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        McpAgentRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var email = SupabaseClaims.ReadEmail(context.User);
        var clientId = AgentClaims.ClientId(context.User);
        if (email is null || !auth.IsAllowed(email) || clientId is null)
        {
            context.Fail();
            return;
        }

        if (!SupabaseClaims.IssuedToAgent(context.User))
        {
            logger?.LogWarning(
                "Refused an agent token for an allowed account: not issued by the OAuth server for a Google account (client {ClientId}; sign-in methods: {Methods}; Google account: {GoogleAccount})",
                clientId,
                string.Join(", ", SupabaseClaims.SignInMethods(context.User)),
                SupabaseClaims.IsGoogleAccount(context.User));
            context.Fail();
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (mcp.IsBlocked(clientId) || !await IsAllowedAsync(clientId, cancellationToken))
        {
            logger?.LogWarning(
                "Refused agent {ClientId}: {Reason}",
                clientId,
                mcp.IsBlocked(clientId) ? "in Mcp:BlockedClients" : "not allowed in LifeOS");
            await RecordRefusedAsync(clientId, cancellationToken);
            context.Fail();
            return;
        }

        context.Succeed(requirement);
    }

    private async Task<bool> IsAllowedAsync(string clientId, CancellationToken cancellationToken)
    {
        if (access is null || !AgentAccessStore.IsClientId(clientId))
        {
            return false;
        }

        try
        {
            return await access.IsAllowedAsync(clientId, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogError(e, "Couldn't read the agent allow list; refusing {ClientId}", clientId);
            return false;
        }
    }

    private async Task RecordRefusedAsync(string clientId, CancellationToken cancellationToken)
    {
        if (access is null || !AgentAccessStore.IsClientId(clientId))
        {
            return;
        }

        try
        {
            await access.RecordRefusedAsync(clientId, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogError(e, "Couldn't log the refused call from {ClientId}", clientId);
        }
    }
}
