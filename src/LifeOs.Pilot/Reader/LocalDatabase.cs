using Npgsql;

namespace LifeOs.Pilot.Reader;

/// <summary>
/// A quick, best-effort reachability probe for a PostgreSQL connection string.
/// Used at startup to decide whether the local DEV database is actually up, so
/// the pilot can fail over to the STAGING .env when it isn't.
/// </summary>
internal static class LocalDatabase
{
    /// <summary>
    /// Tries to open <paramref name="connectionString"/> with a short timeout.
    /// Returns false on any failure (no server, bad credentials, timeout) — the
    /// caller only needs a yes/no, not the reason.
    /// </summary>
    public static bool CanConnect(string connectionString)
    {
        try
        {
            // Keep the probe snappy so a missing local DB doesn't stall startup.
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 3 };
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
