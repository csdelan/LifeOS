using Dapper;
using Npgsql;

namespace LifeOs.Infrastructure.Migrations;

/// <summary>
/// Applies versioned SQL migrations in order and records which have run, so the
/// runner is safe to invoke repeatedly. Already-applied migrations are skipped;
/// an already-applied migration whose SQL has since changed, or a pending one
/// numbered below the highest applied, is treated as an error rather than run.
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

    /// <summary>How long one migration waits for a table lock before it gives up.</summary>
    /// <remarks>
    /// A migration that waits on a long read also blocks every query queued behind it, which
    /// stalls the API. Failing fast leaves the database as it was, and the run can be retried.
    /// </remarks>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Applies every migration that has not yet been recorded, in version order.
    /// Returns the migrations applied by this call (empty when already current).
    /// </summary>
    public Task<IReadOnlyList<MigrationScript>> ApplyAsync(CancellationToken cancellationToken = default)
        => ApplyAsync(allowOutOfOrder: false, cancellationToken);

    /// <summary>
    /// Applies every migration that has not yet been recorded, in version order, holding a
    /// session advisory lock so two runners (a deploy and a manual run, say) take turns.
    /// Nothing is applied when any applied migration has changed, or, unless
    /// <paramref name="allowOutOfOrder"/>, when a pending migration is numbered below the
    /// highest applied one: that is a migration renumbered or merged after a later one ran.
    /// </summary>
    public async Task<IReadOnlyList<MigrationScript>> ApplyAsync(
        bool allowOutOfOrder, CancellationToken cancellationToken = default)
    {
        var scripts = _source.Load();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Advisory locks are per database, so test databases on one server don't wait on each other.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_lock(hashtextextended('bsk.migrate', 0));", cancellationToken: cancellationToken));
        try
        {
            await EnsureHistoryTableAsync(connection, cancellationToken);
            var applied = await LoadAppliedAsync(connection, cancellationToken);
            var pending = Pending(scripts, applied, allowOutOfOrder);

            foreach (var script in pending)
            {
                await ApplyOneAsync(connection, script, cancellationToken);
            }

            return pending;
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_unlock(hashtextextended('bsk.migrate', 0));", cancellationToken: CancellationToken.None));
        }
    }

    /// <summary>The scripts to apply, after checking the applied ones are unchanged and in order.</summary>
    private static List<MigrationScript> Pending(
        IReadOnlyList<MigrationScript> scripts, Dictionary<long, string> applied, bool allowOutOfOrder)
    {
        var pending = new List<MigrationScript>();
        foreach (var script in scripts)
        {
            if (!applied.TryGetValue(script.Version, out var existingChecksum))
            {
                pending.Add(script);
                continue;
            }

            if (!string.Equals(existingChecksum, script.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Migration {script.Version:D4} ('{script.Name}') has already been applied but its " +
                    "contents have changed. Migrations are immutable once applied; add a new migration instead.");
            }
        }

        if (!allowOutOfOrder && applied.Count > 0)
        {
            var highest = applied.Keys.Max();
            var early = pending.FirstOrDefault(s => s.Version < highest);
            if (early is not null)
            {
                throw new InvalidOperationException(
                    $"Migration {early.Version:D4} ('{early.Name}') is numbered below {highest:D4}, the highest " +
                    "migration already applied, so it was probably renumbered or merged after a later one ran. " +
                    $"Renumber it above {highest:D4}, or run `bsk migrate --allow-out-of-order` if applying it " +
                    "out of order is intended. Nothing was applied.");
            }
        }

        return pending;
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
            $"SET LOCAL lock_timeout = '{(int)LockTimeout.TotalMilliseconds}ms';",
            transaction: transaction, cancellationToken: cancellationToken));

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
