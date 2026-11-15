using Dapper;
using LifeOs.Infrastructure.Migrations;
using Npgsql;

namespace LifeOs.Tests.Scale;

/// <summary>
/// The newest migrations applied to a database that holds three years of use (code review R13),
/// as a deploy applies them to staging: the template is migrated empty, so a migration whose
/// rebuilt index, new CHECK or rewritten view fails on rows, or turns slow on them, shows here
/// first. The database is migrated to just before the newest, seeded (<c>Scale/seed.sql</c>),
/// and then <c>bsk migrate</c>'s runner applies the rest.
/// </summary>
public sealed class MigrationOnDataTests(PostgresFixture postgres)
{
    /// <summary>How many of the newest migrations are applied to the data.</summary>
    private const int Newest = 3;

    /// <summary>A generous ceiling for the newest migrations on three years of rows, for CI's slower runners.</summary>
    private static readonly TimeSpan MigrateTime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The views a newest migration changes the rows of on purpose, and why. A view whose rows
    /// change otherwise lost or gained some it shouldn't have.
    /// </summary>
    private static readonly Dictionary<string, string> Changed = new(StringComparer.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_newest_migrations_apply_to_a_database_holding_data_and_every_view_keeps_its_rows()
    {
        var scripts = new EmbeddedMigrationSource().Load().OrderBy(s => s.Version).ToList();
        var older = scripts.SkipLast(Newest).ToList();
        var name = $"migrate_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync(Ct);
            await admin.ExecuteAsync(new CommandDefinition($"CREATE DATABASE {name};", cancellationToken: Ct));
            await admin.ExecuteAsync(new CommandDefinition(
                $"ALTER DATABASE {name} SET bsk.home_zone = '{SeededTemplate.Zone}';", cancellationToken: Ct));
        }

        var connectionString = SeededTemplate.Unpooled(PostgresFixture.On(postgres.ConnectionString, name));
        try
        {
            await ApplyAsync(connectionString, older);
            await SeededTemplate.RunAsync(connectionString, SeededTemplate.SeedSql);
            var before = await CountViewsAsync(connectionString);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var applied = await new MigrationRunner(connectionString).ApplyAsync(Ct);
            clock.Stop();
            ScaleReport.Write($"migrate: {string.Join(", ", applied.Select(s => s.Version))} on the data in {clock.Elapsed.TotalSeconds:F1} s");
            Assert.Equal(scripts.TakeLast(Newest).Select(s => s.Version), applied.Select(s => s.Version));
            Assert.True(clock.Elapsed < MigrateTime, $"the newest migrations took {clock.Elapsed.TotalSeconds:F1} s on the data.");

            var after = await CountViewsAsync(connectionString);
            var changed = before
                .Where(b => after.TryGetValue(b.Key, out var rows) && rows != b.Value && !Changed.ContainsKey(b.Key))
                .Select(b => $"{b.Key}: {b.Value} rows, then {after[b.Key]}")
                .ToList();
            Assert.True(changed.Count == 0, $"the newest migrations changed what these views answer:\n{string.Join('\n', changed)}");
            Assert.True(after.GetValueOrDefault("bsk.v_meal") > 0, "the seed left no meals, so the views were read empty.");
        }
        finally
        {
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync(CancellationToken.None);
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE);");
        }
    }

    /// <summary>
    /// <paramref name="scripts"/> applied as the runner applies them: each in a transaction of its
    /// own, recorded in its history table, so the runner takes up from the next.
    /// </summary>
    private static async Task ApplyAsync(string connectionString, IEnumerable<MigrationScript> scripts)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            CREATE TABLE public.schema_migrations (
                version     bigint      PRIMARY KEY,
                name        text        NOT NULL,
                checksum    text        NOT NULL,
                applied_at  timestamptz NOT NULL DEFAULT now()
            );
            """, cancellationToken: Ct));
        foreach (var script in scripts)
        {
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            await connection.ExecuteAsync(new CommandDefinition(script.Sql, transaction: transaction, commandTimeout: 0, cancellationToken: Ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO public.schema_migrations (version, name, checksum) VALUES (@Version, @Name, @Checksum);",
                new { script.Version, script.Name, script.Checksum }, transaction, cancellationToken: Ct));
            await transaction.CommitAsync(Ct);
        }
    }

    /// <summary>The rows of every view in the kernel's schemas, each read whole.</summary>
    private static async Task<Dictionary<string, long>> CountViewsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        var views = await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT format('%I.%I', n.nspname, c.relname)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('v', 'm') AND n.nspname IN ('bsk', 'bsk_derived')
            ORDER BY 1;
            """, cancellationToken: Ct));
        var rows = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            try
            {
                rows[view] = await connection.QuerySingleAsync<long>(new CommandDefinition(
                    $"SELECT count(*) FROM {view};", commandTimeout: 0, cancellationToken: Ct));
            }
            catch (PostgresException e)
            {
                Assert.Fail($"{view} doesn't answer on the data: {e.MessageText}");
            }
        }

        return rows;
    }
}
