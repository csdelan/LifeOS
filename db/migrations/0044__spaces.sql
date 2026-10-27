-- 0044__spaces.sql
-- HOME-1 / HOME-2, D18 (environment plan E1, §4.1–§4.3): the home's spaces and their ratings.
--
--   * A Space is the 16th subject type: a part of the home Chris rates (the office, the
--     kitchen, the stairway and halls together). Its title is its name. attributes.weight
--     (a whole number 1-10, how much it counts) and attributes.pace (1-5, how fast it gets
--     messy; 3 is normal) are written only by SpaceService. A Space has no status: it is
--     there, or archived (D9's archive_change), and an archived space leaves the Home Score
--     from the day it was archived.
--   * A rating is Chris's own `measurement` event (declared) with a `concerns` edge to the
--     space. The payload has one key:
--       space_rating  {id, space, day, tidiness?, cleanliness?, aesthetics?, note?}
--     Each part is 1-5 in halves; any of them can be left out (a quick check rates tidiness
--     and cleanliness only). `day` is a home-zone date.
--   * The NEWEST version per rating id WINS, by recorded_at (then id), as food entries do.
--     An edit writes a newer version; a delete writes {id, voided: true}.
--
-- Scoring is C#'s job (HomeScore in Domain): the fades, the space scores and the Home Score
-- are worked out on every read and never stored, so no number of the formula lives here.

-- ---------------------------------------------------------------------------
-- 1. Admit the Space subject type
-- ---------------------------------------------------------------------------
ALTER TABLE bsk.subject DROP CONSTRAINT IF EXISTS subject_type_check;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area', 'Habit', 'Appointment',
        'Reward', 'Space'));

-- ---------------------------------------------------------------------------
-- 2. Spaces
-- ---------------------------------------------------------------------------
-- weight and pace read as 3 when missing, not a whole number, or out of range, so a
-- hand-edited attribute can't break the Home Score. archived_at is when the newest
-- archive_change archived the space (null while it isn't archived); archived_on is that
-- instant's day in the home zone.
CREATE OR REPLACE VIEW bsk.v_space AS
SELECT
    s.id,
    s.urn,
    s.title                                                            AS name,
    CASE WHEN jsonb_typeof(s.attributes->'weight') = 'number'
              AND (s.attributes->>'weight')::numeric BETWEEN 1 AND 10
              AND (s.attributes->>'weight')::numeric = trunc((s.attributes->>'weight')::numeric)
         THEN (s.attributes->>'weight')::numeric::int ELSE 3 END       AS weight,
    CASE WHEN jsonb_typeof(s.attributes->'pace') = 'number'
              AND (s.attributes->>'pace')::numeric BETWEEN 1 AND 5
              AND (s.attributes->>'pace')::numeric = trunc((s.attributes->>'pace')::numeric)
         THEN (s.attributes->>'pace')::numeric::int ELSE 3 END         AS pace,
    CASE WHEN a.archived THEN a.occurred_at END                        AS archived_at,
    CASE WHEN a.archived
         THEN (a.occurred_at AT TIME ZONE bsk.home_zone())::date END   AS archived_on,
    s.created_at
FROM bsk.subject s
LEFT JOIN LATERAL (
    SELECT coalesce((e.payload->>'archived')::boolean, false) AS archived, e.occurred_at
    FROM bsk.event e
    WHERE e.kind = 'archive_change'
      AND e.payload->>'subject_id' = s.id::text
    ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
    LIMIT 1
) a ON true
WHERE s.type = 'Space';

COMMENT ON VIEW bsk.v_space IS
    'HOME-1 (D18): one row per Space subject — its name, weight (1-10) and pace (1-5), each 3 when missing or malformed, and when it was archived (null while it is not). The Home Score is computed in C# from this and bsk.v_space_rating.';

-- ---------------------------------------------------------------------------
-- 3. Ratings
-- ---------------------------------------------------------------------------
-- A part reads as null when it was left out or isn't 1-5 in halves. A rating of a subject
-- that isn't a Space drops out.
CREATE OR REPLACE VIEW bsk.v_space_rating AS
WITH version AS (
    SELECT
        e.id                                                           AS event_id,
        e.payload->'space_rating'                                      AS r,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY e.payload->'space_rating'->>'id'
            ORDER BY e.recorded_at DESC, e.id DESC)                    AS rn
    FROM bsk.event e
    WHERE e.kind = 'measurement'
      AND jsonb_exists(e.payload, 'space_rating')
      AND jsonb_typeof(e.payload->'space_rating') = 'object'
      AND e.payload->'space_rating'->>'id' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
),
live AS (
    SELECT v.*
    FROM version v
    WHERE v.rn = 1
      AND coalesce(v.r->>'voided', 'false') <> 'true'
      AND v.r->>'space' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
      AND bsk.try_to_date(v.r->>'day') IS NOT NULL
)
SELECT
    (l.r->>'id')::uuid                                                 AS id,
    s.id                                                               AS space_id,
    bsk.try_to_date(l.r->>'day')                                       AS day,
    CASE WHEN jsonb_typeof(l.r->'tidiness') = 'number'
              AND (l.r->>'tidiness')::numeric BETWEEN 1 AND 5
              AND (l.r->>'tidiness')::numeric * 2 = trunc((l.r->>'tidiness')::numeric * 2)
         THEN (l.r->>'tidiness')::numeric END                          AS tidiness,
    CASE WHEN jsonb_typeof(l.r->'cleanliness') = 'number'
              AND (l.r->>'cleanliness')::numeric BETWEEN 1 AND 5
              AND (l.r->>'cleanliness')::numeric * 2 = trunc((l.r->>'cleanliness')::numeric * 2)
         THEN (l.r->>'cleanliness')::numeric END                       AS cleanliness,
    CASE WHEN jsonb_typeof(l.r->'aesthetics') = 'number'
              AND (l.r->>'aesthetics')::numeric BETWEEN 1 AND 5
              AND (l.r->>'aesthetics')::numeric * 2 = trunc((l.r->>'aesthetics')::numeric * 2)
         THEN (l.r->>'aesthetics')::numeric END                        AS aesthetics,
    nullif(btrim(CASE WHEN jsonb_typeof(l.r->'note') = 'string' THEN l.r->>'note' END), '') AS note,
    l.recorded_at                                                      AS rated_at,
    l.event_id
FROM live l
JOIN bsk.subject s ON s.id = (l.r->>'space')::uuid AND s.type = 'Space';

COMMENT ON VIEW bsk.v_space_rating IS
    'HOME-2 (D18): one row per live space rating — the newest version of each rating id by recorded_at, unless voided. day is a home-zone date; each part is 1-5 in halves or null when left out. Fades and the Home Score are computed in C#.';

-- Keep the view off a full scan of the stream, as 0043 does for exception days.
CREATE INDEX IF NOT EXISTS event_space_rating_idx
    ON bsk.event ((payload->'space_rating'->>'id'), recorded_at)
    WHERE kind = 'measurement' AND jsonb_exists(payload, 'space_rating');

GRANT SELECT ON bsk.v_space, bsk.v_space_rating TO bsk_reader;
