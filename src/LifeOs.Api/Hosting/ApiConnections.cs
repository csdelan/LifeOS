using LifeOs.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Resolves the writer and reader connection strings. Development may fall back
/// to local Docker defaults; every other environment fails closed so a misconfigured
/// deploy cannot silently talk to localhost or start without credentials.
/// </summary>
public static class ApiConnections
{
    /// <summary>
    /// The connection the API writes on: as <c>bsk_writer</c>, from <c>ConnectionStrings:Writer</c>
    /// or <c>BSK_WRITER_CONNECTION_STRING</c> (migration 0071, code review CR126). Where neither is
    /// set it's still the owner's, from <c>ConnectionStrings:Owner</c> or <c>BSK_CONNECTION_STRING</c>,
    /// so staging keeps working between the push that makes the role and the deploy that gives the
    /// API its password (docs/pilot/deploy.md). The owner's is read only then, so an environment
    /// that has switched can drop it.
    /// </summary>
    public static string ResolveWriter(
        IConfiguration configuration,
        IHostEnvironment environment,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var readEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        foreach (var writer in new[] { configuration["ConnectionStrings:Writer"], readEnv(BskWriter.EnvironmentVariable) })
        {
            if (!string.IsNullOrWhiteSpace(writer))
            {
                return writer.Trim();
            }
        }

        return Resolve(
            configuration["ConnectionStrings:Owner"],
            KernelConnectionString.EnvironmentVariable,
            KernelConnectionString.LocalDevelopmentDefault,
            "BSK_WRITER_CONNECTION_STRING (or ConnectionStrings:Writer)",
            environment,
            readEnv);
    }

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

public sealed record ApiConnectionStrings(string Writer, string Reader);
