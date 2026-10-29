using System.Globalization;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// The SuperJournal pointer events in <c>bsk.event</c> (D19, migration 0049): kind
/// <c>journal</c>, provenance <c>declared</c>, <c>occurred_at</c> when the version was saved,
/// <c>source_id</c> who saved it, <c>external_id</c> <see cref="JournalPointers.ExternalId"/>, and
/// the payload <c>{"superjournal": {journal_id, entry_id, version, subject_type, subject_key,
/// excerpt[, reconciled]}}</c>. A version that already has a pointer, under either unique key,
/// keeps the one it has.
/// </summary>
public sealed class NpgsqlJournalPointerStore(string connectionString) : IJournalPointerStore
{
    public async Task<JournalPointerWrite> AppendAsync(
        JournalPointer pointer, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string insert = """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
            VALUES ('journal', 'declared', @SavedAt, @RecordedAt, @Source, @ExternalId, @Payload::jsonb)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;
        var written = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            insert,
            new
            {
                // Npgsql writes timestamptz from UTC only.
                SavedAt = pointer.SavedAt.ToUniversalTime(),
                RecordedAt = recordedAt.ToUniversalTime(),
                pointer.Source,
                ExternalId = JournalPointers.ExternalId(pointer.EntryId, pointer.Version),
                Payload = Payload(pointer),
            },
            cancellationToken: cancellationToken));
        if (written is { } id)
        {
            return new JournalPointerWrite(id, Written: true);
        }

        // Either key collapsed it, and both mean this version already has a pointer.
        const string existing = """
            SELECT id FROM bsk.event
            WHERE kind = 'journal'
              AND (payload->'superjournal'->>'entry_id') = @EntryId
              AND (payload->'superjournal'->>'version') = @Version
            """;
        var found = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            existing,
            new { pointer.EntryId, Version = pointer.Version.ToString(CultureInfo.InvariantCulture) },
            cancellationToken: cancellationToken));
        return found is { } kept
            ? new JournalPointerWrite(kept, Written: false)
            : throw new InvalidOperationException(
                $"The pointer for entry {pointer.EntryId} version {pointer.Version} collided with an event that isn't its pointer.");
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetVersionsAsync(
        IReadOnlyCollection<string> entryIds, CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlySet<int>>();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT payload->'superjournal'->>'entry_id' AS entry_id,
                   (payload->'superjournal'->>'version')::int AS version
            FROM bsk.event
            WHERE kind = 'journal'
              AND (payload->'superjournal'->>'entry_id') = ANY(@ids)
            """;
        var rows = await connection.QueryAsync<(string EntryId, int Version)>(new CommandDefinition(
            sql, new { ids = entryIds.Distinct(StringComparer.Ordinal).ToArray() }, cancellationToken: cancellationToken));
        return rows
            .GroupBy(r => r.EntryId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(r => r.Version).ToHashSet(), StringComparer.Ordinal);
    }

    private static string Payload(JournalPointer pointer)
    {
        var inner = new Dictionary<string, object>
        {
            ["journal_id"] = pointer.JournalId,
            ["entry_id"] = pointer.EntryId,
            ["version"] = pointer.Version,
            ["subject_type"] = pointer.SubjectType,
            ["subject_key"] = pointer.SubjectKey,
            ["excerpt"] = pointer.Excerpt,
        };
        if (pointer.Reconciled)
        {
            inner["reconciled"] = true;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { [JournalPointers.PayloadKey] = inner });
    }
}
