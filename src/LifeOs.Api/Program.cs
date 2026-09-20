using Dapper;
using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using LifeOs.Api.Read;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.DependencyInjection;

namespace LifeOs.Api;

public static class Program
{
    public static void Main(string[] args)
    {
        // Postgres column names are snake_case; Dapper maps them to PascalCase
        // (expected_cadence -> ExpectedCadence). Same setting as the Pilot reader.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var builder = WebApplication.CreateBuilder(args);

        var ownerConnectionString = KernelConnectionString.Resolve(
            builder.Configuration["ConnectionStrings:Owner"]);
        var readerConnectionString = ReaderConnectionString.Resolve(
            builder.Configuration["ConnectionStrings:Reader"]);

        builder.Services.AddLifeOsKernel(ownerConnectionString, KernelSources.Api);
        builder.Services.AddSingleton(new SubjectReader(readerConnectionString));

        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("vite", policy =>
                policy.SetIsOriginAllowed(static origin =>
                    {
                        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                        {
                            return false;
                        }

                        return uri.Host is "localhost" or "127.0.0.1";
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        var app = builder.Build();

        app.UseKernelExceptionHandler();
        app.UseCors("vite");
        app.UseMiddleware<AuthSeamMiddleware>();

        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "LifeOS API");
            options.RoutePrefix = "swagger";
        });

        app.MapReadEndpoints();
        app.MapWriteEndpoints();

        app.Run();
    }
}
