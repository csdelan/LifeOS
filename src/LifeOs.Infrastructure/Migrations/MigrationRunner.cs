using Dapper;
using Npgsql;

namespace LifeOs.Infrastructure.Migrations;

/// <summary>
/// Applies versioned SQL migrations in order and records which have run, so the
/// runner is safe to invoke repeatedly. Already-applied migrations are skipped;
/// an already-applied migration whose SQL has since changed is treated as an
/// error rather than silently re-run.
/// </summary>
public sealed class MigrationRunner
{
    private readonly string _connectionString;
    private readonly EmbeddedMigrationSource _source;

    public MigrationRunner(string connectionString, EmbeddedMigrationSource? source = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _source = source ?? new EmbeddedMigrationSource();
    }

    /// <summary>
    /// Applies every migration that has not yet been recorded, in version order.
    /// Returns the migrations applied by this call (empty when already current).
    /// </summary>
    public async Task<IReadOnlyList<MigrationScript>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var scripts = _source.Load();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureHistoryTableAsync(connection, cancellationToken);
        var applied = await LoadAppliedAsync(connection, cancellationToken);

        var runNow = new List<MigrationScript>();

        foreach (var script in scripts)
        {
            if (applied.TryGetValue(script.Version, out var existingChecksum))
            {
                if (!string.Equals(existingChecksum, script.Checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Migration {script.Version:D4} ('{script.Name}') has already been applied but its " +
                        "contents have changed. Migrations are immutable once applied; add a new migration instead.");
                }

                continue;
            }

            await ApplyOneAsync(connection, script, cancellationToken);
            runNow.Add(script);
        }

        return runNow;
    }

    /// <summary>
    /// Creates the history table if needed and keeps it closed to everyone but its owner.
    /// Supabase serves the public schema through its REST API and grants the public anon key
    /// full access to new tables there, so the history could otherwise be read or rewritten
    /// from any browser. Row level security with no policies denies every other role; the
    /// runner owns the table, so it is unaffected. Runs on every migrate, so existing
    /// databases are closed on their next run.
    /// </summary>
    private static async Task EnsureHistoryTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS public.schema_migrations (
                version     bigint      PRIMARY KEY,
                name        text        NOT NULL,
                checksum    text        NOT NULL,
                applied_at  timestamptz NOT NULL DEFAULT now()
            );

            ALTER TABLE public.schema_migrations ENABLE ROW LEVEL SECURITY;

            -- Supabase's API roles; absent on plain Postgres.
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
                    REVOKE ALL ON public.schema_migrations FROM anon;
                END IF;
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
                    REVOKE ALL ON public.schema_migrations FROM authenticated;
                END IF;
            END $$;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    private static async Task<Dictionary<long, string>> LoadAppliedAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT version, checksum FROM public.schema_migrations;";
        var rows = await connection.QueryAsync<(long Version, string Checksum)>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Version, r => r.Checksum);
    }

    private static async Task ApplyOneAsync(
        NpgsqlConnection connection, MigrationScript script, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            script.Sql, transaction: transaction, cancellationToken: cancellationToken));

        const string record = """
            INSERT INTO public.schema_migrations (version, name, checksum)
            VALUES (@Version, @Name, @Checksum);
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            record,
            new { script.Version, script.Name, script.Checksum },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }
}
