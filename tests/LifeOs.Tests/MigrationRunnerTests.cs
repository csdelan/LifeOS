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
    public async Task Re_running_is_idempotent_and_applies_nothing()
    {
        // The fixture already applied every migration, so a second run is a no-op.
        var runner = new MigrationRunner(postgres.ConnectionString);

        var applied = await runner.ApplyAsync(Ct);

        Assert.Empty(applied);
    }
}
