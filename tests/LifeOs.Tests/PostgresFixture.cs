using Dapper;
using LifeOs.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(LifeOs.Tests.PostgresFixture))]

namespace LifeOs.Tests;

/// <summary>
/// One PostgreSQL container for the whole test run. Every migration is applied once, to a
/// template database; the shared database and each test's own database
/// (<see cref="DomainTestDatabase"/>) are copies of it, which take a moment instead of a full
/// migration each.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>The migrated database every other one is copied from. Nothing connects to it.</summary>
    public const string TemplateDatabase = "lifeos_template";

    private const string SharedDatabase = "lifeos_shared";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    /// <summary>
    /// The shared database, for the tests in <see cref="PostgresCollection"/>. They see each
    /// other's rows, so a test whose answer depends on the whole database takes a
    /// <see cref="DomainTestDatabase"/> instead.
    /// </summary>
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
        var server = _container.GetConnectionString();

        await using (var admin = new NpgsqlConnection(server))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE {TemplateDatabase};");
        }

        await new MigrationRunner(On(server, TemplateDatabase)).ApplyAsync();

        // A copy refuses to start while anything is connected to its template.
        NpgsqlConnection.ClearAllPools();

        await CopyAsync(server, SharedDatabase, HabitViewZone, CancellationToken.None);
        ConnectionString = On(server, SharedDatabase);
    }

    /// <summary>
    /// A new database copied from the template, with <paramref name="zone"/> as its home zone
    /// (<c>bsk.home_zone</c>, which a copy doesn't inherit). Database settings reach new sessions
    /// only, so this runs before anything connects to it.
    /// </summary>
    internal static async Task CopyAsync(string adminConnectionString, string name, string zone, CancellationToken ct)
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync(ct);
        await admin.ExecuteAsync(new CommandDefinition(
            $"CREATE DATABASE {name} TEMPLATE {TemplateDatabase};", cancellationToken: ct));
        await admin.ExecuteAsync(new CommandDefinition(
            $"ALTER DATABASE {name} SET bsk.home_zone = '{zone}';", cancellationToken: ct));
    }

    /// <summary><paramref name="connectionString"/> pointed at the database <paramref name="database"/>.</summary>
    internal static string On(string connectionString, string database)
        => new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// The tests that use the shared database. They run one at a time, because some count or list
/// rows the others write. A new test that needs a database takes a
/// <see cref="DomainTestDatabase"/> and stays out of this collection, so it runs alongside.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection
{
    public const string Name = "postgres";
}
