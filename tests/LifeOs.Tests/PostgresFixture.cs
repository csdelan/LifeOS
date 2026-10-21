using LifeOs.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LifeOs.Tests;

/// <summary>
/// Spins up a real PostgreSQL instance in a container for the duration of the
/// test run and applies every migration against it. Integration tests take a
/// dependency on this fixture and share the one container.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    /// <summary>Connection string for the running container's default database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// The home zone the habit views read "today" in (migration 0033). UTC, so the views agree
    /// with the tests' own <c>DateTime.UtcNow</c> dates whatever the hour; a test that checks
    /// another zone sets <c>bsk.home_zone</c> on its own session.
    /// </summary>
    public const string HabitViewZone = "UTC";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Before any connection is pooled: a database setting reaches new sessions only.
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DO $$ BEGIN EXECUTE format('ALTER DATABASE %I SET bsk.home_zone = %L', current_database(), '{HabitViewZone}'); END $$;",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        NpgsqlConnection.ClearAllPools();

        var runner = new MigrationRunner(ConnectionString);
        await runner.ApplyAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
