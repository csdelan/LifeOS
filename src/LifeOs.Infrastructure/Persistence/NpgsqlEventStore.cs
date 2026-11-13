using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Appends events to <c>bsk.event</c>.</summary>
public sealed class NpgsqlEventStore(NpgsqlDataSource dataSource) : IEventStore
{
    public async Task<Guid> AppendAsync(NewEvent newEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await EventSql.InsertAsync(connection, newEvent, null, cancellationToken);
    }
}
