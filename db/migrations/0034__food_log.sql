-- 0034__food_log.sql
-- FOOD-1 / FOOD-2 / FOOD-4, D13 (food-log plan F1, §3.1–§3.2): the Food Log's entries,
-- fasts and closed days, folded from append-only events. Nothing about them is stored but
-- the events.
--
--   * A food entry (a meal, a snack, a drink) is a `measurement` event whose payload holds
--     `meal`: {id, name, score 1-5, calories, protein_g?, carbs_g?, fat_g?, meal_type,
--     photo?}. occurred_at is when it was eaten; recorded_at when it was logged.
--   * A fast is an `activity` event whose payload holds `fast`: {id, started_at,
--     ended_at?}. Begin writes a version without ended_at; End, a backfill or an edit
--     writes one with both.
--   * Either is deleted by a version holding {id, voided: true}.
--   * Every version carries its entry's or fast's id, and the NEWEST BY recorded_at (then
--     id) WINS — never by occurred_at, because an edit can move the time earlier.
--   * Closing a day is an `activity` event whose payload holds `food_day`: {on, closed}.
--
-- None of these events has a subject_id or an edge, so the habit views (which match
-- payload->>'subject_id'), the task timer (payload 'timer') and the subject diagnostics
-- never see them.
--
-- Days are home-zone dates (bsk.home_zone(), migration 0033), like the habit views. Scoring
-- a day is C#'s job (DietRules), so no number of the diet formula lives here.

-- ---------------------------------------------------------------------------
-- 1. Food entries
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_meal AS
WITH version AS (
    SELECT
        e.id                                     AS event_id,
        (e.payload->'meal'->>'id')::uuid         AS meal_id,
        e.payload->'meal'                        AS m,
        e.occurred_at,
        e.recorded_at,
        row_number() OVER w_desc                 AS rn,
        count(*) OVER w_all                      AS versions,
        min(e.recorded_at) OVER w_all            AS first_logged_at
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND jsonb_typeof(e.payload->'meal') = 'object'
      AND e.payload->'meal'->>'id' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    WINDOW w_all  AS (PARTITION BY e.payload->'meal'->>'id'),
           w_desc AS (PARTITION BY e.payload->'meal'->>'id' ORDER BY e.recorded_at DESC, e.id DESC)
)
SELECT
    v.meal_id,
    v.occurred_at                                              AS eaten_at,
    (v.occurred_at AT TIME ZONE bsk.home_zone())::date         AS eaten_on,
    v.occurred_at AT TIME ZONE bsk.home_zone()                 AS eaten_local,
    v.m->>'name'                                               AS name,
    CASE WHEN jsonb_typeof(v.m->'score') = 'number' THEN (v.m->>'score')::numeric::int END       AS score,
    CASE WHEN jsonb_typeof(v.m->'calories') = 'number' THEN (v.m->>'calories')::numeric::int END AS calories,
    CASE WHEN jsonb_typeof(v.m->'protein_g') = 'number' THEN (v.m->>'protein_g')::numeric::int END AS protein_g,
    CASE WHEN jsonb_typeof(v.m->'carbs_g') = 'number' THEN (v.m->>'carbs_g')::numeric::int END     AS carbs_g,
    CASE WHEN jsonb_typeof(v.m->'fat_g') = 'number' THEN (v.m->>'fat_g')::numeric::int END         AS fat_g,
    v.m->>'meal_type'                                          AS meal_type,
    CASE WHEN v.m->>'photo' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
         THEN (v.m->>'photo')::uuid END                        AS photo_artifact_id,
    v.first_logged_at,
    v.recorded_at                                              AS logged_at,
    v.versions::int                                            AS versions,
    v.event_id
FROM version v
WHERE v.rn = 1
  AND coalesce(v.m->>'voided', 'false') <> 'true';

COMMENT ON VIEW bsk.v_meal IS
    'FOOD-1 (D13): one row per live food entry — the newest version of each meal id by recorded_at, unless it is voided. eaten_on / eaten_local read eaten_at in the home zone (bsk.home_zone()).';

-- ---------------------------------------------------------------------------
-- 2. Fasts
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_fast AS
WITH version AS (
    SELECT
        e.id                                     AS event_id,
        (e.payload->'fast'->>'id')::uuid         AS fast_id,
        e.payload->'fast'                        AS f,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY e.payload->'fast'->>'id'
            ORDER BY e.recorded_at DESC, e.id DESC) AS rn
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND jsonb_typeof(e.payload->'fast') = 'object'
      AND e.payload->'fast'->>'id' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
),
live AS (
    SELECT
        v.fast_id,
        (v.f->>'started_at')::timestamptz                                           AS started_at,
        CASE WHEN jsonb_typeof(v.f->'ended_at') = 'string'
             THEN (v.f->>'ended_at')::timestamptz END                               AS ended_at,
        v.recorded_at,
        v.event_id
    FROM version v
    WHERE v.rn = 1
      AND coalesce(v.f->>'voided', 'false') <> 'true'
      AND jsonb_typeof(v.f->'started_at') = 'string'
)
SELECT
    l.fast_id,
    l.started_at,
    l.ended_at,
    CASE WHEN l.ended_at IS NULL THEN 'running' ELSE 'ended' END                    AS state,
    CASE WHEN l.ended_at IS NOT NULL
         THEN floor(extract(epoch FROM l.ended_at - l.started_at))::bigint END      AS seconds,
    (l.started_at AT TIME ZONE bsk.home_zone())::date                               AS started_on,
    (l.ended_at AT TIME ZONE bsk.home_zone())::date                                 AS ended_on,
    l.started_at AT TIME ZONE bsk.home_zone()                                       AS started_local,
    l.ended_at AT TIME ZONE bsk.home_zone()                                         AS ended_local,
    -- Food with calories eaten strictly inside the fast (the last meal before it and the
    -- one that breaks it sit on its edges, so they don't count).
    (SELECT count(*) FROM bsk.v_meal m
      WHERE m.calories > 0
        AND m.eaten_at > l.started_at
        AND m.eaten_at < coalesce(l.ended_at, now()))::int                          AS meals_during,
    l.recorded_at,
    l.event_id
FROM live l;

COMMENT ON VIEW bsk.v_fast IS
    'FOOD-2 (D13): one row per live fast — the newest version of each fast id by recorded_at, unless voided. running while ended_at is null; seconds once ended; ended_on (the day it counts on) in the home zone; meals_during counts food with calories eaten inside it.';

-- ---------------------------------------------------------------------------
-- 3. Food days
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_food_day AS
WITH meal_day AS (
    SELECT
        m.eaten_on                                                   AS day,
        count(*)::int                                                AS meals,
        coalesce(sum(m.calories), 0)::bigint                         AS calories,
        coalesce(sum(m.score::bigint * m.calories), 0)::bigint       AS score_calories,
        max(m.logged_at)                                             AS last_recorded_at,
        array_agg(m.event_id ORDER BY m.eaten_at, m.event_id)        AS food_event_ids
    FROM bsk.v_meal m
    GROUP BY m.eaten_on
),
fast_day AS (
    -- The longest fast that ended on each day; only it counts.
    SELECT DISTINCT ON (f.ended_on)
        f.ended_on                                                   AS day,
        f.seconds                                                    AS longest_fast_seconds,
        f.fast_id                                                    AS longest_fast_id,
        f.event_id                                                   AS fast_event_id,
        f.recorded_at
    FROM bsk.v_fast f
    WHERE f.state = 'ended'
    ORDER BY f.ended_on, f.seconds DESC, f.event_id
),
close_day AS (
    SELECT
        bsk.try_to_date(e.payload->'food_day'->>'on')                AS day,
        min(e.occurred_at)                                           AS closed_at,
        (array_agg(e.id ORDER BY e.occurred_at, e.id))[1]            AS close_event_id
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND jsonb_typeof(e.payload->'food_day') = 'object'
      AND e.payload->'food_day'->>'closed' = 'true'
    GROUP BY 1
),
days AS (
    SELECT day FROM meal_day
    UNION
    SELECT day FROM fast_day
    UNION
    SELECT day FROM close_day WHERE day IS NOT NULL
)
SELECT
    d.day,
    coalesce(m.meals, 0)                                             AS meals,
    coalesce(m.calories, 0)                                          AS calories,
    coalesce(m.score_calories, 0)                                    AS score_calories,
    f.longest_fast_seconds,
    f.longest_fast_id,
    f.fast_event_id,
    (c.day IS NOT NULL)                                              AS closed_by_hand,
    c.closed_at,
    c.close_event_id,
    (c.day IS NOT NULL OR d.day < bsk.home_today())                  AS closed,
    greatest(m.last_recorded_at, f.recorded_at, c.closed_at)         AS last_recorded_at,
    coalesce(m.food_event_ids, '{}'::uuid[])                         AS food_event_ids
FROM days d
LEFT JOIN meal_day m  ON m.day = d.day
LEFT JOIN fast_day f  ON f.day = d.day
LEFT JOIN close_day c ON c.day = d.day;

COMMENT ON VIEW bsk.v_food_day IS
    'FOOD-3 / FOOD-4 (D13): one row per home-zone day with a live food entry, an ended fast or a close — calories, score x calories, the longest fast ending that day, and whether it is closed (by hand, or because the day is over). DietRules (C#) scores it.';

-- Food reads filter on these events constantly; keep them off a full scan of the stream.
CREATE INDEX IF NOT EXISTS event_meal_idx
    ON bsk.event ((payload->'meal'->>'id'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'meal');

CREATE INDEX IF NOT EXISTS event_fast_idx
    ON bsk.event ((payload->'fast'->>'id'), recorded_at)
    WHERE kind = 'activity' AND jsonb_exists(payload, 'fast');

CREATE INDEX IF NOT EXISTS event_food_day_idx
    ON bsk.event ((payload->'food_day'->>'on'))
    WHERE kind = 'activity' AND jsonb_exists(payload, 'food_day');

GRANT SELECT ON bsk.v_meal TO bsk_reader;
GRANT SELECT ON bsk.v_fast TO bsk_reader;
GRANT SELECT ON bsk.v_food_day TO bsk_reader;
