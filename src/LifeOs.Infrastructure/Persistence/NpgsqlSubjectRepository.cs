using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Reads and creates subjects in <c>bsk.subject</c>.</summary>
public sealed class NpgsqlSubjectRepository(string connectionString) : ISubjectRepository
{
    public async Task<SubjectRef?> FindByUrnAsync(
        string urn, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<SubjectRef>(new CommandDefinition(
            "SELECT id, urn, type, title FROM bsk.subject WHERE urn = @urn;",
            new { urn }, cancellationToken: cancellationToken));
    }

    public async Task<SubjectRef?> FindByTypeAndTitleAsync(
        string type, string title, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Oldest match wins, so repeated resolves are stable.
        return await connection.QuerySingleOrDefaultAsync<SubjectRef>(new CommandDefinition(
            """
            SELECT id, urn, type, title
            FROM bsk.subject
            WHERE type = @type AND title = @title
            ORDER BY created_at, id
            LIMIT 1;
            """,
            new { type, title }, cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateAsync(
        NewSubject newSubject, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.subject (urn, type, title, attributes, origin_event_id)
            VALUES (@Urn, @Type, @Title, @Attributes::jsonb, @OriginEventId)
            RETURNING id;
            """;

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql,
            new
            {
                newSubject.Urn,
                newSubject.Type,
                newSubject.Title,
                Attributes = newSubject.AttributesJson,
                newSubject.OriginEventId
            },
            cancellationToken: cancellationToken));
    }
}
