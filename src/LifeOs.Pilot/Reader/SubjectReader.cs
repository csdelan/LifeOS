using Dapper;
using Npgsql;

namespace LifeOs.Pilot.Reader;

/// <summary>
/// Every read the Browse screen makes, as plain SQL against the <c>bsk_reader</c>
/// flattened views (migrations 0005/0007). One short-lived connection per call —
/// fine for a single-user desktop pilot. No writes live here by design.
/// </summary>
public sealed class SubjectReader(string connectionString)
{
    private NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Subject types with counts — the tree.</summary>
    public IReadOnlyList<TypeCount> GetTypeCounts()
    {
        using var db = Open();
        return db.Query<TypeCount>(
            "SELECT type, count(*) AS n FROM bsk.v_subject GROUP BY type ORDER BY type").AsList();
    }

    /// <summary>Subjects of one type, with their folded current status.</summary>
    public IReadOnlyList<SubjectListItem> GetSubjects(string type)
    {
        using var db = Open();
        return db.Query<SubjectListItem>(
            """
            SELECT s.id, s.urn, s.title,
                   coalesce(c.status, 'open') AS status,
                   s.attributes->>'due' AS due,
                   s.expected_cadence, s.next_review_at
            FROM bsk.v_subject s
            LEFT JOIN bsk.v_subject_current c ON c.subject_id = s.id
            WHERE s.type = @type
            ORDER BY s.title
            """,
            new { type }).AsList();
    }

    /// <summary>The detail-pane header for one subject.</summary>
    public SubjectDetail? GetSubject(Guid id)
    {
        using var db = Open();
        return db.QueryFirstOrDefault<SubjectDetail>(
            """
            SELECT s.id, s.urn, s.type, s.title,
                   coalesce(c.status, 'open') AS status,
                   s.attributes->>'due' AS due,
                   s.expected_cadence, s.next_review_at, s.scope, s.statement, s.created_at
            FROM bsk.v_subject s
            LEFT JOIN bsk.v_subject_current c ON c.subject_id = s.id
            WHERE s.id = @id
            """,
            new { id });
    }

    /// <summary>What this subject serves / results in (outgoing edges).</summary>
    public IReadOnlyList<RelationEdge> GetServes(Guid id)
    {
        using var db = Open();
        return db.Query<RelationEdge>(
            """
            SELECT relation, to_urn AS urn, to_type AS type, to_subject AS subject_id
            FROM bsk.v_subject_relation
            WHERE from_subject = @id
            ORDER BY to_type, to_urn
            """,
            new { id }).AsList();
    }

    /// <summary>What serves / results in this subject (incoming edges).</summary>
    public IReadOnlyList<RelationEdge> GetServedBy(Guid id)
    {
        using var db = Open();
        return db.Query<RelationEdge>(
            """
            SELECT relation, from_urn AS urn, from_type AS type, from_subject AS subject_id
            FROM bsk.v_subject_relation
            WHERE to_subject = @id
            ORDER BY from_type, from_urn
            """,
            new { id }).AsList();
    }

    /// <summary>Events that <c>concern</c> this subject, newest first.</summary>
    public IReadOnlyList<ConcerningEvent> GetConcerningEvents(Guid id)
    {
        using var db = Open();
        return db.Query<ConcerningEvent>(
            """
            SELECT se.event_kind AS kind, e.occurred_at, se.event_id
            FROM bsk.v_subject_event se
            JOIN bsk.v_event e ON e.id = se.event_id
            WHERE se.subject_id = @id AND se.relation = 'concerns'
            ORDER BY e.occurred_at DESC
            """,
            new { id }).AsList();
    }

    /// <summary>
    /// Untriaged captures: note/journal events that no subject was promoted from and
    /// that are not yet related to any subject. This is the Inbox's worklist.
    /// </summary>
    public IReadOnlyList<CaptureItem> GetUnprocessedCaptures()
    {
        using var db = Open();
        return db.Query<CaptureItem>(
            """
            SELECT e.id, e.kind, e.occurred_at, a.content
            FROM bsk.event e
            LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE e.kind IN ('note', 'journal')
              AND NOT EXISTS (SELECT 1 FROM bsk.subject s WHERE s.origin_event_id = e.id)
              AND NOT EXISTS (SELECT 1 FROM bsk.subject_event se WHERE se.event_id = e.id)
            ORDER BY e.occurred_at DESC
            """).AsList();
    }

    /// <summary>The subject's status history, folded from state_change events.</summary>
    public IReadOnlyList<StatusHistoryEntry> GetStatusHistory(Guid id)
    {
        using var db = Open();
        // v_event.subject_id is the text payload field, so compare as text.
        return db.Query<StatusHistoryEntry>(
            """
            SELECT status, occurred_at, id
            FROM bsk.v_event
            WHERE kind = 'state_change' AND subject_id = @id
            ORDER BY occurred_at DESC
            """,
            new { id = id.ToString() }).AsList();
    }
}
