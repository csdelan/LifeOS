using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Migration 0062 (code review CR35): the event log refuses a payload value that a view casts with
/// no guard and would fail on. The log is append-only, so such a row would break that view for good.
/// Each payload the kernel writes, and each with the key left out, is still taken.
/// </summary>
public sealed class PayloadCheckTests(PostgresFixture postgres)
{
    private const string Id = "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("archive_change", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b", "archived": true}""")]
    [InlineData("archive_change", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b"}""")]
    [InlineData("triage", """{"item_kind": "event", "item_id": "6F1C2B9E-3A4D-4E5F-8A7B-1C2D3E4F5A6B", "state": "dismissed"}""")]
    [InlineData("activity", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b", "occurrence": "2026-02-28", "result": "done"}""")]
    [InlineData("activity", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b", "timer": "start"}""")]
    [InlineData("activity", """{"meal": {"id": "x"}}""")]
    [InlineData("ledger", """{"entry": "earn", "amounts": {"coins": 10, "health_xp": -25}, "subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b"}""")]
    [InlineData("ledger", """{"entry": "redeem", "amounts": {"coins": -400}, "dollars": 4.00, "coin_rate": 0.01, "subject_id": ""}""")]
    [InlineData("ledger", """{"entry": "earn", "amounts": {}}""")]
    public async Task A_payload_the_views_can_read_is_taken(string kind, string payload)
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await InsertAsync(database, kind, payload);
    }

    [Theory]
    [InlineData("archive_change", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b", "archived": "maybe"}""", "event_archive_change_archived_check")]
    [InlineData("triage", """{"item_kind": "event", "item_id": "not-an-id", "state": "dismissed"}""", "event_triage_item_id_check")]
    [InlineData("activity", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b", "occurrence": "Nov 13", "result": "done"}""", "event_activity_occurrence_check")]
    [InlineData("activity", """{"subject_id": "6f1c2b9e-3a4d-4e5f-8a7b-1c2d3e4f5a6b", "occurrence": "2026-02-30", "result": "done"}""", "event_activity_occurrence_check")]
    [InlineData("activity", """{"subject_id": "task-1", "timer": "start"}""", "event_timer_subject_id_check")]
    [InlineData("ledger", """{"entry": "earn", "amounts": {"coins": 10}, "subject_id": "reward-1"}""", "event_ledger_subject_id_check")]
    [InlineData("ledger", """{"entry": "redeem", "amounts": {"coins": -400}, "dollars": "four"}""", "event_ledger_dollars_check")]
    [InlineData("ledger", """{"entry": "redeem", "amounts": {"coins": -400}, "coin_rate": "1c"}""", "event_ledger_coin_rate_check")]
    [InlineData("ledger", """{"entry": "earn", "amounts": {"coins": 1.5}}""", "event_ledger_amounts_check")]
    [InlineData("ledger", """{"entry": "earn", "amounts": {"coins": "10"}}""", "event_ledger_amounts_check")]
    [InlineData("ledger", """{"entry": "earn", "amounts": {"coins": 1e19}}""", "event_ledger_amounts_check")]
    [InlineData("ledger", """{"entry": "earn", "amounts": [10]}""", "event_ledger_amounts_check")]
    public async Task A_payload_a_view_would_fail_to_cast_is_refused(string kind, string payload, string constraint)
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(database, kind, payload));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal(constraint, ex.ConstraintName);
    }

    private async Task InsertAsync(DomainTestDatabase database, string kind, string payload)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, payload)
            VALUES (@kind, 'declared', now(), @source, @payload::jsonb);
            """, new { kind, source = Id, payload }, cancellationToken: Ct));
    }
}
