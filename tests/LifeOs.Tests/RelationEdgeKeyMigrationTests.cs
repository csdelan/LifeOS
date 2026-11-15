using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Migration 0068 (code review CR111): the duplicate alignment edges a second link stored are
/// folded to the oldest of each before the unique key goes on, so the migration applies to a
/// database that has them, as staging may.
/// </summary>
public sealed class RelationEdgeKeyMigrationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_migration_keeps_the_oldest_of_each_duplicate_edge_and_then_refuses_another()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var subjects = db.Kernel.GetRequiredService<SubjectService>();
        var task = await subjects.CreateAsync(SubjectTypes.Task, "Sweep the porch", cancellationToken: Ct);
        var project = await subjects.CreateAsync(SubjectTypes.Project, "Tidy the house", cancellationToken: Ct);
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, "A calm home", cancellationToken: Ct);

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);

        // The table as it was before 0068: no key, and the same edge stored three times.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DROP INDEX bsk.subject_relation_edge_key;
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance, created_at) VALUES
                (@task, 'serves', @project, 'declared', '2026-03-02T10:00:00Z'),
                (@task, 'serves', @project, 'declared', '2026-03-01T10:00:00Z'),
                (@task, 'serves', @project, 'derived',  '2026-03-03T10:00:00Z'),
                (@project, 'serves', @goal, 'declared', '2026-03-01T10:00:00Z');
            """,
            new { task = task.Id, project = project.Id, goal = goal.Id }, cancellationToken: Ct));

        var migration = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot.Path, "db", "migrations", "0068__relation_edge_key.sql"), Ct);
        await connection.ExecuteAsync(new CommandDefinition(migration, cancellationToken: Ct));

        var kept = Assert.Single(await connection.QueryAsync<(string Provenance, DateTime CreatedAt)>(new CommandDefinition(
            "SELECT provenance, created_at FROM bsk.subject_relation WHERE from_subject = @task AND to_subject = @project;",
            new { task = task.Id, project = project.Id }, cancellationToken: Ct)));
        Assert.Equal(new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), kept.CreatedAt.ToUniversalTime());
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @project AND to_subject = @goal;",
            new { project = project.Id, goal = goal.Id }, cancellationToken: Ct)));

        var again = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance) VALUES (@task, 'serves', @project, 'declared');",
            new { task = task.Id, project = project.Id }, cancellationToken: Ct)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, again.SqlState);
    }
}
