using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The one way the repositories append to the event log (code review CR20): an event's row, and
/// an edge from it to a subject. Instants are written in UTC, which Npgsql requires of a
/// <c>timestamptz</c>.
/// </summary>
internal static class EventSql
{
    private const string InsertSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, derived_from, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @ExternalId,
             @Payload::jsonb, @DerivedFrom, @ArtifactId)
        """;

    // A source's external id is unique (event_source_external_key, invariant 5). A second insert
    // of one waits for the first to commit or roll back, then inserts nothing.
    private const string OnceSql = " ON CONFLICT (source_id, external_id) WHERE external_id IS NOT NULL DO NOTHING";

    /// <summary>An edge from an event to a subject: <c>@EventId</c>, <c>@Relation</c>, <c>@SubjectId</c>, <c>@Provenance</c>.</summary>
    public const string InsertEdgeSql = """
        INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
        VALUES (@EventId, @Relation, @SubjectId, @Provenance);
        """;

    /// <summary>Appends <paramref name="newEvent"/>, in <paramref name="transaction"/> when given, and answers its id.</summary>
    public static Task<Guid> InsertAsync(
        NpgsqlConnection connection, NewEvent newEvent, NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<Guid>(Command(InsertSql + " RETURNING id;", newEvent, transaction, cancellationToken));

    /// <summary>
    /// Appends <paramref name="newEvent"/> unless its source already wrote its external id, and
    /// answers its id, or null when it wrote nothing (code review CR68).
    /// </summary>
    public static Task<Guid?> InsertOnceAsync(
        NpgsqlConnection connection, NewEvent newEvent, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<Guid?>(Command(InsertSql + OnceSql + " RETURNING id;", newEvent, transaction, cancellationToken));

    private static CommandDefinition Command(
        string sql, NewEvent newEvent, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
        => new(
            sql,
            new
            {
                newEvent.Kind,
                newEvent.Provenance,
                OccurredAt = newEvent.OccurredAt.ToUniversalTime(),
                RecordedAt = newEvent.RecordedAt.ToUniversalTime(),
                newEvent.SourceId,
                newEvent.ExternalId,
                Payload = newEvent.PayloadJson,
                DerivedFrom = (newEvent.DerivedFrom ?? []).ToArray(),
                newEvent.ArtifactId
            },
            transaction,
            cancellationToken: cancellationToken);

    /// <summary>Links <paramref name="eventId"/> to each edge's subject.</summary>
    public static async Task InsertEdgesAsync(
        NpgsqlConnection connection, Guid eventId, IEnumerable<SubjectEventEdge> edges,
        NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        foreach (var edge in edges)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertEdgeSql,
                new { EventId = eventId, edge.Relation, edge.SubjectId, edge.Provenance },
                transaction,
                cancellationToken: cancellationToken));
        }
    }
}
