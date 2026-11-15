using Dapper;
using LifeOs.Api.Mcp;
using Npgsql;

namespace LifeOs.Api.Read;

/// <summary>
/// What was recorded lately, newest first (agent-access plan §4.3, <c>recent_changes</c>): every
/// event since a time, on the <c>bsk_reader</c> connection, with the subject it's about and its
/// text. An agent catches up with it. Areas switched off for agents leave their events out, so
/// <c>Mcp:Domains</c> keeps them out of every tool.
/// </summary>
public sealed class ChangeReader(NpgsqlDataSource dataSource)
{
    /// <summary>The most text an event's content or payload brings along.</summary>
    public const int MaxTextLength = 500;

    /// <summary>The longest payload passed through whole; a longer one is left out.</summary>
    public const int MaxPayloadLength = 1024;

    /// <summary>
    /// Events at or after <paramref name="since"/>, newest first, of the <paramref name="kinds"/>
    /// given, leaving out the areas in <paramref name="excluded"/> (<c>health</c>, <c>food</c>,
    /// <c>rewards</c>, <c>home</c>) as <see cref="McpAreas"/> says: with any area off, an event
    /// no area claims shows only if its payload is the core's. One more row than
    /// <paramref name="limit"/> may come back, so the caller can say it was cut.
    /// <list type="bullet">
    /// <item><paramref name="byOccurred"/> picks which time counts: when it happened, or (the
    /// default) when LifeOS recorded it. An Oura backfill is recent by the second, not the first.</item>
    /// <item>Null <paramref name="kinds"/> is every kind but <c>ledger</c>: a day of meals writes
    /// many earns and reversals, which would drown the rest; ask for <c>ledger</c> by name.</item>
    /// <item>Some records are kept as versions (<c>bsk.event_record</c>): Oura re-sends a day as
    /// it changes, and editing a meal, a fast, a space photo, a rating, a reading or a lab visit
    /// writes it again under the same id. Only the newest version comes back, with how many there
    /// are in <see cref="ChangeRow.Versions"/>. An older version in the window whose newest one is
    /// outside it (a meal moved to another day) is left out too.</item>
    /// <item><paramref name="before"/> continues a cut answer: only events older than that one,
    /// in the same order.</item>
    /// </list>
    /// The log is read newest first and the read stops at the page (code review CR122): a version
    /// shows when no newer one of its record was recorded, which <c>event_record_idx</c> looks up,
    /// and only the page counts its versions.
    /// </summary>
    public async Task<IReadOnlyList<ChangeRow>> GetChangesAsync(
        DateTimeOffset since, IReadOnlyCollection<string>? kinds, IReadOnlySet<string> excluded, int limit,
        bool byOccurred = false, ChangeCursor? before = null, CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = Query(since, kinds, excluded, limit, byOccurred, before);
        await using var db = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await db.QueryAsync<ChangeRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>The query <see cref="GetChangesAsync"/> runs and its parameters, for a test to plan.</summary>
    public static (string Sql, object Parameters) Query(
        DateTimeOffset since, IReadOnlyCollection<string>? kinds, IReadOnlySet<string> excluded, int limit,
        bool byOccurred = false, ChangeCursor? before = null)
    {
        // A column name from a bool, never input.
        var at = byOccurred ? "occurred_at" : "recorded_at";
        // The bound on the time alone lets the time's index stop there; the pair breaks a tie.
        var older = before is null ? "" : $"AND e.{at} <= @beforeAt AND (e.{at}, e.id) < (@beforeAt, @beforeId)";
        var off = McpAreas.All.Where(a => excluded.Contains(a.Name)).ToList();
        var on = McpAreas.All.Except(off).ToList();
        var sql = $"""
            SELECT m.id, m.kind, m.source_id AS source, m.provenance,
                   m.occurred_at, m.recorded_at,
                   m.subject_id, m.subject_urn, m.subject_type, m.subject_title,
                   m.status,
                   left(a.content, @maxText) AS text,
                   CASE WHEN length(m.payload::text) <= @maxPayload THEN m.payload::text END AS payload,
                   CASE WHEN m.record IS NULL THEN 1
                        ELSE (SELECT count(*) FROM bsk.event v
                              WHERE bsk.event_record(v.source_id, v.external_id, v.payload) = m.record)
                   END AS versions
            FROM (
                SELECT e.id, e.kind, e.source_id, e.provenance, e.occurred_at, e.recorded_at,
                       e.payload->>'status' AS status, e.artifact_id, e.payload,
                       s.id AS subject_id, s.urn AS subject_urn, s.type AS subject_type, s.title AS subject_title,
                       bsk.event_record(e.source_id, e.external_id, e.payload) AS record
                FROM bsk.event e
                -- An event whose payload names no subject is about the first one it's linked to.
                LEFT JOIN LATERAL (
                    SELECT se.subject_id
                    FROM bsk.subject_event se
                    WHERE se.event_id = e.id AND e.payload->>'subject_id' IS NULL
                    ORDER BY se.relation, se.subject_id
                    LIMIT 1
                ) about ON true
                LEFT JOIN bsk.subject s ON s.id = CASE
                    WHEN e.payload->>'subject_id' IS NULL THEN about.subject_id
                    WHEN bsk.is_uuid_text(e.payload->>'subject_id') THEN (e.payload->>'subject_id')::uuid
                END
                WHERE e.{at} >= @since
                  {older}
                  AND ((@kinds::text[] IS NULL AND e.kind <> 'ledger') OR e.kind = ANY(@kinds::text[]))
                  -- With an area off (McpAreas): nothing of that area, and nothing no area claims
                  -- unless every key in its payload is a core one. An event no one placed stays out.
                  -- An attribute change of a key an off area owns (a bounty) is that area's.
                  AND (NOT @anyOff OR (
                      NOT (jsonb_exists_any(e.payload, @offKeys::text[]) OR e.kind = ANY(@offKinds::text[])
                           OR e.source_id = ANY(@offSources::text[]) OR coalesce(s.type = ANY(@offTypes::text[]), false)
                           OR coalesce(jsonb_exists_any(e.payload -> 'attributes', @offAttributes::text[]), false))
                      AND (jsonb_exists_any(e.payload, @onKeys::text[]) OR e.kind = ANY(@onKinds::text[])
                           OR e.source_id = ANY(@onSources::text[]) OR coalesce(s.type = ANY(@onTypes::text[]), false)
                           OR CASE WHEN jsonb_typeof(e.payload) = 'object'
                                   THEN NOT EXISTS (SELECT 1 FROM jsonb_object_keys(e.payload) k
                                                    WHERE k <> ALL(@coreKeys::text[]))
                                   ELSE false END)))
                  -- A record's newest version only, wherever its time falls: none of its record
                  -- recorded after it.
                  AND (bsk.event_record(e.source_id, e.external_id, e.payload) IS NULL OR NOT EXISTS (
                      SELECT 1 FROM bsk.event n
                      WHERE bsk.event_record(n.source_id, n.external_id, n.payload)
                                = bsk.event_record(e.source_id, e.external_id, e.payload)
                        AND (n.recorded_at, n.id) > (e.recorded_at, e.id)))
                ORDER BY e.{at} DESC, e.id DESC
                LIMIT @take
            ) m
            LEFT JOIN bsk.artifact a ON a.id = m.artifact_id
            ORDER BY m.{at} DESC, m.id DESC
            """;
        return (sql, new
        {
            since,
            kinds = kinds is { Count: > 0 } ? kinds.ToArray() : null,
            anyOff = off.Count > 0,
            offKeys = off.SelectMany(a => a.PayloadKeys).ToArray(),
            offKinds = off.SelectMany(a => a.Kinds).ToArray(),
            offSources = off.SelectMany(a => a.Sources).ToArray(),
            offTypes = off.SelectMany(a => a.Types).ToArray(),
            offAttributes = off.SelectMany(a => a.AttributeKeys).ToArray(),
            onKeys = on.SelectMany(a => a.PayloadKeys).ToArray(),
            onKinds = on.SelectMany(a => a.Kinds).ToArray(),
            onSources = on.SelectMany(a => a.Sources).ToArray(),
            onTypes = on.SelectMany(a => a.Types).ToArray(),
            coreKeys = McpAreas.CorePayloadKeys.ToArray(),
            maxText = MaxTextLength,
            maxPayload = MaxPayloadLength,
            beforeAt = before?.At,
            beforeId = before?.Id,
            take = limit + 1,
        });
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

    /// <summary>
    /// How many versions of this record there are: above 1 for an Oura record Oura re-sent as it
    /// changed, and a meal, fast, space photo, rating, reading or lab visit that was edited. This
    /// row is the newest.
    /// </summary>
    public long Versions { get; set; } = 1;
}

/// <summary>Where a cut answer stopped: its last event's time (the one the order goes by) and id.</summary>
public sealed record ChangeCursor(DateTimeOffset At, Guid Id);
