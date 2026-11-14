using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LifeOs.Api.Auth;
using LifeOs.Api.Hosting;
using LifeOs.Api.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Tests;

public sealed class ApiHostTests
{
    private const string Issuer = "https://test.supabase.co/auth/v1";
    private const string Audience = "authenticated";
    private const string Secret = "unit-test-hmac-secret-32bytes-min!";
    private const string Allowed = "owner@example.com";
    private const string Unreachable =
        "Host=127.0.0.1;Port=1;Database=postgres;Username=postgres;Password=x;Timeout=1";

    [Fact]
    public void Production_refuses_to_start_without_allowlist()
    {
        var builder = CreateBuilder("Production", includeAuth: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Owner"] = Unreachable,
            ["ConnectionStrings:Reader"] = Unreachable,
            ["Auth:Issuer"] = Issuer,
            ["Auth:Audience"] = Audience,
            ["Auth:JwtSecret"] = Secret,
            ["ALLOWED_EMAILS"] = "",
            ["Auth:AllowedEmails"] = "",
        });
        var ex = Assert.Throws<InvalidOperationException>(() => ApiHost.ConfigureServices(builder));
        Assert.Contains("ALLOWED_EMAILS", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_is_anonymous_and_api_requires_auth()
    {
        await using var host = await StartAsync(Ct);
        var health = await host.Client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var me = await host.Client.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var subjects = await host.Client.GetAsync("/api/subjects", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, subjects.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_anonymous_and_503_when_database_is_unreachable()
    {
        await using var host = await StartAsync(Ct);
        var response = await host.Client.GetAsync("/health/ready", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("unready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allowlisted_token_reaches_me_non_allowlisted_is_403()
    {
        await using var host = await StartAsync(Ct);

        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        allowed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(Allowed));
        var ok = await host.Client.SendAsync(allowed, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        denied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("intruder@example.com"));
        var forbidden = await host.Client.SendAsync(denied, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Production_pipeline_does_not_expose_openapi_or_migrate_on_start()
    {
        await using var host = await StartAsync(Ct, environmentName: "Production");
        var openapi = await host.Client.GetAsync("/openapi/v1.json", Ct);
        Assert.NotEqual(HttpStatusCode.OK, openapi.StatusCode);

        var hosted = host.App.Services.GetServices<IHostedService>()
            .Select(service => service.GetType().FullName ?? "")
            .ToList();
        Assert.DoesNotContain(
            hosted,
            name => name.Contains("Migrat", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(host.App.Services.GetService<ApiConnectionStrings>());
    }

    [Fact]
    public void Same_origin_csp_includes_supabase_and_google()
    {
        var options = new SupabaseAuthOptions { Issuer = Issuer };
        options.Normalize();
        var csp = SecurityHeaders.BuildContentSecurityPolicy(options, includeStrictCsp: true);
        Assert.Contains("https://test.supabase.co", csp, StringComparison.Ordinal);
        Assert.Contains("wss://test.supabase.co", csp, StringComparison.Ordinal);
        Assert.Contains("https://accounts.google.com", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public void Csp_lets_cover_images_load_from_unsplash_and_nothing_more()
    {
        var options = new SupabaseAuthOptions { Issuer = Issuer };
        options.Normalize();
        var directives = SecurityHeaders.BuildContentSecurityPolicy(options, includeStrictCsp: true)
            .Split("; ")
            .ToDictionary(d => d.Split(' ')[0], d => d);

        Assert.Equal("img-src 'self' data: blob: https://images.unsplash.com", directives["img-src"]);
        // The browser never calls Unsplash's API: the picker searches through ours.
        Assert.DoesNotContain("unsplash", directives["connect-src"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("default-src 'self'", directives["default-src"]);

        // Development sends no policy at all.
        Assert.Equal("", SecurityHeaders.BuildContentSecurityPolicy(options, includeStrictCsp: false));
    }

    [Fact]
    public async Task Production_pages_may_record_and_play_back_voice_captures()
    {
        // Voice capture (CAP-2) was built in mock mode, which sends no headers, and staging's
        // microphone=() refused the mic whatever Chrome's site settings said.
        await using var host = await StartAsync(Ct, environmentName: "Production");
        var response = await host.Client.GetAsync("/health", Ct);

        var permissions = Assert.Single(response.Headers.GetValues("Permissions-Policy"))
            .Split(", ")
            .ToDictionary(p => p.Split('=')[0], p => p.Split('=')[1]);
        Assert.Equal("(self)", permissions["microphone"]);
        Assert.Equal("(self)", permissions["camera"]);
        Assert.Equal("()", permissions["geolocation"]);

        // The recording plays back from a blob: URL before it's saved, and kept audio in the
        // Inbox does too, so media-src must allow blob: (it would otherwise fall back to 'self').
        var media = Assert.Single(response.Headers.GetValues("Content-Security-Policy"))
            .Split("; ")
            .Single(d => d.StartsWith("media-src ", StringComparison.Ordinal));
        Assert.Equal("media-src 'self' blob:", media);
    }

    [Fact]
    public void Spa_fallback_does_not_capture_api_or_health()
    {
        Assert.True(SpaFallback.IsReserved("/api/subjects"));
        Assert.True(SpaFallback.IsReserved("/health/ready"));
        Assert.True(SpaFallback.IsReserved("/assets/browse-page-Ab12.js"));
        Assert.False(SpaFallback.IsReserved("/inbox"));
    }

    /// <summary>
    /// After a deploy, a page loaded from the old build asks for files the new one doesn't have.
    /// Each must answer 404, not the app's page, and every copy of the app's page must say not to
    /// cache it, a link straight to a page included (code review CR93).
    /// </summary>
    [Fact]
    public async Task A_removed_build_file_is_404_and_no_copy_of_the_page_is_cached()
    {
        var webRoot = Directory.CreateTempSubdirectory("lifeos-webroot-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, "index.html"), "<!doctype html><title>LifeOS</title>", Ct);
            Directory.CreateDirectory(Path.Combine(webRoot.FullName, "assets"));
            await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, "assets", "app-New1.js"), "export {};", Ct);
            await using var host = await StartAsync(Ct, webRoot: webRoot.FullName);

            var kept = await host.Client.GetAsync("assets/app-New1.js", Ct);
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
            Assert.Contains("immutable", kept.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);

            var removed = await host.Client.GetAsync("assets/browse-page-Old1.js", Ct);
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);

            foreach (var page in new[] { "", "index.html", "browse?selected=goal-lifeos", "inbox" })
            {
                var response = await host.Client.GetAsync(page, Ct);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                Assert.True(response.Headers.CacheControl?.NoStore, $"/{page} may be cached: {response.Headers.CacheControl}");
            }
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_PORT_fails_closed()
    {
        var builder = CreateBuilder("Production", includeAuth: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PORT"] = "not-a-port" });
        var ex = Assert.Throws<InvalidOperationException>(() => ApiHost.BindListenPort(builder));
        Assert.Contains("PORT", ex.Message, StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationBuilder CreateBuilder(string environmentName, bool includeAuth, string? webRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ApplicationName = "LifeOs.Api.HostTests",
            WebRootPath = webRoot,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (includeAuth)
        {
            builder.Configuration.AddInMemoryCollection(AuthSettings());
        }

        return builder;
    }

    private static Dictionary<string, string?> AuthSettings() => new()
    {
        ["Auth:Issuer"] = Issuer,
        ["Auth:Audience"] = Audience,
        ["Auth:JwtSecret"] = Secret,
        ["ALLOWED_EMAILS"] = Allowed,
        ["ConnectionStrings:Owner"] = Unreachable,
        ["ConnectionStrings:Reader"] = Unreachable,
        ["VITE_SUPABASE_URL"] = "https://placeholder.supabase.co",
        ["VITE_SUPABASE_ANON_KEY"] = "placeholder-anon-key",
    };

    private static async Task<TestHost> StartAsync(
        CancellationToken cancellationToken,
        string environmentName = "Development",
        string? webRoot = null)
    {
        var builder = CreateBuilder(environmentName, includeAuth: true, webRoot);
        ApiHost.ConfigureServices(builder);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        ApiHost.ConfigurePipeline(app);
        await app.StartAsync(cancellationToken);
        var url = app.Urls.Single();
        var client = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
        return new TestHost { App = app, Client = client };
    }

    private static string Token(string email)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = SupabaseTokens.Claims(email),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    private sealed class TestHost : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
