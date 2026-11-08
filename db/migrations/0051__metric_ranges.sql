-- 0051__metric_ranges.sql
-- MEASURE-3, D20 (measurements plan M3, §4.8): a metric's normal range.
--
--   * attributes.low and attributes.high are a metric's normal range: numbers, either or both,
--     written only by MetricService (bsk set refuses them, as it does unit and target). The
--     bounds themselves are normal, as on a lab report.
--   * A reading is flagged low below `low` and high above `high`. The flag is C#'s job
--     (Metric.Flag), worked out on read from the range as it is now, so no flag is stored and a
--     changed range re-flags old readings.
--
-- bsk.v_metric gains `low` and `high` at its end (CREATE OR REPLACE VIEW can only add columns
-- there); each reads as null unless it's a number. Nothing else changes: readings, their view
-- and their index are 0050's.

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
    bsk.json_num(m.attributes->'high')                                 AS high
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
    'MEASURE-1 / MEASURE-3 (D20): one row per Metric subject — its name, unit, decimals (0-3, 1 when missing or malformed), optional target, which way is better, the starter-list key it was made from, when it was archived (null while it is not), and its normal range (low and high, either or both; null unless a number). Day values, the trend, the goal date and a reading''s high or low flag are computed in C# from this and bsk.v_metric_reading.';

GRANT SELECT ON bsk.v_metric TO bsk_reader;
