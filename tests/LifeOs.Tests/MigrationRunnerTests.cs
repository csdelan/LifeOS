using Dapper;
using LifeOs.Infrastructure.Migrations;
using Npgsql;

namespace LifeOs.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationRunnerTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Records_applied_migrations_in_history_table()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
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
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
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
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
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
        var runner = new MigrationRunner(postgres.ConnectionString);

        var applied = await runner.ApplyAsync(Ct);

        Assert.Empty(applied);
    }
}
