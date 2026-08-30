-- 0005__reader_and_indexes.sql
-- Make the store queryable (indexes + flattened views) and give Python a
-- read-only door that cannot write (the bsk_reader role). Satisfies invariant 9:
-- bsk is the only write path; every other consumer reads Postgres directly.

-- ---------------------------------------------------------------------------
-- Indexes
-- ---------------------------------------------------------------------------

-- Containment / key-existence queries over jsonb.
CREATE INDEX IF NOT EXISTS event_payload_gin      ON bsk.event   USING gin (payload);
CREATE INDEX IF NOT EXISTS subject_attributes_gin ON bsk.subject USING gin (attributes);

-- Supports the subject_current fold: latest state_change per subject.
CREATE INDEX IF NOT EXISTS event_state_change_subject
    ON bsk.event ((payload->>'subject_id'), occurred_at DESC)
    WHERE kind = 'state_change';

-- Review-due diagnostics. Partial: only subjects that carry a review date.
-- Indexed on the raw text (not a timestamptz cast): text->>timestamptz is only
-- STABLE and cannot appear in an index expression, whereas ISO-8601 UTC text
-- sorts chronologically, so range scans still work. jsonb_exists(...) is used
-- instead of the `?` operator to avoid any driver treating `?` as a placeholder.
CREATE INDEX IF NOT EXISTS subject_next_review_at
    ON bsk.subject ((attributes->>'next_review_at'))
    WHERE jsonb_exists(attributes, 'next_review_at');

-- Relation traversal in both directions.
CREATE INDEX IF NOT EXISTS relation_from ON bsk.relation (from_subject, relation);
CREATE INDEX IF NOT EXISTS relation_to   ON bsk.relation (to_subject, relation);

-- ---------------------------------------------------------------------------
-- Flattened read-only views for consumers (e.g. Python)
-- ---------------------------------------------------------------------------

-- Subjects with common jsonb attributes unpacked into columns.
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
    s.attributes
FROM bsk.subject s;

-- Events with the common state_change payload fields unpacked.
CREATE OR REPLACE VIEW bsk.v_event AS
SELECT
    e.id,
    e.kind,
    e.provenance,
    e.occurred_at,
    e.recorded_at,
    e.source_id,
    e.external_id,
    e.payload->>'subject_id' AS subject_id,
    e.payload->>'status'     AS status,
    e.artifact_id,
    e.derived_from,
    e.payload
FROM bsk.event e;

-- Relations with the endpoint urns and types joined in for readability.
CREATE OR REPLACE VIEW bsk.v_relation AS
SELECT
    r.id,
    r.from_subject,
    f.urn        AS from_urn,
    f.type       AS from_type,
    r.relation,
    r.to_subject,
    t.urn        AS to_urn,
    t.type       AS to_type,
    r.provenance,
    r.created_at
FROM bsk.relation r
JOIN bsk.subject f ON f.id = r.from_subject
JOIN bsk.subject t ON t.id = r.to_subject;

-- The current-status projection, exposed for consumers.
CREATE OR REPLACE VIEW bsk.v_subject_current AS
SELECT
    subject_id,
    urn,
    type,
    title,
    status,
    status_event_id,
    status_occurred_at
FROM bsk_derived.subject_current;

-- ---------------------------------------------------------------------------
-- bsk_reader: SELECT-only role
-- ---------------------------------------------------------------------------
-- Roles are cluster-global, so create idempotently. The password here is a
-- LOCAL DEVELOPMENT credential (matches docker-compose.yml); production must set
-- its own. Membership: point read-only consumers (Python, BI, ad-hoc psql) at
-- this role; never at the owner used by `bsk migrate` / `bsk rebuild`.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bsk_reader') THEN
        CREATE ROLE bsk_reader LOGIN PASSWORD 'bsk_reader';
    END IF;
END
$$;

GRANT USAGE ON SCHEMA bsk, bsk_derived TO bsk_reader;

-- SELECT on all current tables and views in both schemas.
GRANT SELECT ON ALL TABLES IN SCHEMA bsk         TO bsk_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA bsk_derived TO bsk_reader;

-- And on anything added later by the migration owner, so the reader stays whole.
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk         GRANT SELECT ON TABLES TO bsk_reader;
ALTER DEFAULT PRIVILEGES IN SCHEMA bsk_derived GRANT SELECT ON TABLES TO bsk_reader;
