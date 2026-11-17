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

    // The shortest path up from @toSubject to @fromSubject by @relations, as titles from the first
    // to the last, or none. A path stops at an item it has been through, so a loop already in the
    // graph can't keep it walking.
    private const string LoopSql = """
        WITH RECURSIVE up(node, path) AS (
            SELECT @toSubject::uuid, ARRAY[@toSubject::uuid]
            UNION ALL
            SELECT r.to_subject, up.path || r.to_subject
            FROM up
            JOIN bsk.subject_relation r ON r.from_subject = up.node AND r.relation = ANY(@relations)
            WHERE up.node <> @fromSubject AND r.to_subject <> ALL(up.path)
        ),
        loop AS (
            SELECT path FROM up WHERE node = @fromSubject ORDER BY cardinality(path) LIMIT 1
        )
        SELECT s.title
        FROM loop
        CROSS JOIN LATERAL unnest(loop.path) WITH ORDINALITY AS p(id, n)
        JOIN bsk.subject s ON s.id = p.id
        ORDER BY p.n;
        """;

    public async Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await InsertAsync(connection, null, fromSubject, relation, toSubject, provenance, cancellationToken);
    }

    public async Task<LinkOutcome> CreateUnlessLoopAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance, IReadOnlyList<string> acyclic,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A link beside this one waits here, and its check then sees the edge this one added.
        await AdvisoryLocks.TakeAsync(connection, transaction, AdvisoryLocks.Alignment, cancellationToken);
        var loop = (await connection.QueryAsync<string>(new CommandDefinition(
            LoopSql, new { fromSubject, toSubject, relations = acyclic.ToArray() }, transaction,
            cancellationToken: cancellationToken))).AsList();
        if (loop.Count > 0)
        {
            return new LinkOutcome(null, loop);
        }

        var id = await InsertAsync(connection, transaction, fromSubject, relation, toSubject, provenance, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LinkOutcome(id, []);
    }

    private static async Task<Guid> InsertAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid fromSubject, string relation, Guid toSubject, string provenance, CancellationToken cancellationToken)
    {
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
                sql, args, transaction, cancellationToken: cancellationToken))
            ?? await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                ExistingSql, args, transaction, cancellationToken: cancellationToken));
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
