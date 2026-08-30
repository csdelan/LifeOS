using System.Text.Json;
using Dapper;
using LifeOs.Infrastructure.Rebuild;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Verifies the derived layer from migration 0004: subject_current folds the
/// latest state_change per subject, rebuild is deterministic (byte-identical
/// across runs), and verify detects drift.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RebuildTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DerivedRebuilder Rebuilder => new(postgres.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertSubjectAsync(NpgsqlConnection connection, string ns)
    {
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, 'Task', 'a task') RETURNING id;
            """,
            new { urn = $"urn:bsk:task:{ns}" }, cancellationToken: Ct));
    }

    private async Task<Guid> InsertStateChangeAsync(
        NpgsqlConnection connection, Guid subjectId, string status, DateTimeOffset occurredAt)
    {
        var payload = JsonSerializer.Serialize(new { subject_id = subjectId.ToString(), status });
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, payload)
            VALUES ('state_change', 'declared', @occurredAt, @sourceId, @payload::jsonb)
            RETURNING id;
            """,
            new { occurredAt, sourceId = "test", payload }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Subject_current_reflects_the_latest_state_change()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var subject = await InsertSubjectAsync(connection, ns);

        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await InsertStateChangeAsync(connection, subject, "open", t0);
        var latest = await InsertStateChangeAsync(connection, subject, "done", t0.AddDays(1));

        await Rebuilder.RebuildAsync(Ct);

        var row = await connection.QuerySingleAsync<(string Status, Guid StatusEventId)>(
            new CommandDefinition(
                "SELECT status, status_event_id FROM bsk_derived.subject_current WHERE subject_id = @subject;",
                new { subject }, cancellationToken: Ct));

        Assert.Equal("done", row.Status);
        Assert.Equal(latest, row.StatusEventId);
    }

    [Fact]
    public async Task Subject_without_state_change_has_null_status()
    {
        await using var connection = await OpenAsync();
        var subject = await InsertSubjectAsync(connection, Guid.NewGuid().ToString("N"));

        await Rebuilder.RebuildAsync(Ct);

        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current WHERE subject_id = @subject;",
            new { subject }, cancellationToken: Ct));

        Assert.Null(status);
    }

    [Fact]
    public async Task Consecutive_rebuilds_produce_byte_identical_state()
    {
        // Seed something so the snapshot is non-trivial.
        await using var connection = await OpenAsync();
        var subject = await InsertSubjectAsync(connection, Guid.NewGuid().ToString("N"));
        await InsertStateChangeAsync(connection, subject, "open",
            new DateTimeOffset(2026, 2, 2, 3, 4, 5, TimeSpan.Zero));

        var rebuilder = Rebuilder;

        await rebuilder.RebuildAsync(Ct);
        var first = await rebuilder.SnapshotAsync(Ct);

        await rebuilder.RebuildAsync(Ct);
        var second = await rebuilder.SnapshotAsync(Ct);

        Assert.Equal(first, second);
        Assert.NotEqual(string.Empty, first);
    }

    [Fact]
    public async Task Verify_reports_no_drift_immediately_after_rebuild()
    {
        var rebuilder = Rebuilder;
        await rebuilder.RebuildAsync(Ct);

        var result = await rebuilder.VerifyAsync(Ct);

        Assert.False(result.HasDrift);
        Assert.Equal(result.MaterializedChecksum, result.FreshChecksum);
    }

    [Fact]
    public async Task Verify_detects_a_manual_edit_as_drift_and_rebuild_heals_it()
    {
        await using var connection = await OpenAsync();
        var subject = await InsertSubjectAsync(connection, Guid.NewGuid().ToString("N"));
        await InsertStateChangeAsync(connection, subject, "open",
            new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero));

        var rebuilder = Rebuilder;
        await rebuilder.RebuildAsync(Ct);
        Assert.False((await rebuilder.VerifyAsync(Ct)).HasDrift);

        // Tamper with the projection directly — exactly what the invariant forbids.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk_derived.subject_current SET status = 'tampered' WHERE subject_id = @subject;",
            new { subject }, cancellationToken: Ct));

        Assert.True((await rebuilder.VerifyAsync(Ct)).HasDrift);

        // Rebuild regenerates from source and clears the drift.
        await rebuilder.RebuildAsync(Ct);
        Assert.False((await rebuilder.VerifyAsync(Ct)).HasDrift);
    }
}
