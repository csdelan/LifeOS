-- 0040__food_half_scores.sql
-- FOOD-1 (food-log plan §8): a food entry's health score moves in half points, 1 to 5
-- (3.5 sits between average and good).
--
--   * The payload's `meal.score` may now be a half (`3.5`). FoodLogService checks the step
--     and writes it without trailing zeros (4, not 4.0), so every entry logged before reads
--     exactly as it did.
--   * bsk.v_meal.score was read as an int, which rounded a half to the next whole point
--     (3.5 -> 4); it's now numeric and keeps the half.
--   * bsk.v_food_day.score_calories (the sum of score x calories) follows it from bigint to
--     numeric, so a day's calorie-weighted average keeps the halves too. DietRules (C#)
--     still does all the scoring: the quality line is already straight, so 3.5 pays halfway
--     between 3 and 4 and no number of the formula changes.
--
-- A view's column can't change type in place, so the three food views are dropped and made
-- again exactly as 0034 / 0038 left them, apart from those two columns (v_fast reads v_meal,
-- and v_food_day reads both). Nothing else depends on them.

DROP VIEW bsk.v_food_day;
DROP VIEW bsk.v_fast;
DROP VIEW bsk.v_meal;

-- ---------------------------------------------------------------------------
-- 1. Food entries
-- ---------------------------------------------------------------------------
CREATE VIEW bsk.v_meal AS
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
    CASE WHEN jsonb_typeof(v.m->'score') = 'number' THEN (v.m->>'score')::numeric END            AS score,
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
    v.event_id,
    coalesce(v.m->'alcoholic' = 'true'::jsonb, false)          AS alcoholic
FROM version v
WHERE v.rn = 1
  AND coalesce(v.m->>'voided', 'false') <> 'true';

COMMENT ON VIEW bsk.v_meal IS
    'FOOD-1 / FOOD-6 (D13): one row per live food entry — the newest version of each meal id by recorded_at, unless it is voided. score is 1 to 5 in half points (numeric). eaten_on / eaten_local read eaten_at in the home zone (bsk.home_zone()); alcoholic marks a drink (one entry, one drink).';

-- ---------------------------------------------------------------------------
-- 2. Fasts (unchanged from 0034; made again because it reads v_meal)
-- ---------------------------------------------------------------------------
CREATE VIEW bsk.v_fast AS
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
CREATE VIEW bsk.v_food_day AS
WITH meal_day AS (
    SELECT
        m.eaten_on                                                   AS day,
        count(*)::int                                                AS meals,
        coalesce(sum(m.calories), 0)::bigint                         AS calories,
        coalesce(sum(m.score * m.calories), 0)::numeric              AS score_calories,
        max(m.logged_at)                                             AS last_recorded_at,
        array_agg(m.event_id ORDER BY m.eaten_at, m.event_id)        AS food_event_ids,
        (count(*) FILTER (WHERE m.alcoholic))::int                   AS drinks,
        coalesce(array_agg(m.event_id ORDER BY m.eaten_at, m.event_id) FILTER (WHERE m.alcoholic),
                 '{}'::uuid[])                                       AS drink_event_ids
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
    coalesce(m.food_event_ids, '{}'::uuid[])                         AS food_event_ids,
    coalesce(m.drinks, 0)                                            AS drinks,
    coalesce(m.drink_event_ids, '{}'::uuid[])                        AS drink_event_ids
FROM days d
LEFT JOIN meal_day m  ON m.day = d.day
LEFT JOIN fast_day f  ON f.day = d.day
LEFT JOIN close_day c ON c.day = d.day;

COMMENT ON VIEW bsk.v_food_day IS
    'FOOD-3 / FOOD-4 / FOOD-6 (D13): one row per home-zone day with a live food entry, an ended fast or a close — calories, score x calories (numeric, since a score can be a half), the longest fast ending that day, its drinks, and whether it is closed (by hand, or because the day is over). DietRules (C#) scores it.';

GRANT SELECT ON bsk.v_meal TO bsk_reader;
GRANT SELECT ON bsk.v_fast TO bsk_reader;
GRANT SELECT ON bsk.v_food_day TO bsk_reader;
