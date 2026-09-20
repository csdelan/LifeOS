using System.Security.Claims;
using LifeOs.Api.Auth;

namespace LifeOs.Api.Http;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .WithTags("Auth")
            .RequireAuthorization(AuthServiceCollectionExtensions.AllowlistedUserPolicy);

        api.MapGet("/me", GetMe).WithName("GetMe").Produces<MeResponse>();
    }

    private static IResult GetMe(ClaimsPrincipal user)
    {
        var email = EmailClaims.ReadEmail(user);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Forbid();
        }

        return Results.Ok(new MeResponse(email));
    }
}

public sealed record MeResponse(string Email);
