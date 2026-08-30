using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Creates event → subject edges in <c>bsk.subject_event</c>.</summary>
public sealed class NpgsqlSubjectEventRepository(string connectionString) : ISubjectEventRepository
{
    public async Task<Guid> CreateAsync(
        Guid eventId, string relation, Guid subjectId, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
            VALUES (@eventId, @relation, @subjectId, @provenance)
            RETURNING id;
            """;

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql,
            new { eventId, relation, subjectId, provenance },
            cancellationToken: cancellationToken));
    }
}
