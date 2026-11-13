using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using LifeOs.Api.Mcp;
using LifeOs.Api.Read;
using LifeOs.Application.Rewards;
using LifeOs.Domain;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Integrations;
using LifeOs.Infrastructure.Persistence;
using LifeOs.Infrastructure.Storage;
using Microsoft.AspNetCore.HttpOverrides;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Production hosting for the API + bundled SPA. Schema is never applied here —
/// <c>bsk migrate</c> in CI owns staging and production. Do not resolve or run
/// <c>MigrationRunner</c> on startup.
/// </summary>
public static class ApiHost
{
    public static void BindListenPort(WebApplicationBuilder builder)
    {
        var port = builder.Configuration["PORT"] ?? Environment.GetEnvironmentVariable("PORT");
        if (string.IsNullOrWhiteSpace(port))
        {
            return;
        }

        if (!int.TryParse(port, out var parsed) || parsed is < 1 or > 65535)
        {
            throw new InvalidOperationException($"PORT must be a valid TCP port, got '{port}'.");
        }

        builder.WebHost.UseUrls($"http://0.0.0.0:{parsed}");
    }

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = false;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "O";
            });
        }

        var owner = ApiConnections.ResolveOwner(builder.Configuration, builder.Environment);
        var reader = ApiConnections.ResolveReader(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new ApiConnectionStrings(owner, reader));
        // Coins are real money (REWARD-3): Rewards:CoinValueUsd, default $0.50; a bad value
        // fails startup rather than mis-pricing deposits. Rewards:HomeTimeZone (default
        // America/Los_Angeles) decides which day an earn counts on; a malformed one fails too.
        var homeZone = KernelSettings.ParseHomeTimeZone(builder.Configuration[HomeZone.ConfigurationKey]);
        // Artifact bytes (food photos) go to Supabase Storage when Storage:SupabaseSecretKey is
        // set; a malformed setting fails startup. Without it, photos are kept in the database only
        // when it's on this machine, and refused otherwise (Supabase's free plan holds 500 MB), as
        // bsk does (code review CR71).
        var storage = SupabaseStorageOptions.Resolve(key => builder.Configuration[key]);
        // Oura (activity plan §3.1) is optional: without Oura:ClientId the Activity page says it
        // isn't set up. A client id without Integrations:TokenKey, or a malformed one, fails startup.
        // Unsplash (cover-images plan §3.2) is optional too: without Unsplash:AccessKey the cover
        // picker says covers aren't set up; covers already set still show.
        var integrations = IntegrationSettings.Resolve(key => builder.Configuration[key]);
        builder.Services.AddLifeOsKernel(KernelSettings.Resolve(
            owner, KernelSources.Api,
            KernelSettings.ParseCoinValueUsd(builder.Configuration[CoinRates.ConfigurationKey]),
            homeZone,
            storage,
            integrations: integrations));
        // The zone must be the database's own bsk.home_zone too; startup fails when it isn't.
        builder.Services.AddHostedService(sp => new HomeZoneStartupCheck(
            KernelDataSource.For(owner), homeZone, sp.GetRequiredService<ILogger<HomeZoneStartupCheck>>()));
        // What fell due while the machine was stopped is caught up before the API answers anything,
        // unless CatchUp:OnStart is false (code review CR57); a value that isn't true or false fails.
        if (StartupCatchUp.Enabled(builder.Configuration[StartupCatchUp.ConfigurationKey]))
        {
            builder.Services.AddHostedService<StartupCatchUp>();
        }
        // Every read on the reader connection, built from a kernel there with this host's settings
        // and clock (code review CR81).
        builder.Services.AddLifeOsReaders(reader);

        // The agents Chris allowed and what each called (agent-access plan §4.5), on the owner
        // connection: bsk_ops is never granted to bsk_reader. /mcp checks the list on every call.
        builder.Services.AddSingleton(new AgentAccessStore(owner, homeZone));

        // Fail closed: missing issuer/audience/JWKS-or-secret/ALLOWED_EMAILS throws.
        builder.Services.AddLifeOsAuth(builder.Configuration);

        // The read-only MCP server for Chris's agents (agent-access plan §4), off unless
        // Mcp:Enabled is true. Its tools read on the reader connection only.
        builder.AddLifeOsMcp();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        builder.Services.AddOpenApi(WebClientDocument.Configure);
        builder.Services.AddProblemDetails();

        var spaOrigins = SpaCors.ResolveOrigins(builder.Configuration);
        SpaCors.EnsureSafe(spaOrigins);
        if (spaOrigins.Length > 0)
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(SpaCors.PolicyName, policy =>
                    policy.WithOrigins(spaOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials());
            });
        }
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        app.UseKernelExceptionHandler();

        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders();
            app.UseHsts();
        }

        app.UseSecurityHeaders();
        var spaPublic = SpaPublicConfig.Resolve(app.Configuration, app.Environment);
        SpaPublicConfig.Write(
            app.Environment.WebRootPath, spaPublic.ApiBaseUrl, spaPublic.SupabaseUrl, spaPublic.SupabaseAnonKey,
            KernelSettings.ParseHomeTimeZone(app.Configuration[HomeZone.ConfigurationKey]));
        app.UseSpaStaticFiles();

        var spaOrigins = SpaCors.ResolveOrigins(app.Configuration);
        if (spaOrigins.Length > 0)
        {
            app.UseCors(SpaCors.PolicyName);
        }

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "LifeOS API");
                options.RoutePrefix = "swagger";
            });
        }

        app.MapHealthEndpoints();
        app.MapAuthEndpoints();
        app.MapReadEndpoints();
        app.MapWriteEndpoints();
        app.MapCaptureEndpoints();
        app.MapAppointmentEndpoints();
        app.MapTaskEndpoints();
        app.MapRewardEndpoints();
        app.MapDomainEndpoints();
        app.MapFoodEndpoints();
        app.MapEnvironmentEndpoints();
        app.MapMeasurementEndpoints();
        app.MapReviewEndpoints();
        app.MapArtifactEndpoints();
        app.MapOuraEndpoints();
        app.MapActivityEndpoints();
        app.MapCoverEndpoints();
        app.MapAgentEndpoints();
        app.MapLifeOsMcp();
        app.MapSpaFallback();
    }
}
