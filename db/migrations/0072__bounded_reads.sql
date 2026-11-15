-- 0072__bounded_reads.sql
-- Code review CR128 (fifth review R12): the hot reads stop growing with the square of history.
--
-- R13's template of three years of made-up use (Scale/seed.sql) measured what each hot path
-- reads, and these are what it found:
--
--   * v_fast counted the meals during each fast with a subquery per fast over every meal, 3.4
--     million rows for 885 fasts, so the settle and every meal took about 3 s. It now sorts the
--     meals' times once into an array and finds each fast's in it by binary search (width_bucket
--     gives how many are at or before an instant, a microsecond before the end makes the end
--     exclusive), in a subquery only a read of meals_during runs. The answer is the same.
--   * v_sleep_day found the naps (the periods that aren't the night) by an anti-join of every
--     period against every night, which the planner ran as a nested loop: 692,040 rows. Each
--     day's periods are now ranked once, the night is the first long sleep, and the rest are naps.
--   * v_reward_due asked bsk.ledger_opened_at(), an SQL function Postgres can't inline, of every
--     row it read: 994 statements for one check-off. It now asks once (an initplan).
--   * v_activity_day and v_readiness_day are each keyed by Oura's day, and their indexes are on
--     that key as text, but they answered it only as a date worked out above the window, so a
--     week's read folded every version of every day. Each now answers day_key, the key as text,
--     and a range on it is read from its index (AGENTS.md: a filter goes below a window only on
--     the partition key itself).
--   * v_workout answered its id from the newest version, above the window, so a read of one
--     workout folded every workout. It now answers the window's key, so the lookup goes below.
--
-- Every view answers the rows it did (MigrationOnDataTests), and only day_key is new. The reads
-- that use these, and the ledger and credit lookups by award key, change in C# with them.

CREATE OR REPLACE VIEW bsk.v_fast AS
 WITH version AS (
         SELECT e.id AS event_id,
            (e.payload -> 'fast'::text) ->> 'id'::text AS fast_key,
            ((e.payload -> 'fast'::text) ->> 'id'::text)::uuid AS fast_id,
            e.payload -> 'fast'::text AS f,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'fast'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND jsonb_exists(e.payload, 'fast'::text) AND jsonb_typeof(e.payload -> 'fast'::text) = 'object'::text AND ((e.payload -> 'fast'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
        ), live AS (
         SELECT v.fast_id,
            bsk.try_to_timestamptz(v.f ->> 'started_at'::text) AS started_at,
                CASE
                    WHEN jsonb_typeof(v.f -> 'ended_at'::text) = 'string'::text THEN bsk.try_to_timestamptz(v.f ->> 'ended_at'::text)
                    ELSE NULL::timestamp with time zone
                END AS ended_at,
            v.recorded_at,
            v.event_id,
            v.fast_key
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.f ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(v.f -> 'started_at'::text) = 'string'::text AND bsk.try_to_timestamptz(v.f ->> 'started_at'::text) IS NOT NULL
        ), meal_time AS MATERIALIZED (
         SELECT COALESCE(array_agg(m.eaten_at ORDER BY m.eaten_at), '{}'::timestamp with time zone[]) AS eaten
           FROM bsk.v_meal m
          WHERE m.calories > 0 AND m.eaten_at IS NOT NULL
        )
 SELECT fast_id,
    started_at,
    ended_at,
        CASE
            WHEN ended_at IS NULL THEN 'running'::text
            ELSE 'ended'::text
        END AS state,
        CASE
            WHEN ended_at IS NOT NULL THEN floor(EXTRACT(epoch FROM ended_at - started_at))::bigint
            ELSE NULL::bigint
        END AS seconds,
    (started_at AT TIME ZONE bsk.home_zone())::date AS started_on,
    (ended_at AT TIME ZONE bsk.home_zone())::date AS ended_on,
    (started_at AT TIME ZONE bsk.home_zone()) AS started_local,
    (ended_at AT TIME ZONE bsk.home_zone()) AS ended_local,
    ( SELECT GREATEST(0, width_bucket(COALESCE(l.ended_at, now()) - '00:00:00.000001'::interval, mt.eaten) - width_bucket(l.started_at, mt.eaten)) AS greatest
           FROM meal_time mt) AS meals_during,
    recorded_at,
    event_id,
    fast_key
   FROM live l;

CREATE OR REPLACE VIEW bsk.v_sleep_day AS
 WITH score AS (
         SELECT DISTINCT ON (((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text)) bsk.try_to_date((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text) AS day,
            bsk.json_int((e.payload -> 'oura_sleep_day'::text) -> 'score'::text) AS score,
                CASE
                    WHEN jsonb_typeof((e.payload -> 'oura_sleep_day'::text) -> 'contributors'::text) = 'object'::text THEN (e.payload -> 'oura_sleep_day'::text) -> 'contributors'::text
                    ELSE '{}'::jsonb
                END AS contributors,
            e.recorded_at,
            e.id AS event_id
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_sleep_day'::text) AND jsonb_typeof(e.payload -> 'oura_sleep_day'::text) = 'object'::text AND bsk.try_to_date((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text) IS NOT NULL
          ORDER BY ((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text), e.recorded_at DESC, e.id DESC
        ), period AS (
         SELECT p_1.period_id,
            p_1.day,
            p_1.type,
            p_1.started_at,
            p_1.ended_at,
            p_1.in_bed_s,
            p_1.asleep_s,
            p_1.deep_s,
            p_1.light_s,
            p_1.rem_s,
            p_1.awake_s,
            p_1.latency_s,
            p_1.efficiency,
            p_1.average_hrv,
            p_1.average_hr,
            p_1.lowest_hr,
            p_1.synced_at,
            p_1.event_id,
            row_number() OVER (PARTITION BY p_1.day, (p_1.type = 'long_sleep'::text) ORDER BY p_1.asleep_s DESC NULLS LAST, p_1.in_bed_s DESC NULLS LAST, p_1.started_at, p_1.period_id) AS nth
           FROM bsk.v_sleep_period p_1
        ), night AS (
         SELECT p_1.period_id,
            p_1.day,
            p_1.type,
            p_1.started_at,
            p_1.ended_at,
            p_1.in_bed_s,
            p_1.asleep_s,
            p_1.deep_s,
            p_1.light_s,
            p_1.rem_s,
            p_1.awake_s,
            p_1.latency_s,
            p_1.efficiency,
            p_1.average_hrv,
            p_1.average_hr,
            p_1.lowest_hr,
            p_1.synced_at,
            p_1.event_id
           FROM period p_1
          WHERE p_1.type = 'long_sleep'::text AND p_1.nth = 1
        ), nap AS (
         SELECT p_1.day,
            count(*)::integer AS naps,
            COALESCE(sum(p_1.asleep_s), 0::bigint)::integer AS nap_s,
            max(p_1.synced_at) AS synced_at,
            array_agg(p_1.event_id ORDER BY p_1.started_at, p_1.event_id) AS event_ids
           FROM period p_1
          WHERE p_1.type <> 'long_sleep'::text OR p_1.nth > 1
          GROUP BY p_1.day
        ), days AS (
         SELECT score.day
           FROM score
        UNION
         SELECT night.day
           FROM night
        UNION
         SELECT nap.day
           FROM nap
        )
 SELECT d.day,
    s.score,
    COALESCE(s.contributors, '{}'::jsonb) AS contributors,
    n.started_at AS bed_at,
    n.ended_at AS woke_at,
    n.in_bed_s,
    n.asleep_s,
    n.deep_s,
    n.light_s,
    n.rem_s,
    n.awake_s,
    n.efficiency,
    n.average_hrv,
    n.lowest_hr,
    COALESCE(p.naps, 0) AS naps,
    COALESCE(p.nap_s, 0) AS nap_s,
    GREATEST(s.recorded_at, n.synced_at, p.synced_at) AS synced_at,
    array_remove(ARRAY[s.event_id, n.event_id], NULL::uuid) || COALESCE(p.event_ids, '{}'::uuid[]) AS event_ids
   FROM days d
     LEFT JOIN score s ON s.day = d.day
     LEFT JOIN night n ON n.day = d.day
     LEFT JOIN nap p ON p.day = d.day;

CREATE OR REPLACE VIEW bsk.v_reward_due AS
 SELECT subject_id,
    subject_type,
    award_key,
    trigger,
    source_event_ids,
    triggered_at,
    timed_seconds,
    occurred_on
   FROM bsk.v_reward_trigger t
  WHERE triggered_at >= (( SELECT bsk.ledger_opened_at() AS ledger_opened_at));

CREATE OR REPLACE VIEW bsk.v_activity_day AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_activity'::text AS a,
            e.recorded_at,
            (e.payload -> 'oura_activity'::text) ->> 'day'::text AS day_key,
            row_number() OVER w_desc AS rn,
            count(*) OVER w_all AS versions
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_activity'::text) AND jsonb_typeof(e.payload -> 'oura_activity'::text) = 'object'::text AND bsk.try_to_date((e.payload -> 'oura_activity'::text) ->> 'day'::text) IS NOT NULL
          WINDOW w_all AS (PARTITION BY ((e.payload -> 'oura_activity'::text) ->> 'day'::text)), w_desc AS (PARTITION BY ((e.payload -> 'oura_activity'::text) ->> 'day'::text) ORDER BY e.recorded_at DESC, e.id DESC)
        )
 SELECT bsk.try_to_date(a ->> 'day'::text) AS day,
    bsk.json_int(a -> 'steps'::text) AS steps,
    bsk.json_int(a -> 'active_calories'::text) AS active_calories,
    bsk.json_int(a -> 'total_calories'::text) AS total_calories,
    bsk.json_int(a -> 'walking_m'::text) AS walking_m,
    bsk.json_int(a -> 'high_s'::text) AS high_s,
    bsk.json_int(a -> 'medium_s'::text) AS medium_s,
    bsk.json_int(a -> 'low_s'::text) AS low_s,
    bsk.json_int(a -> 'sedentary_s'::text) AS sedentary_s,
    bsk.json_int(a -> 'resting_s'::text) AS resting_s,
    bsk.json_int(a -> 'non_wear_s'::text) AS non_wear_s,
    bsk.json_int(a -> 'score'::text) AS score,
    recorded_at AS synced_at,
    versions::integer AS versions,
    event_id,
    day_key
   FROM version v
  WHERE rn = 1;

CREATE OR REPLACE VIEW bsk.v_readiness_day AS
 SELECT DISTINCT ON (((payload -> 'oura_readiness'::text) ->> 'day'::text)) bsk.try_to_date((payload -> 'oura_readiness'::text) ->> 'day'::text) AS day,
    bsk.json_int((payload -> 'oura_readiness'::text) -> 'score'::text) AS score,
        CASE
            WHEN jsonb_typeof((payload -> 'oura_readiness'::text) -> 'contributors'::text) = 'object'::text THEN (payload -> 'oura_readiness'::text) -> 'contributors'::text
            ELSE '{}'::jsonb
        END AS contributors,
    bsk.json_num((payload -> 'oura_readiness'::text) -> 'temperature_deviation'::text) AS temperature_deviation,
    bsk.json_num((payload -> 'oura_readiness'::text) -> 'temperature_trend_deviation'::text) AS temperature_trend_deviation,
    recorded_at AS synced_at,
    id AS event_id,
    (payload -> 'oura_readiness'::text) ->> 'day'::text AS day_key
   FROM bsk.event e
  WHERE kind = 'measurement'::text AND source_id = 'oura'::text AND jsonb_exists(payload, 'oura_readiness'::text) AND jsonb_typeof(payload -> 'oura_readiness'::text) = 'object'::text AND bsk.try_to_date((payload -> 'oura_readiness'::text) ->> 'day'::text) IS NOT NULL
  ORDER BY ((payload -> 'oura_readiness'::text) ->> 'day'::text), recorded_at DESC, id DESC;

CREATE OR REPLACE VIEW bsk.v_workout AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_workout'::text AS w,
            (e.payload -> 'oura_workout'::text) ->> 'id'::text AS workout_key,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_workout'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_workout'::text) AND jsonb_typeof(e.payload -> 'oura_workout'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_workout'::text) ->> 'id'::text, ''::text) <> ''::text
        ), live AS (
         SELECT v.workout_key AS workout_id,
            bsk.try_to_date(v.w ->> 'day'::text) AS day,
            v.w,
            bsk.try_to_timestamptz(v.w ->> 'start'::text) AS started_at,
            bsk.try_to_timestamptz(v.w ->> 'end'::text) AS ended_at,
            v.recorded_at,
            v.event_id
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.w ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(v.w -> 'start'::text) = 'string'::text AND jsonb_typeof(v.w -> 'end'::text) = 'string'::text AND bsk.try_to_timestamptz(v.w ->> 'start'::text) IS NOT NULL AND bsk.try_to_timestamptz(v.w ->> 'end'::text) IS NOT NULL
        ), note AS (
         SELECT DISTINCT ON (((e.payload -> 'workout_note'::text) ->> 'workout'::text)) (e.payload -> 'workout_note'::text) ->> 'workout'::text AS workout_id,
            NULLIF(btrim((e.payload -> 'workout_note'::text) ->> 'text'::text), ''::text) AS text,
            e.recorded_at AS noted_at
           FROM bsk.event e
          WHERE e.kind = 'note'::text AND jsonb_exists(e.payload, 'workout_note'::text) AND jsonb_typeof(e.payload -> 'workout_note'::text) = 'object'::text
          ORDER BY ((e.payload -> 'workout_note'::text) ->> 'workout'::text), e.recorded_at DESC, e.id DESC
        )
 SELECT l.workout_id,
    l.day,
    COALESCE(l.w ->> 'activity'::text, 'unknown'::text) AS activity,
    l.w ->> 'label'::text AS label,
    l.started_at,
    l.ended_at,
    GREATEST(0::numeric, floor(EXTRACT(epoch FROM l.ended_at - l.started_at)))::integer AS seconds,
    bsk.json_int(l.w -> 'calories'::text) AS calories,
    bsk.json_int(l.w -> 'distance_m'::text) AS distance_m,
    l.w ->> 'intensity'::text AS intensity,
    COALESCE(l.w ->> 'source'::text, 'unknown'::text) AS source,
    n.text AS note,
        CASE
            WHEN n.text IS NOT NULL THEN n.noted_at
            ELSE NULL::timestamp with time zone
        END AS note_at,
    l.recorded_at AS synced_at,
    l.event_id
   FROM live l
     LEFT JOIN note n ON n.workout_id = l.workout_id
  WHERE l.day IS NOT NULL;

COMMENT ON VIEW bsk.v_activity_day IS
    'ACTIVITY-2 (D14): one row per Oura activity day (starting 4 a.m.) — the newest version by recorded_at. Steps, calories, walking distance, activity time by intensity, non-wear time, in seconds where a time. day_key is the day as Oura sends it, the key its versions are folded by: a range on it reads only those days.';

COMMENT ON VIEW bsk.v_readiness_day IS
    'ACTIVITY-4 (D14): one row per day with a readiness score — the newest version by recorded_at: the score, its contributors and the temperature deviation in degrees C. day_key is the day as Oura sends it, the key its versions are folded by: a range on it reads only those days.';
