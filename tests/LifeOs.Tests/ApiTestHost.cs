using System.Net.Http.Headers;
using System.Text;
using Dapper;
using LifeOs.Api.Hosting;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

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
    /// <c>Rewards:CoinValueUsd</c>; <paramref name="services"/> adds services after the host's
    /// own, e.g. an in-memory object store.
    /// </summary>
    public static async Task<ApiTestHost> StartAsync(
        string connectionString, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? services = null)
    {
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
            // A test's host doesn't catch up as it starts, so what a test seeds before it stays as
            // seeded; StartupCatchUpTests turns it on.
            ["CatchUp:OnStart"] = "false",
        };
        foreach (var (key, value) in configuration ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        // The API refuses a home zone that isn't the database's own (code review CR22), so a test
        // that doesn't name one gets the database's.
        if (!settings.ContainsKey(HomeZone.ConfigurationKey))
        {
            await using var connection = new NpgsqlConnection(connectionString);
            settings[HomeZone.ConfigurationKey] = await connection.ExecuteScalarAsync<string>(
                new CommandDefinition("SELECT bsk.home_zone();", cancellationToken: cancellationToken));
        }

        builder.Configuration.AddInMemoryCollection(settings);

        ApiHost.ConfigureServices(builder);
        builder.Logging.ClearProviders();
        // After clearing, so a test can add a logger provider (LogCapture) and read what was logged.
        services?.Invoke(builder.Services);
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

    private static string Token() => TokenFor();

    /// <summary>
    /// A token shaped like Supabase's (see <see cref="SupabaseTokens"/>): by default the
    /// allowlisted owner's web session, signed in with Google. With <paramref name="clientId"/>,
    /// an agent's token, as Supabase's OAuth server issues one (agent-access plan §4.4).
    /// </summary>
    public static string TokenFor(string email = Allowed, string? clientId = null, bool google = true, string? signIn = null)
    {
        var claims = SupabaseTokens.Claims(email, clientId, signIn, google);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>The issuer the host trusts, which the MCP metadata names as the authorization server.</summary>
    public const string TrustedIssuer = Issuer;
}
