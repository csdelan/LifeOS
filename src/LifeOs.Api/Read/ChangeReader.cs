using Dapper;
using Npgsql;

namespace LifeOs.Api.Read;

/// <summary>
/// What was recorded lately, newest first (agent-access plan §4.3, <c>recent_changes</c>): every
/// event since a time, on the <c>bsk_reader</c> connection, with the subject it's about and its
/// text. An agent catches up with it. Areas switched off for agents leave their events out, so
/// <c>Mcp:Domains</c> keeps them out of every tool.
/// </summary>
public sealed class ChangeReader(string readerConnectionString)
{
    /// <summary>The most text an event's content or payload brings along.</summary>
    public const int MaxTextLength = 500;

    /// <summary>The longest payload passed through whole; a longer one is left out.</summary>
    public const int MaxPayloadLength = 1024;

    /// <summary>
    /// Events recorded at or after <paramref name="since"/>, newest first, of the
    /// <paramref name="kinds"/> given (every kind when null), leaving out the areas in
    /// <paramref name="excluded"/> (<c>health</c>, <c>food</c>, <c>rewards</c>, <c>home</c>). One
    /// more row than <paramref name="limit"/> may come back, so the caller can say it was cut.
    /// </summary>
    public IReadOnlyList<ChangeRow> GetChanges(
        DateTimeOffset since, IReadOnlyCollection<string>? kinds, IReadOnlySet<string> excluded, int limit)
    {
        using var db = new NpgsqlConnection(readerConnectionString);
        db.Open();
        return db.Query<ChangeRow>(
            """
            SELECT e.id, e.kind, e.source_id AS source, e.provenance,
                   e.occurred_at, e.recorded_at,
                   s.id AS subject_id, s.urn AS subject_urn, s.type AS subject_type, s.title AS subject_title,
                   e.status,
                   left(a.content, @maxText) AS text,
                   CASE WHEN length(e.payload::text) <= @maxPayload THEN e.payload::text END AS payload
            FROM bsk.v_event e
            LEFT JOIN LATERAL (
                SELECT se.subject_id
                FROM bsk.v_subject_event se
                WHERE se.event_id = e.id
                ORDER BY se.relation, se.subject_id
                LIMIT 1
            ) about ON true
            LEFT JOIN bsk.v_subject s ON s.id::text = coalesce(e.subject_id, about.subject_id::text)
            LEFT JOIN bsk.artifact a ON a.id = e.artifact_id
            WHERE e.recorded_at >= @since
              AND (@kinds::text[] IS NULL OR e.kind = ANY(@kinds::text[]))
              AND NOT (@noHealth AND (e.source_id = 'oura' OR jsonb_exists(e.payload, 'workout_note')))
              AND NOT (@noFood AND jsonb_exists_any(e.payload, ARRAY['meal', 'fast', 'food_day']))
              AND NOT (@noRewards AND (e.kind = 'ledger' OR jsonb_exists(e.payload, 'exception_day')
                                       OR s.type IS NOT DISTINCT FROM 'Reward'))
              AND NOT (@noHome AND (jsonb_exists_any(e.payload, ARRAY['space_rating', 'space_photo'])
                                    OR s.type IS NOT DISTINCT FROM 'Space'))
            ORDER BY e.recorded_at DESC, e.id
            LIMIT @take
            """,
            new
            {
                since,
                kinds = kinds is { Count: > 0 } ? kinds.ToArray() : null,
                noHealth = excluded.Contains("health"),
                noFood = excluded.Contains("food"),
                noRewards = excluded.Contains("rewards"),
                noHome = excluded.Contains("home"),
                maxText = MaxTextLength,
                maxPayload = MaxPayloadLength,
                take = limit + 1,
            }).AsList();
    }
}

/// <summary>One recorded event, with the subject it's about when it's about one.</summary>
public sealed class ChangeRow
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";

    /// <summary>Where it came from: <c>api</c> (the web), <c>cli</c>, <c>oura</c>, …</summary>
    public string Source { get; set; } = "";

    /// <summary><c>declared</c> (Chris said it), <c>observed</c> (measured) or <c>derived</c>.</summary>
    public string Provenance { get; set; } = "";

    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; }
    public Guid? SubjectId { get; set; }
    public string? SubjectUrn { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectTitle { get; set; }

    /// <summary>The new status, on a <c>state_change</c>.</summary>
    public string? Status { get; set; }

    /// <summary>The event's text (a journal entry, a note, a capture), cut to 500 characters.</summary>
    public string? Text { get; set; }

    /// <summary>The event's payload as JSON text, when it's at most 1 KB.</summary>
    public string? Payload { get; set; }
}
