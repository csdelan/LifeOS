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
}
