using System.Text.Json;
using Dapper;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the M4.6 <c>constraint</c> check: a capacity Constraint
/// exceeded by reality is flagged with limit vs. actual and the contributing
/// subjects; an interaction-scope Constraint never reads as a capacity violation.
///
/// The observed values (active-Project count, total committed hours) are global
/// aggregates over the whole kernel, which is the right semantics for a
/// single-user life but means the shared test container accumulates unrelated
/// subjects. Tests stay deterministic by reading the live baseline and setting
/// each test's own Constraint limit relative to it, and by keying findings off the
/// test's own Constraint (unique urn) rather than asserting an absolute count.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConstraintDiagnosticTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticRunner Runner => new(postgres.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertSubjectAsync(
        NpgsqlConnection connection, string type, string ns, object? attributes = null)
    {
        var attrs = JsonSerializer.Serialize(attributes ?? new { });
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes)
            VALUES (@urn, @type, @title, @attrs::jsonb) RETURNING id;
            """,
            new { urn = $"urn:bsk:{type.ToLowerInvariant()}:{ns}", type, title = $"{type} {ns}", attrs },
            cancellationToken: Ct));
    }

    private async Task SetStatusAsync(NpgsqlConnection connection, Guid subjectId, string status)
    {
        var payload = JsonSerializer.Serialize(new { subject_id = subjectId.ToString(), status });
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, payload)
            VALUES ('state_change', 'declared', now(), 'test', @payload::jsonb);
            """,
            new { payload }, cancellationToken: Ct));
    }

    // The diagnostic's own definition of the live pools, so tests can size a limit
    // relative to reality regardless of what other tests have left in the container.
    private async Task<int> LiveActiveProjectCountAsync(NpgsqlConnection connection)
        => await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
            FROM bsk.subject s
            LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = s.id
            WHERE s.type = 'Project' AND NOT bsk.is_terminal_status(scs.status);
            """,
            cancellationToken: Ct));

    private async Task<decimal> LiveCommittedHoursAsync(NpgsqlConnection connection)
        => await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            SELECT coalesce(sum((s.attributes->>'committed_hours')::numeric), 0)
            FROM bsk.subject s
            LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = s.id
            WHERE NOT bsk.is_terminal_status(scs.status)
              AND s.attributes->>'committed_hours' ~ '^\s*\d+(\.\d+)?\s*$';
            """,
            cancellationToken: Ct));

    private async Task<Finding?> FindAsync(string diagnostic, Guid subjectId)
    {
        var report = await Runner.RunAsync(only: diagnostic, cancellationToken: Ct);
        var result = Assert.Single(report.Diagnostics);
        Assert.Equal(diagnostic, result.Name);
        return result.Findings.FirstOrDefault(f => f.Subject.Id == subjectId);
    }

    // ---- projects dimension ----

    [Fact]
    public async Task Active_projects_over_the_limit_are_flagged_with_limit_and_actual()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        var mine = await InsertSubjectAsync(connection, "Project", $"{ns}-p1");
        var total = await LiveActiveProjectCountAsync(connection); // includes mine
        // A limit strictly under reality: guaranteed to fire.
        var limit = total - 1;
        var constraint = await InsertSubjectAsync(
            connection, "Constraint", ns, new { scope = "capacity", limit = $"{limit} active projects" });

        var finding = await FindAsync("constraint", constraint);

        Assert.NotNull(finding);
        Assert.Contains("Over capacity", finding!.Summary, StringComparison.Ordinal);
        var limitRow = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "limit");
        Assert.Equal("projects", limitRow.GetProperty("dimension").GetString());
        Assert.Equal(limit, limitRow.GetProperty("limit").GetInt32());
        Assert.Equal(total, (int)limitRow.GetProperty("observed").GetDecimal());
        // The offending Project I added is cited among the contributing subjects.
        Assert.Contains(finding.Evidence.EnumerateArray(),
            e => e.GetProperty("kind").GetString() == "subject"
                 && e.GetProperty("id").GetString() == mine.ToString());
    }

    [Fact]
    public async Task Active_projects_within_the_limit_are_not_flagged()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var total = await LiveActiveProjectCountAsync(connection);
        // A limit comfortably above reality: cannot fire.
        var constraint = await InsertSubjectAsync(
            connection, "Constraint", ns, new { scope = "capacity", limit = $"{total + 5} active projects" });

        Assert.Null(await FindAsync("constraint", constraint));
    }

    [Fact]
    public async Task A_finished_project_does_not_count_against_the_limit()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var done = await InsertSubjectAsync(connection, "Project", $"{ns}-p1");
        await SetStatusAsync(connection, done, "done");
        var total = await LiveActiveProjectCountAsync(connection); // excludes the done one

        // Limit set to exactly the live active count: my finished Project must not push it over.
        var constraint = await InsertSubjectAsync(
            connection, "Constraint", ns, new { scope = "capacity", limit = $"{total} projects" });

        Assert.Null(await FindAsync("constraint", constraint));
    }

    // ---- hours dimension ----

    [Fact]
    public async Task Committed_hours_over_available_focused_hours_are_flagged()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var project = await InsertSubjectAsync(
            connection, "Project", $"{ns}-p", new { committed_hours = "8" });
        var total = await LiveCommittedHoursAsync(connection); // includes my 8
        var limit = (int)total - 1; // strictly under reality

        var constraint = await InsertSubjectAsync(
            connection, "Constraint", ns, new { scope = "capacity", limit = $"{limit} focused hours" });

        var finding = await FindAsync("constraint", constraint);

        Assert.NotNull(finding);
        var limitRow = finding!.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "limit");
        Assert.Equal("hours", limitRow.GetProperty("dimension").GetString());
        Assert.Equal(limit, limitRow.GetProperty("limit").GetInt32());
        // My hour-booking Project is cited with its hours.
        var mine = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "subject"
                         && e.GetProperty("id").GetString() == project.ToString());
        Assert.Equal(8, mine.GetProperty("committed_hours").GetInt32());
    }

    // ---- scope + interpretability ----

    [Fact]
    public async Task An_interaction_scope_constraint_is_not_a_capacity_violation()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        await InsertSubjectAsync(connection, "Project", $"{ns}-p1");
        // Limit 0 would fire loudly if it were treated as capacity; scope=interaction must exempt it.
        var constraint = await InsertSubjectAsync(
            connection, "Constraint", ns, new { scope = "interaction", limit = "0 active projects" });

        Assert.Null(await FindAsync("constraint", constraint));
    }

    [Fact]
    public async Task A_capacity_constraint_with_an_uninterpretable_limit_is_skipped()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        await InsertSubjectAsync(connection, "Project", $"{ns}-p1");
        // No number and no recognised unit: nothing to compare, so it is skipped, not guessed at.
        var constraint = await InsertSubjectAsync(
            connection, "Constraint", ns, new { scope = "capacity", limit = "as few as possible" });

        Assert.Null(await FindAsync("constraint", constraint));
    }
}
