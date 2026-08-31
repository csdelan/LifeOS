using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Creates <c>event → subject</c> edges in <c>bsk.subject_event</c> for events that
/// already exist. Idempotent on the table's <c>(event_id, subject_id, relation)</c>
/// unique key, so relating the same capture to the same subject twice is a no-op.
/// </summary>
public sealed class NpgsqlSubjectEventRepository(string connectionString) : ISubjectEventRepository
{
    public async Task<bool> CreateEdgeAsync(
        Guid eventId, Guid subjectId, string relation, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
            VALUES (@EventId, @Relation, @SubjectId, @Provenance)
            ON CONFLICT (event_id, subject_id, relation) DO NOTHING
            RETURNING id;
            """;

        // RETURNING yields a row only on a real insert; a conflict returns nothing.
        var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            sql,
            new { EventId = eventId, Relation = relation, SubjectId = subjectId, Provenance = provenance },
            cancellationToken: cancellationToken));

        return id.HasValue;
    }
}
