using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-7 (Phase 6): parent-first creation. A child subject and its parent edge are
/// created atomically, with the relation inferred from the canonical map
/// (Goal→Value serves, Project→Goal results_in, Task→Project/Goal serves). These
/// tests cover each inferred pair, the leaf-rule guard, the ambiguous-pair error,
/// and — at the repository — that a failed edge leaves no orphan child.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChildCreationServiceTests(PostgresFixture postgres)
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

    private async Task<bool> EdgeExistsAsync(
        NpgsqlConnection connection, Guid from, string relation, Guid to)
        => await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM bsk.subject_relation
                WHERE from_subject = @from AND relation = @relation AND to_subject = @to);
            """,
            new { from, relation, to }, cancellationToken: Ct));

    private async Task<SubjectRef> MakeValueAsync(SubjectService subjects, string ns)
        => await subjects.CreateAsync(
            SubjectTypes.Value, $"value {ns}",
            attributesJson: """{"statement":"I am someone who ships."}""", cancellationToken: Ct);

    [Fact]
    public async Task Goal_under_a_value_infers_serves()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var children = provider.GetRequiredService<ChildCreationService>();
        var ns = Guid.NewGuid().ToString("N");

        var value = await MakeValueAsync(subjects, ns);
        var result = await children.CreateChildAsync(SubjectTypes.Goal, $"goal {ns}", value.Urn, cancellationToken: Ct);

        Assert.Equal(SubjectRelations.Serves, result.Relation);
        await using var connection = await OpenAsync();
        Assert.True(await EdgeExistsAsync(connection, result.Child.Id, SubjectRelations.Serves, value.Id));
    }

    [Theory]
    [InlineData(SubjectTypes.Project, SubjectTypes.Goal, SubjectRelations.ResultsIn)]
    [InlineData(SubjectTypes.Task, SubjectTypes.Project, SubjectRelations.Serves)]
    [InlineData(SubjectTypes.Task, SubjectTypes.Goal, SubjectRelations.Serves)]
    public async Task Canonical_pairs_infer_the_expected_relation(
        string childType, string parentType, string expectedRelation)
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var children = provider.GetRequiredService<ChildCreationService>();
        var ns = Guid.NewGuid().ToString("N");

        var parent = await subjects.CreateAsync(parentType, $"{parentType} {ns}", cancellationToken: Ct);
        var result = await children.CreateChildAsync(childType, $"{childType} {ns}", parent.Urn, cancellationToken: Ct);

        Assert.Equal(expectedRelation, result.Relation);
        await using var connection = await OpenAsync();
        Assert.True(await EdgeExistsAsync(connection, result.Child.Id, expectedRelation, parent.Id));
    }

    [Fact]
    public async Task An_ambiguous_pair_without_an_explicit_relation_is_rejected()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var children = provider.GetRequiredService<ChildCreationService>();
        var ns = Guid.NewGuid().ToString("N");

        // Task under Value is not in the canonical map.
        var value = await MakeValueAsync(subjects, ns);
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await children.CreateChildAsync(SubjectTypes.Task, $"task {ns}", value.Urn, cancellationToken: Ct));
    }

    [Fact]
    public async Task The_leaf_rule_blocks_creating_something_that_serves_a_task()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var children = provider.GetRequiredService<ChildCreationService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"leaf {ns}", cancellationToken: Ct);

        // Forcing serves onto a Task parent must be refused (nothing serves a Task).
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await children.CreateChildAsync(
                SubjectTypes.Goal, $"child {ns}", task.Urn, relationOverride: SubjectRelations.Serves, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_failed_parent_edge_leaves_no_orphan_child()
    {
        // Repository-level: a bogus parent id fails the FK, and the whole create rolls back.
        var repository = new NpgsqlSubjectRepository(postgres.ConnectionString);
        var urn = $"urn:bsk:task:orphan-{Guid.NewGuid():N}";
        var child = new NewSubject(urn, SubjectTypes.Task, "orphan task", "{}", OriginEventId: null);

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await repository.CreateWithParentEdgeAsync(
                child, SubjectRelations.Serves, Guid.NewGuid(), Provenances.Declared, Ct));

        await using var connection = await OpenAsync();
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk.subject WHERE urn = @urn);",
            new { urn }, cancellationToken: Ct));
        Assert.False(exists);
    }
}
