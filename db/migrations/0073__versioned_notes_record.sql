-- 0073__versioned_notes_record.sql
-- Code review CR136 (nightly review 2026-11-17): exception days and workout notes are records.
--
-- bsk.event_record names the record an event is a version of, and the change feed (ChangeReader,
-- /mcp's recent_changes) shows a version only when no newer one of its record was recorded. An
-- exception day (REWARD-8, newest per day by recorded_at, v_exception_day) and a workout's note
-- (newest per workout, v_workout) are kept as versions too, but weren't named, so the feed showed
-- every comment a day was ever marked with, and its unmarking, as changes of their own. They are
-- named now: an exception day by its day, a note by its workout. VersionedRecordTests holds every
-- newest-version view's payload key to this function.
--
-- Changing the function changes what event_record_idx holds, so the index is rebuilt (AGENTS.md).
-- The log is small enough that a plain REINDEX inside the migration's transaction is fine.

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
        WHEN payload->'exception_day'->>'day' IS NOT NULL THEN 'exception_day:' || (payload->'exception_day'->>'day')
        WHEN payload->'workout_note'->>'workout' IS NOT NULL THEN 'workout_note:' || (payload->'workout_note'->>'workout')
    END;
$$;

COMMENT ON FUNCTION bsk.event_record(text, text, jsonb) IS
    'The record an event is a version of (an Oura record, a meal, a fast, a space photo or rating, a metric reading, a lab visit, an exception day, a workout''s note), or null when it stands alone. Its newest version by recorded_at is the record.';

REINDEX INDEX bsk.event_record_idx;
