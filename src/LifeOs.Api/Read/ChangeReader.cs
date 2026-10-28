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
    /// Events at or after <paramref name="since"/>, newest first, of the <paramref name="kinds"/>
    /// given, leaving out the areas in <paramref name="excluded"/> (<c>health</c>, <c>food</c>,
    /// <c>rewards</c>, <c>home</c>). One more row than <paramref name="limit"/> may come back, so
    /// the caller can say it was cut.
    /// <list type="bullet">
    /// <item><paramref name="byOccurred"/> picks which time counts: when it happened, or (the
    /// default) when LifeOS recorded it. An Oura backfill is recent by the second, not the first.</item>
    /// <item>Null <paramref name="kinds"/> is every kind but <c>ledger</c>: a day of meals writes
    /// many earns and reversals, which would drown the rest; ask for <c>ledger</c> by name.</item>
    /// <item>Some records are kept as versions: Oura re-sends a day as it changes, and editing a
    /// meal, a fast, a space photo or a rating writes it again under the same id. Only the newest
    /// version comes back, with how many it saw in <see cref="ChangeRow.Versions"/>. An older
    /// version in the window whose newest one is outside it (a meal moved to another day) is
    /// left out too.</item>
    /// <item><paramref name="before"/> continues a cut answer: only events older than that one,
    /// in the same order.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<ChangeRow> GetChanges(
        DateTimeOffset since, IReadOnlyCollection<string>? kinds, IReadOnlySet<string> excluded, int limit,
        bool byOccurred = false, ChangeCursor? before = null)
    {
        // A column name from a bool, never input.
        var at = byOccurred ? "occurred_at" : "recorded_at";
        using var db = new NpgsqlConnection(readerConnectionString);
        db.Open();
        return db.Query<ChangeRow>(
            $"""
            WITH matched AS (
                SELECT e.id, e.kind, e.source_id, e.provenance, e.occurred_at, e.recorded_at,
                       e.status, e.artifact_id, e.payload,
                       s.id AS subject_id, s.urn AS subject_urn, s.type AS subject_type, s.title AS subject_title,
                       {RecordKey("e")} AS record
                FROM bsk.v_event e
                LEFT JOIN LATERAL (
                    SELECT se.subject_id
                    FROM bsk.v_subject_event se
                    WHERE se.event_id = e.id
                    ORDER BY se.relation, se.subject_id
                    LIMIT 1
                ) about ON true
                LEFT JOIN bsk.v_subject s ON s.id::text = coalesce(e.subject_id, about.subject_id::text)
                WHERE e.{at} >= @since
                  AND ((@kinds::text[] IS NULL AND e.kind <> 'ledger') OR e.kind = ANY(@kinds::text[]))
                  AND NOT (@noHealth AND (e.source_id = 'oura' OR jsonb_exists(e.payload, 'workout_note')))
                  AND NOT (@noFood AND jsonb_exists_any(e.payload, ARRAY['meal', 'fast', 'food_day']))
                  AND NOT (@noRewards AND (e.kind = 'ledger' OR jsonb_exists(e.payload, 'exception_day')
                                           OR s.type IS NOT DISTINCT FROM 'Reward'))
                  AND NOT (@noHome AND (jsonb_exists_any(e.payload, ARRAY['space_rating', 'space_photo'])
                                        OR s.type IS NOT DISTINCT FROM 'Space'))
            ),
            -- The newest version of each versioned record in the window, wherever its time falls.
            -- A newer version is never recorded before an older one, so looking from the earliest
            -- one in the window finds it.
            latest AS (
                SELECT v.record,
                       (array_agg(v.id ORDER BY v.recorded_at DESC, v.id DESC))[1] AS id,
                       count(*) AS versions
                FROM (
                    SELECT {RecordKey("e")} AS record, e.id, e.recorded_at
                    FROM bsk.v_event e
                    WHERE e.recorded_at >= (SELECT min(recorded_at) FROM matched WHERE record IS NOT NULL)
                ) v
                WHERE v.record IN (SELECT record FROM matched WHERE record IS NOT NULL)
                GROUP BY v.record
            )
            SELECT m.id, m.kind, m.source_id AS source, m.provenance,
                   m.occurred_at, m.recorded_at,
                   m.subject_id, m.subject_urn, m.subject_type, m.subject_title,
                   m.status,
                   left(a.content, @maxText) AS text,
                   CASE WHEN length(m.payload::text) <= @maxPayload THEN m.payload::text END AS payload,
                   coalesce(l.versions, 1) AS versions
            FROM matched m
            LEFT JOIN latest l ON l.record = m.record
            LEFT JOIN bsk.artifact a ON a.id = m.artifact_id
            WHERE (m.record IS NULL OR l.id = m.id)
              AND (@beforeAt::timestamptz IS NULL OR (m.{at}, m.id) < (@beforeAt::timestamptz, @beforeId::uuid))
            ORDER BY m.{at} DESC, m.id DESC
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
                beforeAt = before?.At,
                beforeId = before?.Id,
                take = limit + 1,
            }).AsList();
    }

    /// <summary>
    /// Which record an event is a version of, or null when it stands alone. An Oura version's
    /// external_id is <c>&lt;collection&gt;:&lt;key&gt;:&lt;hash&gt;:&lt;what it replaces&gt;</c>,
    /// and the key may hold a colon, so the record is all but the last two parts. A meal, a fast,
    /// a space photo and a space rating keep their id in the payload, as their views fold them.
    /// </summary>
    private static string RecordKey(string e) => $"""
        CASE WHEN {e}.source_id = 'oura' AND {e}.external_id IS NOT NULL
                  THEN 'oura:' || regexp_replace({e}.external_id, ':[^:]*:[^:]*$', '')
             WHEN {e}.payload->'meal'->>'id' IS NOT NULL THEN 'meal:' || ({e}.payload->'meal'->>'id')
             WHEN {e}.payload->'fast'->>'id' IS NOT NULL THEN 'fast:' || ({e}.payload->'fast'->>'id')
             WHEN {e}.payload->'space_photo'->>'id' IS NOT NULL
                  THEN 'space_photo:' || ({e}.payload->'space_photo'->>'id')
             WHEN {e}.payload->'space_rating'->>'id' IS NOT NULL
                  THEN 'space_rating:' || ({e}.payload->'space_rating'->>'id')
        END
        """;
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

    /// <summary>
    /// How many versions of this record it saw: above 1 for an Oura record Oura re-sent as it
    /// changed, and a meal, fast, space photo or rating that was edited. This row is the newest.
    /// </summary>
    public long Versions { get; set; } = 1;
}

/// <summary>Where a cut answer stopped: its last event's time (the one the order goes by) and id.</summary>
public sealed record ChangeCursor(DateTimeOffset At, Guid Id);
