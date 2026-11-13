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
public sealed class NpgsqlTimerRepository(NpgsqlDataSource dataSource) : ITimerRepository
{
    // Aliased to the row's property names: the CLI host does not turn on Dapper's
    // underscore matching, and Dapper matches the rest case-insensitively.
    private const string SelectSql = """
        SELECT subject_id AS subjectid, subject_urn AS subjecturn, subject_type AS subjecttype,
               subject_title AS subjecttitle, state, running_since AS runningsince,
               total_seconds AS totalseconds, session_seconds AS sessionseconds
        FROM bsk.v_task_timer
        """;

    public async Task<TimerSnapshot?> GetAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<TimerRow>(new CommandDefinition(
            SelectSql + " WHERE subject_id = @subjectId",
            new { subjectId },
            cancellationToken: cancellationToken));
        return row?.ToSnapshot();
    }

    public async Task<IReadOnlyList<TimerSnapshot>> GetRunningAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<TimerRow>(new CommandDefinition(
            SelectSql + " WHERE state = @running ORDER BY running_since",
            new { running = TimerStates.Running },
            cancellationToken: cancellationToken));
        return rows.Select(r => r.ToSnapshot()).ToList();
    }

    public async Task<string?> GetTaskStatusAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM bsk_derived.subject_current_source WHERE subject_id = @subjectId",
            new { subjectId },
            cancellationToken: cancellationToken));
    }

    public async Task AppendAsync(IReadOnlyList<TimerWrite> writes, CancellationToken cancellationToken = default)
    {
        if (writes.Count == 0)
        {
            return;
        }

        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await AdvisoryLocks.TakeAsync(connection, transaction, AdvisoryLocks.Timers, cancellationToken);

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
            var eventId = await EventSql.InsertAsync(connection, timerEvent, transaction, cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                EventSql.InsertEdgeSql,
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
