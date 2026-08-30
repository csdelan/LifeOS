using Dapper;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the breach diagnostic (M4.3): a Commitment with a
/// violating event is flagged with that event as evidence; a Commitment with
/// only evidencing activity is not; and the framing is a breach, distinct from
/// neglect. Runs the real embedded diagnostic through the M4.1 runner, keying
/// assertions off each test's own Commitment so it is robust to the shared
/// container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BreachDiagnosticTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticRunner Runner => new(postgres.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertCommitmentAsync(NpgsqlConnection connection, string ns)
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, 'Commitment', @title) RETURNING id;
            """,
            new { urn = $"urn:bsk:commitment:{ns}", title = $"commitment {ns}" }, cancellationToken: Ct));

    // An activity event edged to a Commitment, as `bsk log activity --violates/--evidences` writes it.
    private async Task<Guid> InsertActivityEdgeAsync(
        NpgsqlConnection connection, Guid commitmentId, string relation, DateTimeOffset occurredAt)
    {
        var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id)
            VALUES ('activity', 'declared', @occurredAt, 'test') RETURNING id;
            """,
            new { occurredAt }, cancellationToken: Ct));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject_event (event_id, subject_id, relation, provenance)
            VALUES (@eventId, @commitmentId, @relation, 'declared');
            """,
            new { eventId, commitmentId, relation }, cancellationToken: Ct));

        return eventId;
    }

    private async Task<Finding?> FindAsync(Guid subjectId)
    {
        var report = await Runner.RunAsync(only: "breach", cancellationToken: Ct);
        var breach = Assert.Single(report.Diagnostics);
        Assert.Equal("breach", breach.Name);
        return breach.Findings.FirstOrDefault(f => f.Subject.Id == subjectId);
    }

    [Fact]
    public async Task Commitment_with_a_violating_activity_is_flagged_with_its_evidence()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        var violation = await InsertActivityEdgeAsync(
            connection, commitment, "violates", DateTimeOffset.UtcNow.AddDays(-3));

        var finding = await FindAsync(commitment);

        Assert.NotNull(finding);
        Assert.False(string.IsNullOrWhiteSpace(finding!.Summary));

        var ev = Assert.Single(finding.Evidence.EnumerateArray());
        Assert.Equal(violation.ToString(), ev.GetProperty("id").GetString());
        Assert.Equal("violates", ev.GetProperty("relation").GetString());
        // The violating event's timestamp is part of the evidence.
        Assert.False(string.IsNullOrWhiteSpace(ev.GetProperty("occurred_at").GetString()));
    }

    [Fact]
    public async Task Commitment_with_only_evidencing_activity_is_not_flagged()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        await InsertActivityEdgeAsync(connection, commitment, "evidences", DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Null(await FindAsync(commitment));
    }

    [Fact]
    public async Task Evidencing_activity_does_not_cancel_a_violation()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        await InsertActivityEdgeAsync(connection, commitment, "evidences", DateTimeOffset.UtcNow.AddDays(-5));
        var violation = await InsertActivityEdgeAsync(
            connection, commitment, "violates", DateTimeOffset.UtcNow.AddDays(-2));

        var finding = await FindAsync(commitment);

        Assert.NotNull(finding);
        // Only the violating event is evidence; the evidencing one is not a breach.
        var ev = Assert.Single(finding!.Evidence.EnumerateArray());
        Assert.Equal(violation.ToString(), ev.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Multiple_violations_are_all_cited_newest_first()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        var older = await InsertActivityEdgeAsync(
            connection, commitment, "violates", DateTimeOffset.UtcNow.AddDays(-10));
        var newer = await InsertActivityEdgeAsync(
            connection, commitment, "violates", DateTimeOffset.UtcNow.AddDays(-1));

        var finding = await FindAsync(commitment);

        Assert.NotNull(finding);
        var ids = finding!.Evidence.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(newer.ToString(), ids[0]); // newest first
        Assert.Equal(older.ToString(), ids[1]);
    }

    [Fact]
    public async Task Framing_names_it_as_broken_confrontational_not_informational()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        await InsertActivityEdgeAsync(connection, commitment, "violates", DateTimeOffset.UtcNow.AddDays(-1));

        var finding = await FindAsync(commitment);

        Assert.NotNull(finding);
        // Distinct from neglect's soft "no activity" wording.
        Assert.Contains("Broken", finding!.Summary, StringComparison.Ordinal);
        Assert.Contains("violation", finding.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_violation_against_a_non_commitment_is_not_a_breach()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        // A Goal (not a Commitment) carrying a stray violates edge must not be flagged.
        var goal = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, 'Goal', 'a goal') RETURNING id;
            """,
            new { urn = $"urn:bsk:goal:{ns}" }, cancellationToken: Ct));
        await InsertActivityEdgeAsync(connection, goal, "violates", DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Null(await FindAsync(goal));
    }
}
