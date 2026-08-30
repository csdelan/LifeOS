using System.Text.Json;
using Dapper;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the Wishes and Drift diagnostics (M4.4) and the
/// terminal-status predicate they rest on (migration 0008). Wishes flags a Goal
/// with no active Project serving it (a Goal served only by finished Projects is
/// still a wish); Drift flags a Project that serves no Goal, while a standalone
/// Task is left alone. Runs the real embedded diagnostics through the M4.1
/// runner, keyed off each test's own subjects.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WishesDriftDiagnosticTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticRunner Runner => new(postgres.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<Guid> InsertSubjectAsync(NpgsqlConnection connection, string type, string ns)
        => await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title)
            VALUES (@urn, @type, @title) RETURNING id;
            """,
            new { urn = $"urn:bsk:{type.ToLowerInvariant()}:{ns}", type, title = $"{type} {ns}" },
            cancellationToken: Ct));

    private async Task InsertServesAsync(NpgsqlConnection connection, Guid from, Guid to)
        => await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
            VALUES (@from, 'serves', @to, 'declared');
            """,
            new { from, to }, cancellationToken: Ct));

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

    private async Task<Finding?> FindAsync(string diagnostic, Guid subjectId)
    {
        var report = await Runner.RunAsync(only: diagnostic, cancellationToken: Ct);
        var result = Assert.Single(report.Diagnostics);
        Assert.Equal(diagnostic, result.Name);
        return result.Findings.FirstOrDefault(f => f.Subject.Id == subjectId);
    }

    // ---- terminal-status predicate (migration 0008) ----

    [Theory]
    [InlineData("done", true)]
    [InlineData("  Done ", true)]
    [InlineData("ABANDONED", true)]
    [InlineData("superseded", true)]
    [InlineData("active", false)]
    [InlineData("open", false)]
    [InlineData("in-progress", false)]
    [InlineData("", false)]
    public async Task Is_terminal_status_classifies_known_words(string status, bool expected)
    {
        await using var connection = await OpenAsync();
        var actual = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT bsk.is_terminal_status(@status);", new { status }, cancellationToken: Ct));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Is_terminal_status_treats_null_as_active()
    {
        await using var connection = await OpenAsync();
        var actual = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT bsk.is_terminal_status(NULL);", cancellationToken: Ct));
        Assert.False(actual);
    }

    // ---- Wishes ----

    [Fact]
    public async Task Goal_with_an_active_serving_project_is_not_a_wish()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertServesAsync(connection, project, goal); // no status => active

        Assert.Null(await FindAsync("wishes", goal));
    }

    [Fact]
    public async Task Goal_with_no_serving_project_is_a_wish()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);

        var finding = await FindAsync("wishes", goal);

        Assert.NotNull(finding);
        Assert.Contains("No Project serves", finding!.Summary, StringComparison.Ordinal);
        // Evidence names the absent/expected relation.
        var expected = Assert.Single(finding.Evidence.EnumerateArray());
        Assert.Equal("expected_relation", expected.GetProperty("kind").GetString());
        Assert.Equal("serves", expected.GetProperty("relation").GetString());
    }

    [Fact]
    public async Task Goal_served_only_by_a_finished_project_is_still_a_wish()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertServesAsync(connection, project, goal);
        await SetStatusAsync(connection, project, "done");

        var finding = await FindAsync("wishes", goal);

        Assert.NotNull(finding);
        Assert.Contains("finished", finding!.Summary, StringComparison.Ordinal);
        // The finished serving Project is cited as context, with its status.
        var contextProject = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "subject");
        Assert.Equal(project.ToString(), contextProject.GetProperty("id").GetString());
        Assert.Equal("done", contextProject.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Goal_served_by_a_project_with_no_status_is_not_a_wish()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertServesAsync(connection, project, goal);
        await SetStatusAsync(connection, project, "active"); // explicitly active

        Assert.Null(await FindAsync("wishes", goal));
    }

    // ---- Drift ----

    [Fact]
    public async Task Project_serving_a_goal_is_not_drift()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertServesAsync(connection, project, goal);

        Assert.Null(await FindAsync("drift", project));
    }

    [Fact]
    public async Task Project_serving_nothing_is_drift()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var project = await InsertSubjectAsync(connection, "Project", ns);

        var finding = await FindAsync("drift", project);

        Assert.NotNull(finding);
        Assert.Contains("serves nothing", finding!.Summary, StringComparison.Ordinal);
        var expected = Assert.Single(finding.Evidence.EnumerateArray());
        Assert.Equal("expected_relation", expected.GetProperty("kind").GetString());
        Assert.Equal("Goal", expected.GetProperty("expected_to_type").GetString());
    }

    [Fact]
    public async Task Project_serving_a_commitment_but_no_goal_is_drift_with_context()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var project = await InsertSubjectAsync(connection, "Project", ns);
        var commitment = await InsertSubjectAsync(connection, "Commitment", $"{ns}-c");
        await InsertServesAsync(connection, project, commitment);

        var finding = await FindAsync("drift", project);

        Assert.NotNull(finding);
        Assert.Contains("no Goal", finding!.Summary, StringComparison.Ordinal);
        // The Commitment it does serve is cited as context.
        var context = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "subject");
        Assert.Equal(commitment.ToString(), context.GetProperty("id").GetString());
        Assert.Equal("Commitment", context.GetProperty("subject_type").GetString());
    }

    [Fact]
    public async Task A_standalone_task_serving_nothing_is_not_drift()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var task = await InsertSubjectAsync(connection, "Task", ns);

        // Drift only examines Projects; a leaf Task legitimately serves nothing.
        Assert.Null(await FindAsync("drift", task));
    }
}
