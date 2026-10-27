-- 0045__space_photos.sql
-- HOME-4, D18 (environment plan E6, §8): photos of each space, from a baseline.
--
--   * A photo is Chris's own `observation` event (declared) with a `concerns` edge to the
--     space. The payload has one key:
--       space_photo  {id, space, shot, day, photo?}
--     `shot` names the spot it was taken from (a space's shots are its attributes.shots;
--     "main" when left out). `day` is a home-zone date.
--   * The first version is the event that stores the bytes, so its artifact_id is the photo
--     and its payload has no `photo`. A later version (a new day or shot) carries the photo
--     as `photo` and sets no artifact_id, since one event already owns the artifact, as the
--     Food Log's versions do. When the same bytes were stored before, the first version
--     carries `photo` too.
--   * The NEWEST version per photo id WINS, by recorded_at (then id), as ratings do. Remove
--     writes {id, voided: true}; the bytes stay in the record.
--   * A space's shots and their baselines are attributes.shots, an array of
--     {id, name?, baseline?}, written only by SpaceService. v_space now reads it as `shots`
--     ('[]' when missing or not an array); C# works out the default shot and which photo is
--     each shot's baseline.
--
-- Photos never feed the Home Score, the level or the ledger.

-- ---------------------------------------------------------------------------
-- 1. Spaces, with their shots
-- ---------------------------------------------------------------------------
-- As 0044, with `shots` added at the end.
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
    s.created_at,
    CASE WHEN jsonb_typeof(s.attributes->'shots') = 'array'
         THEN s.attributes->'shots' ELSE '[]'::jsonb END               AS shots
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
    'HOME-1, HOME-4 (D18): one row per Space subject — its name, weight (1-10) and pace (1-5), each 3 when missing or malformed, when it was archived (null while it is not), and its shots (attributes.shots, [] when missing). The Home Score is computed in C# from this and bsk.v_space_rating.';

-- ---------------------------------------------------------------------------
-- 2. Photos
-- ---------------------------------------------------------------------------
-- A photo of a subject that isn't a Space, with no day, or with no photo drops out. added_at
-- is when its first version was written; changed_at when its newest was.
CREATE OR REPLACE VIEW bsk.v_space_photo AS
WITH version AS (
    SELECT
        e.id                                                           AS event_id,
        e.payload->'space_photo'                                       AS p,
        e.artifact_id,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY e.payload->'space_photo'->>'id'
            ORDER BY e.recorded_at DESC, e.id DESC)                    AS rn,
        min(e.recorded_at) OVER (
            PARTITION BY e.payload->'space_photo'->>'id')              AS added_at
    FROM bsk.event e
    WHERE e.kind = 'observation'
      AND jsonb_exists(e.payload, 'space_photo')
      AND jsonb_typeof(e.payload->'space_photo') = 'object'
      AND e.payload->'space_photo'->>'id' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
),
live AS (
    SELECT
        v.*,
        CASE WHEN v.p->>'photo' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
             THEN (v.p->>'photo')::uuid ELSE v.artifact_id END        AS photo
    FROM version v
    WHERE v.rn = 1
      AND coalesce(v.p->>'voided', 'false') <> 'true'
      AND v.p->>'space' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
      AND bsk.try_to_date(v.p->>'day') IS NOT NULL
)
SELECT
    (l.p->>'id')::uuid                                                 AS id,
    s.id                                                               AS space_id,
    coalesce(nullif(btrim(CASE WHEN jsonb_typeof(l.p->'shot') = 'string'
                               THEN l.p->>'shot' END), ''), 'main')    AS shot_id,
    bsk.try_to_date(l.p->>'day')                                       AS day,
    a.id                                                               AS artifact_id,
    l.added_at,
    l.recorded_at                                                      AS changed_at,
    l.event_id
FROM live l
JOIN bsk.subject s ON s.id = (l.p->>'space')::uuid AND s.type = 'Space'
JOIN bsk.artifact a ON a.id = l.photo;

COMMENT ON VIEW bsk.v_space_photo IS
    'HOME-4 (D18): one row per live photo of a space — the newest version of each photo id by recorded_at, unless voided. shot_id is the spot it was taken from (main when left out); day is a home-zone date; artifact_id is the photo, served by /api/artifacts/{id}/content. Photos never feed the Home Score.';

-- Keep the view off a full scan of the stream, as 0044 does for ratings.
CREATE INDEX IF NOT EXISTS event_space_photo_idx
    ON bsk.event ((payload->'space_photo'->>'id'), recorded_at)
    WHERE kind = 'observation' AND jsonb_exists(payload, 'space_photo');

GRANT SELECT ON bsk.v_space, bsk.v_space_photo TO bsk_reader;
