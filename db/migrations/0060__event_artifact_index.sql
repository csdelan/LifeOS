-- 0060__event_artifact_index.sql
-- Code review CR33: an index on the event that carries each stored file.
--
-- bsk.v_artifact finds, for every artifact, the first event that carries it
-- (WHERE ev.artifact_id = a.id ORDER BY ev.recorded_at, ev.id LIMIT 1). bsk.event.artifact_id had
-- no index, so each artifact the view returned was a scan of the whole event log: the Files
-- section, the Inbox's attachments and every /api/artifacts/{id} request grew with the log. The
-- key matches the lookup's filter and its ORDER BY, so the first event is the first row read.
-- Most events carry no file, so only those that do are indexed.
--
-- The log is small enough today that a plain CREATE INDEX is fine inside the migration's
-- transaction (as in 0056).

CREATE INDEX IF NOT EXISTS event_artifact_idx
    ON bsk.event (artifact_id, recorded_at, id)
    WHERE artifact_id IS NOT NULL;
