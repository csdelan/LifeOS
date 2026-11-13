using Npgsql;

namespace LifeOs.Infrastructure;

/// <summary>
/// Resolves the PostgreSQL connection string the kernel should use, preferring
/// an explicit value, then the <c>BSK_CONNECTION_STRING</c> environment
/// variable, then the local development default that matches
/// <c>docker-compose.yml</c>.
/// </summary>
public static class KernelConnectionString
{
    public const string EnvironmentVariable = "BSK_CONNECTION_STRING";

    /// <summary>
    /// Connection string for the local Docker Postgres described in the README.
    /// Development-only credentials; production supplies its own via the
    /// environment variable.
    /// </summary>
    public const string LocalDevelopmentDefault =
        "Host=localhost;Port=5432;Database=lifeos;Username=lifeos;Password=lifeos";

    public static string Resolve(string? explicitValue = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return LocalDevelopmentDefault;
    }

    /// <summary>
    /// Where <paramref name="connectionString"/> points, as <c>user@host:port/database</c>, without
    /// its password. Supabase's pooler serves every project from one host, so the user (which
    /// carries the project ref) is what tells staging from production.
    /// </summary>
    public static string Describe(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"{builder.Username}@{builder.Host}:{builder.Port}/{builder.Database}";
        }
        catch (ArgumentException)
        {
            return "a connection string that can't be read";
        }
    }

    /// <summary>Whether <paramref name="connectionString"/> is a database on this machine.</summary>
    public static bool IsLocal(string connectionString)
    {
        try
        {
            var hosts = new NpgsqlConnectionStringBuilder(connectionString).Host ?? "";
            return hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .All(h => h.StartsWith('/')
                    || h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || h is "127.0.0.1" or "::1" or "[::1]");
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// What <c>bsk</c> says on stderr before it runs when <see cref="EnvironmentVariable"/>, not a
    /// <c>--connection</c> given on the command line, points it at a database off this machine
    /// (code review CR59). A variable left in a shell, or saved as a user variable, otherwise turns
    /// every "dev" <c>bsk</c> call into a write to staging, where the log keeps it. Null when there's
    /// nothing to say.
    /// </summary>
    public static string? AmbientRemoteNotice(string? explicitValue, string? environmentValue)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue) || string.IsNullOrWhiteSpace(environmentValue)
            || IsLocal(environmentValue))
        {
            return null;
        }

        return $"bsk: {EnvironmentVariable} points at {Describe(environmentValue)}, not this machine.";
    }
}
