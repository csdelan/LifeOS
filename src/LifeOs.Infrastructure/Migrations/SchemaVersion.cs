using Dapper;
using Npgsql;

namespace LifeOs.Infrastructure.Migrations;

/// <summary>
/// Whether this build is older than the database it's pointed at (code review CR66): the newest
/// migration it ships against the newest one applied. A build made before a migration doesn't
/// know that migration's rules (a key a row must carry, a column to fill), so writing with it
/// leaves rows the newer code then has to cope with. The Pilot writes to staging through the
/// <c>bsk</c> <c>scripts/install-bsk.ps1</c> installed, which goes stale whenever a push migrates
/// staging and the script isn't run again.
/// </summary>
public static class SchemaVersion
{
    private static readonly Lazy<long> NewestShipped =
        new(() => new EmbeddedMigrationSource().Load().Select(s => s.Version).DefaultIfEmpty().Max());

    /// <summary>The newest migration this build ships.</summary>
    public static long Shipped => NewestShipped.Value;

    /// <summary>The newest migration applied to the database, or null when it has none yet.</summary>
    public static async Task<AppliedMigration?> NewestAppliedAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            return await connection.QueryFirstOrDefaultAsync<AppliedMigration>(new CommandDefinition(
                "SELECT version, name FROM public.schema_migrations ORDER BY version DESC LIMIT 1;",
                cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Never migrated: nothing is newer than this build.
            return null;
        }
    }

    /// <summary>Why a build shipping <paramref name="shipped"/> won't run against <paramref name="applied"/>, or null when it may.</summary>
    public static string? OlderThanDatabase(long shipped, AppliedMigration? applied)
        => applied is null || applied.Version <= shipped
            ? null
            : $"This bsk is older than the database: it knows migrations through {shipped:D4}, and the database has "
              + $"{applied.Version:D4} ({applied.Name}) applied. It won't run against a newer schema, whose rules it "
              + "doesn't know. Update it: run ./scripts/install-bsk.ps1 for the copy the Pilot uses, or build from the "
              + "latest main.";
}

/// <summary>A row of <c>public.schema_migrations</c>.</summary>
public sealed class AppliedMigration
{
    public long Version { get; set; }
    public string Name { get; set; } = "";
}
