-- 0041__recurrence_text_repair.sql
-- D8 / TASKS-3 (recurring-tasks plan §8.1, RT1): a recurrence stored as text becomes the
-- object bsk.recurrence_occurrences reads.
--
-- Until now the web sent a recurrence as a plain string attribute through POST /api/subjects
-- (and `bsk new --attr recurrence=…` did the same), so attributes.recurrence held text such as
-- '{"freq": "daily"}' or the web's own '{"kind":"weekly","weekdays":[3]}'. The expander reads
-- recur->>'freq' and found nothing, so those habits had no scheduled days: an unlogged day was
-- never missed. From this release every door checks a recurrence and stores the object
-- (Recurrence.Canonicalize), and this migration repairs what is already stored.
--
-- bsk.recurrence_from_text(text) reads the shapes that reached the store as text:
--   * the kernel's own object as text: '{"freq":"daily"}' (kept as it is);
--   * the web's shape: {"kind":"daily"}, {"kind":"weekly","weekdays":[0..6]} (an empty list
--     meant every day), {"kind":"interval","intervalDays":n} (every n days from the start);
--   * the mock's words: 'daily', 'weekdays' (Monday to Friday), 'weekly' (Sundays).
-- Anything else gives NULL and is left untouched.

CREATE OR REPLACE FUNCTION bsk.recurrence_from_text(t text)
    RETURNS jsonb
    LANGUAGE plpgsql
    IMMUTABLE
    PARALLEL SAFE
AS $$
DECLARE
    j     jsonb;
    names text[] := ARRAY['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'];
    days  jsonb;
    every int;
BEGIN
    IF t IS NULL THEN
        RETURN NULL;
    END IF;

    t := btrim(t);
    IF t = 'daily' THEN
        RETURN '{"freq":"daily"}'::jsonb;
    ELSIF t = 'weekdays' THEN
        RETURN '{"freq":"weekly","on":["mon","tue","wed","thu","fri"]}'::jsonb;
    ELSIF t = 'weekly' THEN
        RETURN '{"freq":"weekly","on":["sun"]}'::jsonb;
    END IF;

    BEGIN
        j := t::jsonb;
    EXCEPTION WHEN others THEN
        RETURN NULL;
    END;

    IF jsonb_typeof(j) IS DISTINCT FROM 'object' THEN
        RETURN NULL;
    END IF;

    IF j ? 'freq' THEN
        RETURN j;
    END IF;

    IF j->>'kind' = 'daily' THEN
        RETURN '{"freq":"daily"}'::jsonb;
    ELSIF j->>'kind' = 'weekly' THEN
        IF jsonb_typeof(j->'weekdays') = 'array' THEN
            SELECT jsonb_agg(names[d + 1] ORDER BY d)
            INTO days
            FROM (
                SELECT DISTINCT x::int AS d
                FROM jsonb_array_elements_text(j->'weekdays') x
                WHERE x ~ '^[0-6]$'
            ) w;
        END IF;
        -- The web read an empty or missing list as every day.
        IF days IS NULL THEN
            RETURN '{"freq":"daily"}'::jsonb;
        END IF;
        RETURN jsonb_build_object('freq', 'weekly', 'on', days);
    ELSIF j->>'kind' = 'interval' THEN
        IF (j->>'intervalDays') ~ '^[0-9]+$' THEN
            every := greatest(1, (j->>'intervalDays')::int);
        ELSE
            every := 1;
        END IF;
        RETURN jsonb_build_object('freq', 'interval', 'every', every, 'unit', 'days');
    END IF;

    RETURN NULL;
END;
$$;

COMMENT ON FUNCTION bsk.recurrence_from_text(text) IS
    'D8: the recurrence object for a recurrence that reached attributes.recurrence as text (the kernel shape as text, the web''s {kind} shape, or the mock''s words), or NULL. Used once by migration 0041 to repair them.';

UPDATE bsk.subject s
SET attributes = jsonb_set(s.attributes, '{recurrence}', r.fixed)
FROM (
    SELECT id, bsk.recurrence_from_text(attributes->>'recurrence') AS fixed
    FROM bsk.subject
    WHERE jsonb_typeof(attributes->'recurrence') = 'string'
) r
WHERE s.id = r.id
  AND r.fixed IS NOT NULL;
