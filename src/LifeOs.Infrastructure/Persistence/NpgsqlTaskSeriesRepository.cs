using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Repeating Tasks in the store (D16, migration 0042): reads through
/// <c>bsk.v_task_repetition</c> and status folded from source, and makes repetitions under a
/// transaction-scoped advisory lock per series that re-checks the series' latest date. The
/// unique index on (series, occurrence) backs that up. Dates travel as text, since Dapper has
/// no DateOnly mapping in every host.
/// </summary>
public sealed class NpgsqlTaskSeriesRepository(string connectionString) : ITaskSeriesRepository
{
    // How far ahead NextOccurrenceAsync looks for a pattern's next date.
    private const int LookAheadDays = 3 * 366;

    private const string RepetitionSql = """
        SELECT id, urn, title,
               to_char(occurrence, 'YYYY-MM-DD') AS occurrence,
               to_char(due, 'YYYY-MM-DD') AS due,
               to_char(scheduled, 'YYYY-MM-DD') AS scheduled,
               status, closed,
               to_char(closed_on, 'YYYY-MM-DD') AS closedon,
               archived
        FROM bsk.v_task_repetition
        """;

    public async Task<TaskSeriesState?> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<TaskRow>(new CommandDefinition(
            """
            SELECT s.id, s.urn, s.type, s.title,
                   CASE WHEN jsonb_typeof(s.attributes->'recurrence') = 'object'
                        THEN (s.attributes->'recurrence')::text END AS recurrence,
                   to_char(bsk.try_to_date(s.attributes->>'start'), 'YYYY-MM-DD') AS start,
                   to_char(bsk.try_to_date(s.attributes->>'due'), 'YYYY-MM-DD') AS due,
                   c.status,
                   bsk.is_archived(s.id) AS archived,
                   s.attributes->>'series' AS repetitionof,
                   s.attributes->>'description' AS description,
                   s.attributes->>'area' AS area,
                   s.attributes->>'priority' AS priority,
                   s.attributes->>'estimated_duration' AS estimatedduration
            FROM bsk.subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            WHERE s.id = @taskId AND s.type = 'Task';
            """,
            new { taskId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var latest = await connection.QueryFirstOrDefaultAsync<RepetitionRow>(new CommandDefinition(
            RepetitionSql + " WHERE series_urn = @urn ORDER BY occurrence DESC NULLS LAST LIMIT 1;",
            new { urn = row.Urn }, cancellationToken: cancellationToken));

        var copied = new Dictionary<string, string>(StringComparer.Ordinal);
        void Copy(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                copied[key] = value;
            }
        }

        Copy("description", row.Description);
        Copy("area", row.Area);
        Copy("priority", row.Priority);
        Copy("estimated_duration", row.EstimatedDuration);

        return new TaskSeriesState(
            new SubjectRef(row.Id, row.Urn, row.Type, row.Title),
            row.Recurrence,
            Date(row.Start),
            Date(row.Due),
            row.Status,
            row.Archived,
            row.RepetitionOf,
            copied,
            latest?.ToRepetition());
    }

    public async Task<IReadOnlyList<Guid>> RepeatingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM bsk.v_task_series WHERE NOT stopped ORDER BY id;",
            cancellationToken: cancellationToken));
        return ids.AsList();
    }

    public async Task<IReadOnlyList<TaskRepetition>> RepetitionsAsync(
        string seriesUrn, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<RepetitionRow>(new CommandDefinition(
            RepetitionSql + " WHERE series_urn = @seriesUrn ORDER BY occurrence, created_at;",
            new { seriesUrn }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToRepetition()).ToList();
    }

    public async Task<DateOnly?> NextOccurrenceAsync(
        string recurrenceJson, DateOnly anchor, DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var next = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT to_char(min(d), 'YYYY-MM-DD')
            FROM bsk.recurrence_occurrences(@recurrence::jsonb, @anchor::date, @from::date, @from::date + @days) AS d;
            """,
            new { recurrence = recurrenceJson, anchor = Iso(anchor), from = Iso(from), days = LookAheadDays },
            cancellationToken: cancellationToken));
        return Date(next);
    }

    public async Task<IReadOnlyList<SubjectRef>?> CreateAsync(
        TaskSeriesState series, DateOnly? expectedLatest, IReadOnlyList<NewTaskRepetition> repetitions,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockAsync(connection, transaction, series.Series.Id, cancellationToken);

        var latest = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT to_char(max(bsk.try_to_date(attributes->>'occurrence')), 'YYYY-MM-DD')
            FROM bsk.subject
            WHERE type = 'Task' AND attributes->>'series' = @urn;
            """,
            new { urn = series.Series.Urn }, transaction, cancellationToken: cancellationToken));
        if (Date(latest) != expectedLatest)
        {
            return null;
        }

        var made = new List<SubjectRef>(repetitions.Count);
        foreach (var repetition in repetitions)
        {
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.subject (urn, type, title, attributes)
                VALUES (@Urn, 'Task', @Title, @Attributes::jsonb)
                RETURNING id;
                """,
                new { repetition.Urn, repetition.Title, Attributes = repetition.AttributesJson },
                transaction, cancellationToken: cancellationToken));

            // The series' tags and parent links come with it.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.item_tag (subject_id, tag)
                SELECT @id, tag FROM bsk.item_tag WHERE subject_id = @seriesId
                ON CONFLICT DO NOTHING;
                """,
                new { id, seriesId = series.Series.Id }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance)
                SELECT @id, relation, to_subject, provenance
                FROM bsk.subject_relation
                WHERE from_subject = @seriesId AND relation = 'serves';
                """,
                new { id, seriesId = series.Series.Id }, transaction, cancellationToken: cancellationToken));

            made.Add(new SubjectRef(id, repetition.Urn, SubjectTypes.Task, repetition.Title));
        }

        await transaction.CommitAsync(cancellationToken);
        return made;
    }

    public async Task<bool> RedateAsync(
        Guid seriesId, Guid repetitionId, string patchJson, IReadOnlyList<string> removeKeys,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockAsync(connection, transaction, seriesId, cancellationToken);

        var taken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM bsk.subject o
                JOIN bsk.subject r ON r.id = @repetitionId
                WHERE o.type = 'Task'
                  AND o.id <> r.id
                  AND o.attributes->>'series' = r.attributes->>'series'
                  AND o.attributes->>'occurrence' = @patch::jsonb->>'occurrence');
            """,
            new { repetitionId, patch = patchJson }, transaction, cancellationToken: cancellationToken));
        if (taken)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.subject SET attributes = (attributes || @patch::jsonb) - @remove WHERE id = @repetitionId;",
            new { repetitionId, patch = patchJson, remove = removeKeys.ToArray() },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<(bool Updated, int RepetitionsRenamed)> RenameWithOpenRepetitionsAsync(
        Guid id, string urn, string previousTitle, string newTitle, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.subject SET title = @newTitle WHERE id = @id;",
            new { id, newTitle }, transaction, cancellationToken: cancellationToken));

        // Only open repetitions still carrying the old title follow, so one renamed by hand
        // keeps its own name, and a closed one keeps the name it was done under.
        var repetitions = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bsk.subject r
            SET title = @newTitle
            FROM bsk.v_task_repetition v
            WHERE v.id = r.id
              AND v.series_urn = @urn
              AND NOT v.closed
              AND r.title = @previousTitle;
            """,
            new { urn, previousTitle, newTitle }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return (updated > 0, repetitions);
    }

    public async Task<int> FollowAttributeAsync(
        string seriesUrn, string key, string? previousValue, string? newValue,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bsk.subject r
            SET attributes = CASE WHEN @newValue::text IS NULL THEN r.attributes - @key
                                  ELSE jsonb_set(r.attributes, ARRAY[@key], to_jsonb(@newValue::text)) END
            FROM bsk.v_task_repetition v
            WHERE v.id = r.id
              AND v.series_urn = @seriesUrn
              AND NOT v.closed
              AND (r.attributes->>@key) IS NOT DISTINCT FROM @previousValue::text;
            """,
            new { seriesUrn, key, previousValue, newValue }, cancellationToken: cancellationToken));
    }

    public async Task<int> FollowTagsAsync(
        string seriesUrn, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var open = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM bsk.v_task_repetition WHERE series_urn = @seriesUrn AND NOT closed;",
            new { seriesUrn }, transaction, cancellationToken: cancellationToken))).ToArray();
        if (open.Length > 0 && remove.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bsk.item_tag WHERE subject_id = ANY(@open) AND tag = ANY(@remove);",
                new { open, remove = remove.ToArray() }, transaction, cancellationToken: cancellationToken));
        }

        if (open.Length > 0 && add.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO bsk.item_tag (subject_id, tag)
                SELECT o, t FROM unnest(@open) AS o CROSS JOIN unnest(@add) AS t
                ON CONFLICT DO NOTHING;
                """,
                new { open, add = add.ToArray() }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return open.Length;
    }

    private static Task LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid seriesId, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended('bsk.task_series:' || @seriesId::text, 0));",
            new { seriesId }, transaction, cancellationToken: cancellationToken));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? Date(string? text)
        => string.IsNullOrEmpty(text) ? null : DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class TaskRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Recurrence { get; set; }
        public string? Start { get; set; }
        public string? Due { get; set; }
        public string? Status { get; set; }
        public bool Archived { get; set; }
        public string? RepetitionOf { get; set; }
        public string? Description { get; set; }
        public string? Area { get; set; }
        public string? Priority { get; set; }
        public string? EstimatedDuration { get; set; }
    }

    private sealed class RepetitionRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Occurrence { get; set; }
        public string? Due { get; set; }
        public string? Scheduled { get; set; }
        public string? Status { get; set; }
        public bool Closed { get; set; }
        public string? ClosedOn { get; set; }
        public bool Archived { get; set; }

        public TaskRepetition ToRepetition()
            => new(Id, Urn, Title, Date(Occurrence) ?? DateOnly.MinValue, Date(Due), Date(Scheduled), Status, Closed,
                Date(ClosedOn), Archived);
    }
}
