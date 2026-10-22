-- 0039__oura_tags.sql
-- The tags Chris adds in the Oura app, folded from append-only events (activity plan A10, §3.9;
-- ACTIVITY-6, D14).
--
--   * Each tag is an `observed` `observation` event from source `oura`, with no subject and no
--     edge: something Chris noted about himself, read off Oura's Enhanced Tags. The payload has
--     one key:
--       oura_tag  {id, day, end_day, type_code, custom_name, start, end, comment}
--     `day` is Oura's start_day; `type_code` is Oura's tag_type_code: the tag he picked,
--     'custom' for one he named himself (custom_name), or null for a text-only tag. `end` is
--     null for a moment, with no length.
--   * As for workouts (0037): a sync appends a version only when a tag changed, tags are
--     identified by Oura's id, and the NEWEST BY recorded_at (then id) WINS. A tag Oura no
--     longer has gets a version holding {id, day, voided: true}; if it comes back, a newer
--     version restores it.
--
-- How a tag reads ("Late meal" from tag_generic_late_meal) is C#'s OuraTags.Name, so the CLI
-- and the API word it the same. None of these events concerns an item, so the inbox, the
-- diagnostics, the habit and timer views and the ledger never see them.

CREATE OR REPLACE VIEW bsk.v_oura_tag AS
WITH version AS (
    SELECT
        e.id                                    AS event_id,
        e.payload->'oura_tag'                   AS t,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY e.payload->'oura_tag'->>'id'
            ORDER BY e.recorded_at DESC, e.id DESC) AS rn
    FROM bsk.event e
    WHERE e.kind = 'observation'
      AND e.source_id = 'oura'
      AND jsonb_exists(e.payload, 'oura_tag')
      AND jsonb_typeof(e.payload->'oura_tag') = 'object'
      AND coalesce(e.payload->'oura_tag'->>'id', '') <> ''
)
SELECT
    v.t->>'id'                                  AS tag_id,
    bsk.try_to_date(v.t->>'day')                AS day,
    bsk.try_to_date(v.t->>'end_day')            AS end_day,
    nullif(btrim(v.t->>'type_code'), '')        AS type_code,
    nullif(btrim(v.t->>'custom_name'), '')      AS custom_name,
    (v.t->>'start')::timestamptz                AS started_at,
    CASE WHEN jsonb_typeof(v.t->'end') = 'string' THEN (v.t->>'end')::timestamptz END AS ended_at,
    nullif(btrim(v.t->>'comment'), '')          AS comment,
    v.recorded_at                               AS synced_at,
    v.event_id
FROM version v
WHERE v.rn = 1
  AND coalesce(v.t->>'voided', 'false') <> 'true'
  AND bsk.try_to_date(v.t->>'day') IS NOT NULL
  AND jsonb_typeof(v.t->'start') = 'string';

COMMENT ON VIEW bsk.v_oura_tag IS
    'ACTIVITY-6 (D14): one row per live tag from the Oura app — the newest version of each Oura id by recorded_at, unless voided. day is the day it starts; ended_at is null for a moment. Its name is C#''s OuraTags.Name.';

-- Keep the view off a full scan of the stream, as 0037 does for the other collections.
CREATE INDEX IF NOT EXISTS event_oura_tag_idx
    ON bsk.event ((payload->'oura_tag'->>'id'), recorded_at)
    WHERE kind = 'observation' AND jsonb_exists(payload, 'oura_tag');

GRANT SELECT ON bsk.v_oura_tag TO bsk_reader;
