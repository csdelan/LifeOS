-- 0007__subject_event.sql
-- A design correction, not a feature. The Stage-1 ontology has three relation
-- kinds that are fundamentally event -> subject, not subject -> subject:
--   * concerns   — an event concerns any subject (a journal entry concerns a
--                  Project and a Person; a Project is concerned by many events)
--   * evidences  — an event evidences a Commitment
--   * violates   — an event violates a Commitment
-- `bsk.relation` is a subject -> subject table, yet its CHECK enumerated these
-- event-oriented kinds — so it could name them but structurally could not point
-- them at an event. This migration:
--   1. adds `bsk.subject_event`, a SOURCE link table for the event -> subject edges;
--   2. renames `bsk.relation` -> `bsk.subject_relation` so the two edge tables read
--      as a pair, and narrows it to the subject -> subject kinds it can actually hold;
--   3. drops `promoted_from` from the enum entirely — that fact is already a
--      first-class column (`subject.origin_event_id`); a relation edge would be a
--      second, competing representation of the same thing.
--
-- `bsk.subject_event` is a SOURCE table (human/agent-asserted, provenance-tagged),
-- never rebuilt; the diagnostics read it. `bsk.event`/`bsk.artifact` are untouched
-- and stay append-only — this table references events, never mutates them.

-- ---------------------------------------------------------------------------
-- 1. The event -> subject link table
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS bsk.subject_event (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id   uuid        NOT NULL REFERENCES bsk.event (id),
    subject_id uuid        NOT NULL REFERENCES bsk.subject (id),
    relation   text        NOT NULL
                           CHECK (relation IN ('concerns', 'evidences', 'violates')),
    -- Provenance-tagged like relations: an edge you assert is 'declared'; one an
    -- agent infers is 'derived'; one read off behaviour is 'observed'.
    provenance text        NOT NULL
                           CHECK (provenance IN ('declared', 'observed', 'derived')),
    created_at timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE bsk.subject_event IS 'SOURCE: event -> subject edges (concerns/evidences/violates); provenance-tagged, never rebuilt.';

-- One (event, subject, relation) edge at most once — the same dedupe discipline
-- as the source stream's (source_id, external_id).
CREATE UNIQUE INDEX IF NOT EXISTS subject_event_edge_key
    ON bsk.subject_event (event_id, subject_id, relation);

-- The reverse lookups the diagnostics run: "events concerning subject X",
-- "violating events for Commitment Y".
CREATE INDEX IF NOT EXISTS subject_event_subject_idx
    ON bsk.subject_event (subject_id, relation);

-- ---------------------------------------------------------------------------
-- 2. Narrow `relation` to the subject -> subject kinds, and rename it for symmetry
-- ---------------------------------------------------------------------------
ALTER TABLE bsk.relation RENAME TO subject_relation;

-- Keep the traversal index names in step with the table.
ALTER INDEX bsk.relation_from RENAME TO subject_relation_from;
ALTER INDEX bsk.relation_to   RENAME TO subject_relation_to;

-- Drop the old relation-kind CHECK by its discovered name (the auto-generated
-- name is not relied upon) and re-add the narrowed one. Safe as an unconditional
-- swap: no edge rows exist yet.
DO $$
DECLARE
    check_name text;
BEGIN
    SELECT conname INTO check_name
    FROM pg_constraint
    WHERE conrelid = 'bsk.subject_relation'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ILIKE '%serves%';

    IF check_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE bsk.subject_relation DROP CONSTRAINT %I', check_name);
    END IF;
END
$$;

ALTER TABLE bsk.subject_relation
    ADD CONSTRAINT subject_relation_relation_check
    CHECK (relation IN ('serves', 'results_in', 'supersedes'));

COMMENT ON TABLE bsk.subject_relation IS 'Directed, provenance-tagged edges between subjects (serves/results_in/supersedes).';

-- ---------------------------------------------------------------------------
-- 3. Consumer-facing views, kept symmetric with the tables
-- ---------------------------------------------------------------------------
-- The old v_relation now points at the renamed table; replace it with a
-- consistently named view and add the sibling for the new edge table.
DROP VIEW IF EXISTS bsk.v_relation;

CREATE OR REPLACE VIEW bsk.v_subject_relation AS
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
FROM bsk.subject_relation r
JOIN bsk.subject f ON f.id = r.from_subject
JOIN bsk.subject t ON t.id = r.to_subject;

CREATE OR REPLACE VIEW bsk.v_subject_event AS
SELECT
    se.id,
    se.event_id,
    e.kind       AS event_kind,
    se.relation,
    se.subject_id,
    s.urn        AS subject_urn,
    s.type       AS subject_type,
    se.provenance,
    se.created_at
FROM bsk.subject_event se
JOIN bsk.event   e ON e.id = se.event_id
JOIN bsk.subject s ON s.id = se.subject_id;

-- The reader role gets SELECT on new objects automatically via the default
-- privileges set in 0005; grant explicitly too so the intent is visible here.
GRANT SELECT ON bsk.subject_event, bsk.v_subject_relation, bsk.v_subject_event TO bsk_reader;
