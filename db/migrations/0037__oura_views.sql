-- 0037__oura_views.sql
-- Oura's records and Chris's workout notes, folded from append-only events (activity plan A2,
-- §3.2, §3.5; ACTIVITY-1..4, D14).
--
--   * Every Oura record is an `observed` event from source `oura`, with no subject and no edge.
--     A workout is an `activity` event; a day's activity, sleep score and readiness, and each
--     sleep period are `measurement` events. The payload has one key:
--       oura_activity  {day, oura_id, steps, active_calories, total_calories, walking_m, high_s,
--                       medium_s, low_s, sedentary_s, resting_s, non_wear_s, score}
--       oura_workout   {id, day, activity, label, start, end, calories, distance_m, intensity, source}
--       oura_sleep_day {day, oura_id, score, contributors}
--       oura_readiness {day, oura_id, score, temperature_deviation, temperature_trend_deviation, contributors}
--       oura_sleep     {id, day, type, start, end, in_bed_s, asleep_s, deep_s, light_s, rem_s,
--                       awake_s, latency_s, efficiency, average_hrv, average_hr, lowest_hr}
--   * A sync appends a version only when a record changed. Daily records are identified by
--     their Oura day, workouts and sleep periods by Oura's id, and the NEWEST BY recorded_at
--     (then id) WINS. A workout or sleep period Oura no longer has gets a version holding
--     {id, day, voided: true}; if it comes back, a newer version restores it.
--   * A workout note is Chris's own `note` event (declared): {workout_note: {workout, text}}. The
--     newest per workout wins and an empty text clears it. Syncs never touch it.
--
-- Days are Oura's own: an activity day starts at 4 a.m., and a night counts on the morning it
-- ends. None of these events concerns an item, so the inbox, the diagnostics, the habit and
-- timer views and the ledger never see them.
--
-- The connection row also learns whether the history backfill has found where the history
-- starts (A2's first sync after connecting reads back a window at a time).

ALTER TABLE bsk_ops.integration
    ADD COLUMN IF NOT EXISTS history_done boolean NOT NULL DEFAULT false;

-- ---------------------------------------------------------------------------
-- 0. Reading numbers out of jsonb
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION bsk.json_int(value jsonb)
    RETURNS int
    LANGUAGE sql
    IMMUTABLE
AS $$
    SELECT CASE WHEN jsonb_typeof(value) = 'number' THEN round((value #>> '{}')::numeric)::int END;
$$;

CREATE OR REPLACE FUNCTION bsk.json_num(value jsonb)
    RETURNS numeric
    LANGUAGE sql
    IMMUTABLE
AS $$
    SELECT CASE WHEN jsonb_typeof(value) = 'number' THEN (value #>> '{}')::numeric END;
$$;

COMMENT ON FUNCTION bsk.json_int(jsonb) IS 'A jsonb number as an int (rounded); null for anything else.';
COMMENT ON FUNCTION bsk.json_num(jsonb) IS 'A jsonb number as numeric; null for anything else.';

-- ---------------------------------------------------------------------------
-- 1. Days of activity
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_activity_day AS
WITH version AS (
    SELECT
        e.id                                    AS event_id,
        e.payload->'oura_activity'              AS a,
        e.recorded_at,
        row_number() OVER w_desc                AS rn,
        count(*) OVER w_all                     AS versions
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND e.source_id = 'oura'
      AND jsonb_exists(e.payload, 'oura_activity')
      AND jsonb_typeof(e.payload->'oura_activity') = 'object'
      AND bsk.try_to_date(e.payload->'oura_activity'->>'day') IS NOT NULL
    WINDOW w_all  AS (PARTITION BY e.payload->'oura_activity'->>'day'),
           w_desc AS (PARTITION BY e.payload->'oura_activity'->>'day' ORDER BY e.recorded_at DESC, e.id DESC)
)
SELECT
    bsk.try_to_date(v.a->>'day')                AS day,
    bsk.json_int(v.a->'steps')                  AS steps,
    bsk.json_int(v.a->'active_calories')        AS active_calories,
    bsk.json_int(v.a->'total_calories')         AS total_calories,
    bsk.json_int(v.a->'walking_m')              AS walking_m,
    bsk.json_int(v.a->'high_s')                 AS high_s,
    bsk.json_int(v.a->'medium_s')               AS medium_s,
    bsk.json_int(v.a->'low_s')                  AS low_s,
    bsk.json_int(v.a->'sedentary_s')            AS sedentary_s,
    bsk.json_int(v.a->'resting_s')              AS resting_s,
    bsk.json_int(v.a->'non_wear_s')             AS non_wear_s,
    bsk.json_int(v.a->'score')                  AS score,
    v.recorded_at                               AS synced_at,
    v.versions::int                             AS versions,
    v.event_id
FROM version v
WHERE v.rn = 1;

COMMENT ON VIEW bsk.v_activity_day IS
    'ACTIVITY-2 (D14): one row per Oura activity day (starting 4 a.m.) — the newest version by recorded_at. Steps, calories, walking distance, activity time by intensity, non-wear time, in seconds where a time.';

-- ---------------------------------------------------------------------------
-- 2. Workouts, with Chris's notes
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_workout AS
WITH version AS (
    SELECT
        e.id                                    AS event_id,
        e.payload->'oura_workout'               AS w,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY e.payload->'oura_workout'->>'id'
            ORDER BY e.recorded_at DESC, e.id DESC) AS rn
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND e.source_id = 'oura'
      AND jsonb_exists(e.payload, 'oura_workout')
      AND jsonb_typeof(e.payload->'oura_workout') = 'object'
      AND coalesce(e.payload->'oura_workout'->>'id', '') <> ''
),
live AS (
    SELECT
        v.w->>'id'                              AS workout_id,
        bsk.try_to_date(v.w->>'day')            AS day,
        v.w,
        (v.w->>'start')::timestamptz            AS started_at,
        (v.w->>'end')::timestamptz              AS ended_at,
        v.recorded_at,
        v.event_id
    FROM version v
    WHERE v.rn = 1
      AND coalesce(v.w->>'voided', 'false') <> 'true'
      AND jsonb_typeof(v.w->'start') = 'string'
      AND jsonb_typeof(v.w->'end') = 'string'
),
note AS (
    SELECT DISTINCT ON (e.payload->'workout_note'->>'workout')
        e.payload->'workout_note'->>'workout'   AS workout_id,
        nullif(btrim(e.payload->'workout_note'->>'text'), '') AS text,
        e.recorded_at                           AS noted_at
    FROM bsk.event e
    WHERE e.kind = 'note'
      AND jsonb_exists(e.payload, 'workout_note')
      AND jsonb_typeof(e.payload->'workout_note') = 'object'
    ORDER BY e.payload->'workout_note'->>'workout', e.recorded_at DESC, e.id DESC
)
SELECT
    l.workout_id,
    l.day,
    coalesce(l.w->>'activity', 'unknown')       AS activity,
    l.w->>'label'                               AS label,
    l.started_at,
    l.ended_at,
    greatest(0, floor(extract(epoch FROM l.ended_at - l.started_at)))::int AS seconds,
    bsk.json_int(l.w->'calories')               AS calories,
    bsk.json_int(l.w->'distance_m')             AS distance_m,
    l.w->>'intensity'                           AS intensity,
    coalesce(l.w->>'source', 'unknown')         AS source,
    n.text                                      AS note,
    CASE WHEN n.text IS NOT NULL THEN n.noted_at END AS note_at,
    l.recorded_at                               AS synced_at,
    l.event_id
FROM live l
LEFT JOIN note n ON n.workout_id = l.workout_id
WHERE l.day IS NOT NULL;

COMMENT ON VIEW bsk.v_workout IS
    'ACTIVITY-3 (D14): one row per live Oura workout — the newest version of each Oura id by recorded_at, unless voided — with Chris''s newest note on it (null when there is none or it was cleared). Whether it counts as exercise is C#''s ExerciseRules.';

-- ---------------------------------------------------------------------------
-- 3. Sleep periods
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_sleep_period AS
WITH version AS (
    SELECT
        e.id                                    AS event_id,
        e.payload->'oura_sleep'                 AS s,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY e.payload->'oura_sleep'->>'id'
            ORDER BY e.recorded_at DESC, e.id DESC) AS rn
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND e.source_id = 'oura'
      AND jsonb_exists(e.payload, 'oura_sleep')
      AND jsonb_typeof(e.payload->'oura_sleep') = 'object'
      AND coalesce(e.payload->'oura_sleep'->>'id', '') <> ''
)
SELECT
    v.s->>'id'                                  AS period_id,
    bsk.try_to_date(v.s->>'day')                AS day,
    coalesce(v.s->>'type', 'unknown')           AS type,
    (v.s->>'start')::timestamptz                AS started_at,
    (v.s->>'end')::timestamptz                  AS ended_at,
    bsk.json_int(v.s->'in_bed_s')               AS in_bed_s,
    bsk.json_int(v.s->'asleep_s')               AS asleep_s,
    bsk.json_int(v.s->'deep_s')                 AS deep_s,
    bsk.json_int(v.s->'light_s')                AS light_s,
    bsk.json_int(v.s->'rem_s')                  AS rem_s,
    bsk.json_int(v.s->'awake_s')                AS awake_s,
    bsk.json_int(v.s->'latency_s')              AS latency_s,
    bsk.json_int(v.s->'efficiency')             AS efficiency,
    bsk.json_int(v.s->'average_hrv')            AS average_hrv,
    bsk.json_num(v.s->'average_hr')             AS average_hr,
    bsk.json_int(v.s->'lowest_hr')              AS lowest_hr,
    v.recorded_at                               AS synced_at,
    v.event_id
FROM version v
WHERE v.rn = 1
  AND coalesce(v.s->>'voided', 'false') <> 'true'
  AND coalesce(v.s->>'type', '') NOT IN ('rest', 'deleted')
  AND bsk.try_to_date(v.s->>'day') IS NOT NULL
  AND jsonb_typeof(v.s->'start') = 'string'
  AND jsonb_typeof(v.s->'end') = 'string';

COMMENT ON VIEW bsk.v_sleep_period IS
    'ACTIVITY-4 (D14): one row per live Oura sleep period — the newest version of each Oura id by recorded_at — leaving out voided, rejected (rest) and deleted ones. day is the morning it counts on; durations in seconds.';

-- ---------------------------------------------------------------------------
-- 4. Days of sleep
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_sleep_day AS
WITH score AS (
    SELECT DISTINCT ON (e.payload->'oura_sleep_day'->>'day')
        bsk.try_to_date(e.payload->'oura_sleep_day'->>'day') AS day,
        bsk.json_int(e.payload->'oura_sleep_day'->'score')   AS score,
        CASE WHEN jsonb_typeof(e.payload->'oura_sleep_day'->'contributors') = 'object'
             THEN e.payload->'oura_sleep_day'->'contributors'
             ELSE '{}'::jsonb END                             AS contributors,
        e.recorded_at,
        e.id                                                  AS event_id
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND e.source_id = 'oura'
      AND jsonb_exists(e.payload, 'oura_sleep_day')
      AND jsonb_typeof(e.payload->'oura_sleep_day') = 'object'
      AND bsk.try_to_date(e.payload->'oura_sleep_day'->>'day') IS NOT NULL
    ORDER BY e.payload->'oura_sleep_day'->>'day', e.recorded_at DESC, e.id DESC
),
night AS (
    -- The night is the day's longest long_sleep.
    SELECT DISTINCT ON (p.day) p.*
    FROM bsk.v_sleep_period p
    WHERE p.type = 'long_sleep'
    ORDER BY p.day, p.asleep_s DESC NULLS LAST, p.in_bed_s DESC NULLS LAST, p.started_at, p.period_id
),
nap AS (
    -- Every other period adds up separately.
    SELECT
        p.day,
        count(*)::int                                         AS naps,
        coalesce(sum(p.asleep_s), 0)::int                     AS nap_s,
        max(p.synced_at)                                      AS synced_at,
        array_agg(p.event_id ORDER BY p.started_at, p.event_id) AS event_ids
    FROM bsk.v_sleep_period p
    LEFT JOIN night n ON n.period_id = p.period_id
    WHERE n.period_id IS NULL
    GROUP BY p.day
),
days AS (
    SELECT day FROM score
    UNION
    SELECT day FROM night
    UNION
    SELECT day FROM nap
)
SELECT
    d.day,
    s.score,
    coalesce(s.contributors, '{}'::jsonb)                     AS contributors,
    n.started_at                                              AS bed_at,
    n.ended_at                                                AS woke_at,
    n.in_bed_s,
    n.asleep_s,
    n.deep_s,
    n.light_s,
    n.rem_s,
    n.awake_s,
    n.efficiency,
    n.average_hrv,
    n.lowest_hr,
    coalesce(p.naps, 0)                                       AS naps,
    coalesce(p.nap_s, 0)                                      AS nap_s,
    greatest(s.recorded_at, n.synced_at, p.synced_at)         AS synced_at,
    array_remove(ARRAY[s.event_id, n.event_id], NULL) || coalesce(p.event_ids, '{}'::uuid[]) AS event_ids
FROM days d
LEFT JOIN score s ON s.day = d.day
LEFT JOIN night n ON n.day = d.day
LEFT JOIN nap p   ON p.day = d.day;

COMMENT ON VIEW bsk.v_sleep_day IS
    'ACTIVITY-4 (D14): one row per day with a sleep score or a sleep period — the score and its contributors; the night (the longest long_sleep): bed and wake times and its stages; and the other periods as naps.';

-- ---------------------------------------------------------------------------
-- 5. Days of readiness
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_readiness_day AS
SELECT DISTINCT ON (e.payload->'oura_readiness'->>'day')
    bsk.try_to_date(e.payload->'oura_readiness'->>'day')                 AS day,
    bsk.json_int(e.payload->'oura_readiness'->'score')                   AS score,
    CASE WHEN jsonb_typeof(e.payload->'oura_readiness'->'contributors') = 'object'
         THEN e.payload->'oura_readiness'->'contributors'
         ELSE '{}'::jsonb END                                             AS contributors,
    bsk.json_num(e.payload->'oura_readiness'->'temperature_deviation')       AS temperature_deviation,
    bsk.json_num(e.payload->'oura_readiness'->'temperature_trend_deviation') AS temperature_trend_deviation,
    e.recorded_at                                                         AS synced_at,
    e.id                                                                  AS event_id
FROM bsk.event e
WHERE e.kind = 'measurement'
  AND e.source_id = 'oura'
  AND jsonb_exists(e.payload, 'oura_readiness')
  AND jsonb_typeof(e.payload->'oura_readiness') = 'object'
  AND bsk.try_to_date(e.payload->'oura_readiness'->>'day') IS NOT NULL
ORDER BY e.payload->'oura_readiness'->>'day', e.recorded_at DESC, e.id DESC;

COMMENT ON VIEW bsk.v_readiness_day IS
    'ACTIVITY-4 (D14): one row per day with a readiness score — the newest version by recorded_at: the score, its contributors and the temperature deviation in degrees C.';

-- ---------------------------------------------------------------------------
-- Indexes: keep these reads off a full scan of the stream
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS event_oura_activity_idx
    ON bsk.event ((payload->'oura_activity'->>'day'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'oura_activity');

CREATE INDEX IF NOT EXISTS event_oura_workout_idx
    ON bsk.event ((payload->'oura_workout'->>'id'), recorded_at)
    WHERE kind = 'activity' AND jsonb_exists(payload, 'oura_workout');

CREATE INDEX IF NOT EXISTS event_oura_sleep_day_idx
    ON bsk.event ((payload->'oura_sleep_day'->>'day'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'oura_sleep_day');

CREATE INDEX IF NOT EXISTS event_oura_readiness_idx
    ON bsk.event ((payload->'oura_readiness'->>'day'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'oura_readiness');

CREATE INDEX IF NOT EXISTS event_oura_sleep_idx
    ON bsk.event ((payload->'oura_sleep'->>'id'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'oura_sleep');

CREATE INDEX IF NOT EXISTS event_workout_note_idx
    ON bsk.event ((payload->'workout_note'->>'workout'), recorded_at)
    WHERE kind = 'note' AND jsonb_exists(payload, 'workout_note');

GRANT EXECUTE ON FUNCTION bsk.json_int(jsonb) TO bsk_reader;
GRANT EXECUTE ON FUNCTION bsk.json_num(jsonb) TO bsk_reader;
GRANT SELECT ON bsk.v_activity_day TO bsk_reader;
GRANT SELECT ON bsk.v_workout TO bsk_reader;
GRANT SELECT ON bsk.v_sleep_period TO bsk_reader;
GRANT SELECT ON bsk.v_sleep_day TO bsk_reader;
GRANT SELECT ON bsk.v_readiness_day TO bsk_reader;
