-- 0074__primary_focus.sql
-- Focus plan G1 (TODAY-1, D6): a Primary focus Chris chooses in a review.
--
--   * Until now Focus picked the Active Goal with the nearest target date, and the review's Keep
--     focus switch saved a flag nothing read. Choosing the focus now writes a `note` event
--     (declared):
--       focus  {level, goal_id, review_id}
--     level is 'weekly', D6's weekly focus (the monthly one comes with monthly reviews). goal_id
--     is the Goal, or null for "No focus". review_id is the review it was chosen in, or null
--     (bsk). The event `concerns` the Goal and the Review, so each shows it in its history.
--   * The NEWEST per level WINS, by recorded_at (then id), so every change stays in the log.
--   * With none chosen, or a chosen Goal that is no longer Active, Focus suggests the Active Goal
--     with the nearest target date, as it did.
--
-- bsk.event_record names a focus by its level, so the change feed shows only its newest version,
-- and event_record_idx, whose contents the function decides, is rebuilt (AGENTS.md).

CREATE INDEX event_focus_idx ON bsk.event ((payload->'focus'->>'level'), recorded_at)
    WHERE kind = 'note' AND jsonb_exists(payload, 'focus');

CREATE VIEW bsk.v_focus AS
WITH version AS (
    SELECT e.id AS event_id,
           e.payload->'focus'->>'level' AS level,
           e.payload->'focus' AS f,
           e.recorded_at,
           row_number() OVER (PARTITION BY e.payload->'focus'->>'level' ORDER BY e.recorded_at DESC, e.id DESC) AS rn
    FROM bsk.event e
    WHERE e.kind = 'note'
      AND jsonb_exists(e.payload, 'focus')
      AND jsonb_typeof(e.payload->'focus') = 'object'
)
SELECT level,
       CASE WHEN bsk.is_uuid_text(f->>'goal_id') THEN (f->>'goal_id')::uuid END     AS goal_id,
       CASE WHEN bsk.is_uuid_text(f->>'review_id') THEN (f->>'review_id')::uuid END AS review_id,
       recorded_at                                                                  AS chosen_at,
       event_id
FROM version
WHERE rn = 1
  AND level IS NOT NULL;

COMMENT ON VIEW bsk.v_focus IS
    'TODAY-1, D6 (focus plan G1): the Primary focus Chris chose, one row per level (weekly): the newest focus event by recorded_at. goal_id is the Goal, or null when he chose No focus; review_id the review it was chosen in, or null; chosen_at when. Focus shows the Goal while it is Active and suggests one otherwise.';

GRANT SELECT ON bsk.v_focus TO bsk_reader;

CREATE OR REPLACE FUNCTION bsk.event_record(source_id text, external_id text, payload jsonb)
RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$
    SELECT CASE
        WHEN source_id = 'oura' AND external_id IS NOT NULL
            THEN 'oura:' || regexp_replace(external_id, ':[^:]*:[^:]*$', '')
        WHEN payload->'meal'->>'id' IS NOT NULL THEN 'meal:' || (payload->'meal'->>'id')
        WHEN payload->'fast'->>'id' IS NOT NULL THEN 'fast:' || (payload->'fast'->>'id')
        WHEN payload->'space_photo'->>'id' IS NOT NULL THEN 'space_photo:' || (payload->'space_photo'->>'id')
        WHEN payload->'space_rating'->>'id' IS NOT NULL THEN 'space_rating:' || (payload->'space_rating'->>'id')
        WHEN payload->'metric_reading'->>'id' IS NOT NULL THEN 'metric_reading:' || (payload->'metric_reading'->>'id')
        WHEN payload->'lab_visit'->>'id' IS NOT NULL THEN 'lab_visit:' || (payload->'lab_visit'->>'id')
        WHEN payload->'exception_day'->>'day' IS NOT NULL THEN 'exception_day:' || (payload->'exception_day'->>'day')
        WHEN payload->'workout_note'->>'workout' IS NOT NULL THEN 'workout_note:' || (payload->'workout_note'->>'workout')
        WHEN payload->'focus'->>'level' IS NOT NULL THEN 'focus:' || (payload->'focus'->>'level')
    END;
$$;

COMMENT ON FUNCTION bsk.event_record(text, text, jsonb) IS
    'The record an event is a version of (an Oura record, a meal, a fast, a space photo or rating, a metric reading, a lab visit, an exception day, a workout''s note, the Primary focus), or null when it stands alone. Its newest version by recorded_at is the record.';

REINDEX INDEX bsk.event_record_idx;
