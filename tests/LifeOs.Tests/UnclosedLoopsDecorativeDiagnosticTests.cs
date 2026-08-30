using System.Text.Json;
using Dapper;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the M4.5 diagnostics: <c>unclosed_loops</c> (a Decision
/// past its <c>next_review_at</c> with no recorded outcome — i.e. not closed out
/// into a terminal status) and <c>decorative_identity</c> (a Value with no Goal
/// serving it). Runs the real embedded diagnostics through the M4.1 runner, keyed
/// off each test's own subjects (unique urns) so they are robust to the shared
/// container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnclosedLoopsDecorativeDiagnosticTests(PostgresFixture postgres)
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
        // A Value must carry a statement (migration 0010); default one when a test
        // doesn't set attributes of its own, so these fixtures satisfy the CHECK.
        var effective = attributes ?? (type == "Value" ? new { statement = $"identity {ns}" } : new object());
        var attrs = JsonSerializer.Serialize(effective);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes)
            VALUES (@urn, @type, @title, @attrs::jsonb) RETURNING id;
            """,
            new { urn = $"urn:bsk:{type.ToLowerInvariant()}:{ns}", type, title = $"{type} {ns}", attrs },
            cancellationToken: Ct));
    }

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

    // Yesterday / tomorrow as bare ISO dates, the shape `next_review_at` carries.
    private static string Yesterday => DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
    private static string Tomorrow => DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");

    // ---- unclosed_loops ----

    [Fact]
    public async Task Decision_past_review_with_no_outcome_is_an_open_loop()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var decision = await InsertSubjectAsync(connection, "Decision", ns, new { next_review_at = Yesterday });

        var finding = await FindAsync("unclosed_loops", decision);

        Assert.NotNull(finding);
        Assert.Contains("Open loop", finding!.Summary, StringComparison.Ordinal);
        var review = Assert.Single(finding.Evidence.EnumerateArray());
        Assert.Equal("review", review.GetProperty("kind").GetString());
        Assert.Equal(Yesterday, review.GetProperty("review_date").GetString());
        Assert.Equal(1, review.GetProperty("days_overdue").GetInt32());
    }

    [Fact]
    public async Task Decision_with_a_future_review_date_is_not_an_open_loop()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var decision = await InsertSubjectAsync(connection, "Decision", ns, new { next_review_at = Tomorrow });

        Assert.Null(await FindAsync("unclosed_loops", decision));
    }

    [Fact]
    public async Task Decision_past_review_but_closed_out_is_not_an_open_loop()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var decision = await InsertSubjectAsync(connection, "Decision", ns, new { next_review_at = Yesterday });
        await SetStatusAsync(connection, decision, "resolved"); // terminal => outcome recorded

        Assert.Null(await FindAsync("unclosed_loops", decision));
    }

    [Fact]
    public async Task Decision_past_review_with_a_nonterminal_status_is_still_an_open_loop()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var decision = await InsertSubjectAsync(connection, "Decision", ns, new { next_review_at = Yesterday });
        await SetStatusAsync(connection, decision, "active"); // not terminal => loop still open

        var finding = await FindAsync("unclosed_loops", decision);

        Assert.NotNull(finding);
        Assert.Equal("active", finding!.Evidence.EnumerateArray().Single().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Decision_with_a_malformed_review_date_is_skipped()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var decision = await InsertSubjectAsync(connection, "Decision", ns, new { next_review_at = "soon-ish" });

        Assert.Null(await FindAsync("unclosed_loops", decision));
    }

    [Fact]
    public async Task Decision_with_no_review_date_is_not_a_loop()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var decision = await InsertSubjectAsync(connection, "Decision", ns, new { rationale = "no date set" });

        Assert.Null(await FindAsync("unclosed_loops", decision));
    }

    // ---- decorative_identity ----

    [Fact]
    public async Task Value_with_a_goal_beneath_it_is_not_decorative()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var value = await InsertSubjectAsync(connection, "Value", ns);
        var goal = await InsertSubjectAsync(connection, "Goal", $"{ns}-g");
        await InsertServesAsync(connection, goal, value); // Goal serves Value

        Assert.Null(await FindAsync("decorative_identity", value));
    }

    [Fact]
    public async Task Value_with_no_goal_beneath_it_is_decorative()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var value = await InsertSubjectAsync(connection, "Value", ns);

        var finding = await FindAsync("decorative_identity", value);

        Assert.NotNull(finding);
        Assert.Contains("No Goal serves", finding!.Summary, StringComparison.Ordinal);
        var expected = Assert.Single(finding.Evidence.EnumerateArray());
        Assert.Equal("expected_relation", expected.GetProperty("kind").GetString());
        Assert.Equal("Goal", expected.GetProperty("expected_from_type").GetString());
    }

    [Fact]
    public async Task Value_with_a_finished_goal_beneath_it_is_still_not_decorative()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var value = await InsertSubjectAsync(connection, "Value", ns);
        var goal = await InsertSubjectAsync(connection, "Goal", $"{ns}-g");
        await InsertServesAsync(connection, goal, value);
        await SetStatusAsync(connection, goal, "done"); // structural check: a finished Goal still clears it

        Assert.Null(await FindAsync("decorative_identity", value));
    }

    [Fact]
    public async Task Value_served_only_by_a_project_not_a_goal_is_decorative()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var value = await InsertSubjectAsync(connection, "Value", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertServesAsync(connection, project, value); // a Project, not a Goal

        var finding = await FindAsync("decorative_identity", value);

        Assert.NotNull(finding); // only a Goal beneath it counts
    }
}
