using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Repeating appointments in the store (CAL-1, D10). Materialize expands the series' recurrence
/// from its start through the requested day and makes each date no occurrence stands for yet,
/// under a transaction-scoped advisory lock per series; migration 0064's unique index on
/// (series, occurrence) backs that up (code review CR65).
/// </summary>
public sealed class NpgsqlAppointmentRepository(NpgsqlDataSource dataSource, EventSource eventSource, IClock clock) : IAppointmentRepository
{
    public async Task<OccurrencesMade> MaterializeAsync(
        SubjectRef series, DateOnly through, int max, CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AdvisoryLocks.TakeAsync(connection, transaction, $"bsk.appointment_series:{series.Id}", cancellationToken);

        // Anchor and window start at the series' start_date, or the day it was made at home
        // (code review CR62: created_at::date was the session's UTC day, tomorrow by evening in
        // Pacific time). start_date is the series anchor; start/end are the time-of-day fields (CAL-1).
        // A date is taken by the occurrence that stands for it, wherever it's been moved to since;
        // one made before CR65 stands for its date.
        const string sql = """
            SELECT to_char(d, 'YYYY-MM-DD')
            FROM bsk.subject s
            CROSS JOIN LATERAL (
                SELECT coalesce(bsk.try_to_date(s.attributes->>'start_date'), (s.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date
            ) anchor
            CROSS JOIN LATERAL bsk.recurrence_occurrences(
                s.attributes->'recurrence', anchor.start_date, anchor.start_date, @through::date) AS d
            WHERE s.id = @seriesId
              AND NOT EXISTS (
                  SELECT 1 FROM bsk.subject o
                  WHERE o.type = 'Appointment'
                    AND o.attributes->>'series' = @seriesUrn
                    AND coalesce(o.attributes->>'occurrence', o.attributes->>'date') = to_char(d, 'YYYY-MM-DD'))
            ORDER BY d;
            """;

        var due = (await connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new { seriesId = series.Id, seriesUrn = series.Urn, through = through.ToString("yyyy-MM-dd") },
            transaction, cancellationToken: cancellationToken))).ToList();
        if (due.Count > max)
        {
            return new OccurrencesMade([], due.Count);
        }

        var created = new List<SubjectRef>(due.Count);
        foreach (var day in due)
        {
            // An occurrence is born with no status (it reads Scheduled), as a Task repetition is.
            var urn = Urns.Build(SubjectTypes.Appointment, series.Title);
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO bsk.subject (urn, type, title, attributes)
                VALUES (@urn, 'Appointment', @title, @attributes::jsonb)
                RETURNING id;
                """,
                new
                {
                    urn,
                    title = series.Title,
                    attributes = AppointmentSeries.OccurrenceAttributes(series.Urn, DateOnly.ParseExact(day, "yyyy-MM-dd")),
                },
                transaction, cancellationToken: cancellationToken));
            created.Add(new SubjectRef(id, urn, SubjectTypes.Appointment, series.Title));
        }

        await transaction.CommitAsync(cancellationToken);
        return new OccurrencesMade(created, due.Count);
    }

    public async Task<(bool Updated, int OccurrencesRenamed)> RenameWithUpcomingOccurrencesAsync(
        Guid id, string urn, string previousTitle, string newTitle, DateOnly fromDate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The series' own rename is recorded (code review CR24), and each upcoming occurrence's
        // that follows it, as derived (CR51).
        var before = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT title FROM bsk.subject WHERE id = @id FOR UPDATE;",
            new { id }, transaction, cancellationToken: cancellationToken));
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.subject SET title = @newTitle WHERE id = @id;",
            new { id, newTitle }, transaction, cancellationToken: cancellationToken));
        Guid? renamed = null;
        if (before is not null && AttributeChanges.Title(before, newTitle) is { } change)
        {
            renamed = await AttributeChanges.AppendAsync(connection, transaction, id, change, eventSource, clock, cancellationToken);
        }

        // Only occurrences that still match the old series title follow it, so one
        // renamed by hand keeps its own name; past dates keep the name they happened under.
        var occurrences = (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            UPDATE bsk.subject
            SET title = @newTitle
            WHERE type = 'Appointment'
              AND attributes->>'series' = @urn
              AND title = @previousTitle
              AND bsk.try_to_date(attributes->>'date') >= @fromDate::date
            RETURNING id;
            """,
            new { urn, previousTitle, newTitle, fromDate = fromDate.ToString("yyyy-MM-dd") },
            transaction, cancellationToken: cancellationToken))).ToList();
        if (AttributeChanges.Title(previousTitle, newTitle) is { } followed)
        {
            foreach (var occurrence in occurrences)
            {
                await AttributeChanges.AppendAsync(
                    connection, transaction, occurrence, followed, eventSource, clock, cancellationToken, renamed);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return (updated > 0, occurrences.Count);
    }
}
