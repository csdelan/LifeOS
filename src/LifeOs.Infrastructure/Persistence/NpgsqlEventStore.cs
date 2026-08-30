using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Appends events to <c>bsk.event</c>.</summary>
public sealed class NpgsqlEventStore(string connectionString) : IEventStore
{
    public async Task<Guid> AppendAsync(NewEvent newEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.event
                (kind, provenance, occurred_at, recorded_at, source_id, external_id,
                 payload, derived_from, artifact_id)
            VALUES
                (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @ExternalId,
                 @Payload::jsonb, @DerivedFrom, @ArtifactId)
            RETURNING id;
            """;

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql,
            new
            {
                newEvent.Kind,
                newEvent.Provenance,
                newEvent.OccurredAt,
                newEvent.RecordedAt,
                newEvent.SourceId,
                newEvent.ExternalId,
                Payload = newEvent.PayloadJson,
                DerivedFrom = (newEvent.DerivedFrom ?? []).ToArray(),
                newEvent.ArtifactId
            },
            cancellationToken: cancellationToken));
    }
}
