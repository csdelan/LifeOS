using Npgsql;

namespace LifeOs.Infrastructure;

/// <summary>
/// The read-only <c>bsk_reader</c> role and how to connect as it. Consumers that
/// only read (Python, BI, ad-hoc queries) should use this role; it has SELECT
/// and USAGE but no write grants (see migration 0005).
/// </summary>
public static class BskReader
{
    public const string RoleName = "bsk_reader";

    /// <summary>
    /// Local-development password for the role, matching migration 0005.
    /// Production supplies its own credentials.
    /// </summary>
    public const string LocalDevelopmentPassword = "bsk_reader";

    /// <summary>
    /// Derives a bsk_reader connection string from an owner connection string,
    /// keeping host/port/database and swapping in the reader's credentials.
    /// </summary>
    public static string ConnectionStringFrom(string ownerConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = RoleName,
            Password = LocalDevelopmentPassword
        };
        return builder.ConnectionString;
    }
}
