using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Creates directed subject → subject edges in <c>bsk.subject_relation</c>.</summary>
public sealed class NpgsqlRelationRepository(NpgsqlDataSource dataSource) : IRelationRepository
{
    private const string ExistingSql = """
        SELECT id FROM bsk.subject_relation
        WHERE from_subject = @fromSubject AND relation = @relation AND to_subject = @toSubject;
        """;

    public async Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // An edge is stored once (subject_relation_edge_key, code review CR111): a second link
        // answers the edge already there. The statement's snapshot can miss an edge another link
        // committed while it ran, which the second read sees.
        const string sql = """
            WITH inserted AS (
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@fromSubject, @relation, @toSubject, @provenance)
                ON CONFLICT (from_subject, relation, to_subject) DO NOTHING
                RETURNING id)
            SELECT id FROM inserted
            UNION ALL
            SELECT id FROM bsk.subject_relation
            WHERE from_subject = @fromSubject AND relation = @relation AND to_subject = @toSubject
            LIMIT 1;
            """;

        var args = new { fromSubject, relation, toSubject, provenance };
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                sql, args, cancellationToken: cancellationToken))
            ?? await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                ExistingSql, args, cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteAsync(
        Guid fromSubject, string relation, Guid toSubject,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            DELETE FROM bsk.subject_relation
            WHERE from_subject = @fromSubject
              AND relation = @relation
              AND to_subject = @toSubject;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { fromSubject, relation, toSubject },
            cancellationToken: cancellationToken));
    }

    public async Task<int?> DeleteUnlessLastAsync(
        Guid fromSubject, string relation, Guid toSubject, KeptEdges kept,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A removal beside this one waits here, and its count then sees what this one removed.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT 1 FROM bsk.subject WHERE id = @fromSubject FOR UPDATE;",
            new { fromSubject }, transaction, cancellationToken: cancellationToken));

        const string countSql = """
            SELECT count(*) FILTER (WHERE r.relation = @relation AND r.to_subject = @toSubject) AS this_edge,
                   count(*) FILTER (WHERE NOT (r.relation = @relation AND r.to_subject = @toSubject)) AS others
            FROM bsk.subject_relation r
            JOIN bsk.subject s ON s.id = r.to_subject
            WHERE r.from_subject = @fromSubject
              AND r.relation = @keptRelation
              AND s.type = ANY(@keptTypes);
            """;
        var counts = await connection.QuerySingleAsync<(long ThisEdge, long Others)>(new CommandDefinition(
            countSql,
            new { fromSubject, relation, toSubject, keptRelation = kept.Relation, keptTypes = kept.ToTypes.ToArray() },
            transaction, cancellationToken: cancellationToken));
        if (counts.ThisEdge > 0 && counts.Others == 0)
        {
            return null;
        }

        var removed = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM bsk.subject_relation
            WHERE from_subject = @fromSubject AND relation = @relation AND to_subject = @toSubject;
            """,
            new { fromSubject, relation, toSubject }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return removed;
    }
}
