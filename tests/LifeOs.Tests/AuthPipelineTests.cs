using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Tests;

public sealed class AuthPipelineTests
{
    private const string Issuer = "https://test.supabase.co/auth/v1";
    private const string Audience = "authenticated";
    private const string Secret = "unit-test-hmac-secret-32bytes-min!";
    private const string Allowed = "owner@example.com";

    [Fact]
    public async Task Missing_token_is_401()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        var response = await host.Client.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Allowlisted_verified_token_returns_email()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(email: Allowed, verified: true));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(Allowed, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_allowlisted_verified_token_is_403()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(email: "intruder@example.com", verified: true));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_allowlisted_token_is_403()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(email: Allowed, verified: false));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_401()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Token(email: Allowed, verified: true, expires: DateTime.UtcNow.AddMinutes(-10)));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Google_token_with_nested_supabase_claims_is_200()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["email"] = Allowed,
                ["role"] = "authenticated",
                ["app_metadata"] = new Dictionary<string, object>
                {
                    ["provider"] = "google",
                    ["providers"] = new[] { "google" },
                },
                ["user_metadata"] = new Dictionary<string, object>
                {
                    ["email"] = Allowed,
                    ["email_verified"] = true,
                    ["iss"] = "https://accounts.google.com",
                },
            },
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_audience_is_401()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(email: Allowed, verified: true, audience: "anon"));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Token(
        string email,
        bool verified,
        DateTime? expires = null,
        string audience = Audience)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["email"] = email,
                ["email_verified"] = verified,
                ["role"] = "authenticated",
            },
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    private sealed class AuthTestHost : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }

        public static async Task<AuthTestHost> StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ApplicationName = "LifeOs.Api.AuthTests",
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = Issuer,
                ["Auth:Audience"] = Audience,
                ["Auth:JwtSecret"] = Secret,
                ["ALLOWED_EMAILS"] = Allowed,
            });
            builder.Services.AddLifeOsAuth(builder.Configuration);

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapAuthEndpoints();
            await app.StartAsync(cancellationToken);

            var url = app.Urls.Single();
            var client = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
            return new AuthTestHost { App = app, Client = client };
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
