using Dapper;
using LifeOs.Infrastructure.Migrations;
using Npgsql;

namespace LifeOs.Tests;

public sealed class MigrationRunnerTests(PostgresFixture postgres, ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Records_applied_migrations_in_history_table()
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);

        var versions = (await connection.QueryAsync<long>(
            new CommandDefinition(
                "SELECT version FROM public.schema_migrations ORDER BY version;",
                cancellationToken: Ct)))
            .ToList();

        Assert.Contains(1L, versions);
    }

    [Fact]
    public async Task Baseline_migration_created_the_bsk_schema()
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);

        var exists = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'bsk');",
                cancellationToken: Ct));

        Assert.True(exists);
    }

    [Fact]
    public async Task History_table_is_hidden_from_other_roles()
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);

        var rowSecurity = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT relrowsecurity FROM pg_class WHERE oid = 'public.schema_migrations'::regclass;",
                cancellationToken: Ct));
        Assert.True(rowSecurity);

        // A role granted SELECT, as Supabase's anon role is by default, still sees no rows.
        // CREATE ROLE is transactional, so the rollback leaves nothing behind.
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            CREATE ROLE schema_migrations_probe NOLOGIN;
            GRANT SELECT ON public.schema_migrations TO schema_migrations_probe;
            SET LOCAL ROLE schema_migrations_probe;
            """,
            transaction: transaction,
            cancellationToken: Ct));

        var visible = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM public.schema_migrations;",
            transaction: transaction,
            cancellationToken: Ct));
        Assert.Equal(0L, visible);

        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task Re_running_is_idempotent_and_applies_nothing()
    {
        // The fixture already applied every migration, so a second run is a no-op.
        var runner = new MigrationRunner(classDb.ConnectionString);

        var applied = await runner.ApplyAsync(Ct);

        Assert.Empty(applied);
    }

    [Fact]
    public async Task A_pending_migration_numbered_below_an_applied_one_is_refused()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await ForgetAsync(database.ConnectionString, 10);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MigrationRunner(database.ConnectionString).ApplyAsync(Ct));

        Assert.Contains("0010", error.Message);
        Assert.Contains("--allow-out-of-order", error.Message);
        Assert.False(await RecordedAsync(database.ConnectionString, 10));
    }

    [Fact]
    public async Task A_changed_migration_is_refused_before_anything_is_applied()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await ForgetAsync(database.ConnectionString, 53);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE public.schema_migrations SET checksum = 'edited' WHERE version = 1;", cancellationToken: Ct));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MigrationRunner(database.ConnectionString).ApplyAsync(Ct));

        Assert.False(await RecordedAsync(database.ConnectionString, 53));
    }

    [Fact]
    public async Task Two_runners_on_an_empty_database_take_turns()
    {
        var name = $"migrate_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(classDb.ConnectionString))
        {
            await admin.ExecuteAsync(new CommandDefinition($"CREATE DATABASE {name};", cancellationToken: Ct));
        }

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(classDb.ConnectionString) { Database = name }.ConnectionString;
            var runs = await Task.WhenAll(
                new MigrationRunner(connectionString).ApplyAsync(Ct),
                new MigrationRunner(connectionString).ApplyAsync(Ct));

            var all = new EmbeddedMigrationSource().Load().Count;
            Assert.Equal([0, all], runs.Select(r => r.Count).Order());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(classDb.ConnectionString);
            await admin.ExecuteAsync(new CommandDefinition($"DROP DATABASE {name} WITH (FORCE);", cancellationToken: CancellationToken.None));
        }
    }

    /// <summary>Deletes a migration's history row, as if it had never been applied.</summary>
    private async Task ForgetAsync(string connectionString, long version)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM public.schema_migrations WHERE version = @version;", new { version }, cancellationToken: Ct));
    }

    private async Task<bool> RecordedAsync(string connectionString, long version)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.schema_migrations WHERE version = @version);",
            new { version }, cancellationToken: Ct));
    }
}
