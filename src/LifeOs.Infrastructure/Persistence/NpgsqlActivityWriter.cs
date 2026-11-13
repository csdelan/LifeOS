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
public sealed class NpgsqlActivityWriter(NpgsqlDataSource dataSource) : IActivityWriter
{
    public async Task<Guid> WriteAsync(
        NewEvent activityEvent, IReadOnlyList<SubjectEventEdge> edges,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var eventId = await EventSql.InsertAsync(connection, activityEvent, transaction, cancellationToken);

        await EventSql.InsertEdgesAsync(connection, eventId, edges, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return eventId;
    }

    public async Task<EventWrite> WriteOnceAsync(
        NewEvent newEvent, string? text, IReadOnlyList<SubjectEventEdge> edges,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var artifactId = text is null
                ? newEvent.ArtifactId
                : await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    "INSERT INTO bsk.artifact (content) VALUES (@text) RETURNING id;",
                    new { text }, transaction, cancellationToken: cancellationToken));
            var written = await EventSql.InsertOnceAsync(
                connection, newEvent with { ArtifactId = artifactId }, transaction, cancellationToken);
            if (written is { } eventId)
            {
                await EventSql.InsertEdgesAsync(connection, eventId, edges, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new EventWrite(eventId, artifactId, newEvent.Kind, AlreadyWritten: false);
            }

            // Already written: the text goes with the transaction, so none is left that nothing names.
            await transaction.RollbackAsync(cancellationToken);
        }

        var earlier = await connection.QuerySingleAsync<(Guid Id, Guid? ArtifactId, string Kind)>(new CommandDefinition(
            "SELECT id, artifact_id, kind FROM bsk.event WHERE source_id = @SourceId AND external_id = @ExternalId;",
            new { newEvent.SourceId, newEvent.ExternalId }, cancellationToken: cancellationToken));
        return new EventWrite(earlier.Id, earlier.ArtifactId, earlier.Kind, AlreadyWritten: true);
    }
}
