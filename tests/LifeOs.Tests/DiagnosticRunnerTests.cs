using System.Text.Json;
using Dapper;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the diagnostic runner (M4.1): it groups findings by
/// diagnostic, every finding carries its subject / summary / evidence, --only
/// selects one, diagnostics run read-only, and the result contract is enforced
/// so that no finding is ever unexplained. The runner is exercised with
/// hand-built diagnostics (full control over the SQL) plus the embedded fixtures
/// for the discovery-driven paths.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DiagnosticRunnerTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticRunner Runner => new(postgres.ConnectionString);

    private DiagnosticRunner RunnerWithFixtures =>
        new(postgres.ConnectionString, new EmbeddedDiagnosticSource(typeof(DiagnosticRunnerTests).Assembly));

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
            VALUES (@urn, 'Commitment', 'no late trading') RETURNING id;
            """,
            new { urn = $"urn:bsk:commitment:{ns}" }, cancellationToken: Ct));

    private async Task<Guid> InsertActivityEventAsync(NpgsqlConnection connection)
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id)
            VALUES ('activity', 'declared', now(), 'test') RETURNING id;
            """,
            cancellationToken: Ct));

    private async Task InsertViolatesAsync(NpgsqlConnection connection, Guid eventId, Guid subjectId)
        => await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject_event (event_id, subject_id, relation, provenance)
            VALUES (@eventId, @subjectId, 'violates', 'declared');
            """,
            new { eventId, subjectId }, cancellationToken: Ct));

    // A breach-shaped diagnostic scoped to one urn namespace so it only sees this
    // test's rows on the shared container.
    private static Diagnostic BreachFor(string ns) => new(
        Name: "breach",
        Title: "Commitments with a violating activity",
        Order: 10,
        Sql: $$"""
            SELECT
                s.id    AS subject_id,
                s.urn   AS subject_urn,
                s.type  AS subject_type,
                s.title AS subject_title,
                'Breached by ' || count(*) || ' activity event(s).' AS summary,
                jsonb_agg(jsonb_build_object(
                    'kind', 'event', 'id', se.event_id, 'relation', se.relation)) AS evidence
            FROM bsk.subject s
            JOIN bsk.subject_event se ON se.subject_id = s.id AND se.relation = 'violates'
            WHERE s.urn = 'urn:bsk:commitment:{{ns}}'
            GROUP BY s.id, s.urn, s.type, s.title;
            """);

    [Fact]
    public async Task Finding_carries_subject_summary_and_evidence_rows()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        var violatingEvent = await InsertActivityEventAsync(connection);
        await InsertViolatesAsync(connection, violatingEvent, commitment);

        var report = await Runner.ExecuteAsync([BreachFor(ns)], Ct);

        var result = Assert.Single(report.Diagnostics);
        Assert.Equal("breach", result.Name);
        Assert.Equal("Commitments with a violating activity", result.Title);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(commitment, finding.Subject.Id);
        Assert.Equal($"urn:bsk:commitment:{ns}", finding.Subject.Urn);
        Assert.Equal("Commitment", finding.Subject.Type);
        Assert.False(string.IsNullOrWhiteSpace(finding.Summary));

        // The evidence cites the violating event's id — the "why it fired".
        var ids = finding.Evidence.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToList();
        Assert.Contains(violatingEvent.ToString(), ids);
        Assert.Equal(1, report.FindingCount);
    }

    [Fact]
    public async Task Groups_findings_under_each_diagnostic_in_order()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var commitment = await InsertCommitmentAsync(connection, ns);
        var violatingEvent = await InsertActivityEventAsync(connection);
        await InsertViolatesAsync(connection, violatingEvent, commitment);

        // An empty diagnostic ordered before breach; it should still appear, with
        // no findings, so the report shows every diagnostic that ran.
        var empty = new Diagnostic("empty", "Finds nothing", 5, """
            SELECT NULL::uuid AS subject_id, ''::text AS subject_urn, ''::text AS subject_type,
                   ''::text AS subject_title, ''::text AS summary, '[]'::jsonb AS evidence
            WHERE false;
            """);

        var report = await Runner.ExecuteAsync([empty, BreachFor(ns)], Ct);

        Assert.Equal(2, report.Diagnostics.Count);
        Assert.Equal("empty", report.Diagnostics[0].Name);
        Assert.Empty(report.Diagnostics[0].Findings);
        Assert.Equal("breach", report.Diagnostics[1].Name);
        Assert.Single(report.Diagnostics[1].Findings);
    }

    [Fact]
    public async Task Absence_finding_may_carry_empty_evidence_but_still_explains()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, 'Goal', 'a wish') RETURNING id;
            """,
            new { urn = $"urn:bsk:goal:{ns}" }, cancellationToken: Ct));

        // A "wish"-shaped diagnostic: the cause is an absence, so there is no row
        // to cite — evidence is an empty array, and the summary carries the why.
        var wish = new Diagnostic("wish", "Goals with no serving project", 10, $"""
            SELECT s.id AS subject_id, s.urn AS subject_urn, s.type AS subject_type,
                   s.title AS subject_title,
                   'No active project serves this goal.' AS summary,
                   '[]'::jsonb AS evidence
            FROM bsk.subject s
            WHERE s.urn = 'urn:bsk:goal:{ns}';
            """);

        var report = await Runner.ExecuteAsync([wish], Ct);

        var finding = Assert.Single(report.Diagnostics[0].Findings);
        Assert.Equal(goal, finding.Subject.Id);
        Assert.Equal(JsonValueKind.Array, finding.Evidence.ValueKind);
        Assert.Equal(0, finding.Evidence.GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(finding.Summary));
    }

    [Fact]
    public async Task Only_runs_the_named_diagnostic()
    {
        var report = await RunnerWithFixtures.RunAsync(only: "titled", cancellationToken: Ct);

        var result = Assert.Single(report.Diagnostics);
        Assert.Equal("titled", result.Name);
        Assert.Equal("A titled fixture diagnostic", result.Title);
    }

    [Fact]
    public async Task Unknown_only_is_rejected_and_names_what_is_available()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunnerWithFixtures.RunAsync(only: "nope", cancellationToken: Ct));

        Assert.Contains("nope", ex.Message);
        Assert.Contains("titled", ex.Message);
        Assert.Contains("untitled", ex.Message);
    }

    [Fact]
    public async Task Run_without_only_executes_every_discovered_diagnostic()
    {
        var report = await RunnerWithFixtures.RunAsync(cancellationToken: Ct);

        var names = report.Diagnostics.Select(d => d.Name).ToList();
        Assert.Contains("titled", names);
        Assert.Contains("untitled", names);
    }

    [Fact]
    public async Task A_diagnostic_that_tries_to_write_fails_read_only()
    {
        await using var connection = await OpenAsync();
        var probe = $"urn:bsk:task:{Guid.NewGuid():N}";

        var writer = new Diagnostic("writer", "Tries to write", 10, $"""
            INSERT INTO bsk.subject (urn, type, title) VALUES ('{probe}', 'Task', 'nope');
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner.ExecuteAsync([writer], Ct));
        Assert.Contains("writer", ex.Message);

        // And nothing was written: the read-only transaction refused it.
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.subject WHERE urn = @probe);",
            new { probe }, cancellationToken: Ct));
        Assert.False(exists);
    }

    [Fact]
    public async Task A_missing_contract_column_is_a_clear_error()
    {
        // Returns a row but omits subject_urn.
        var broken = new Diagnostic("broken", "Missing a column", 10, """
            SELECT gen_random_uuid() AS subject_id, 'T'::text AS subject_type,
                   't'::text AS subject_title, 's'::text AS summary, '[]'::jsonb AS evidence;
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner.ExecuteAsync([broken], Ct));
        Assert.Contains("broken", ex.Message);
        Assert.Contains("subject_urn", ex.Message);
    }

    [Fact]
    public async Task A_blank_summary_is_rejected_so_no_finding_is_unexplained()
    {
        var blank = new Diagnostic("blank", "Blank summary", 10, """
            SELECT gen_random_uuid() AS subject_id, 'u'::text AS subject_urn, 'T'::text AS subject_type,
                   't'::text AS subject_title, '   '::text AS summary, '[]'::jsonb AS evidence;
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner.ExecuteAsync([blank], Ct));
        Assert.Contains("blank", ex.Message);
        Assert.Contains("summary", ex.Message);
    }

    [Fact]
    public async Task Evidence_that_is_not_an_array_is_rejected()
    {
        var notArray = new Diagnostic("notarray", "Object evidence", 10, """
            SELECT gen_random_uuid() AS subject_id, 'u'::text AS subject_urn, 'T'::text AS subject_type,
                   't'::text AS subject_title, 's'::text AS summary,
                   jsonb_build_object('kind', 'event') AS evidence;
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner.ExecuteAsync([notArray], Ct));
        Assert.Contains("notarray", ex.Message);
        Assert.Contains("evidence", ex.Message);
    }
}
