using System.Globalization;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Api.Read;

/// <summary>
/// The reviews' reads (REVIEW-1, REVIEW-2, reviews plan §3) on the <c>bsk_reader</c> connection:
/// the reviews through <c>bsk.v_review</c>, each with what it reads as today, and the recap of the
/// days a review looks back on, read straight from the record. Days are in
/// <paramref name="homeTimeZone"/>. No writes live here; they go through <c>ReviewService</c>.
/// </summary>
public sealed class ReviewReader(string readerConnectionString, IClock clock, string homeTimeZone = HomeZone.Default)
{
    /// <summary>The longest recap read, in days: a month and a bit.</summary>
    public const int MaxRecapDays = 62;

    private readonly NpgsqlReviewRepository reviews = new(readerConnectionString);
    private readonly NpgsqlHomeCalendar calendar = new(readerConnectionString, homeTimeZone);

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
        => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    /// <summary>The reviews whose period is from <paramref name="from"/> to <paramref name="to"/>, daily first on a day.</summary>
    public async Task<IReadOnlyList<ReviewRow>> ListAsync(
        string? kind, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var today = await TodayAsync(cancellationToken);
        return (await reviews.ListAsync(kind, from, to, cancellationToken: cancellationToken))
            .Select(r => ReviewRow.From(r, today)).ToList();
    }

    /// <summary>A review as a row, with what it reads as today.</summary>
    public async Task<ReviewRow> RowAsync(Review review, CancellationToken cancellationToken = default)
        => ReviewRow.From(review, await TodayAsync(cancellationToken));

    /// <summary>
    /// What was recorded from <paramref name="from"/> to <paramref name="to"/>: status changes
    /// (reviews' own left out), Tasks completed, Tasks open and due before <paramref name="asOf"/>,
    /// habit days, appointments, journal entries and what came into the Inbox. Newest first in
    /// each section; habit days and appointments by day. A repeating Task's series is left out of
    /// the Tasks; the web adds the repeating Tasks' own section (TASKS-3).
    /// </summary>
    public async Task<IReadOnlyList<RecapSectionRow>> RecapAsync(
        DateOnly from, DateOnly to, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var db = new NpgsqlConnection(readerConnectionString);
        await db.OpenAsync(cancellationToken);
        var args = new { zone = homeTimeZone, from = Text(from), to = Text(to), asOf = Text(asOf) };

        async Task<IReadOnlyList<RecapItemRow>> ItemsAsync(string kind, string sql)
            => (await db.QueryAsync<ItemRow>(new CommandDefinition(sql, args, cancellationToken: cancellationToken)))
                .Select(r => r.ToItem(kind)).ToList();

        // The home-zone day of an event, for the window.
        const string Day = "(e.occurred_at AT TIME ZONE @zone)::date";

        var status = await ItemsAsync("status", $"""
            SELECT e.occurred_at AS attime, NULL AS atday, s.title, e.payload->>'status' AS detail, s.id AS subjectid
            FROM bsk.event e
            JOIN bsk.subject s ON s.id::text = e.payload->>'subject_id'
            WHERE e.kind = 'state_change'
              AND s.type <> 'Review'
              AND {Day} BETWEEN @from::date AND @to::date
            ORDER BY e.occurred_at DESC, e.id DESC
            """);

        var completed = await ItemsAsync("task-completed", $"""
            SELECT attime, atday, title, detail, subjectid
            FROM (
                SELECT DISTINCT ON (s.id) e.occurred_at AS attime, NULL AS atday, s.title, NULL AS detail, s.id AS subjectid
                FROM bsk.event e
                JOIN bsk.v_subject s ON s.id::text = e.payload->>'subject_id'
                WHERE e.kind = 'state_change'
                  AND lower(e.payload->>'status') = 'completed'
                  AND s.type = 'Task'
                  AND NOT s.archived
                  AND NOT bsk.is_task_series(s.type, s.attributes)
                  AND {Day} BETWEEN @from::date AND @to::date
                ORDER BY s.id, e.occurred_at DESC
            ) done
            ORDER BY attime DESC
            """);

        var overdue = await ItemsAsync("task-overdue", """
            SELECT NULL::timestamptz AS attime, to_char(due, 'YYYY-MM-DD') AS atday, title,
                   'Due ' || to_char(due, 'YYYY-MM-DD') AS detail, id AS subjectid
            FROM (
                SELECT s.id, s.title, bsk.try_to_date(s.attributes->>'due') AS due
                FROM bsk.v_subject s
                LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
                WHERE s.type = 'Task'
                  AND NOT s.archived
                  AND NOT bsk.is_task_series(s.type, s.attributes)
                  AND NOT bsk.is_terminal_status(c.status)
            ) t
            WHERE due < @asOf::date
            ORDER BY due, title
            """);

        var habits = await ItemsAsync("habit", """
            SELECT NULL::timestamptz AS attime, to_char(o.occurrence_date, 'YYYY-MM-DD') AS atday, h.name AS title,
                   CASE o.state WHEN 'missed' THEN 'not followed' ELSE replace(o.state, '_', ' ') END AS detail,
                   o.habit_id AS subjectid
            FROM bsk.v_habit_occurrence o
            JOIN bsk.v_habit h ON h.id = o.habit_id
            WHERE o.occurrence_date BETWEEN @from::date AND @to::date
            ORDER BY o.occurrence_date DESC, h.name
            """);

        var appointments = await ItemsAsync("appointment", """
            SELECT NULL::timestamptz AS attime, a.date AS atday,
                   a.title, concat_ws(' · ', nullif(a.start_time, ''), a.status) AS detail, a.id AS subjectid
            FROM bsk.v_appointment a
            WHERE NOT a.archived
              AND bsk.try_to_date(a.date) BETWEEN @from::date AND @to::date
            ORDER BY bsk.try_to_date(a.date), a.start_time NULLS FIRST, a.title
            """);

        var journal = await ItemsAsync("journal", $"""
            SELECT e.occurred_at AS attime, NULL AS atday, s.title,
                   left(regexp_replace(coalesce(a.content, ''), '\s+', ' ', 'g'), 140) AS detail, s.id AS subjectid
            FROM bsk.event e
            JOIN bsk.subject_event se ON se.event_id = e.id AND se.relation = 'concerns'
            JOIN bsk.subject s ON s.id = se.subject_id
            LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE e.kind = 'journal'
              AND {Day} BETWEEN @from::date AND @to::date
            ORDER BY e.occurred_at DESC, e.id DESC
            """);

        // What came into the Inbox: each item's first flag in the window, whatever became of it.
        var inbox = await ItemsAsync("inbox", $"""
            SELECT attime, atday, title, detail, subjectid
            FROM (
                SELECT DISTINCT ON (e.payload->>'item_id')
                       e.occurred_at AS attime, NULL AS atday,
                       coalesce(s.title, left(regexp_replace(coalesce(ca.content, ''), '\s+', ' ', 'g'), 140), 'Capture') AS title,
                       coalesce(s.type, ce.kind) AS detail,
                       s.id AS subjectid
                FROM bsk.event e
                LEFT JOIN bsk.subject s ON e.payload->>'item_kind' = 'subject' AND s.id::text = e.payload->>'item_id'
                LEFT JOIN bsk.event ce ON e.payload->>'item_kind' = 'event' AND ce.id::text = e.payload->>'item_id'
                LEFT JOIN bsk.artifact ca ON ca.id = ce.artifact_id
                WHERE e.kind = 'triage'
                  AND e.payload->>'state' = 'flagged'
                  AND {Day} BETWEEN @from::date AND @to::date
                ORDER BY e.payload->>'item_id', e.occurred_at, e.id
            ) flagged
            ORDER BY attime DESC
            """);

        return
        [
            new RecapSectionRow { Id = "status", Label = "Status changes", Items = status },
            new RecapSectionRow { Id = "completed", Label = "Completed tasks", Items = completed },
            new RecapSectionRow { Id = "overdue", Label = "Overdue tasks", Items = overdue },
            new RecapSectionRow { Id = "habits", Label = "Habit adherence", Items = habits },
            new RecapSectionRow { Id = "appointments", Label = "Appointments", Items = appointments },
            new RecapSectionRow { Id = "journal", Label = "Journal entries", Items = journal },
            new RecapSectionRow { Id = "inbox", Label = "Inbox history", Items = inbox },
        ];
    }

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class ItemRow
    {
        public DateTime? AtTime { get; set; }
        public string? AtDay { get; set; }
        public string Title { get; set; } = "";
        public string? Detail { get; set; }
        public Guid? SubjectId { get; set; }

        public RecapItemRow ToItem(string kind) => new()
        {
            Kind = kind,
            At = AtTime is { } at
                ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : AtDay ?? "",
            Title = Title,
            Detail = string.IsNullOrWhiteSpace(Detail) ? null : Detail,
            SubjectId = SubjectId,
        };
    }
}
