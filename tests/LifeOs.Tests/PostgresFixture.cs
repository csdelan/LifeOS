using LifeOs.Infrastructure.Migrations;
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

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

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
