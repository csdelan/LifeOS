-- 0056__event_indexes.sql
-- Code review CR6: indexes for the event-log reads that scanned the whole log.
--
--   * Habit check-offs. bsk_derived.habit_occurrence_source (0049) looks up, for each habit and
--     day, the newest `activity` event for that subject and occurrence. With no index that was a
--     scan of every activity event per habit-day, and the habit, rewards and Health XP reads all
--     go through it. The key matches the lookup's filter and its ORDER BY, so the newest one is
--     the first row read.
--   * recorded_at. The change feed (recent_changes, ChangeReader) reads "recorded since" and
--     pages newest first by recorded_at.
--   * (kind, occurred_at). Reads of one kind over a time range, newest first.
--
-- The log is small enough today that a plain CREATE INDEX (which holds writes for the moment it
-- takes) is fine inside the migration's transaction.

CREATE INDEX IF NOT EXISTS event_activity_occurrence_idx
    ON bsk.event ((payload->>'subject_id'), (payload->>'occurrence'), occurred_at DESC, recorded_at DESC, id DESC)
    WHERE kind = 'activity';

CREATE INDEX IF NOT EXISTS event_recorded_at_idx
    ON bsk.event (recorded_at);

CREATE INDEX IF NOT EXISTS event_kind_occurred_idx
    ON bsk.event (kind, occurred_at);
