using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Adds and removes rows in <c>bsk.item_tag</c> (GEN-1). Set-based: one statement
/// per call over the tag array. Adding is idempotent on the per-item unique index;
/// the affected-row count is how many tags actually changed. The item is a subject
/// or an event — the caller says which, and the unused id column stays NULL.
/// </summary>
public sealed class NpgsqlTagRepository(string connectionString) : ITagRepository
{
    public async Task<int> AddAsync(
        bool isEvent, Guid itemId, IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        if (tags.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO bsk.item_tag (subject_id, event_id, tag)
            SELECT @SubjectId, @EventId, t FROM unnest(@Tags) AS t
            ON CONFLICT DO NOTHING;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(
            sql, Parameters(isEvent, itemId, tags), cancellationToken: cancellationToken));
    }

    public async Task<int> RemoveAsync(
        bool isEvent, Guid itemId, IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        if (tags.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // NOT DISTINCT FROM so the NULL side of the polymorphic item matches NULL.
        const string sql = """
            DELETE FROM bsk.item_tag
            WHERE tag = ANY(@Tags)
              AND subject_id IS NOT DISTINCT FROM @SubjectId
              AND event_id   IS NOT DISTINCT FROM @EventId;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(
            sql, Parameters(isEvent, itemId, tags), cancellationToken: cancellationToken));
    }

    public async Task<int> CopyToSubjectAsync(
        bool fromIsEvent, Guid fromItemId, Guid toSubjectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // One set-based copy; the per-item unique index makes it idempotent.
        const string sql = """
            INSERT INTO bsk.item_tag (subject_id, tag)
            SELECT @ToSubjectId, it.tag
            FROM bsk.item_tag it
            WHERE it.subject_id IS NOT DISTINCT FROM @SubjectId
              AND it.event_id   IS NOT DISTINCT FROM @EventId
            ON CONFLICT DO NOTHING;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                ToSubjectId = toSubjectId,
                SubjectId = fromIsEvent ? (Guid?)null : fromItemId,
                EventId = fromIsEvent ? fromItemId : (Guid?)null
            },
            cancellationToken: cancellationToken));
    }

    public async Task<(int Renamed, int Merged)> RenameAsync(
        string oldTag, string newTag, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Items already carrying the new tag keep that row; their old row goes, or the
        // relabel below would trip the per-item unique index.
        const string merge = """
            DELETE FROM bsk.item_tag o
            WHERE o.tag = @OldTag
              AND EXISTS (
                  SELECT 1 FROM bsk.item_tag n
                  WHERE n.tag = @NewTag
                    AND n.subject_id IS NOT DISTINCT FROM o.subject_id
                    AND n.event_id   IS NOT DISTINCT FROM o.event_id);
            """;
        var merged = await connection.ExecuteAsync(new CommandDefinition(
            merge, new { OldTag = oldTag, NewTag = newTag }, transaction, cancellationToken: cancellationToken));

        var renamed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.item_tag SET tag = @NewTag WHERE tag = @OldTag;",
            new { OldTag = oldTag, NewTag = newTag }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return (renamed, merged);
    }

    private static object Parameters(bool isEvent, Guid itemId, IReadOnlyCollection<string> tags) => new
    {
        SubjectId = isEvent ? (Guid?)null : itemId,
        EventId = isEvent ? itemId : (Guid?)null,
        Tags = tags.ToArray()
    };
}
