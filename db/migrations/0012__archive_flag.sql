-- 0012__archive_flag.sql
-- D9: a universal archive flag — nothing is ever deleted, only archived (hidden
-- from default views) and restorable. Archive is orthogonal to workflow status,
-- so a status-less type (Identity Statement / Value) can still archive, and an
-- archived Goal keeps whatever status it had.
--
-- Option B (chosen with Chris 2026-09-14): archive/restore is recorded as an
-- append-only event and the "is it archived now?" answer is derived on read —
-- reversible with full history, matching the state_change spine, never a mutable
-- boolean. A new event kind `archive_change` carries {subject_id, archived}, and
-- bsk.is_archived folds the newest one per subject (no archive events -> active).
-- No materialized table and no rebuild change: the fold is a small STABLE
-- predicate, the same idiom as bsk.is_terminal_status / bsk.try_to_date.

-- ---------------------------------------------------------------------------
-- 1. Admit the new event kind
-- ---------------------------------------------------------------------------
-- The kind CHECK is an inline column constraint with an auto-generated name;
-- discover it by its definition (it enumerates 'state_change'), drop it, and
-- re-add the widened set. Adding a kind never invalidates existing rows.
DO $$
DECLARE
    check_name text;
BEGIN
    SELECT conname INTO check_name
    FROM pg_constraint
    WHERE conrelid = 'bsk.event'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ILIKE '%state_change%';

    IF check_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE bsk.event DROP CONSTRAINT %I', check_name);
    END IF;
END
$$;

ALTER TABLE bsk.event
    ADD CONSTRAINT event_kind_check
    CHECK (kind IN (
        'journal', 'note', 'voice', 'idea_session', 'observation',
        'activity', 'measurement', 'interaction', 'state_change', 'archive_change'));

-- ---------------------------------------------------------------------------
-- 2. The derived-on-read archive predicate
-- ---------------------------------------------------------------------------
-- Newest archive_change per subject wins, tie-broken like the status fold
-- (occurred_at, then recorded_at, then id). A subject with no archive_change is
-- active. STABLE (not IMMUTABLE): it reads the event stream.
CREATE OR REPLACE FUNCTION bsk.is_archived(subject uuid)
    RETURNS boolean
    LANGUAGE sql
    STABLE
    PARALLEL SAFE
AS $$
    SELECT coalesce((
        SELECT (e.payload->>'archived')::boolean
        FROM bsk.event e
        WHERE e.kind = 'archive_change'
          AND e.payload->>'subject_id' = subject::text
        ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
        LIMIT 1
    ), false);
$$;

COMMENT ON FUNCTION bsk.is_archived(uuid) IS
    'True when a subject''s newest archive_change event set archived=true (D9). No archive_change events means active. Reversible: a later restore appends archived=false.';

GRANT EXECUTE ON FUNCTION bsk.is_archived(uuid) TO bsk_reader;

-- Supports the fold: newest archive_change per subject.
CREATE INDEX IF NOT EXISTS event_archive_change_subject
    ON bsk.event ((payload->>'subject_id'), occurred_at DESC)
    WHERE kind = 'archive_change';

-- ---------------------------------------------------------------------------
-- 3. Expose the flag to readers
-- ---------------------------------------------------------------------------
-- CREATE OR REPLACE VIEW may only append columns, so `archived` lands at the end
-- (after `statement`, added in 0010). Consumers filter default lists with
-- `WHERE NOT archived`.
CREATE OR REPLACE VIEW bsk.v_subject AS
SELECT
    s.id,
    s.urn,
    s.type,
    s.title,
    s.attributes->>'expected_cadence'               AS expected_cadence,
    (s.attributes->>'next_review_at')::timestamptz   AS next_review_at,
    s.attributes->>'scope'                           AS scope,
    s.origin_event_id,
    s.created_at,
    s.attributes,
    s.attributes->>'statement'                       AS statement,
    bsk.is_archived(s.id)                            AS archived
FROM bsk.subject s;
