namespace LifeOs.Tests;

/// <summary>
/// A database of one test class's own, copied from the template when the class starts and dropped
/// when it ends (code review CR39). The class's tests share it, one at a time as xUnit runs a
/// class's tests, but no other class sees its rows, so the class needs no collection and runs
/// alongside the others. Its home zone is UTC (<see cref="PostgresFixture.HabitViewZone"/>). A
/// test whose answer depends on every row in the database takes a <see cref="DomainTestDatabase"/>
/// of its own instead.
/// </summary>
public sealed class ClassDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    private DomainTestDatabase? database;

    public string ConnectionString
        => database?.ConnectionString ?? throw new InvalidOperationException("The class's database isn't made yet.");

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
