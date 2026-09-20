namespace LifeOs.Infrastructure;

/// <summary>
/// Resolves the read-only PostgreSQL connection string for <c>bsk_reader</c>
/// SELECT over <c>bsk.v_*</c> views. Prefers <c>BSK_READER_CONNECTION_STRING</c>,
/// then the local-development credentials that match <c>docker-compose.yml</c>
/// and migration 0005. Never used for writes — those go through Application
/// services on the owner connection.
/// </summary>
public static class ReaderConnectionString
{
    public const string EnvironmentVariable = "BSK_READER_CONNECTION_STRING";

    public const string LocalDevelopmentDefault =
        "Host=localhost;Port=5432;Database=lifeos;Username=bsk_reader;Password=bsk_reader";

    public static string Resolve(string? explicitValue = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? LocalDevelopmentDefault : fromEnvironment;
    }
}
