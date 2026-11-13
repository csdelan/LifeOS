-- 0054__review_reflection.sql
-- Reviews plan R5 (§4.1), notes plan N8: a review links to the reflection that answers it.
--
--   * The reflection is a note in Chris's notes vault. LifeOS holds its path in the vault, the
--     vault's name and the summary an agent wrote, never the note's text.
--   * A link is an `observation` event (declared) with a `concerns` edge to the Review:
--       reflection  {path, vault, summary}
--     The connector's link_reflection writes it, as the agent that filed the note. The NEWEST
--     link per review WINS, by recorded_at (then id), so linking again writes a new version and
--     the log keeps every one.
--   * Nothing else about a review changes: linking never completes it.

-- ---------------------------------------------------------------------------
-- Reviews, with their reflection
-- ---------------------------------------------------------------------------
-- The columns of 0052, then the newest reflection link: its path, vault and summary, and when it
-- was recorded (all null until one is linked).
CREATE OR REPLACE VIEW bsk.v_review AS
SELECT
    r.id,
    r.urn,
    r.title,
    r.attributes->>'kind'                                              AS kind,
    bsk.try_to_date(r.attributes->>'period')                           AS period,
    scs.status,
    (r.attributes->>'keep_focus') IS DISTINCT FROM 'false'             AS keep_focus,
    CASE WHEN lower(scs.status) = 'completed' THEN scs.status_occurred_at END AS completed_at,
    fc.first_completed_at,
    (fc.first_completed_at AT TIME ZONE bsk.home_zone())::date         AS first_completed_on,
    bsk.is_archived(r.id)                                              AS archived,
    r.created_at,
    rf.reflection->>'path'                                             AS reflection_path,
    rf.reflection->>'vault'                                            AS reflection_vault,
    rf.reflection->>'summary'                                          AS reflection_summary,
    rf.recorded_at                                                     AS reflection_at
FROM bsk.subject r
LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = r.id
LEFT JOIN LATERAL (
    SELECT min(e.occurred_at) AS first_completed_at
    FROM bsk.event e
    WHERE e.kind = 'state_change'
      AND e.payload->>'subject_id' = r.id::text
      AND lower(e.payload->>'status') = 'completed'
) fc ON true
LEFT JOIN LATERAL (
    SELECT e.payload->'reflection' AS reflection, e.recorded_at
    FROM bsk.subject_event se
    JOIN bsk.event e ON e.id = se.event_id
    WHERE se.subject_id = r.id
      AND se.relation = 'concerns'
      AND e.kind = 'observation'
      AND jsonb_typeof(e.payload->'reflection') = 'object'
    ORDER BY e.recorded_at DESC, e.id DESC
    LIMIT 1
) rf ON true
WHERE r.type = 'Review'
  AND r.attributes->>'kind' IN ('daily', 'weekly')
  AND bsk.try_to_date(r.attributes->>'period') IS NOT NULL;

COMMENT ON VIEW bsk.v_review IS
    'REVIEW-1/REVIEW-2 (D3, D8): one row per Review — its kind (daily, weekly), its period (the day, or the Sunday naming the Monday-to-Sunday week), the folded status, keep_focus, when the current Completed was set and when it was first completed (first_completed_on in the home zone), and the newest reflection link (reviews plan §4.1: its vault path, vault, summary and when it was recorded). Missed is computed from these on read.';

GRANT SELECT ON bsk.v_review TO bsk_reader;
