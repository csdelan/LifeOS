using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests.Scale;

/// <summary>
/// The meter the hot-path budgets rest on (<see cref="QueryMeter"/>) counts what a statement read:
/// a scan of the whole log reads every row, a lookup by key one, and a function's own statements
/// are counted beside its caller's. A meter that caught nothing would let every budget pass.
/// </summary>
public sealed class QueryMeterTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_scan_of_the_whole_log_reads_every_row_and_a_lookup_one()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var kernel = QueryMeter.Kernel(db);
        var source = kernel.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await source.OpenConnectionAsync(Ct);

        // A filter no index answers.
        var (events, scan) = await QueryMeter.MeasureAsync(() => connection.QuerySingleAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE length(payload::text) > 0;", cancellationToken: Ct)));
        Assert.Equal(1, scan.Count);
        Assert.Single(scan.FullScans);
        Assert.Equal(events, scan.EventRowsRead);

        var id = await connection.QuerySingleAsync<Guid>(new CommandDefinition("SELECT id FROM bsk.event LIMIT 1;", cancellationToken: Ct));
        var (_, lookup) = await QueryMeter.MeasureAsync(() => connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT kind FROM bsk.event WHERE id = @id;", new { id }, cancellationToken: Ct)));
        Assert.Equal(1, lookup.EventRowsRead);
        Assert.Empty(lookup.FullScans);
        Assert.Equal(1, lookup.CountOf("SELECT kind FROM bsk.event WHERE id = @other;"));
    }

    [Fact]
    public async Task A_functions_statements_are_counted_beside_its_callers()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var kernel = QueryMeter.Kernel(db);
        await using var connection = await kernel.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(Ct);

        // An SQL function with an aggregate in it isn't inlined: it runs as a statement of its own.
        var (_, cost) = await QueryMeter.MeasureAsync(() => connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
            "SELECT bsk.ledger_opened_at();", cancellationToken: Ct)));
        Assert.Equal(2, cost.Count);
        Assert.True(cost.EventRowsRead > 0, cost.Describe());
    }

    [Fact]
    public async Task What_runs_outside_a_measure_is_not_counted()
    {
        await using var db = await DomainTestDatabase.CreateSeededAsync(postgres, Ct);
        await using var kernel = QueryMeter.Kernel(db);
        await using var connection = await kernel.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition("SELECT count(*) FROM bsk.event;", cancellationToken: Ct));

        var (_, cost) = await QueryMeter.MeasureAsync(() => connection.QuerySingleAsync<int>(new CommandDefinition("SELECT 1;", cancellationToken: Ct)));
        Assert.Equal(1, cost.Count);
        Assert.Equal(0, cost.EventRowsRead);
    }
}
