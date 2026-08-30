-- title: Breach — Commitments with a violating event
--
-- "I set a rule and broke it." A Commitment is breached when an event `violates`
-- it (recorded via `bsk log activity --violates <commitment>`, which writes an
-- activity event and a `violates` edge in bsk.subject_event). Every such edge is
-- a recorded breach; the evidence is the violating event(s) and when they
-- happened.
--
-- This is confrontational by design, and distinct from neglect: neglect reports
-- what has gone quiet ("nothing has happened here"); breach reports a line you
-- drew and crossed ("this happened, against a rule you set"). The summary names
-- it as broken rather than softening it.
--
-- Adherence-over-a-window is THIS SAME QUERY with a date filter on the
-- violations — "how many times did I break this in the last 30 days?" — which is
-- the seam the future trade-DB plugs into (trade events supplying the
-- violations). To get it, add to the `violations` CTE:
--     AND e.occurred_at >= now() - interval '30 days'
-- Left out here because Stage 1 has no window parameterization and the runner
-- passes no arguments; the deterministic all-time query is the M4.3 deliverable.

WITH violations AS (
    SELECT
        se.subject_id,
        se.event_id,
        e.kind        AS event_kind,
        e.occurred_at
    FROM bsk.subject_event se
    JOIN bsk.event e ON e.id = se.event_id
    WHERE se.relation = 'violates'
)
SELECT
    s.id    AS subject_id,
    s.urn   AS subject_urn,
    s.type  AS subject_type,
    s.title AS subject_title,
    format(
        'Broken — %s recorded violation(s), most recently on %s.',
        count(v.event_id),
        to_char(max(v.occurred_at) AT TIME ZONE 'UTC', 'YYYY-MM-DD')
    ) AS summary,
    jsonb_agg(
        jsonb_build_object(
            'kind', 'event',
            'id', v.event_id,
            'relation', 'violates',
            'event_kind', v.event_kind,
            'occurred_at', to_char(v.occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
        )
        ORDER BY v.occurred_at DESC, v.event_id
    ) AS evidence
FROM bsk.subject s
JOIN violations v ON v.subject_id = s.id
-- Only Commitments can be breached; evidences/violates are Commitment-oriented.
WHERE s.type = 'Commitment'
GROUP BY s.id, s.urn, s.type, s.title
ORDER BY count(v.event_id) DESC, s.urn;
