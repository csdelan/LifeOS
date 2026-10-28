using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Api.Read;

/// <summary>
/// Every read the API makes, as plain SQL against the <c>bsk_reader</c>
/// flattened views. Ported from <c>LifeOs.Pilot.Reader.SubjectReader</c>.
/// No writes live here by design.
/// </summary>
/// <remarks>
/// Status is folded from <c>bsk_derived.subject_current_source</c> (the live
/// fold of <c>state_change</c> events) rather than the materialized
/// <c>bsk.v_subject_current</c>. Archive is already derived-on-read via
/// <c>bsk.is_archived</c> on <c>v_subject</c>. Together that means a status or
/// archive write is visible on the next read without a <c>bsk rebuild</c>.
/// </remarks>
/// <param name="connectionString">The reader connection.</param>
/// <param name="coverAppName">The <c>utm_source</c> on a cover's links back to Unsplash (COVER-1).</param>
public sealed class SubjectReader(string connectionString, string coverAppName = Cover.DefaultAppName)
{
    private NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Subject types with counts — the Browse tree. Archived items are excluded by default (D9).</summary>
    public IReadOnlyList<TypeCount> GetTypeCounts(bool includeArchived = false)
    {
        using var db = Open();
        var archived = includeArchived ? "" : "WHERE NOT archived";
        return db.Query<TypeCount>(
            $"""
            SELECT type, count(*) AS n FROM bsk.v_subject {archived} GROUP BY type ORDER BY type
            """).AsList();
    }

    public IReadOnlyList<SubjectListItem> GetSubjects(string type, bool includeArchived = false)
        => QuerySubjects(type, includeArchived);

    // A list row's columns. A repetition of a repeating Task (D16) carries its series, its date
    // and its series' pattern, and pays its series' price unless it has its own.
    private const string ListSelect = """
        SELECT s.id, s.urn, s.type, s.title,
               c.status,
               s.attributes->>'due' AS due,
               s.attributes->>'scheduled' AS scheduled,
               s.attributes->>'target_date' AS target_date,
               s.area,
               a.name AS area_name,
               (SELECT string_agg(t.tag, ', ' ORDER BY t.tag)
                FROM bsk.v_item_tag t
                WHERE t.item_kind = 'subject' AND t.item_id = s.id) AS tags,
               s.archived,
               s.created_at,
               s.expected_cadence,
               s.next_review_at,
               s.attributes->>'person_kind' AS person_kind,
               (s.attributes->>'vision_order')::int AS vision_order,
               bsk.is_task_series(s.type, s.attributes) AS is_series,
               ser.id AS series_id,
               CASE WHEN ser.id IS NOT NULL THEN s.attributes->>'occurrence' END AS occurrence,
               CASE WHEN bsk.is_task_series(s.type, s.attributes) THEN (s.attributes->'recurrence')::text
                    WHEN ser.id IS NOT NULL THEN (ser.attributes->'recurrence')::text END AS repeat,
               coalesce(s.attributes->>'bounty', ser.attributes->>'bounty') AS bounty_json,
               s.attributes->>'cover' AS cover_json
        FROM bsk.v_subject s
        LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
        LEFT JOIN bsk.v_area a ON a.urn = s.area
        LEFT JOIN bsk.v_subject ser
               ON s.type = 'Task' AND ser.type = 'Task' AND ser.urn = s.attributes->>'series'
        """;

    /// <summary>
    /// Subjects of <paramref name="type"/> (every type when null), by title. Archived ones only
    /// when asked (D9), and a repeating Task's series only when asked: its repetitions are the
    /// Tasks to do (D16).
    /// </summary>
    public IReadOnlyList<SubjectListItem> QuerySubjects(
        string? type, bool includeArchived = false, bool includeSeries = false)
    {
        using var db = Open();
        return db.Query<SubjectListItem, BountyText, CoverText, SubjectListItem>(
            ListSelect + """
            WHERE (@type IS NULL OR s.type = @type)
              AND (@includeArchived OR NOT s.archived)
              AND (@includeSeries OR NOT bsk.is_task_series(s.type, s.attributes))
            ORDER BY s.title
            """,
            WithHeadline,
            new { type, includeArchived, includeSeries },
            splitOn: "bounty_json,cover_json").AsList();
    }

    /// <summary>
    /// Subjects whose title or one of whose tags contains <paramref name="query"/>, ignoring case
    /// (agent-access plan §4.3, <c>find_items</c>): titles that start with it first, then by
    /// title. Archived ones only when asked; a repeating Task's series is included, since an
    /// agent may be asking about the series itself. At most <paramref name="limit"/> rows.
    /// </summary>
    public IReadOnlyList<SubjectListItem> SearchSubjects(
        string query, string? type = null, bool includeArchived = false, int limit = 50)
    {
        var pattern = "%" + EscapeLike(query.Trim()) + "%";
        var prefix = EscapeLike(query.Trim()) + "%";
        using var db = Open();
        return db.Query<SubjectListItem, BountyText, CoverText, SubjectListItem>(
            ListSelect + """
            WHERE (@type IS NULL OR s.type = @type)
              AND (@includeArchived OR NOT s.archived)
              AND (s.title ILIKE @pattern
                   OR EXISTS (SELECT 1 FROM bsk.v_item_tag t
                              WHERE t.item_kind = 'subject' AND t.item_id = s.id AND t.tag ILIKE @pattern))
            ORDER BY (s.title ILIKE @prefix) DESC, s.archived, s.title
            LIMIT @limit
            """,
            WithHeadline,
            new { type, includeArchived, pattern, prefix, limit },
            splitOn: "bounty_json,cover_json").AsList();
    }

    // ILIKE's own wildcards and its escape character, matched literally.
    private static string EscapeLike(string text)
        => text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public SubjectListItem? GetListItem(Guid id)
    {
        using var db = Open();
        return db.Query<SubjectListItem, BountyText, CoverText, SubjectListItem>(
            ListSelect + " WHERE s.id = @id",
            WithHeadline,
            new { id },
            splitOn: "bounty_json,cover_json").FirstOrDefault();
    }

    public SubjectDetail? GetSubject(Guid id)
    {
        using var db = Open();
        var detail = db.QueryFirstOrDefault<SubjectDetail>(
            """
            SELECT s.id, s.urn, s.type, s.title,
                   c.status,
                   s.attributes->>'due' AS due,
                   s.attributes->>'target_date' AS target_date,
                   s.expected_cadence, s.next_review_at, s.scope, s.statement, s.created_at,
                   s.archived, s.area, a.name AS area_name,
                   s.attributes::text AS attributes
            FROM bsk.v_subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE s.id = @id
            """,
            new { id });
        if (detail is not null)
        {
            var bountyJson = MemberJsonOf(detail.Attributes, "bounty");
            if (detail.Type == SubjectTypes.Task && Repeat(db, id) is { } repeat)
            {
                detail.Repeating = repeat.Read;

                // A repetition without a price of its own pays its series' (D16).
                if (!repeat.Read.IsSeries && bountyJson is null)
                {
                    bountyJson = repeat.SeriesBountyJson;
                    detail.BountyFromSeries = true;
                }
            }

            detail.Bounty = BountyRows.Lines(detail.Type, bountyJson);
            detail.Cover = CoverOf(detail.Type, MemberJsonOf(detail.Attributes, Cover.AttributeKey));
        }

        return detail;
    }

    /// <summary>The repeating Task a Task is (or belongs to), with the series' price; null for a plain Task.</summary>
    private static (TaskRepeatRead Read, string? SeriesBountyJson)? Repeat(NpgsqlConnection db, Guid id)
    {
        var row = db.QueryFirstOrDefault<RepeatRow>(
            """
            SELECT (ts.id = t.id) AS is_series,
                   ts.id AS series_id, ts.urn AS series_urn, ts.title AS series_title,
                   ts.recurrence::text AS recurrence,
                   to_char(ts.start_date, 'YYYY-MM-DD') AS start,
                   ts.status AS series_status, ts.stopped,
                   CASE WHEN ts.id <> t.id THEN t.attributes->>'occurrence' END AS occurrence,
                   ts.open_count, ts.completed_count, ts.missed_count, ts.cancelled_count,
                   to_char(ts.last_completed_on, 'YYYY-MM-DD') AS last_completed_on,
                   to_char(ts.next_due, 'YYYY-MM-DD') AS next_due,
                   to_char(ts.latest_occurrence, 'YYYY-MM-DD') AS latest_occurrence,
                   ser.attributes->>'bounty' AS series_bounty_json
            FROM bsk.v_subject t
            JOIN bsk.v_task_series ts ON ts.id = t.id OR ts.urn = t.attributes->>'series'
            JOIN bsk.v_subject ser ON ser.id = ts.id
            WHERE t.id = @id AND t.type = 'Task'
            LIMIT 1
            """,
            new { id });
        return row is null
            ? null
            : (new TaskRepeatRead
            {
                IsSeries = row.IsSeries,
                SeriesId = row.SeriesId,
                SeriesUrn = row.SeriesUrn,
                SeriesTitle = row.SeriesTitle,
                Recurrence = row.Recurrence,
                Start = row.Start,
                SeriesStatus = row.SeriesStatus,
                Stopped = row.Stopped,
                Occurrence = row.Occurrence,
                OpenCount = row.OpenCount,
                CompletedCount = row.CompletedCount,
                MissedCount = row.MissedCount,
                CancelledCount = row.CancelledCount,
                LastCompletedOn = row.LastCompletedOn,
                NextDue = row.NextDue,
                LatestOccurrence = row.LatestOccurrence
            }, row.SeriesBountyJson);
    }

    /// <summary>A <see cref="TaskRepeatRead"/> as read, with the series' bounty alongside.</summary>
    private sealed class RepeatRow
    {
        public bool IsSeries { get; set; }
        public Guid SeriesId { get; set; }
        public string SeriesUrn { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string Recurrence { get; set; } = "";
        public string? Start { get; set; }
        public string? SeriesStatus { get; set; }
        public bool Stopped { get; set; }
        public string? Occurrence { get; set; }
        public long OpenCount { get; set; }
        public long CompletedCount { get; set; }
        public long MissedCount { get; set; }
        public long CancelledCount { get; set; }
        public string? LastCompletedOn { get; set; }
        public string? NextDue { get; set; }
        public string? LatestOccurrence { get; set; }
        public string? SeriesBountyJson { get; set; }
    }

    /// <summary>Every repeating Task (D16), by title; archived ones only when asked.</summary>
    public IReadOnlyList<TaskSeriesRow> GetTaskSeries(bool includeArchived = false)
    {
        using var db = Open();
        return QuerySeries(db, includeArchived, id: null);
    }

    /// <summary>A repeating Task and every repetition it has made, latest first; null when it isn't one.</summary>
    public TaskSeriesHistory? GetTaskSeriesHistory(Guid id)
    {
        using var db = Open();
        var series = QuerySeries(db, includeArchived: true, id).FirstOrDefault();
        if (series is null)
        {
            return null;
        }

        var repetitions = db.Query<TaskRepetitionRow>(
            """
            SELECT id, urn, title,
                   to_char(occurrence, 'YYYY-MM-DD') AS occurrence,
                   to_char(due, 'YYYY-MM-DD') AS due,
                   to_char(scheduled, 'YYYY-MM-DD') AS scheduled,
                   status, closed,
                   to_char(closed_on, 'YYYY-MM-DD') AS closed_on,
                   archived
            FROM bsk.v_task_repetition
            WHERE series_id = @id
            ORDER BY occurrence DESC NULLS LAST, created_at DESC
            """,
            new { id }).AsList();
        return new TaskSeriesHistory { Series = series, Repetitions = repetitions };
    }

    private static List<TaskSeriesRow> QuerySeries(NpgsqlConnection db, bool includeArchived, Guid? id)
        => db.Query<TaskSeriesRow, BountyText, TaskSeriesRow>(
            """
            SELECT ts.id, ts.urn, ts.title,
                   ts.recurrence::text AS recurrence,
                   to_char(ts.start_date, 'YYYY-MM-DD') AS start,
                   ts.status, ts.archived, ts.stopped,
                   s.area, a.name AS area_name,
                   ts.open_count, ts.completed_count, ts.missed_count, ts.cancelled_count,
                   to_char(ts.last_completed_on, 'YYYY-MM-DD') AS last_completed_on,
                   to_char(ts.next_due, 'YYYY-MM-DD') AS next_due,
                   to_char(ts.latest_occurrence, 'YYYY-MM-DD') AS latest_occurrence,
                   s.attributes->>'bounty' AS bounty_json
            FROM bsk.v_task_series ts
            JOIN bsk.v_subject s ON s.id = ts.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE (@includeArchived OR NOT ts.archived)
              AND (@id::uuid IS NULL OR ts.id = @id::uuid)
            ORDER BY ts.title
            """,
            (row, bounty) =>
            {
                row.Bounty = BountyRows.Headline(SubjectTypes.Task, bounty?.BountyJson);
                return row;
            },
            new { includeArchived, id },
            splitOn: "bounty_json").AsList();

    /// <summary>
    /// The raw <paramref name="key"/> member of an attributes document, as
    /// <c>attributes-&gt;&gt;'key'</c> reads it.
    /// </summary>
    private static string? MemberJsonOf(string? attributes, string key)
    {
        if (string.IsNullOrWhiteSpace(attributes))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(attributes) is JsonObject bag
                   && bag[key] is { } member
                ? member.ToJsonString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SubjectListItem WithHeadline(SubjectListItem item, BountyText? bounty, CoverText? cover)
    {
        item.Bounty = BountyRows.Headline(item.Type, bounty?.BountyJson);
        item.Cover = CoverOf(item.Type, cover?.CoverJson);
        return item;
    }

    /// <summary>A cover as pages read it; null when there's none, it's malformed, or the type can't have one.</summary>
    private CoverRead? CoverOf(string type, string? coverJson)
        => Cover.Allowed(type) ? CoverRead.FromJson(coverJson, coverAppName) : null;

    /// <summary>The split-off <c>bounty_json</c> column of a list read; Dapper hands null when it is.</summary>
    private sealed class BountyText
    {
        public string? BountyJson { get; set; }
    }

    /// <summary>
    /// The split-off <c>cover_json</c> column of a list read. It's its own split, not a second
    /// column on <see cref="BountyText"/>: Dapper hands a null object whenever a split's first
    /// column is null, so a cover sharing the bounty's split would vanish on every subject
    /// without a bounty.
    /// </summary>
    private sealed class CoverText
    {
        public string? CoverJson { get; set; }
    }

    public SubjectDetail? GetSubjectByUrn(string urn)
    {
        using var db = Open();
        var detail = db.QueryFirstOrDefault<SubjectDetail>(
            """
            SELECT s.id, s.urn, s.type, s.title,
                   c.status,
                   s.attributes->>'due' AS due,
                   s.attributes->>'target_date' AS target_date,
                   s.expected_cadence, s.next_review_at, s.scope, s.statement, s.created_at,
                   s.archived, s.area, a.name AS area_name,
                   s.attributes::text AS attributes
            FROM bsk.v_subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE s.urn = @urn
            """,
            new { urn });
        if (detail is not null)
        {
            detail.Cover = CoverOf(detail.Type, MemberJsonOf(detail.Attributes, Cover.AttributeKey));
        }

        return detail;
    }

    /// <summary>What this subject serves / results in (outgoing edges) — parents in the Map.</summary>
    public IReadOnlyList<RelationEdge> GetServes(Guid id)
    {
        using var db = Open();
        return db.Query<RelationEdge>(
            """
            SELECT r.relation, r.to_urn AS urn, r.to_type AS type, r.to_subject AS subject_id, s.title
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject s ON s.id = r.to_subject
            WHERE r.from_subject = @id
            ORDER BY r.to_type, r.to_urn
            """,
            new { id }).AsList();
    }

    /// <summary>What serves / results in this subject (incoming edges) — children in the Map.</summary>
    public IReadOnlyList<RelationEdge> GetServedBy(Guid id)
    {
        using var db = Open();
        return db.Query<RelationEdge>(
            """
            SELECT r.relation, r.from_urn AS urn, r.from_type AS type, r.from_subject AS subject_id, s.title
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject s ON s.id = r.from_subject
            WHERE r.to_subject = @id
            ORDER BY r.from_type, r.from_urn
            """,
            new { id }).AsList();
    }

    public IReadOnlyList<AlignmentEdge> GetAlignmentEdges(bool includeArchived = false)
    {
        using var db = Open();
        return db.Query<AlignmentEdge>(
            """
            SELECT r.from_subject AS from_id, r.to_subject AS to_id, r.relation
            FROM bsk.v_subject_relation r
            JOIN bsk.v_subject f ON f.id = r.from_subject
            JOIN bsk.v_subject t ON t.id = r.to_subject
            WHERE @includeArchived OR (NOT f.archived AND NOT t.archived)
            """,
            new { includeArchived }).AsList();
    }

    public IReadOnlyList<ConcerningEvent> GetConcerningEvents(Guid id)
    {
        using var db = Open();
        return db.Query<ConcerningEvent>(
            """
            SELECT se.event_kind AS kind, e.occurred_at, se.event_id, a.content
            FROM bsk.v_subject_event se
            JOIN bsk.v_event e ON e.id = se.event_id
            LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE se.subject_id = @id AND se.relation = 'concerns'
            ORDER BY e.occurred_at DESC
            """,
            new { id }).AsList();
    }

    public IReadOnlyList<JournalEntry> GetJournal(Guid id)
    {
        using var db = Open();
        return db.Query<JournalEntry>(
            """
            SELECT se.event_id, e.occurred_at, coalesce(a.content, '') AS content
            FROM bsk.v_subject_event se
            JOIN bsk.v_event e ON e.id = se.event_id
            LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE se.subject_id = @id AND se.relation = 'concerns' AND e.kind = 'journal'
            ORDER BY e.occurred_at ASC
            """,
            new { id }).AsList();
    }

    public IReadOnlyList<InboxItem> GetInbox()
    {
        using var db = Open();
        return db.Query<InboxItem>(
            """
            SELECT i.item_id, i.item_kind, i.triaged_at,
                   i.subject_urn, i.subject_type, i.subject_title,
                   i.event_kind, i.event_content,
                   ARRAY(SELECT t.tag FROM bsk.v_item_tag t
                         WHERE t.item_kind = i.item_kind AND t.item_id = i.item_id
                         ORDER BY t.tag) AS tags
            FROM bsk.v_inbox i
            ORDER BY i.triaged_at DESC
            """).AsList();
    }

    /// <summary>
    /// Items that have left the inbox, most recently resolved first — the way back to
    /// a dismissed capture. Capped at <paramref name="limit"/> rows.
    /// </summary>
    public IReadOnlyList<ResolvedInboxItem> GetResolvedInbox(int limit)
    {
        using var db = Open();
        return db.Query<ResolvedInboxItem>(
            """
            SELECT r.item_id, r.item_kind, r.triaged_at,
                   r.resolution, r.resolved_at,
                   r.subject_status, r.subject_status_terminal,
                   r.subject_urn, r.subject_type, r.subject_title,
                   r.event_kind, r.event_content,
                   ARRAY(SELECT t.tag FROM bsk.v_item_tag t
                         WHERE t.item_kind = r.item_kind AND t.item_id = r.item_id
                         ORDER BY t.tag) AS tags
            FROM bsk.v_inbox_resolved r
            ORDER BY r.resolved_at DESC
            LIMIT @limit
            """,
            new { limit }).AsList();
    }

    public int GetInboxCount()
    {
        using var db = Open();
        return db.ExecuteScalar<int>("SELECT count(*) FROM bsk.v_inbox");
    }

    public IReadOnlyList<StatusHistoryEntry> GetStatusHistory(Guid id)
    {
        using var db = Open();
        return db.Query<StatusHistoryEntry>(
            """
            SELECT status, occurred_at, id
            FROM bsk.v_event
            WHERE kind = 'state_change' AND subject_id = @id
            ORDER BY occurred_at DESC
            """,
            new { id = id.ToString() }).AsList();
    }

    public IReadOnlyList<string> GetTags(Guid subjectId)
    {
        using var db = Open();
        return db.Query<string>(
            """
            SELECT tag FROM bsk.v_item_tag
            WHERE item_kind = 'subject' AND item_id = @subjectId
            ORDER BY tag
            """,
            new { subjectId }).AsList();
    }

    public IReadOnlyList<TagUniverseItem> GetTagUniverse()
    {
        using var db = Open();
        return db.Query<TagUniverseItem>(
            "SELECT tag, item_count FROM bsk.v_tag_universe ORDER BY tag").AsList();
    }

    /// <summary>
    /// Every timed Task's timer (TASKS-2), running first, then most recently changed.
    /// Archived tasks are left out, as in every default view (D9).
    /// </summary>
    public IReadOnlyList<TaskTimer> GetTimers()
    {
        using var db = Open();
        return db.Query<TaskTimer>(
            """
            SELECT subject_id, subject_urn, subject_title, state, running_since,
                   total_seconds, session_seconds, session_started_at, sessions, last_changed_at
            FROM bsk.v_task_timer
            WHERE NOT archived
            ORDER BY (state = 'running') DESC, (state = 'paused') DESC, last_changed_at DESC
            """).AsList();
    }

    public TaskTimer? GetTimer(Guid subjectId)
    {
        using var db = Open();
        return db.QueryFirstOrDefault<TaskTimer>(
            """
            SELECT subject_id, subject_urn, subject_title, state, running_since,
                   total_seconds, session_seconds, session_started_at, sessions, last_changed_at
            FROM bsk.v_task_timer
            WHERE subject_id = @subjectId
            """,
            new { subjectId });
    }

    public IReadOnlyList<AreaRow> GetAreas()
    {
        using var db = Open();
        return db.Query<AreaRow>(
            "SELECT id, urn, name, description, notes, created_at FROM bsk.v_area ORDER BY name").AsList();
    }

    public IReadOnlyList<HabitRow> GetHabits(bool includeArchived = false)
    {
        using var db = Open();
        var rows = db.Query<HabitRow, BountyText, HabitEstablishment, HabitRow>(
            """
            SELECT h.id, h.urn, h.name, h.cue, h.routine, h.reward,
                   h.start_date, h.end_date, h.allows_partial,
                   h.recurrence::text AS recurrence, h.archived, h.created_at,
                   coalesce(st.current_streak, 0) AS current_streak,
                   st.last_state,
                   c.status,
                   s.attributes->>'bounty' AS bounty_json,
                   e.min_adherence, e.window_days, e.window_start::text AS window_start,
                   e.followed, e.partial, e.missed, e.concluded, e.adherence,
                   e.age_days, e.target_met
            FROM bsk.v_habit h
            JOIN bsk.v_subject s ON s.id = h.id
            LEFT JOIN bsk.v_habit_streak st ON st.habit_id = h.id
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
            LEFT JOIN bsk.v_habit_establishment e ON e.habit_id = h.id
            WHERE @includeArchived OR NOT h.archived
            ORDER BY h.name
            """,
            (habit, bounty, establishment) =>
            {
                habit.Bounty = BountyRows.Headline(SubjectTypes.Habit, bounty?.BountyJson);
                habit.Establishment = establishment;
                return habit;
            },
            new { includeArchived },
            splitOn: "bounty_json,min_adherence").AsList();
        foreach (var row in rows)
        {
            row.LastState = MapAdherenceState(row.LastState);
        }

        return rows;
    }

    public IReadOnlyList<HabitOccurrenceRow> GetHabitOccurrences(Guid? habitId = null, DateOnly? on = null)
    {
        using var db = Open();
        var onText = on?.ToString("yyyy-MM-dd");
        var rows = db.Query<HabitOccurrenceRow>(
            """
            SELECT o.habit_id, o.habit_urn, h.name AS habit_name,
                   o.occurrence_date, o.state, h.allows_partial
            FROM bsk.v_habit_occurrence o
            JOIN bsk.v_habit h ON h.id = o.habit_id
            WHERE (@habitId IS NULL OR o.habit_id = @habitId)
              AND (@onText IS NULL OR o.occurrence_date = @onText::date)
            ORDER BY o.occurrence_date DESC, h.name
            """,
            new { habitId, onText }).AsList();
        foreach (var row in rows)
        {
            row.State = MapAdherenceState(row.State) ?? row.State;
        }

        return rows;
    }

    public IReadOnlyList<AppointmentRow> GetAppointments(bool includeArchived = false, string? onIso = null)
    {
        using var db = Open();
        return db.Query<AppointmentRow>(
            """
            SELECT id, urn, title, date, start_time, end_time, all_day,
                   location, meeting_link, area, series_urn,
                   recurrence::text AS recurrence, status, archived, created_at
            FROM bsk.v_appointment
            WHERE (@includeArchived OR NOT archived)
              AND (@onIso IS NULL OR date = @onIso)
            ORDER BY date NULLS LAST, start_time NULLS LAST, title
            """,
            new { includeArchived, onIso }).AsList();
    }

    public IReadOnlyList<PersonRow> GetPeople(bool includeArchived = false, string? kind = null)
    {
        using var db = Open();
        return db.Query<PersonRow>(
            """
            SELECT s.id, s.urn, s.title,
                   s.attributes->>'person_kind' AS person_kind,
                   s.attributes->>'role' AS role,
                   s.archived
            FROM bsk.v_subject s
            WHERE s.type = 'Person'
              AND (@includeArchived OR NOT s.archived)
              AND (@kind IS NULL OR lower(coalesce(s.attributes->>'person_kind', 'human')) = lower(@kind))
            ORDER BY s.title
            """,
            new { includeArchived, kind }).AsList();
    }

    public IReadOnlyList<PersonAssociationRow> GetInvolvements(Guid personId)
    {
        using var db = Open();
        return db.Query<PersonAssociationRow>(
            """
            SELECT subject_id, subject_urn, subject_type, subject_title, role,
                   person_id, person_urn, person_name
            FROM bsk.v_person_association
            WHERE person_id = @personId
            ORDER BY subject_type, subject_title
            """,
            new { personId }).AsList();
    }

    public IReadOnlyList<PersonAssociationRow> GetPeopleOn(Guid subjectId)
    {
        using var db = Open();
        return db.Query<PersonAssociationRow>(
            """
            SELECT subject_id, subject_urn, subject_type, subject_title, role,
                   person_id, person_urn, person_name
            FROM bsk.v_person_association
            WHERE subject_id = @subjectId
            ORDER BY role, person_name
            """,
            new { subjectId }).AsList();
    }

    /// <summary>
    /// The UI's <c>AdherenceState</c> uses <c>not_followed</c> where the kernel
    /// stores <c>missed</c>. Map at the JSON edge so the contract matches
    /// <c>production-ui-types.ts</c>.
    /// </summary>
    private static string? MapAdherenceState(string? state)
        => string.Equals(state, "missed", StringComparison.OrdinalIgnoreCase)
            ? "not_followed"
            : state;
}
