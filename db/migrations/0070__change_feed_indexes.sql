-- 0070__change_feed_indexes.sql
-- Code review CR122 (fifth review R17): recent_changes reads a page, not its whole window.
--
-- The change feed (ChangeReader, /mcp's recent_changes) built every event of its window, up to
-- 366 days, worked out which record each was a version of with a regex on every row, folded every
-- record's versions, and only then took its 50. It now reads the log newest first and stops at
-- the page: a version shows when no newer version of its record was recorded, which it looks up
-- by the record, and only the page's rows count their versions.
--
-- bsk.event_record names the record an event is a version of, the rule ChangeReader held in C#:
-- an Oura event's external_id without its last two parts (its hash and what it replaces), or a
-- meal's, fast's, space photo's, space rating's, metric reading's or lab visit's id. It's a plain
-- SQL expression, so the planner inlines it and event_record_idx matches the query's call.
-- event_occurred_at_idx is the feed's order by when it happened, as event_recorded_at_idx is by
-- when it was recorded.
--
-- The log is small enough today that a plain CREATE INDEX is fine inside the migration's
-- transaction (as in 0060).

CREATE OR REPLACE FUNCTION bsk.event_record(source_id text, external_id text, payload jsonb)
RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$
    SELECT CASE
        WHEN source_id = 'oura' AND external_id IS NOT NULL
            THEN 'oura:' || regexp_replace(external_id, ':[^:]*:[^:]*$', '')
        WHEN payload->'meal'->>'id' IS NOT NULL THEN 'meal:' || (payload->'meal'->>'id')
        WHEN payload->'fast'->>'id' IS NOT NULL THEN 'fast:' || (payload->'fast'->>'id')
        WHEN payload->'space_photo'->>'id' IS NOT NULL THEN 'space_photo:' || (payload->'space_photo'->>'id')
        WHEN payload->'space_rating'->>'id' IS NOT NULL THEN 'space_rating:' || (payload->'space_rating'->>'id')
        WHEN payload->'metric_reading'->>'id' IS NOT NULL THEN 'metric_reading:' || (payload->'metric_reading'->>'id')
        WHEN payload->'lab_visit'->>'id' IS NOT NULL THEN 'lab_visit:' || (payload->'lab_visit'->>'id')
    END;
$$;

COMMENT ON FUNCTION bsk.event_record(text, text, jsonb) IS
    'The record an event is a version of (an Oura record, a meal, a fast, a space photo or rating, a metric reading, a lab visit), or null when it stands alone. Its newest version by recorded_at is the record.';

GRANT EXECUTE ON FUNCTION bsk.event_record(text, text, jsonb) TO bsk_reader;

CREATE INDEX IF NOT EXISTS event_record_idx
    ON bsk.event (bsk.event_record(source_id, external_id, payload), recorded_at, id)
    WHERE bsk.event_record(source_id, external_id, payload) IS NOT NULL;

CREATE INDEX IF NOT EXISTS event_occurred_at_idx
    ON bsk.event (occurred_at);
