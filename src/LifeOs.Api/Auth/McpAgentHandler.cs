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
/// <c>amr</c>), and a client that isn't in <c>Mcp:BlockedClients</c>. Everyone else is 403.
/// </summary>
public sealed class McpAgentHandler(SupabaseAuthOptions auth, McpOptions mcp, ILogger<McpAgentHandler>? logger = null)
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

        var email = SupabaseClaims.ReadEmail(context.User);
        var clientId = AgentClaims.ClientId(context.User);
        if (email is null || !auth.IsAllowed(email) || clientId is null || mcp.IsBlocked(clientId))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if (!SupabaseClaims.IssuedToAgent(context.User))
        {
            logger?.LogWarning(
                "Refused an agent token for an allowed account: not issued by the OAuth server for a Google account (client {ClientId}; sign-in methods: {Methods}; Google account: {GoogleAccount})",
                clientId,
                string.Join(", ", SupabaseClaims.SignInMethods(context.User)),
                SupabaseClaims.IsGoogleAccount(context.User));
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
