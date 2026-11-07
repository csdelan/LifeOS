-- 0050__metrics.sql
-- MEASURE-1 / MEASURE-2, D20 (measurements plan M1, §4.1–§4.3): metrics and their readings.
--
--   * A Metric is the 17th subject type: something Chris measures about himself (Weight,
--     Waist, HbA1c). Its title is its name. attributes.unit (text), decimals (a whole number
--     0-3), target (a number), better ('lower' or 'higher') and catalog (the starter-list key
--     it was made from) are written only by MetricService. A Metric has no status: it is there,
--     or archived (D9's archive_change), and an archived metric takes no new readings.
--   * A reading is Chris's own `measurement` event (declared) with a `concerns` edge to the
--     metric. occurred_at is when it was taken. The payload has one key:
--       metric_reading  {id, metric, value, unit, note?}
--     `unit` is the metric's unit when the reading was written.
--   * The NEWEST version per reading id WINS, by recorded_at (then id), as food entries do,
--     never by occurred_at, since an edit can move the time earlier. An edit writes a newer
--     version; a delete writes {id, voided: true}.
--
-- Readings are information only: none of them reaches the ledger, XP or a level. The day's
-- value, the trend and the goal date are C#'s job (MetricTrend in Domain), worked out on
-- every read and never stored, so no number of those formulas lives here.

-- ---------------------------------------------------------------------------
-- 1. Admit the Metric subject type
-- ---------------------------------------------------------------------------
ALTER TABLE bsk.subject DROP CONSTRAINT IF EXISTS subject_type_check;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area', 'Habit', 'Appointment',
        'Reward', 'Space', 'Metric'));

-- ---------------------------------------------------------------------------
-- 2. Metrics
-- ---------------------------------------------------------------------------
-- unit reads as '' when missing or not text; decimals as 1 when missing, not a whole number,
-- or outside 0-3; target as null unless it's a number; better as null unless it says lower or
-- higher. archived_at is when the newest archive_change archived the metric (null while it
-- isn't); archived_on is that instant's day in the home zone.
CREATE OR REPLACE VIEW bsk.v_metric AS
SELECT
    m.id,
    m.urn,
    m.title                                                            AS name,
    CASE WHEN jsonb_typeof(m.attributes->'unit') = 'string'
         THEN btrim(m.attributes->>'unit') ELSE '' END                 AS unit,
    CASE WHEN jsonb_typeof(m.attributes->'decimals') = 'number'
              AND (m.attributes->>'decimals')::numeric BETWEEN 0 AND 3
              AND (m.attributes->>'decimals')::numeric = trunc((m.attributes->>'decimals')::numeric)
         THEN (m.attributes->>'decimals')::numeric::int ELSE 1 END     AS decimals,
    bsk.json_num(m.attributes->'target')                               AS target,
    CASE WHEN m.attributes->>'better' IN ('lower', 'higher')
         THEN m.attributes->>'better' END                              AS better,
    CASE WHEN jsonb_typeof(m.attributes->'catalog') = 'string'
         THEN nullif(btrim(m.attributes->>'catalog'), '') END          AS catalog,
    CASE WHEN a.archived THEN a.occurred_at END                        AS archived_at,
    CASE WHEN a.archived
         THEN (a.occurred_at AT TIME ZONE bsk.home_zone())::date END   AS archived_on,
    m.created_at
FROM bsk.subject m
LEFT JOIN LATERAL (
    SELECT coalesce((e.payload->>'archived')::boolean, false) AS archived, e.occurred_at
    FROM bsk.event e
    WHERE e.kind = 'archive_change'
      AND e.payload->>'subject_id' = m.id::text
    ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
    LIMIT 1
) a ON true
WHERE m.type = 'Metric';

COMMENT ON VIEW bsk.v_metric IS
    'MEASURE-1 (D20): one row per Metric subject — its name, unit, decimals (0-3, 1 when missing or malformed), optional target, which way is better, the starter-list key it was made from, and when it was archived (null while it is not). Day values, the trend and the goal date are computed in C# from this and bsk.v_metric_reading.';

-- ---------------------------------------------------------------------------
-- 3. Readings
-- ---------------------------------------------------------------------------
-- A reading drops out when its value isn't a number or its metric isn't a Metric. taken_on and
-- taken_local read taken_at in the home zone. first_logged_at is when the reading was first
-- written; versions counts its versions, the voided one included.
CREATE OR REPLACE VIEW bsk.v_metric_reading AS
WITH version AS (
    SELECT
        e.id                                                           AS event_id,
        e.payload->'metric_reading'                                    AS r,
        e.occurred_at,
        e.recorded_at,
        row_number() OVER w_desc                                       AS rn,
        count(*) OVER w_all                                            AS versions,
        min(e.recorded_at) OVER w_all                                  AS first_logged_at
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND jsonb_exists(e.payload, 'metric_reading')
      AND jsonb_typeof(e.payload->'metric_reading') = 'object'
      AND e.payload->'metric_reading'->>'id' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    WINDOW w_all  AS (PARTITION BY e.payload->'metric_reading'->>'id'),
           w_desc AS (PARTITION BY e.payload->'metric_reading'->>'id' ORDER BY e.recorded_at DESC, e.id DESC)
),
live AS (
    SELECT v.*
    FROM version v
    WHERE v.rn = 1
      AND coalesce(v.r->>'voided', 'false') <> 'true'
      AND v.r->>'metric' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
      AND jsonb_typeof(v.r->'value') = 'number'
)
SELECT
    (l.r->>'id')::uuid                                                 AS id,
    m.id                                                               AS metric_id,
    (l.r->>'value')::numeric                                           AS value,
    CASE WHEN jsonb_typeof(l.r->'unit') = 'string' THEN btrim(l.r->>'unit') ELSE '' END AS unit,
    nullif(btrim(CASE WHEN jsonb_typeof(l.r->'note') = 'string' THEN l.r->>'note' END), '') AS note,
    l.occurred_at                                                      AS taken_at,
    (l.occurred_at AT TIME ZONE bsk.home_zone())::date                 AS taken_on,
    l.occurred_at AT TIME ZONE bsk.home_zone()                         AS taken_local,
    l.first_logged_at,
    l.recorded_at                                                      AS logged_at,
    l.versions::int                                                    AS versions,
    l.event_id
FROM live l
JOIN bsk.subject m ON m.id = (l.r->>'metric')::uuid AND m.type = 'Metric';

COMMENT ON VIEW bsk.v_metric_reading IS
    'MEASURE-1 (D20): one row per live reading of a metric — the newest version of each reading id by recorded_at, unless voided. taken_at is when it was taken; taken_on / taken_local read it in the home zone (bsk.home_zone()). unit is the metric''s unit when the reading was written.';

-- Keep the view off a full scan of the stream, as 0044 does for space ratings.
CREATE INDEX IF NOT EXISTS event_metric_reading_idx
    ON bsk.event ((payload->'metric_reading'->>'id'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'metric_reading');

GRANT SELECT ON bsk.v_metric, bsk.v_metric_reading TO bsk_reader;
