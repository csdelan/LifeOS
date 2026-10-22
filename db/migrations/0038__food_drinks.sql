-- 0038__food_drinks.sql
-- FOOD-6 (food-log plan F7, §7): alcoholic drinks in the Food Log.
--
--   * A food entry may carry `meal.alcoholic: true` (left out when false), on every version
--     like the photo. One entry is one drink; its score is always 1 (FoodLogService sees to
--     that), so it's a 1 in the day's calorie-weighted average.
--   * bsk.v_meal gains `alcoholic`.
--   * bsk.v_food_day gains `drinks` (the day's live alcoholic entries) and `drink_event_ids`
--     (their event versions). Above DietRules.DrinksFree drinks the day's diet XP is cut; a
--     closed day's fast award cites these versions, so adding, removing or flagging a drink
--     re-scores it.
--
-- Columns are appended, so CREATE OR REPLACE keeps every existing column and dependent view.
-- The cut itself is C#'s (DietRules), like the rest of the diet formula.

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
    v.event_id,
    coalesce(v.m->'alcoholic' = 'true'::jsonb, false)          AS alcoholic
FROM version v
WHERE v.rn = 1
  AND coalesce(v.m->>'voided', 'false') <> 'true';

COMMENT ON VIEW bsk.v_meal IS
    'FOOD-1 / FOOD-6 (D13): one row per live food entry — the newest version of each meal id by recorded_at, unless it is voided. eaten_on / eaten_local read eaten_at in the home zone (bsk.home_zone()); alcoholic marks a drink (one entry, one drink).';

CREATE OR REPLACE VIEW bsk.v_food_day AS
WITH meal_day AS (
    SELECT
        m.eaten_on                                                   AS day,
        count(*)::int                                                AS meals,
        coalesce(sum(m.calories), 0)::bigint                         AS calories,
        coalesce(sum(m.score::bigint * m.calories), 0)::bigint       AS score_calories,
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
    'FOOD-3 / FOOD-4 / FOOD-6 (D13): one row per home-zone day with a live food entry, an ended fast or a close — calories, score x calories, the longest fast ending that day, its drinks, and whether it is closed (by hand, or because the day is over). DietRules (C#) scores it.';

GRANT SELECT ON bsk.v_meal TO bsk_reader;
GRANT SELECT ON bsk.v_food_day TO bsk_reader;
