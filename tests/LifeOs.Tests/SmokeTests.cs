using Dapper;
using Npgsql;

namespace LifeOs.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SmokeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Connects_to_postgres_and_reads_select_1()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1;", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, result);
    }
}
