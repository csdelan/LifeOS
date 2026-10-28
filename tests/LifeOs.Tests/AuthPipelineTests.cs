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
    public async Task Allowlisted_Google_sign_in_returns_email()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(SupabaseTokens.Claims(Allowed)));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(Allowed, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_allowlisted_Google_sign_in_is_403()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(SupabaseTokens.Claims("intruder@example.com")));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(SupabaseTokens.Password)]
    [InlineData(SupabaseTokens.MagicLink)]
    public async Task Allowlisted_account_signed_in_without_Google_is_403(string signIn)
    {
        // agent-access plan §4.4: a password an agent set on the account opens nothing here.
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(SupabaseTokens.Claims(Allowed, signIn: signIn)));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Email_only_in_user_metadata_is_403()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(SupabaseTokens.Claims(email: null, userMetadata: new()
            {
                ["email"] = Allowed,
                ["email_verified"] = true,
            })));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_how_it_signed_in_is_403()
    {
        // The old test shape: an email and email_verified, no amr or app_metadata.
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["email"] = Allowed,
                ["email_verified"] = true,
                ["role"] = "authenticated",
            }));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agent_token_is_403()
    {
        await using var host = await AuthTestHost.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", Token(SupabaseTokens.Claims(Allowed, clientId: "client-1")));
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
            Token(SupabaseTokens.Claims(Allowed), expires: DateTime.UtcNow.AddMinutes(-10)));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Supabase_shaped_Google_token_is_200()
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
                ["amr"] = new object[]
                {
                    new Dictionary<string, object> { ["method"] = "oauth", ["timestamp"] = 1793200000L },
                },
                ["aal"] = "aal1",
                ["session_id"] = "session-1",
                ["is_anonymous"] = false,
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
            "Bearer", Token(SupabaseTokens.Claims(Allowed), audience: "anon"));
        var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Token(
        Dictionary<string, object> claims,
        DateTime? expires = null,
        string audience = Audience)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
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
