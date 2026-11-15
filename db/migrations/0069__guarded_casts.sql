-- 0069__guarded_casts.sql
-- Code review CR116 (fifth review R9): no view casts a value it hasn't checked.
--
-- v_fast, v_oura_tag, v_sleep_period and v_workout checked a timestamp was a string but not that
-- it parsed, so one Oura record with a malformed start (or a fast with a malformed end) made the
-- view raise on every read, for good: the log is append-only. Each now reads its timestamps with
-- bsk.try_to_timestamptz. A version whose required time doesn't parse is left out, as one that
-- isn't a string was; an optional end that doesn't parse reads as no end. The version CTEs keep
-- their WHERE clauses word for word, so their partial indexes still apply.
--
-- bsk.json_int rounded any JSON number to an int, so one past an int's range raised; it now
-- answers null for it, as it does for anything that isn't a number.
--
-- A ledger entry's base (what the multipliers were applied to) had no CHECK, though
-- v_award_credit casts it to bigint: it's now held to the amounts' rule, whole numbers within
-- 1e15, or left out. NOT VALID, as 0062's are: the kernel writes it from a Dictionary<string, long>,
-- so every row already written casts cleanly.

CREATE OR REPLACE FUNCTION bsk.json_int(value jsonb)
RETURNS integer
LANGUAGE sql IMMUTABLE
AS $$
    SELECT CASE
        WHEN jsonb_typeof(value) = 'number' AND abs((value #>> '{}')::numeric) < 2147483647.5
            THEN round((value #>> '{}')::numeric)::int
    END;
$$;

ALTER TABLE bsk.event
    ADD CONSTRAINT event_ledger_base_check
        CHECK (kind <> 'ledger' OR coalesce(jsonb_typeof(payload->'base'), 'null') = 'null' OR (
            jsonb_typeof(payload->'base') = 'object'
            AND NOT payload @? '$.base.* ? (@.type() != "number" || @.floor() != @ || @ > 1e15 || @ < -1e15)'))
        NOT VALID;

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
          WHERE v.rn = 1 AND COALESCE(v.f ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(v.f -> 'started_at'::text) = 'string'::text
            AND bsk.try_to_timestamptz(v.f ->> 'started_at'::text) IS NOT NULL
        ), meal_time AS MATERIALIZED (
         SELECT m.eaten_at
           FROM bsk.v_meal m
          WHERE m.calories > 0
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
    (( SELECT count(*) AS count
           FROM meal_time m
          WHERE m.eaten_at > l.started_at AND m.eaten_at < COALESCE(l.ended_at, now())))::integer AS meals_during,
    recorded_at,
    event_id,
    fast_key
   FROM live l;

CREATE OR REPLACE VIEW bsk.v_oura_tag AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_tag'::text AS t,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_tag'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'observation'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_tag'::text) AND jsonb_typeof(e.payload -> 'oura_tag'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_tag'::text) ->> 'id'::text, ''::text) <> ''::text
        )
 SELECT t ->> 'id'::text AS tag_id,
    bsk.try_to_date(t ->> 'day'::text) AS day,
    bsk.try_to_date(t ->> 'end_day'::text) AS end_day,
    NULLIF(btrim(t ->> 'type_code'::text), ''::text) AS type_code,
    NULLIF(btrim(t ->> 'custom_name'::text), ''::text) AS custom_name,
    bsk.try_to_timestamptz(t ->> 'start'::text) AS started_at,
        CASE
            WHEN jsonb_typeof(t -> 'end'::text) = 'string'::text THEN bsk.try_to_timestamptz(t ->> 'end'::text)
            ELSE NULL::timestamp with time zone
        END AS ended_at,
    NULLIF(btrim(t ->> 'comment'::text), ''::text) AS comment,
    recorded_at AS synced_at,
    event_id
   FROM version v
  WHERE rn = 1 AND COALESCE(t ->> 'voided'::text, 'false'::text) <> 'true'::text AND bsk.try_to_date(t ->> 'day'::text) IS NOT NULL AND jsonb_typeof(t -> 'start'::text) = 'string'::text
    AND bsk.try_to_timestamptz(t ->> 'start'::text) IS NOT NULL;

CREATE OR REPLACE VIEW bsk.v_sleep_period AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_sleep'::text AS s,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_sleep'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_sleep'::text) AND jsonb_typeof(e.payload -> 'oura_sleep'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_sleep'::text) ->> 'id'::text, ''::text) <> ''::text
        )
 SELECT s ->> 'id'::text AS period_id,
    bsk.try_to_date(s ->> 'day'::text) AS day,
    COALESCE(s ->> 'type'::text, 'unknown'::text) AS type,
    bsk.try_to_timestamptz(s ->> 'start'::text) AS started_at,
    bsk.try_to_timestamptz(s ->> 'end'::text) AS ended_at,
    bsk.json_int(s -> 'in_bed_s'::text) AS in_bed_s,
    bsk.json_int(s -> 'asleep_s'::text) AS asleep_s,
    bsk.json_int(s -> 'deep_s'::text) AS deep_s,
    bsk.json_int(s -> 'light_s'::text) AS light_s,
    bsk.json_int(s -> 'rem_s'::text) AS rem_s,
    bsk.json_int(s -> 'awake_s'::text) AS awake_s,
    bsk.json_int(s -> 'latency_s'::text) AS latency_s,
    bsk.json_int(s -> 'efficiency'::text) AS efficiency,
    bsk.json_int(s -> 'average_hrv'::text) AS average_hrv,
    bsk.json_num(s -> 'average_hr'::text) AS average_hr,
    bsk.json_int(s -> 'lowest_hr'::text) AS lowest_hr,
    recorded_at AS synced_at,
    event_id
   FROM version v
  WHERE rn = 1 AND COALESCE(s ->> 'voided'::text, 'false'::text) <> 'true'::text AND (COALESCE(s ->> 'type'::text, ''::text) <> ALL (ARRAY['rest'::text, 'deleted'::text])) AND bsk.try_to_date(s ->> 'day'::text) IS NOT NULL AND jsonb_typeof(s -> 'start'::text) = 'string'::text AND jsonb_typeof(s -> 'end'::text) = 'string'::text
    AND bsk.try_to_timestamptz(s ->> 'start'::text) IS NOT NULL AND bsk.try_to_timestamptz(s ->> 'end'::text) IS NOT NULL;

CREATE OR REPLACE VIEW bsk.v_workout AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_workout'::text AS w,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_workout'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_workout'::text) AND jsonb_typeof(e.payload -> 'oura_workout'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_workout'::text) ->> 'id'::text, ''::text) <> ''::text
        ), live AS (
         SELECT v.w ->> 'id'::text AS workout_id,
            bsk.try_to_date(v.w ->> 'day'::text) AS day,
            v.w,
            bsk.try_to_timestamptz(v.w ->> 'start'::text) AS started_at,
            bsk.try_to_timestamptz(v.w ->> 'end'::text) AS ended_at,
            v.recorded_at,
            v.event_id
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.w ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(v.w -> 'start'::text) = 'string'::text AND jsonb_typeof(v.w -> 'end'::text) = 'string'::text
            AND bsk.try_to_timestamptz(v.w ->> 'start'::text) IS NOT NULL AND bsk.try_to_timestamptz(v.w ->> 'end'::text) IS NOT NULL
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
