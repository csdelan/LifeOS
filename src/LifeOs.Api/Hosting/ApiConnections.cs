using LifeOs.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Resolves the owner and reader connection strings. Development may fall back
/// to local Docker defaults; every other environment fails closed so a misconfigured
/// deploy cannot silently talk to localhost or start without credentials.
/// </summary>
public static class ApiConnections
{
    public static string ResolveOwner(
        IConfiguration configuration,
        IHostEnvironment environment,
        Func<string, string?>? getEnvironmentVariable = null)
        => Resolve(
            configuration["ConnectionStrings:Owner"],
            KernelConnectionString.EnvironmentVariable,
            KernelConnectionString.LocalDevelopmentDefault,
            "BSK_CONNECTION_STRING (or ConnectionStrings:Owner)",
            environment,
            getEnvironmentVariable);

    public static string ResolveReader(
        IConfiguration configuration,
        IHostEnvironment environment,
        Func<string, string?>? getEnvironmentVariable = null)
        => Resolve(
            configuration["ConnectionStrings:Reader"],
            ReaderConnectionString.EnvironmentVariable,
            ReaderConnectionString.LocalDevelopmentDefault,
            "BSK_READER_CONNECTION_STRING (or ConnectionStrings:Reader)",
            environment,
            getEnvironmentVariable);

    private static string Resolve(
        string? configured,
        string environmentVariable,
        string localDevelopmentDefault,
        string label,
        IHostEnvironment environment,
        Func<string, string?>? getEnvironmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var readEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var fromEnvironment = readEnv(environmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        if (environment.IsDevelopment())
        {
            return localDevelopmentDefault;
        }

        throw new InvalidOperationException(
            $"{label} must be set outside Development. The API will not start with the local Docker default.");
    }
}

public sealed record ApiConnectionStrings(string Owner, string Reader);
