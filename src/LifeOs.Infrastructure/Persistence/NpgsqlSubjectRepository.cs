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

    public async Task<IReadOnlyList<SubjectRef>> FindByShortIdAsync(
        string shortId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // The short id is the URN's trailing token, separated from the slug by '-'
        // or (when there is no slug) from the type by ':'. Anchor on that separator
        // so a short id cannot match mid-slug. shortId is validated hex upstream, so
        // it carries no regex metacharacters.
        var matches = await connection.QueryAsync<SubjectRef>(new CommandDefinition(
            """
            SELECT id, urn, type, title
            FROM bsk.subject
            WHERE urn ~ ('[:-]' || @shortId || '$')
            ORDER BY created_at, id;
            """,
            new { shortId }, cancellationToken: cancellationToken));

        return matches.AsList();
    }

    public async Task<IReadOnlyList<SubjectRef>> FindByTitleContainsAsync(
        string fragment, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Case-insensitive substring match; an exact (case-insensitive) title sorts
        // first so the resolver can prefer it when several titles contain the text.
        var matches = await connection.QueryAsync<SubjectRef>(new CommandDefinition(
            """
            SELECT id, urn, type, title
            FROM bsk.subject
            WHERE strpos(lower(title), lower(@fragment)) > 0
            ORDER BY (lower(title) = lower(@fragment)) DESC, created_at, id;
            """,
            new { fragment }, cancellationToken: cancellationToken));

        return matches.AsList();
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

        try
        {
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
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Translate the store's unique-violation into an application concept so
            // callers (e.g. SubjectService) don't depend on Npgsql error codes.
            throw new DuplicateSubjectException(newSubject.Type, newSubject.Title, ex);
        }
    }

    public async Task<bool> UpdateAttributesAsync(
        Guid id, string patchJson, IReadOnlyList<string> removeKeys,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Merge the patch (right wins), then strip any keys marked for removal — one
        // update, so a set-and-clear in the same call is atomic. Only the attributes
        // bag changes; the subject row is otherwise untouched.
        const string sql = """
            UPDATE bsk.subject
            SET attributes = (attributes || @Patch::jsonb) - @Remove::text[]
            WHERE id = @Id;
            """;

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Patch = patchJson, Remove = removeKeys.ToArray() },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
