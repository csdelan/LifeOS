using Microsoft.AspNetCore.Authorization;

namespace LifeOs.Api.Auth;

public sealed class AllowlistedUserRequirement : IAuthorizationRequirement;

/// <summary>
/// Authenticated is not enough: the verified email must be in ALLOWED_EMAILS.
/// Everyone else is 403, even with a valid Supabase JWT.
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

        if (!options.IsAllowed(email))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
