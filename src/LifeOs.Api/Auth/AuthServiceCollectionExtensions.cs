using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    public const string AllowlistedUserPolicy = "AllowlistedUser";

    public static IServiceCollection AddLifeOsAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var auth = SupabaseAuthOptions.From(configuration);
        auth.Validate();
        services.AddSingleton(auth);
        services.AddSingleton<IAuthorizationHandler, AllowlistedUserHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureJwtBearer(options, auth));

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AllowlistedUserPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new AllowlistedUserRequirement());
            });
            options.FallbackPolicy = options.GetPolicy(AllowlistedUserPolicy);
        });

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, SupabaseAuthOptions auth)
    {
        options.MapInboundClaims = false;
        options.Authority = auth.Issuer;
        options.Audience = auth.Audience;
        options.RequireHttpsMetadata = auth.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.Issuer,
            ValidateAudience = true,
            ValidAudience = auth.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "email",
            RoleClaimType = "role",
        };

        SymmetricSecurityKey? hmac = null;
        if (!string.IsNullOrWhiteSpace(auth.JwtSecret))
        {
            hmac = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.JwtSecret))
            {
                KeyId = "supabase-legacy-hs256",
            };
            options.TokenValidationParameters.IssuerSigningKey = hmac;
        }

        if (!string.IsNullOrWhiteSpace(auth.JwksUrl))
        {
            var requireHttps = auth.JwksUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                auth.JwksUrl,
                new JwksConfigurationRetriever(auth.Issuer),
                new HttpDocumentRetriever { RequireHttps = requireHttps });
            return;
        }

        // HS256-only: a static configuration prevents JwtBearer from fetching
        // {Authority}/.well-known/openid-configuration.
        var configuration = new OpenIdConnectConfiguration { Issuer = auth.Issuer };
        if (hmac is not null)
        {
            configuration.SigningKeys.Add(hmac);
        }

        options.Configuration = configuration;
    }
}
