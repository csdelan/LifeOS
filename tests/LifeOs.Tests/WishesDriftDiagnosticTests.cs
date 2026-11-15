using System.Reflection;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the Wishes and Drift diagnostics (M4.4) and the
/// terminal-status predicate they rest on (migration 0008). Wishes flags a Goal
/// with no open Project or Task working toward it (a Goal worked toward only by
/// finished ones is still a wish); Drift flags a Project that serves no Goal, while a
/// standalone Task is left alone. Both follow <c>results_in</c> as well as
/// <c>serves</c>, since a Project created under its Goal is linked by
/// <c>results_in</c> (GEN-7), and the last test makes every parent-first link the way the
/// app does and checks no alignment check calls it missing. Runs the real embedded
/// diagnostics through the M4.1 runner, keyed off each test's own subjects.
/// </summary>
public sealed class WishesDriftDiagnosticTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticRunner Runner => new(classDb.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(classDb.ConnectionString);
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

    private async Task InsertRelationAsync(NpgsqlConnection connection, Guid from, string relation, Guid to)
        => await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
            VALUES (@from, @relation, @to, 'declared');
            """,
            new { from, relation, to }, cancellationToken: Ct));

    private Task InsertServesAsync(NpgsqlConnection connection, Guid from, Guid to)
        => InsertRelationAsync(connection, from, SubjectRelations.Serves, to);

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
    public async Task Goal_a_project_results_in_is_not_a_wish()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertRelationAsync(connection, project, SubjectRelations.ResultsIn, goal);

        Assert.Null(await FindAsync("wishes", goal));
    }

    [Fact]
    public async Task Goal_with_an_open_task_serving_it_directly_is_not_a_wish()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var task = await InsertSubjectAsync(connection, "Task", $"{ns}-t");
        await InsertServesAsync(connection, task, goal);
        await SetStatusAsync(connection, task, "In progress");

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
        Assert.Contains("No Project or Task works toward", finding!.Summary, StringComparison.Ordinal);
        // Evidence names the absent/expected relation: the one a Project made under its Goal has.
        var expected = Assert.Single(finding.Evidence.EnumerateArray());
        Assert.Equal("expected_relation", expected.GetProperty("kind").GetString());
        Assert.Equal("results_in", expected.GetProperty("relation").GetString());
        Assert.Equal("Project", expected.GetProperty("expected_from_type").GetString());
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
    public async Task Goal_worked_toward_only_by_finished_work_is_a_wish_citing_each_link()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        var task = await InsertSubjectAsync(connection, "Task", $"{ns}-t");
        await InsertRelationAsync(connection, project, SubjectRelations.ResultsIn, goal);
        await InsertServesAsync(connection, task, goal);
        await SetStatusAsync(connection, project, "Completed");
        await SetStatusAsync(connection, task, "Cancelled");

        var finding = await FindAsync("wishes", goal);

        Assert.NotNull(finding);
        Assert.Contains("finished", finding!.Summary, StringComparison.Ordinal);
        var cited = finding.Evidence.EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "subject")
            .ToDictionary(e => e.GetProperty("id").GetString()!);
        Assert.Equal(2, cited.Count);
        Assert.Equal("results_in", cited[project.ToString()].GetProperty("relation").GetString());
        Assert.Equal("Completed", cited[project.ToString()].GetProperty("status").GetString());
        Assert.Equal("Task", cited[task.ToString()].GetProperty("subject_type").GetString());
        Assert.Equal("serves", cited[task.ToString()].GetProperty("relation").GetString());
        Assert.Equal("Cancelled", cited[task.ToString()].GetProperty("status").GetString());
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
    public async Task Project_resulting_in_a_goal_is_not_drift()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var goal = await InsertSubjectAsync(connection, "Goal", ns);
        var project = await InsertSubjectAsync(connection, "Project", $"{ns}-p");
        await InsertRelationAsync(connection, project, SubjectRelations.ResultsIn, goal);

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
        Assert.Equal("results_in", expected.GetProperty("relation").GetString());
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
        Assert.Equal("serves", context.GetProperty("relation").GetString());
    }

    [Fact]
    public async Task Drift_cites_what_a_project_results_in_by_its_relation()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var project = await InsertSubjectAsync(connection, "Project", ns);
        var commitment = await InsertSubjectAsync(connection, "Commitment", $"{ns}-c");
        await InsertRelationAsync(connection, project, SubjectRelations.ResultsIn, commitment);

        var finding = await FindAsync("drift", project);

        Assert.NotNull(finding);
        Assert.Contains("no Goal", finding!.Summary, StringComparison.Ordinal);
        var context = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "subject");
        Assert.Equal(commitment.ToString(), context.GetProperty("id").GetString());
        Assert.Equal("results_in", context.GetProperty("relation").GetString());
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

    // ---- Links made the way the app makes them (GEN-7) ----

    // The checks that call an item unaligned: the item's type, and whether what it lacks is
    // below it (something serving it) or above it (something it serves).
    private static readonly (string Check, string Type, bool LacksBelow)[] AlignmentChecks =
    [
        ("wishes", SubjectTypes.Goal, true),
        ("decorative_identity", SubjectTypes.Value, true),
        ("drift", SubjectTypes.Project, false),
    ];

    /// <summary>Every pair LifeOS links on its own when a child is created under a parent.</summary>
    private static List<(string Child, string Parent)> Pairs()
    {
        var types = typeof(SubjectTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        return [.. from child in types
                   from parent in types
                   where ParentChildRelations.Infer(child, parent) is not null
                   select (child, parent)];
    }

    public static TheoryData<string, string> ParentFirstPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var (child, parent) in Pairs())
        {
            data.Add(child, parent);
        }

        return data;
    }

    [Fact]
    public void Every_alignment_check_is_reached_by_a_parent_first_pair()
    {
        var pairs = Pairs();

        Assert.All(AlignmentChecks, check => Assert.Contains(pairs, pair =>
            check.LacksBelow ? pair.Parent == check.Type : pair.Child == check.Type));
    }

    [Theory]
    [MemberData(nameof(ParentFirstPairs))]
    public async Task An_item_created_under_its_parent_is_aligned_for_every_check(string childType, string parentType)
    {
        var provider = classDb.Kernel();
        var subjects = provider.GetRequiredService<SubjectService>();
        var children = provider.GetRequiredService<ChildCreationService>();
        var ns = Guid.NewGuid().ToString("N");
        var parent = await subjects.CreateAsync(
            parentType, $"{parentType} {ns}",
            attributesJson: parentType == SubjectTypes.Value ? """{"statement":"I am someone who ships."}""" : "{}",
            cancellationToken: Ct);
        var child = await children.CreateChildAsync(childType, $"{childType} {ns}", parent.Urn, cancellationToken: Ct);

        var report = await Runner.RunAsync(cancellationToken: Ct);

        foreach (var (check, type, lacksBelow) in AlignmentChecks)
        {
            var (endType, endId) = lacksBelow ? (parentType, parent.Id) : (childType, child.Child.Id);
            if (endType != type)
            {
                continue;
            }

            var findings = report.Diagnostics.Single(d => d.Name == check).Findings;
            Assert.DoesNotContain(findings, f => f.Subject.Id == endId);
        }
    }
}
