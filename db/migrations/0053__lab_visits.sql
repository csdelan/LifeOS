-- 0053__lab_visits.sql
-- MEASURE-4, D20 (measurements plan M4, §4.9): lab visits, their results and the report.
--
--   * A lab visit is Chris's own `measurement` event (declared), occurred_at when the sample was
--     collected. The payload has one key:
--       lab_visit  {id, lab?, note?, report?}
--     The NEWEST version per visit id WINS, by recorded_at (then id), as readings do. A delete
--     writes {id, voided: true}.
--   * The report (a PDF or a photo of the paper) is an artifact. The version that adds it is the
--     event that stores the bytes, so its artifact_id is the report and its payload has no
--     `report`; later versions carry the artifact's id as `report`, as a space photo's do. A
--     version with neither has no report.
--   * A result is a metric reading (0050) with three more keys: `visit` (the visit's id) and
--     `low` and `high`, the range printed beside it on the report. A reading with a range of its
--     own is flagged against it, for good; any other against its metric's range (C#'s job,
--     Metric.Flag). A result's time is its visit's, kept so by LabVisitService.
--   * attributes.panel says which lab panel a test is on (cmp, lipids, a1c, or other), written
--     only by MetricService.
--
-- bsk.v_metric gains `panel` and bsk.v_metric_reading gains `visit_id`, `low` and `high`, each at
-- its end (CREATE OR REPLACE VIEW can only add columns there). Lab results never move a level.

-- ---------------------------------------------------------------------------
-- 1. Metrics, with their panel
-- ---------------------------------------------------------------------------
-- As 0051, with `panel` added at the end.
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
    m.created_at,
    bsk.json_num(m.attributes->'low')                                  AS low,
    bsk.json_num(m.attributes->'high')                                 AS high,
    CASE WHEN m.attributes->>'panel' IN ('cmp', 'lipids', 'a1c', 'other')
         THEN m.attributes->>'panel' END                               AS panel
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
    'MEASURE-1 / MEASURE-3 / MEASURE-4 (D20): one row per Metric subject — its name, unit, decimals (0-3, 1 when missing or malformed), optional target, which way is better, the starter-list key it was made from, when it was archived (null while it is not), its normal range (low and high, either or both; null unless a number), and the lab panel it is on (cmp, lipids, a1c or other; null for a metric that is not a lab test). Day values, the trend, the goal date and a reading''s high or low flag are computed in C# from this and bsk.v_metric_reading.';

-- ---------------------------------------------------------------------------
-- 2. Readings, with their visit and printed range
-- ---------------------------------------------------------------------------
-- As 0050, with `visit_id`, `low` and `high` added at the end: null for a reading that isn't a
-- lab result or whose report printed no range.
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
    l.event_id,
    CASE WHEN l.r->>'visit' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
         THEN (l.r->>'visit')::uuid END                                AS visit_id,
    bsk.json_num(l.r->'low')                                           AS low,
    bsk.json_num(l.r->'high')                                          AS high
FROM live l
JOIN bsk.subject m ON m.id = (l.r->>'metric')::uuid AND m.type = 'Metric';

COMMENT ON VIEW bsk.v_metric_reading IS
    'MEASURE-1 / MEASURE-4 (D20): one row per live reading of a metric — the newest version of each reading id by recorded_at, unless voided. taken_at is when it was taken; taken_on / taken_local read it in the home zone (bsk.home_zone()). unit is the metric''s unit when the reading was written. visit_id is the lab visit a result belongs to, and low / high the range printed beside it on the report (null when none).';

-- ---------------------------------------------------------------------------
-- 3. Lab visits
-- ---------------------------------------------------------------------------
-- collected_at is when the sample was taken; collected_on / collected_local read it in the home
-- zone. report_id is the report's artifact while it has bytes. first_logged_at is when the visit
-- was first written; versions counts its versions, the voided one included.
CREATE OR REPLACE VIEW bsk.v_lab_visit AS
WITH version AS (
    SELECT
        e.id                                                           AS event_id,
        e.payload->'lab_visit'                                         AS v,
        e.artifact_id,
        e.occurred_at,
        e.recorded_at,
        row_number() OVER w_desc                                       AS rn,
        count(*) OVER w_all                                            AS versions,
        min(e.recorded_at) OVER w_all                                  AS first_logged_at
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND jsonb_exists(e.payload, 'lab_visit')
      AND jsonb_typeof(e.payload->'lab_visit') = 'object'
      AND e.payload->'lab_visit'->>'id' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    WINDOW w_all  AS (PARTITION BY e.payload->'lab_visit'->>'id'),
           w_desc AS (PARTITION BY e.payload->'lab_visit'->>'id' ORDER BY e.recorded_at DESC, e.id DESC)
),
live AS (
    SELECT
        v.*,
        CASE WHEN v.v->>'report' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
             THEN (v.v->>'report')::uuid ELSE v.artifact_id END        AS report
    FROM version v
    WHERE v.rn = 1
      AND coalesce(v.v->>'voided', 'false') <> 'true'
)
SELECT
    (l.v->>'id')::uuid                                                 AS id,
    nullif(btrim(CASE WHEN jsonb_typeof(l.v->'lab') = 'string' THEN l.v->>'lab' END), '') AS lab,
    nullif(btrim(CASE WHEN jsonb_typeof(l.v->'note') = 'string' THEN l.v->>'note' END), '') AS note,
    l.occurred_at                                                      AS collected_at,
    (l.occurred_at AT TIME ZONE bsk.home_zone())::date                 AS collected_on,
    l.occurred_at AT TIME ZONE bsk.home_zone()                         AS collected_local,
    a.id                                                               AS report_id,
    a.filename                                                         AS report_filename,
    a.content_type                                                     AS report_content_type,
    a.byte_size                                                        AS report_byte_size,
    l.first_logged_at,
    l.recorded_at                                                      AS logged_at,
    l.versions::int                                                    AS versions,
    l.event_id
FROM live l
LEFT JOIN bsk.v_artifact a ON a.id = l.report AND a.has_bytes;

COMMENT ON VIEW bsk.v_lab_visit IS
    'MEASURE-4 (D20): one row per live lab visit — the newest version of each visit id by recorded_at, unless voided. collected_at is when the sample was taken; collected_on / collected_local read it in the home zone. report_id is the report (a PDF or photo), served by /api/artifacts/{id}/content, null without one. Its results are the readings in bsk.v_metric_reading with this visit_id.';

-- Keep the view off a full scan of the stream, as 0050 does for readings.
CREATE INDEX IF NOT EXISTS event_lab_visit_idx
    ON bsk.event ((payload->'lab_visit'->>'id'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'lab_visit');

GRANT SELECT ON bsk.v_metric, bsk.v_metric_reading, bsk.v_lab_visit TO bsk_reader;
