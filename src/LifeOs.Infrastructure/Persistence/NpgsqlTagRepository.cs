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

    private static object Parameters(bool isEvent, Guid itemId, IReadOnlyCollection<string> tags) => new
    {
        SubjectId = isEvent ? (Guid?)null : itemId,
        EventId = isEvent ? itemId : (Guid?)null,
        Tags = tags.ToArray()
    };
}
