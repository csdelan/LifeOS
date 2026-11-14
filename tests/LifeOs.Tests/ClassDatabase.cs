using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// A database of one test class's own, copied from the template when the class starts and dropped
/// when it ends (code review CR39). The class's tests share it, one at a time as xUnit runs a
/// class's tests, but no other class sees its rows, so the class needs no collection and runs
/// alongside the others. Its home zone is <see cref="Zone"/>, and a kernel on it comes from
/// <see cref="Kernel"/> or <see cref="Services"/>, in that zone (code review CR88): a kernel in
/// another dated the ledger's and the domain fold's days in one zone and the views in another,
/// which the API refuses to start on, and now the ledger and the domain credit refuse too.
/// <para>
/// A test whose answer depends on every row in the database, such as one that reads the wallet,
/// settles a fold or totals a domain's credit, takes a <see cref="DomainTestDatabase"/> of its own
/// instead, as does a test about another zone.
/// </para>
/// </summary>
public sealed class ClassDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    private DomainTestDatabase? database;

    /// <summary>The database's home zone (<c>bsk.home_zone</c>), which every kernel on it is given.</summary>
    public string Zone => PostgresFixture.HabitViewZone;

    public string ConnectionString => Database.ConnectionString;

    private DomainTestDatabase Database
        => database ?? throw new InvalidOperationException("The class's database isn't made yet.");

    /// <summary>The kernel's services on this database, in its zone, for a test to add a clock or a fake to.</summary>
    public IServiceCollection Services(string sourceId = KernelSources.Cli, decimal? coinValueUsd = null)
        => Database.Services(sourceId, coinValueUsd);

    /// <summary>A kernel on this database, in its zone.</summary>
    public ServiceProvider Kernel(string sourceId = KernelSources.Cli, decimal? coinValueUsd = null)
        => Services(sourceId, coinValueUsd).BuildServiceProvider();

    public async ValueTask InitializeAsync()
        => database = await DomainTestDatabase.CreateAsync(postgres, CancellationToken.None, PostgresFixture.HabitViewZone);

    public async ValueTask DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }
}
