using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Creates directed edges in <c>bsk.relation</c>.</summary>
public sealed class NpgsqlRelationRepository(string connectionString) : IRelationRepository
{
    public async Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.relation (from_subject, relation, to_subject, provenance)
            VALUES (@fromSubject, @relation, @toSubject, @provenance)
            RETURNING id;
            """;

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql,
            new { fromSubject, relation, toSubject, provenance },
            cancellationToken: cancellationToken));
    }
}
