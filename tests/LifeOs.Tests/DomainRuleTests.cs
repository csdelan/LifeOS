using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Rebuild;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The domain invariants that are easy to erode, locked as executable tests against
/// a real Postgres (M3.6). This suite is the guardrail: a regression that (e.g.)
/// allows <c>serves Task</c>, mutates a promoted source, or edits the projection
/// directly must fail here. Each invariant is exercised through the real write path.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DomainRuleTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    // ------------------------------------------------------------------ leaf-only

    [Fact]
    public async Task Task_is_leaf_only_nothing_may_serve_it()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"leaf task {ns}", cancellationToken: Ct);
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, $"leaf goal {ns}", cancellationToken: Ct);
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"leaf commitment {ns}", cancellationToken: Ct);

        // Nothing may serve a Task.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await relations.LinkAsync(goal.Urn, SubjectRelations.Serves, task.Urn, Ct));

        // A Task may serve a Commitment.
        var served = await relations.LinkAsync(task.Urn, SubjectRelations.Serves, commitment.Urn, Ct);
        Assert.NotEqual(Guid.Empty, served.Id);

        // And a Task may stand alone — a second task created with no relation at all.
        var standalone = await subjects.CreateAsync(SubjectTypes.Task, $"standalone leaf {ns}", cancellationToken: Ct);
        await using var connection = await OpenAsync();
        var standaloneEdges = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @task;",
            new { task = standalone.Id }, cancellationToken: Ct));
        Assert.Equal(0, standaloneEdges);
    }

    // -------------------------------------------------------- promotion immutability

    [Fact]
    public async Task Promotion_never_mutates_the_source_event()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var promotion = provider.GetRequiredService<PromotionService>();

        var source = await capture.CaptureNoteAsync($"a capture that stays a capture {Guid.NewGuid():N}", Ct);

        await using var connection = await OpenAsync();
        var before = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT to_jsonb(e.*)::text FROM bsk.event e WHERE id = @id;",
            new { id = source.EventId }, cancellationToken: Ct));

        await promotion.PromoteAsync(source.EventId, SubjectTypes.Problem, $"a real problem {Guid.NewGuid():N}", Ct);

        var after = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT to_jsonb(e.*)::text FROM bsk.event e WHERE id = @id;",
            new { id = source.EventId }, cancellationToken: Ct));

        Assert.Equal(before, after);
    }

    // -------------------------------------------------------------- event-driven status

    [Fact]
    public async Task Status_is_event_driven_and_the_projection_is_never_written_directly()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var status = provider.GetRequiredService<StatusService>();
        var rebuilder = new DerivedRebuilder(postgres.ConnectionString);

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"status task {Guid.NewGuid():N}", cancellationToken: Ct);
        await status.ChangeStatusAsync(task.Urn, "done", Ct);

        await using var connection = await OpenAsync();

        // The status change is an event...
        var events = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'state_change' AND payload->>'subject_id' = @id;",
            new { id = task.Id.ToString() }, cancellationToken: Ct));
        Assert.Equal(1, events);

        // ...and it does not reach the projection until a rebuild folds it in.
        var beforeRebuild = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk_derived.subject_current WHERE subject_id = @id;",
            new { id = task.Id }, cancellationToken: Ct));
        Assert.Equal(0, beforeRebuild);

        await rebuilder.RebuildAsync(Ct);
        var afterRebuild = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current WHERE subject_id = @id;",
            new { id = task.Id }, cancellationToken: Ct));
        Assert.Equal("done", afterRebuild);

        // A direct edit of the projection is drift the verifier catches — the
        // projection is derived, never a source of truth.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk_derived.subject_current SET status = 'tampered' WHERE subject_id = @id;",
            new { id = task.Id }, cancellationToken: Ct));
        Assert.True((await rebuilder.VerifyAsync(Ct)).HasDrift);
    }

    // ------------------------------------------------------------- idempotent capture

    [Fact]
    public async Task Duplicate_source_and_external_id_does_not_double_write()
    {
        await using var connection = await OpenAsync();
        var sourceId = Guid.NewGuid().ToString();
        const string externalId = "inbox-42";

        async Task InsertAsync() => await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, external_id)
            VALUES ('observation', 'observed', now(), @sourceId, @externalId);
            """,
            new { sourceId, externalId }, cancellationToken: Ct));

        await InsertAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(InsertAsync);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);

        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE source_id = @sourceId AND external_id = @externalId;",
            new { sourceId, externalId }, cancellationToken: Ct));
        Assert.Equal(1, count);
    }

    // ------------------------------------------------- standalone tasks and alignment

    [Fact]
    public async Task Standalone_tasks_are_excluded_from_alignment_queries()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var relations = provider.GetRequiredService<RelationService>();
        var ns = Guid.NewGuid().ToString("N");

        var standalone = await subjects.CreateAsync(SubjectTypes.Task, $"standalone {ns}", cancellationToken: Ct);
        var aligned = await subjects.CreateAsync(SubjectTypes.Task, $"aligned {ns}", cancellationToken: Ct);
        var commitment = await subjects.CreateAsync(SubjectTypes.Commitment, $"the commitment {ns}", cancellationToken: Ct);
        await relations.LinkAsync(aligned.Urn, SubjectRelations.Serves, commitment.Urn, Ct);

        await using var connection = await OpenAsync();

        // "Aligned" = a task that serves at least one subject. This is the seam M4
        // builds on: a standalone task answers to nothing and is excluded.
        const string alignmentQuery = """
            SELECT s.id FROM bsk.subject s
            WHERE s.type = 'Task'
              AND s.id = ANY(@ids)
              AND EXISTS (
                  SELECT 1 FROM bsk.subject_relation r
                  WHERE r.from_subject = s.id AND r.relation = 'serves');
            """;
        var alignedTasks = (await connection.QueryAsync<Guid>(new CommandDefinition(
            alignmentQuery, new { ids = new[] { standalone.Id, aligned.Id } }, cancellationToken: Ct))).ToList();

        Assert.Contains(aligned.Id, alignedTasks);
        Assert.DoesNotContain(standalone.Id, alignedTasks);
    }
}
