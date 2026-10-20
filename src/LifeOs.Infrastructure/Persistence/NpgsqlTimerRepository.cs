using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Task timer reads and writes (TASKS-2). Reads go through the <c>bsk.v_task_timer</c>
/// fold (migration 0024). Writes take a transaction-scoped advisory lock shared by
/// every timer write, re-check each task's state under it, then insert each action's
/// event and its <c>concerns</c> edge — all or nothing, since the stream is append-only.
/// </summary>
public sealed class NpgsqlTimerRepository(string connectionString) : ITimerRepository
{
    // One lock for all timer writes: starting a timer can pause another, so writes
    // to different tasks must serialize too. The key is arbitrary but fixed.
    private const long TimerLockKey = 0x7469_6D65_7273; // "timers"

    // Aliased to the row's property names: the CLI host does not turn on Dapper's
    // underscore matching, and Dapper matches the rest case-insensitively.
    private const string SelectSql = """
        SELECT subject_id AS subjectid, subject_urn AS subjecturn, subject_type AS subjecttype,
               subject_title AS subjecttitle, state, running_since AS runningsince,
               total_seconds AS totalseconds, session_seconds AS sessionseconds
        FROM bsk.v_task_timer
        """;

    private const string InsertEventSql = """
        INSERT INTO bsk.event
            (kind, provenance, occurred_at, recorded_at, source_id, external_id,
             payload, derived_from, artifact_id)
        VALUES
            (@Kind, @Provenance, @OccurredAt, @RecordedAt, @SourceId, @ExternalId,
             @Payload::jsonb, @DerivedFrom, @ArtifactId)
        RETURNING id;
        """;

    private const string InsertEdgeSql = """
        INSERT INTO bsk.subject_event (event_id, relation, subject_id, provenance)
        VALUES (@EventId, @Relation, @SubjectId, @Provenance);
        """;

    public async Task<TimerSnapshot?> GetAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<TimerRow>(new CommandDefinition(
            SelectSql + " WHERE subject_id = @subjectId",
            new { subjectId },
            cancellationToken: cancellationToken));
        return row?.ToSnapshot();
    }

    public async Task<IReadOnlyList<TimerSnapshot>> GetRunningAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<TimerRow>(new CommandDefinition(
            SelectSql + " WHERE state = @running ORDER BY running_since",
            new { running = TimerStates.Running },
            cancellationToken: cancellationToken));
        return rows.Select(r => r.ToSnapshot()).ToList();
    }

    public async Task AppendAsync(IReadOnlyList<TimerWrite> writes, CancellationToken cancellationToken = default)
    {
        if (writes.Count == 0)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@key);",
            new { key = TimerLockKey },
            transaction: transaction,
            cancellationToken: cancellationToken));

        foreach (var write in writes)
        {
            var row = await connection.QueryFirstOrDefaultAsync<TimerRow>(new CommandDefinition(
                SelectSql + " WHERE subject_id = @subjectId",
                new { subjectId = write.SubjectId },
                transaction: transaction,
                cancellationToken: cancellationToken));
            var actual = row?.State ?? TimerStates.Stopped;
            if (!string.Equals(actual, write.ExpectedState, StringComparison.Ordinal))
            {
                var title = row?.SubjectTitle ?? write.SubjectId.ToString();
                throw new TimerConflictException(title, write.ExpectedState, actual);
            }
        }

        foreach (var write in writes)
        {
            var timerEvent = write.Event;
            var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                InsertEventSql,
                new
                {
                    timerEvent.Kind,
                    timerEvent.Provenance,
                    timerEvent.OccurredAt,
                    timerEvent.RecordedAt,
                    timerEvent.SourceId,
                    timerEvent.ExternalId,
                    Payload = timerEvent.PayloadJson,
                    DerivedFrom = (timerEvent.DerivedFrom ?? []).ToArray(),
                    timerEvent.ArtifactId
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                InsertEdgeSql,
                new
                {
                    EventId = eventId,
                    Relation = SubjectEventRelations.Concerns,
                    write.SubjectId,
                    Provenance = timerEvent.Provenance
                },
                transaction: transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class TimerRow
    {
        public Guid SubjectId { get; set; }
        public string SubjectUrn { get; set; } = "";
        public string SubjectType { get; set; } = "";
        public string SubjectTitle { get; set; } = "";
        public string State { get; set; } = TimerStates.Stopped;
        public DateTime? RunningSince { get; set; }
        public long TotalSeconds { get; set; }
        public long SessionSeconds { get; set; }

        public TimerSnapshot ToSnapshot() => new(
            SubjectId,
            SubjectUrn,
            SubjectType,
            SubjectTitle,
            State,
            RunningSince is { } since ? new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)) : null,
            TotalSeconds,
            SessionSeconds);
    }
}
