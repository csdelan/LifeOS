using Microsoft.AspNetCore.Authorization;

namespace LifeOs.Api.Auth;

public sealed class AllowlistedUserRequirement : IAuthorizationRequirement;

/// <summary>
/// Authenticated is not enough: the email must be in ALLOWED_EMAILS, and the session must
/// have signed in with Google (agent-access plan §4.4). A password or magic-link session for
/// the same account is 403, so a password set on it, by an agent holding one of its tokens
/// say, can't reach <c>/api/*</c>. An agent's token (one with a <c>client_id</c>) is 403 too,
/// since <c>/api/*</c> holds every write; agents use <c>/mcp</c>.
/// </summary>
public sealed class AllowlistedUserHandler(SupabaseAuthOptions options, ILogger<AllowlistedUserHandler>? logger = null)
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

        var email = SupabaseClaims.ReadEmail(context.User);
        if (email is null || !options.IsAllowed(email) || AgentClaims.ClientId(context.User) is not null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if (!SupabaseClaims.SignedInWithGoogle(context.User))
        {
            // The right account, signed in some other way: worth a look in the logs.
            logger?.LogWarning(
                "Refused a session for an allowed account: not a Google sign-in (sign-in methods: {Methods}; Google account: {GoogleAccount})",
                string.Join(", ", SupabaseClaims.SignInMethods(context.User)),
                SupabaseClaims.IsGoogleAccount(context.User));
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
