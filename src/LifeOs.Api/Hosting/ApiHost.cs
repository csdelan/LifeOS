using LifeOs.Api.Auth;
using LifeOs.Api.Http;
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
        var homeZone = ServiceCollectionExtensions.ParseHomeTimeZone(builder.Configuration[HomeZone.ConfigurationKey]);
        // Artifact bytes (food photos) go to Supabase Storage when Storage:SupabaseSecretKey is
        // set; a malformed setting fails startup. Outside Development, photos without it are
        // refused rather than kept in the database (Supabase's free plan holds 500 MB).
        var storage = SupabaseStorageOptions.Resolve(key => builder.Configuration[key]);
        // Oura (activity plan §3.1) is optional: without Oura:ClientId the Activity page says it
        // isn't set up. A client id without Integrations:TokenKey, or a malformed one, fails startup.
        var integrations = IntegrationSettings.Resolve(key => builder.Configuration[key]);
        builder.Services.AddLifeOsKernel(
            owner, KernelSources.Api,
            ServiceCollectionExtensions.ParseCoinValueUsd(builder.Configuration[CoinRates.ConfigurationKey]),
            homeZone,
            storage,
            allowPhotosInDatabase: builder.Environment.IsDevelopment(),
            integrations: integrations);
        builder.Services.AddSingleton(new SubjectReader(reader));
        builder.Services.AddSingleton(sp => new RewardReader(
            reader, sp.GetRequiredService<RewardService>().CoinValueUsd, homeZone));
        builder.Services.AddSingleton(new FoodReader(reader, homeZone));
        builder.Services.AddSingleton(sp => new ActivityReader(reader, homeZone, sp.GetRequiredService<IClock>()));
        builder.Services.AddSingleton(sp => new ArtifactReader(
            new NpgsqlArtifactBlobStore(reader, sp.GetService<IObjectStorage>())));

        // Fail closed: missing issuer/audience/JWKS-or-secret/ALLOWED_EMAILS throws.
        builder.Services.AddLifeOsAuth(builder.Configuration);

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

        builder.Services.AddOpenApi();
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
        SpaPublicConfig.Write(app.Environment.WebRootPath, spaPublic.ApiBaseUrl, spaPublic.SupabaseUrl, spaPublic.SupabaseAnonKey);
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
        app.MapRewardEndpoints();
        app.MapDomainEndpoints();
        app.MapFoodEndpoints();
        app.MapArtifactEndpoints();
        app.MapOuraEndpoints();
        app.MapActivityEndpoints();
        app.MapSpaFallback();
    }
}
