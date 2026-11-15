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
}
