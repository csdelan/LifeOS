using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using LifeOs.Api.Read;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
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
        builder.Services.AddLifeOsKernel(owner, KernelSources.Api);
        builder.Services.AddSingleton(new SubjectReader(reader));

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
        app.MapSpaFallback();
    }
}
