using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Reads source events from <c>bsk.event</c>.</summary>
public sealed class NpgsqlEventReader(NpgsqlDataSource dataSource) : IEventReader
{
    public async Task<SourceEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // subject_id is read as text and parsed here; a payload need not carry one,
        // and when present it is a subject uuid (e.g. the Problem an idea_session names).
        var row = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Kind, string? SubjectId)?>(
            new CommandDefinition(
                "SELECT id, kind, payload->>'subject_id' AS SubjectId FROM bsk.event WHERE id = @id;",
                new { id }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var subjectId = Guid.TryParse(row.Value.SubjectId, out var parsed) ? parsed : (Guid?)null;
        return new SourceEvent(row.Value.Id, row.Value.Kind, subjectId);
    }

    public async Task<bool> EverTriagedAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            EverTriagedSql, new { item = itemId.ToString() }, cancellationToken: cancellationToken));
    }

    public async Task<EarlierWrite?> FindWriteAsync(
        string sourceId, string externalId, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<WriteRow>(new CommandDefinition(
            FindWriteSql, new { sourceId, externalId }, cancellationToken: cancellationToken))).AsList();
        return rows.Count == 0
            ? null
            : new EarlierWrite(
                rows[0].Id,
                rows[0].Kind,
                rows[0].ArtifactId,
                rows.Where(r => r.SubjectId is not null)
                    .Select(r => new SubjectRef(r.SubjectId!.Value, r.Urn!, r.Type!, r.Title!))
                    .ToList());
    }

    /// <summary>The read behind <see cref="FindWriteAsync"/>; <c>EventIndexTests</c> checks it uses <c>event_source_external_key</c>.</summary>
    public const string FindWriteSql = """
        SELECT e.id, e.kind, e.artifact_id AS artifactid, s.id AS subjectid, s.urn, s.type, s.title
        FROM bsk.event e
        LEFT JOIN bsk.subject_event se ON se.event_id = e.id AND se.relation = 'concerns'
        LEFT JOIN bsk.subject s ON s.id = se.subject_id
        WHERE e.source_id = @sourceId AND e.external_id = @externalId
        ORDER BY se.created_at, se.id;
        """;

    private sealed class WriteRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "";
        public Guid? ArtifactId { get; set; }
        public Guid? SubjectId { get; set; }
        public string? Urn { get; set; }
        public string? Type { get; set; }
        public string? Title { get; set; }
    }

    /// <summary>The read behind <see cref="EverTriagedAsync"/>; <c>EventIndexTests</c> checks it uses <c>event_triage_item</c>.</summary>
    public const string EverTriagedSql =
        "SELECT EXISTS (SELECT 1 FROM bsk.event WHERE kind = 'triage' AND payload->>'item_id' = @item);";
}
