using System.Net.Http.Headers;
using System.Text;
using Dapper;
using LifeOs.Api.Hosting;
using LifeOs.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Tests;

/// <summary>
/// The real API host on a random local port against the test database, with a client
/// that carries a valid token for the allowlisted owner — so endpoint tests send exactly
/// what the web client sends.
/// </summary>
internal sealed class ApiTestHost(WebApplication app, HttpClient client) : IAsyncDisposable
{
    private const string Issuer = "https://test.supabase.co/auth/v1";
    private const string Audience = "authenticated";
    private const string Secret = "unit-test-hmac-secret-32bytes-min!";
    private const string Allowed = "owner@example.com";

    public HttpClient Client { get; } = client;

    /// <summary>
    /// Starts the host. <paramref name="configuration"/> adds or overrides settings, e.g.
    /// <c>Rewards:CoinValueUsd</c>.
    /// </summary>
    public static async Task<ApiTestHost> StartAsync(
        string connectionString, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        // Program.Main sets this before building the host; the read endpoints need it
        // to map snake_case columns (item_id -> ItemId).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = "LifeOs.Api.Tests",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Issuer"] = Issuer,
            ["Auth:Audience"] = Audience,
            ["Auth:JwtSecret"] = Secret,
            ["ALLOWED_EMAILS"] = Allowed,
            ["ConnectionStrings:Owner"] = connectionString,
            ["ConnectionStrings:Reader"] = BskReader.ConnectionStringFrom(connectionString),
            ["VITE_SUPABASE_URL"] = "https://placeholder.supabase.co",
            ["VITE_SUPABASE_ANON_KEY"] = "placeholder-anon-key",
        };
        foreach (var (key, value) in configuration ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        ApiHost.ConfigureServices(builder);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        ApiHost.ConfigurePipeline(app);
        await app.StartAsync(cancellationToken);

        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single().TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        return new ApiTestHost(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }

    private static string Token()
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["email"] = Allowed,
                ["email_verified"] = true,
                ["role"] = "authenticated",
            },
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256),
        });
}
