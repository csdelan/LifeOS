using System.Net;
using System.Net.Http.Json;
using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Cli;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk link</c> (M3.2): linking produces a relation row, the leaf-only rule
/// rejects <c>X serves Task</c> with a clear message, and a Task serving a
/// Commitment (or standing alone) is allowed. No <c>serves</c> or <c>results_in</c> link may
/// close a loop, from any door (focus plan G2).
/// </summary>
public sealed class RelationServiceTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => classDb.Kernel();

    [Fact]
    public async Task Linking_produces_a_relation_row()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"call the plumber {ns}", cancellationToken: Ct);
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"keep the house running {ns}", cancellationToken: Ct);

        var result = await relations.LinkAsync(task.Urn, SubjectRelations.Serves, commitment.Urn, Ct);

        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(Guid FromSubject, string Relation, Guid ToSubject, string Provenance)>(
            new CommandDefinition(
                "SELECT from_subject, relation, to_subject, provenance FROM bsk.subject_relation WHERE id = @id;",
                new { id = result.Id }, cancellationToken: Ct));

        Assert.Equal(task.Id, row.FromSubject);
        Assert.Equal(SubjectRelations.Serves, row.Relation);
        Assert.Equal(commitment.Id, row.ToSubject);
        Assert.Equal(Provenances.Declared, row.Provenance);
    }

    [Fact]
    public async Task Nothing_may_serve_a_task_and_the_message_is_clear()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var goal = await subjects.CreateAsync(SubjectTypes.Goal, $"be organised {ns}", cancellationToken: Ct);
        var task = await subjects.CreateAsync(SubjectTypes.Task, $"tidy the desk {ns}", cancellationToken: Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await relations.LinkAsync(goal.Urn, SubjectRelations.Serves, task.Urn, Ct));

        Assert.Contains("Task is a leaf", ex.Message);

        // And nothing was written.
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE to_subject = @task;",
            new { task = task.Id }, cancellationToken: Ct));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_link_that_would_close_a_loop_is_refused_from_every_door_and_names_the_path()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var children = provider.GetRequiredService<ChildCreationService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        // Sub-goals made under their parents, as a Goal's + Child makes them: each serves the one above.
        var half = await subjects.CreateAsync(SubjectTypes.Goal, $"Run a half-marathon {ns}", cancellationToken: Ct);
        var tenK = (await children.CreateChildAsync(SubjectTypes.Goal, $"Run a 10K {ns}", half.Urn, cancellationToken: Ct)).Child;
        var fiveK = (await children.CreateChildAsync(SubjectTypes.Goal, $"Run a 5K {ns}", tenK.Urn, cancellationToken: Ct)).Child;
        var plan = (await children.CreateChildAsync(SubjectTypes.Project, $"Couch to 5K {ns}", fiveK.Urn, cancellationToken: Ct)).Child;

        var loop = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relations.LinkAsync(half.Urn, SubjectRelations.Serves, fiveK.Urn, Ct));
        Assert.Equal(
            $"'{half.Title}' can't serve '{fiveK.Title}': '{fiveK.Title}' already sits under '{half.Title}' "
            + $"({fiveK.Title} → {tenK.Title} → {half.Title}), so the link would close a loop. Unlink one of those first.",
            loop.Message);

        // results_in is followed too, and an item can't sit under itself.
        var viaProject = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relations.LinkAsync(half.Urn, SubjectRelations.ResultsIn, plan.Urn, Ct));
        Assert.Contains($"({plan.Title} → {fiveK.Title} → {tenK.Title} → {half.Title})", viaProject.Message, StringComparison.Ordinal);
        var itself = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relations.LinkAsync(tenK.Urn, SubjectRelations.Serves, tenK.Urn, Ct));
        Assert.Equal($"'{tenK.Title}' can't serve itself.", itself.Message);

        // bsk link and the API refuse it in the same words.
        var (exit, _, error) = await ConsoleCapture.RunAsync(() => BskRoot.Create().Command
            .Parse(["link", half.Urn, SubjectRelations.Serves, fiveK.Urn, "--connection", classDb.ConnectionString])
            .InvokeAsync(cancellationToken: Ct));
        Assert.Equal(1, exit);
        Assert.Contains("would close a loop", error, StringComparison.Ordinal);
        await using (var host = await ApiTestHost.StartAsync(classDb.ConnectionString, Ct))
        {
            var answer = await host.Client.PostAsJsonAsync(
                "/api/link", new { from = half.Id.ToString(), relation = SubjectRelations.Serves, to = fiveK.Id.ToString() }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
            Assert.Contains("would close a loop", await answer.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @half OR (from_subject = @tenK AND to_subject = @tenK);",
            new { half = half.Id, tenK = tenK.Id }, cancellationToken: Ct)));

        // A link that closes nothing still lands: the 5K can serve the half-marathon directly too.
        await relations.LinkAsync(fiveK.Urn, SubjectRelations.Serves, half.Urn, Ct);
    }

    [Fact]
    public async Task Two_links_at_once_cannot_close_a_loop_between_them()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");
        var first = await subjects.CreateAsync(SubjectTypes.Goal, $"first goal {ns}", cancellationToken: Ct);
        var second = await subjects.CreateAsync(SubjectTypes.Goal, $"second goal {ns}", cancellationToken: Ct);

        var refused = await Task.WhenAll(
            Refused(relations.LinkAsync(first.Urn, SubjectRelations.Serves, second.Urn, Ct)),
            Refused(relations.LinkAsync(second.Urn, SubjectRelations.Serves, first.Urn, Ct)));

        Assert.Equal(1, refused.Count(r => r));
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        Assert.Equal(1, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject IN (@first, @second);",
            new { first = first.Id, second = second.Id }, cancellationToken: Ct)));

        static async Task<bool> Refused(Task link)
        {
            try
            {
                await link;
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    [Fact]
    public async Task A_task_may_serve_a_commitment()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"draft the report {ns}", cancellationToken: Ct);
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"weekly reporting {ns}", cancellationToken: Ct);

        var result = await relations.LinkAsync(task.Urn, SubjectRelations.Serves, commitment.Urn, Ct);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Unlinking_removes_the_edge_row()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"send the invoice {ns}", cancellationToken: Ct);
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"bill clients weekly {ns}", cancellationToken: Ct);
        await relations.LinkAsync(task.Urn, SubjectRelations.Serves, commitment.Urn, Ct);

        var result = await relations.UnlinkAsync(task.Urn, SubjectRelations.Serves, commitment.Urn, Ct);

        Assert.Equal(1, result.Removed);

        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @from AND to_subject = @to;",
            new { from = task.Id, to = commitment.Id }, cancellationToken: Ct));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Unlinking_a_missing_edge_is_a_no_op()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"unrelated task {ns}", cancellationToken: Ct);
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"unrelated commitment {ns}", cancellationToken: Ct);

        var result = await relations.UnlinkAsync(task.Urn, SubjectRelations.Serves, commitment.Urn, Ct);

        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public async Task Linking_twice_keeps_one_edge_and_answers_it_both_times()
    {
        // Code review CR111: a second link stored the edge again; bsk link, /mcp and the API share this.
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"sweep the porch {ns}", cancellationToken: Ct);
        var project = await subjects.CreateAsync(SubjectTypes.Project, $"tidy the house {ns}", cancellationToken: Ct);

        var first = await relations.LinkAsync(task.Urn, SubjectRelations.Serves, project.Urn, Ct);
        var second = await relations.LinkAsync(task.Urn, SubjectRelations.Serves, project.Urn, Ct);
        Assert.Equal(first.Id, second.Id);

        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        await connection.OpenAsync(Ct);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @from AND to_subject = @to;",
            new { from = task.Id, to = project.Id }, cancellationToken: Ct)));

        // And any door that writes the table: the database itself refuses a second copy.
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
            VALUES (@from, 'serves', @to, 'declared');
            """,
            new { from = task.Id, to = project.Id }, cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);

        Assert.Equal(1, (await relations.UnlinkAsync(task.Urn, SubjectRelations.Serves, project.Urn, Ct)).Removed);
    }
}
