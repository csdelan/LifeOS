namespace LifeOs.Pilot.Reader;

/// <summary>
/// Resolves the read-only PostgreSQL connection string the pilot uses. Prefers
/// the <c>BSK_READER_CONNECTION_STRING</c> environment variable (Npgsql format),
/// then falls back to the local-development <c>bsk_reader</c> credentials from
/// migration 0005. The pilot only ever reads through this role; every write goes
/// through the <c>bsk</c> CLI.
/// </summary>
public static class ReaderConnectionString
{
    public const string EnvironmentVariable = "BSK_READER_CONNECTION_STRING";

    /// <summary>
    /// Local Docker Postgres, read-only role. Development-only credentials that
    /// match <c>docker-compose.yml</c> and migration 0005; production supplies
    /// its own via the environment variable.
    /// </summary>
    public const string LocalDevelopmentDefault =
        "Host=localhost;Port=5432;Database=lifeos;Username=bsk_reader;Password=bsk_reader";

    public static string Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? LocalDevelopmentDefault : fromEnvironment;
    }
}
