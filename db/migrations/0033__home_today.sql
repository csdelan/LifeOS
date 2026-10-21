-- 0033__home_today.sql
-- GEN-3 / REWARD-2: the habit views' "today" is the date in the home zone, not the
-- database's.
--
-- The habit views read `current_date`, which is the session's date, and the database
-- runs in UTC (Supabase, and the local Docker image). So from about 5 pm Pacific the
-- views thought today was over:
--
--   * today's unlogged habits read as missed, and the Habits page's current streak
--     fell to 0, until each was logged
--   * a habit established in the evening graduated on tomorrow's date
--   * a habit with no start attribute started on its creation's UTC date
--   * the establishment window and the habit's age ran a day ahead
--
-- bsk.home_today() is the date now in the home zone, and these views use it in place of
-- `current_date`. Timestamps they turn into dates (graduation, creation) are read in the
-- same zone.
--
-- The home zone is the database setting `bsk.home_zone`, unset by default, which means
-- America/Los_Angeles, the app's default (LifeOs.Domain.HomeZone.Default). It must match
-- Rewards:HomeTimeZone, which dates rewards and check-offs. To move it:
--
--   ALTER DATABASE <db> SET bsk.home_zone = '<IANA zone>';
--
-- The zone lives in the database, not on each connection, because connections go through
-- Supabase's pooler. A zone Postgres doesn't know fails every habit read loudly. The tests'
-- database sets it to UTC, so their dates match the UTC "today" they compute.

-- ---------------------------------------------------------------------------
-- 1. The home zone and today in it
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION bsk.home_zone()
    RETURNS text
    LANGUAGE sql
    STABLE
    PARALLEL SAFE
AS $$
    SELECT coalesce(nullif(btrim(current_setting('bsk.home_zone', true)), ''), 'America/Los_Angeles');
$$;

COMMENT ON FUNCTION bsk.home_zone() IS
    'The home zone (an IANA name): the database setting bsk.home_zone, or America/Los_Angeles when unset. Must match the app''s Rewards:HomeTimeZone.';

CREATE OR REPLACE FUNCTION bsk.home_today()
    RETURNS date
    LANGUAGE sql
    STABLE
    PARALLEL SAFE
AS $$
    SELECT (now() AT TIME ZONE bsk.home_zone())::date;
$$;

COMMENT ON FUNCTION bsk.home_today() IS
    'Today in the home zone (bsk.home_zone()). The habit views use it in place of current_date, which is the database''s (UTC) date.';

GRANT EXECUTE ON FUNCTION bsk.home_zone() TO bsk_reader;
GRANT EXECUTE ON FUNCTION bsk.home_today() TO bsk_reader;

-- ---------------------------------------------------------------------------
-- 2. Occurrences (0027's view, today in the home zone)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk_derived.habit_occurrence_source AS
WITH habits AS NOT MATERIALIZED (
    SELECT
        h.id,
        h.urn,
        h.attributes->'recurrence' AS recurrence,
        coalesce(bsk.try_to_date(h.attributes->>'start'), (h.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date,
        g.graduated_on,
        least(bsk.home_today(),
              coalesce(bsk.try_to_date(h.attributes->>'end'), bsk.home_today()),
              coalesce(g.graduated_on - 1, bsk.home_today())) AS end_cap,
        bsk.is_archived(h.id) AS archived
    FROM bsk.subject h
    LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
    CROSS JOIN LATERAL (
        SELECT CASE WHEN bsk.is_terminal_status(c.status) THEN (c.status_occurred_at AT TIME ZONE bsk.home_zone())::date END AS graduated_on
    ) g
    WHERE h.type = 'Habit'
),
scheduled AS NOT MATERIALIZED (
    SELECT hb.id AS habit_id, occ.d AS occurrence_date
    FROM habits hb
    CROSS JOIN LATERAL bsk.recurrence_occurrences(
        hb.recurrence, hb.start_date, hb.start_date, hb.end_cap) AS occ(d)
),
recorded AS NOT MATERIALIZED (
    SELECT DISTINCT hb.id AS habit_id, (e.payload->>'occurrence')::date AS occurrence_date
    FROM habits hb
    JOIN bsk.event e
      ON e.kind = 'activity'
     AND e.payload->>'subject_id' = hb.id::text
     AND jsonb_exists(e.payload, 'occurrence')
),
dates AS NOT MATERIALIZED (
    SELECT habit_id, occurrence_date FROM scheduled
    UNION
    SELECT habit_id, occurrence_date FROM recorded
)
SELECT
    d.habit_id,
    hb.urn AS habit_urn,
    d.occurrence_date,
    CASE
        WHEN latest.result IS NOT NULL            THEN latest.result
        WHEN d.occurrence_date < bsk.home_today() THEN 'missed'
        ELSE 'unrecorded'
    END AS state,
    latest.event_id    AS adherence_event_id,
    latest.occurred_at AS adherence_at,
    hb.graduated_on,
    hb.archived
FROM dates d
JOIN habits hb ON hb.id = d.habit_id
LEFT JOIN LATERAL (
    SELECT e.payload->>'result' AS result, e.id AS event_id, e.occurred_at
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND e.payload->>'subject_id' = d.habit_id::text
      AND e.payload->>'occurrence' = to_char(d.occurrence_date, 'YYYY-MM-DD')
    ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
    LIMIT 1
) latest ON true;

COMMENT ON VIEW bsk_derived.habit_occurrence_source IS
    'GEN-3 / REWARD-2: every habit''s occurrences (archived included) with resolved state, when the deciding adherence was recorded, and the graduation date. Scheduled dates stop at graduation. Today, graduation and a default start are dates in the home zone (bsk.home_today(), 0033).';

-- ---------------------------------------------------------------------------
-- 3. Establishment (0027's view, today in the home zone)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_habit_establishment AS
WITH habits AS (
    SELECT
        h.id,
        h.urn,
        h.title,
        coalesce(bsk.try_to_date(h.attributes->>'start'), (h.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date,
        CASE WHEN jsonb_typeof(h.attributes->'establish_target'->'min_adherence') = 'number'
             THEN (h.attributes->'establish_target'->>'min_adherence')::numeric END AS min_raw,
        CASE WHEN jsonb_typeof(h.attributes->'establish_target'->'window_days') = 'number'
             THEN (h.attributes->'establish_target'->>'window_days')::numeric END AS window_raw
    FROM bsk.subject h
    LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
    WHERE h.type = 'Habit'
      AND NOT bsk.is_terminal_status(c.status)
      AND NOT bsk.is_archived(h.id)
),
targets AS (
    SELECT
        id, urn, title, start_date,
        CASE WHEN min_raw > 0 AND min_raw <= 1 THEN min_raw
             WHEN min_raw > 1 AND min_raw <= 100 THEN min_raw / 100
             ELSE 0.8 END AS min_adherence,
        CASE WHEN window_raw >= 1 THEN floor(window_raw)::int ELSE 60 END AS window_days
    FROM habits
),
counts AS (
    SELECT
        o.habit_id,
        count(*) FILTER (WHERE o.state = 'followed') AS followed,
        count(*) FILTER (WHERE o.state = 'partial')  AS partial,
        count(*) FILTER (WHERE o.state = 'missed')   AS missed
    FROM bsk.v_habit_occurrence o
    JOIN targets t ON t.id = o.habit_id
    WHERE o.occurrence_date > bsk.home_today() - t.window_days
      AND o.occurrence_date <= bsk.home_today()
    GROUP BY o.habit_id
)
SELECT
    t.id                                        AS habit_id,
    t.urn                                       AS habit_urn,
    t.title                                     AS habit_name,
    t.min_adherence,
    t.window_days,
    bsk.home_today() - t.window_days + 1            AS window_start,
    coalesce(c.followed, 0)                     AS followed,
    coalesce(c.partial, 0)                      AS partial,
    coalesce(c.missed, 0)                       AS missed,
    coalesce(c.followed + c.partial + c.missed, 0) AS concluded,
    CASE WHEN coalesce(c.followed + c.partial + c.missed, 0) > 0
         THEN round((c.followed + 0.5 * c.partial) / (c.followed + c.partial + c.missed), 4)
    END                                         AS adherence,
    bsk.home_today() - t.start_date                 AS age_days,
    coalesce(c.followed + c.partial + c.missed, 0) > 0
        AND (c.followed + 0.5 * c.partial) / (c.followed + c.partial + c.missed) >= t.min_adherence
        AND bsk.home_today() - t.start_date >= t.window_days AS target_met
FROM targets t
LEFT JOIN counts c ON c.habit_id = t.id;

COMMENT ON VIEW bsk.v_habit_establishment IS
    'REWARD-2: per Building habit, adherence over its establish_target window (partial = half) and whether the target is met — the "ready to establish" suggestion. The window ends today in the home zone (0033). Read-only.';

GRANT SELECT ON bsk_derived.habit_occurrence_source TO bsk_reader;
GRANT SELECT ON bsk.v_habit_establishment TO bsk_reader;
