using Dapper;
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
public sealed class SubjectReader(string connectionString)
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

    public IReadOnlyList<SubjectListItem> QuerySubjects(string? type, bool includeArchived = false)
    {
        using var db = Open();
        return db.Query<SubjectListItem>(
            """
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
                   (s.attributes->>'vision_order')::int AS vision_order
            FROM bsk.v_subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE (@type IS NULL OR s.type = @type)
              AND (@includeArchived OR NOT s.archived)
            ORDER BY s.title
            """,
            new { type, includeArchived }).AsList();
    }

    public SubjectListItem? GetListItem(Guid id)
    {
        using var db = Open();
        return db.QueryFirstOrDefault<SubjectListItem>(
            """
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
                   (s.attributes->>'vision_order')::int AS vision_order
            FROM bsk.v_subject s
            LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
            LEFT JOIN bsk.v_area a ON a.urn = s.area
            WHERE s.id = @id
            """,
            new { id });
    }

    public SubjectDetail? GetSubject(Guid id)
    {
        using var db = Open();
        return db.QueryFirstOrDefault<SubjectDetail>(
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
    }

    public SubjectDetail? GetSubjectByUrn(string urn)
    {
        using var db = Open();
        return db.QueryFirstOrDefault<SubjectDetail>(
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
            SELECT item_id, item_kind, triaged_at,
                   subject_urn, subject_type, subject_title,
                   event_kind, event_content
            FROM bsk.v_inbox
            ORDER BY triaged_at DESC
            """).AsList();
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

    public IReadOnlyList<AreaRow> GetAreas()
    {
        using var db = Open();
        return db.Query<AreaRow>(
            "SELECT id, urn, name, description, notes, created_at FROM bsk.v_area ORDER BY name").AsList();
    }

    public IReadOnlyList<HabitRow> GetHabits(bool includeArchived = false)
    {
        using var db = Open();
        var rows = db.Query<HabitRow>(
            """
            SELECT h.id, h.urn, h.name, h.cue, h.routine, h.reward,
                   h.start_date, h.end_date, h.allows_partial,
                   h.recurrence::text AS recurrence, h.archived, h.created_at,
                   coalesce(st.current_streak, 0) AS current_streak,
                   st.last_state
            FROM bsk.v_habit h
            LEFT JOIN bsk.v_habit_streak st ON st.habit_id = h.id
            WHERE @includeArchived OR NOT h.archived
            ORDER BY h.name
            """,
            new { includeArchived }).AsList();
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
