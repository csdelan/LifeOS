using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The event-log indexes (migration 0056 and later) are ones the planner can use for the reads they were
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

    // Code review CR32 (migration 0059): the food views carry their partial indexes' condition.
    [Theory]
    [InlineData("SELECT * FROM bsk.v_meal;", "event_meal_idx")]
    [InlineData("SELECT * FROM bsk.v_fast;", "event_fast_idx")]
    [InlineData("SELECT * FROM bsk.v_fast;", "event_meal_idx")]
    [InlineData("SELECT * FROM bsk.v_food_day;", "event_food_day_idx")]
    public async Task The_food_views_read_through_their_indexes(string query, string index)
        => Assert.Contains(index, await PlanAsync(query));

    [Fact]
    public async Task Fasts_count_their_meals_from_one_fold_of_the_food_log()
        => Assert.Equal(1, (await PlanAsync("SELECT * FROM bsk.v_fast;")).Split("event_meal_idx").Length - 1);

    // Code review CR33 (migration 0060): the event that carries a stored file is found by index.
    [Fact]
    public async Task A_stored_files_event_is_looked_up_by_index()
        => Assert.Contains("event_artifact_idx", await PlanAsync(
            "SELECT * FROM bsk.v_artifact WHERE id = '00000000-0000-0000-0000-000000000001';"));

    // Code review CR34 (migration 0061): the ledger is read by subject and by trigger by index.
    [Theory]
    [InlineData("SELECT * FROM bsk.v_ledger WHERE subject_id = '00000000-0000-0000-0000-000000000001';", "event_ledger_subject_uuid_idx")]
    [InlineData("SELECT DISTINCT award_key FROM bsk.v_ledger WHERE trigger = 'domain_rank';", "event_ledger_trigger_idx")]
    [InlineData("SELECT e.id FROM bsk.event e WHERE e.kind = 'ledger' AND e.payload->>'award_key' IS NOT NULL AND e.payload->>'trigger' = ANY('{food_day,food_meal}');", "event_ledger_trigger_idx")]
    public async Task The_ledger_is_read_by_subject_and_trigger_by_index(string query, string index)
        => Assert.Contains(index, await PlanAsync(query));

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
