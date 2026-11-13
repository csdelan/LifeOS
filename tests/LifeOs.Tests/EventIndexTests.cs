using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The event-log indexes of migration 0056 are ones the planner can use for the reads they were
/// made for. An expression index whose expression differs from the query's by a cast or an
/// operator is silently never used, so each read is planned with sequential scans off and its
/// plan must name the index.
/// </summary>
public sealed class EventIndexTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Habit_check_offs_are_looked_up_by_index()
        => Assert.Contains("event_activity_occurrence_idx", await PlanAsync(
            "SELECT * FROM bsk_derived.habit_occurrence_source;"));

    [Fact]
    public async Task The_change_feed_reads_by_recorded_time_by_index()
        => Assert.Contains("event_recorded_at_idx", await PlanAsync(
            "SELECT id FROM bsk.v_event WHERE recorded_at >= now() - interval '1 day' ORDER BY recorded_at DESC LIMIT 50;"));

    [Fact]
    public async Task One_kind_over_a_time_range_is_read_by_index()
        => Assert.Contains("event_kind_occurred_idx", await PlanAsync(
            "SELECT id FROM bsk.event WHERE kind = 'note' AND occurred_at >= now() - interval '7 days' ORDER BY occurred_at DESC LIMIT 20;"));

    private async Task<string> PlanAsync(string query)
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition("SET enable_seqscan = off;", cancellationToken: Ct));
        var lines = await connection.QueryAsync<string>(new CommandDefinition($"EXPLAIN {query}", cancellationToken: Ct));
        return string.Join('\n', lines);
    }
}
