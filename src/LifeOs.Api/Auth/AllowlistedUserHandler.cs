using Microsoft.AspNetCore.Authorization;

namespace LifeOs.Api.Auth;

public sealed class AllowlistedUserRequirement : IAuthorizationRequirement;

/// <summary>
/// Authenticated is not enough: the verified email must be in ALLOWED_EMAILS.
/// Everyone else is 403, even with a valid Supabase JWT. An agent's token (one with a
/// <c>client_id</c>, agent-access plan §4.4) is 403 too, so an agent can't reach
/// <c>/api/*</c>, which holds every write; agents use <c>/mcp</c>.
/// </summary>
public sealed class AllowlistedUserHandler(SupabaseAuthOptions options)
    : AuthorizationHandler<AllowlistedUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AllowlistedUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var email = EmailClaims.ReadEmail(context.User);
        if (string.IsNullOrWhiteSpace(email) || !EmailClaims.IsEmailVerified(context.User))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if (!options.IsAllowed(email) || AgentClaims.ClientId(context.User) is not null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
