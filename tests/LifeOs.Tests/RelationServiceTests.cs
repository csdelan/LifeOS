using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk link</c> (M3.2): linking produces a relation row, the leaf-only rule
/// rejects <c>X serves Task</c> with a clear message, and a Task serving a
/// Commitment (or standing alone) is allowed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RelationServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

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

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
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
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE to_subject = @task;",
            new { task = task.Id }, cancellationToken: Ct));
        Assert.Equal(0, count);
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
}
