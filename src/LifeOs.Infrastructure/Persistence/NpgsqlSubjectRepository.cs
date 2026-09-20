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

    public async Task<(Guid ChildId, Guid EdgeId)> CreateWithParentEdgeAsync(
        NewSubject child, string relation, Guid parentId, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var childId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.subject (urn, type, title, attributes, origin_event_id)
                VALUES (@Urn, @Type, @Title, @Attributes::jsonb, @OriginEventId)
                RETURNING id;
                """,
                new
                {
                    child.Urn,
                    child.Type,
                    child.Title,
                    Attributes = child.AttributesJson,
                    child.OriginEventId
                },
                transaction: transaction, cancellationToken: cancellationToken));

            // The child is the edge's `from`, the parent the `to`. A bad parent id
            // fails the FK here and rolls the whole thing back — no orphan child.
            var edgeId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@ChildId, @Relation, @ParentId, @Provenance)
                RETURNING id;
                """,
                new { ChildId = childId, Relation = relation, ParentId = parentId, Provenance = provenance },
                transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return (childId, edgeId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Not committed: the transaction is rolled back on dispose, so no partial row.
            throw new DuplicateSubjectException(child.Type, child.Title, ex);
        }
    }

    public async Task<bool> RenameAsync(
        Guid id, string newTitle, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Only the title column changes. The URN is left untouched, so no stored
        // reference is affected — edges and tags key on id, and the URN string is
        // unchanged; the slug it embeds is a birth-time convenience, not identity.
        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE bsk.subject SET title = @Title WHERE id = @Id;",
                new { Id = id, Title = newTitle }, cancellationToken: cancellationToken));

            return affected > 0;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Renaming a reuse-by-title subject (e.g. a Problem) onto an existing title
            // trips subject_reuse_title_key. Read the type back so the message names it,
            // then translate to the application concept (as CreateAsync does).
            var type = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT type FROM bsk.subject WHERE id = @Id;",
                new { Id = id }, cancellationToken: cancellationToken));
            throw new DuplicateSubjectException(type ?? "subject", newTitle, ex);
        }
    }

    public async Task<string?> GetAttributeValueAsync(
        Guid id, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>@key FROM bsk.subject WHERE id = @id;",
            new { id, key }, cancellationToken: cancellationToken));
    }

    public async Task<(Guid NewId, Guid EdgeId)> CreateWithIncomingEdgeAsync(
        NewSubject newSubject, string relation, Guid fromId, string provenance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var newId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.subject (urn, type, title, attributes, origin_event_id)
                VALUES (@Urn, @Type, @Title, @Attributes::jsonb, @OriginEventId)
                RETURNING id;
                """,
                new
                {
                    newSubject.Urn,
                    newSubject.Type,
                    newSubject.Title,
                    Attributes = newSubject.AttributesJson,
                    newSubject.OriginEventId
                },
                transaction: transaction, cancellationToken: cancellationToken));

            // Edge points into the new subject: existing `from` -> new `to`. A bad
            // `from` id fails the FK here and rolls the whole thing back — no orphan.
            var edgeId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                VALUES (@FromId, @Relation, @NewId, @Provenance)
                RETURNING id;
                """,
                new { FromId = fromId, Relation = relation, NewId = newId, Provenance = provenance },
                transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return (newId, edgeId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
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
