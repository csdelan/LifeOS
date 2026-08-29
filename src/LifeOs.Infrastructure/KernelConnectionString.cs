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
}
