-- 0062__payload_checks.sql
-- Code review CR35: the event log refuses a payload value its views would fail to cast.
--
-- Several views cast a payload value with no guard: an archive_change's archived to boolean
-- (bsk.is_archived and the subject views), a triage's item_id to uuid (the Inbox views), a habit
-- check-off's occurrence to date (bsk_derived.habit_occurrence_source), a timer's subject_id to uuid
-- (bsk.v_task_timer), and a ledger entry's subject_id to uuid and its dollars and coin_rate to
-- numeric (bsk.v_ledger). The log is append-only, so one malformed event written by any client
-- would make that view raise on every read, for good: nothing can correct or remove the row.
-- These checks refuse such an event when it is written instead.
--
-- Each check passes when its key is absent, as the views do, and accepts what the kernel writes:
-- booleans, Guid.ToString() ids, yyyy-MM-dd dates, numbers, and whole-number amounts.
--
-- NOT VALID: the checks hold for every new event and skip the rows already written. The views
-- read every one of those today, so they cast cleanly; validating them would only add a scan of
-- the log to the deploy.

-- A uuid in its canonical text form; null for null, so an absent key passes a check.
CREATE OR REPLACE FUNCTION bsk.is_uuid_text(value text)
RETURNS boolean
LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
AS $$
    SELECT value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';
$$;

COMMENT ON FUNCTION bsk.is_uuid_text(text) IS
    'True when value is a uuid in canonical text form; null for null, so an absent payload key passes a CHECK (code review CR35).';

-- A real calendar date as yyyy-MM-dd; null for null.
CREATE OR REPLACE FUNCTION bsk.is_iso_date_text(value text)
RETURNS boolean
LANGUAGE sql STABLE STRICT PARALLEL SAFE
AS $$
    SELECT value ~ '^\d{4}-\d{2}-\d{2}$' AND bsk.try_to_date(value) IS NOT NULL;
$$;

COMMENT ON FUNCTION bsk.is_iso_date_text(text) IS
    'True when value is a real date written yyyy-MM-dd; null for null, so an absent payload key passes a CHECK (code review CR35).';

ALTER TABLE bsk.event
    ADD CONSTRAINT event_archive_change_archived_check
        CHECK (kind <> 'archive_change' OR jsonb_typeof(payload->'archived') IN ('boolean', 'null'))
        NOT VALID,
    ADD CONSTRAINT event_triage_item_id_check
        CHECK (kind <> 'triage' OR bsk.is_uuid_text(payload->>'item_id'))
        NOT VALID,
    ADD CONSTRAINT event_activity_occurrence_check
        CHECK (kind <> 'activity' OR bsk.is_iso_date_text(payload->>'occurrence'))
        NOT VALID,
    ADD CONSTRAINT event_timer_subject_id_check
        CHECK (kind <> 'activity' OR NOT jsonb_exists(payload, 'timer') OR bsk.is_uuid_text(payload->>'subject_id'))
        NOT VALID,
    ADD CONSTRAINT event_ledger_subject_id_check
        CHECK (kind <> 'ledger' OR bsk.is_uuid_text(NULLIF(payload->>'subject_id', '')))
        NOT VALID,
    ADD CONSTRAINT event_ledger_dollars_check
        CHECK (kind <> 'ledger' OR jsonb_typeof(payload->'dollars') IN ('number', 'null'))
        NOT VALID,
    ADD CONSTRAINT event_ledger_coin_rate_check
        CHECK (kind <> 'ledger' OR jsonb_typeof(payload->'coin_rate') IN ('number', 'null'))
        NOT VALID,
    -- amounts is an object of whole numbers that fit v_ledger's bigint with room to spare.
    ADD CONSTRAINT event_ledger_amounts_check
        CHECK (kind <> 'ledger' OR (
            jsonb_typeof(payload->'amounts') = 'object'
            AND NOT payload @? '$.amounts.* ? (@.type() != "number" || @.floor() != @ || @ > 1e15 || @ < -1e15)'))
        NOT VALID;
