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
/// The <c>McpAgent</c> policy that <c>/mcp</c> requires: an allowlisted, verified email (as the
/// web needs), a <c>client_id</c> (an agent's token, not a browser's), and a client that isn't
/// in <c>Mcp:BlockedClients</c>. Everyone else is 403.
/// </summary>
public sealed class McpAgentHandler(SupabaseAuthOptions auth, McpOptions mcp)
    : AuthorizationHandler<McpAgentRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        McpAgentRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var email = EmailClaims.ReadEmail(context.User);
        var clientId = AgentClaims.ClientId(context.User);
        if (string.IsNullOrWhiteSpace(email)
            || !EmailClaims.IsEmailVerified(context.User)
            || !auth.IsAllowed(email)
            || clientId is null
            || mcp.IsBlocked(clientId))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
