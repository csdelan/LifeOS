-- 0002__source_tables.sql
-- The source layer: immutable, provenance-tagged, bitemporal, deduplicated.
--
-- `artifact` holds raw captured content (text only in Stage 1). `event` is the
-- append-only stream; events reference their raw content in `artifact` by id.
-- Both tables are source-of-truth and are never mutated: UPDATE and DELETE are
-- denied at the database level by trigger (epic invariants 3 and 5).

-- Raw captured content. Immutable once written ("never overwritten").
CREATE TABLE IF NOT EXISTS bsk.artifact (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    content     text        NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE bsk.artifact IS 'Raw captured content (text-only in Stage 1); immutable, referenced by events.';

-- The append-only event stream.
CREATE TABLE IF NOT EXISTS bsk.event (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    kind         text        NOT NULL
                             CHECK (kind IN (
                                 'journal', 'note', 'voice', 'idea_session', 'observation',
                                 'activity', 'measurement', 'interaction', 'state_change')),
    -- Every fact carries provenance (invariant 4).
    provenance   text        NOT NULL
                             CHECK (provenance IN ('declared', 'observed', 'derived')),
    -- Bitemporality (invariant 6): when it happened vs. when we recorded it.
    occurred_at  timestamptz NOT NULL,
    recorded_at  timestamptz NOT NULL DEFAULT now(),
    -- Idempotency (invariant 7): the dedupe_key pattern from GlobalInboxService.
    source_id    text        NOT NULL,
    external_id  text,
    payload      jsonb       NOT NULL DEFAULT '{}'::jsonb,
    -- Derived facts reference the events they came from (invariant 4).
    derived_from uuid[]      NOT NULL DEFAULT '{}'::uuid[],
    artifact_id  uuid        REFERENCES bsk.artifact (id),
    -- A derived event must cite at least one source event. cardinality() returns
    -- 0 for an empty array (array_length returns NULL, which would pass the CHECK).
    CONSTRAINT event_derived_has_sources
        CHECK (provenance <> 'derived' OR cardinality(derived_from) >= 1)
);

COMMENT ON TABLE bsk.event IS 'Append-only, provenance-tagged, bitemporal event stream. Never updated or deleted.';

-- Idempotency: a given (source_id, external_id) may appear at most once.
-- Partial, because events captured directly (no external_id) are not deduplicated.
CREATE UNIQUE INDEX IF NOT EXISTS event_source_external_key
    ON bsk.event (source_id, external_id)
    WHERE external_id IS NOT NULL;

-- Append-only enforcement. A single function raises on any attempt to mutate a
-- source row; it is wired to both UPDATE and DELETE on the source tables.
CREATE OR REPLACE FUNCTION bsk.deny_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Table %.% is append-only; % is not permitted',
        TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

CREATE OR REPLACE TRIGGER event_deny_update
    BEFORE UPDATE ON bsk.event
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

CREATE OR REPLACE TRIGGER event_deny_delete
    BEFORE DELETE ON bsk.event
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

CREATE OR REPLACE TRIGGER artifact_deny_update
    BEFORE UPDATE ON bsk.artifact
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

CREATE OR REPLACE TRIGGER artifact_deny_delete
    BEFORE DELETE ON bsk.artifact
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
