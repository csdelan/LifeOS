using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Writes an activity event and its <c>event → subject</c> edges in a single
/// transaction: either the event and every edge land, or nothing does. This matters
/// because <c>bsk.event</c> is append-only — a committed event with missing edges
/// could never be corrected or removed.
/// </summary>
public sealed class NpgsqlActivityWriter(string connectionString) : IActivityWriter
{
    private const string InsertEventSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, derived_from, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @ExternalId,
             @Payload::jsonb, @DerivedFrom, @ArtifactId)
        RETURNING id;
        """;

    private const string InsertEdgeSql = """
        INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
        VALUES (@EventId, @Relation, @SubjectId, @Provenance);
        """;

    public async Task<Guid> WriteAsync(
        NewEvent activityEvent, IReadOnlyList<SubjectEventEdge> edges,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            InsertEventSql,
            new
            {
                activityEvent.Kind,
                activityEvent.Provenance,
                activityEvent.OccurredAt,
                activityEvent.RecordedAt,
                activityEvent.SourceId,
                activityEvent.ExternalId,
                Payload = activityEvent.PayloadJson,
                DerivedFrom = (activityEvent.DerivedFrom ?? []).ToArray(),
                activityEvent.ArtifactId
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        foreach (var edge in edges)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertEdgeSql,
                new { EventId = eventId, edge.Relation, edge.SubjectId, edge.Provenance },
                transaction: transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return eventId;
    }
}
