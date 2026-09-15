using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Creates directed subject → subject edges in <c>bsk.subject_relation</c>.</summary>
public sealed class NpgsqlRelationRepository(string connectionString) : IRelationRepository
{
    public async Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
            VALUES (@fromSubject, @relation, @toSubject, @provenance)
            RETURNING id;
            """;

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql,
            new { fromSubject, relation, toSubject, provenance },
            cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteAsync(
        Guid fromSubject, string relation, Guid toSubject,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
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
