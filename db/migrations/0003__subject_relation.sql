-- 0003__subject_relation.sql
-- The subject layer (a single typed-jsonb table) and the relation edges between
-- subjects. This is the SOURCE-side definition; the derived current-status
-- projection is a later migration (M1.4).
--
-- URN scheme (stable, human-referenceable subject id):
--     urn:bsk:<type>:<slug-or-shortid>
-- where <type> is the lowercased subject type and <slug-or-shortid> is a stable
-- identifier for the subject, e.g.
--     urn:bsk:project:life-kernel
--     urn:bsk:person:chris-delaney
--     urn:bsk:task:7f3a9c
--
-- Subject type is deliberately a single table with a typed `attributes` jsonb
-- column: adding or exercising any of the 11 types costs zero DDL. Per-type
-- tables are intentionally avoided until a type stabilizes and earns real
-- constraints. Cross-cutting attributes (e.g. `expected_cadence`,
-- `next_review_at`, and a Constraint's `scope` of capacity|interaction) live in
-- `attributes` rather than as columns.

CREATE TABLE IF NOT EXISTS bsk.subject (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    urn             text        NOT NULL UNIQUE,
    type            text        NOT NULL
                                CHECK (type IN (
                                    'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
                                    'Decision', 'Idea', 'Person', 'Constraint', 'Season')),
    title           text        NOT NULL,
    attributes      jsonb       NOT NULL DEFAULT '{}'::jsonb,
    -- Set only when a subject is promoted from a capture event; NULL otherwise
    -- (invariant 5: the original capture is never mutated into the subject).
    origin_event_id uuid        REFERENCES bsk.event (id),
    created_at      timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE bsk.subject IS 'Single typed-jsonb table for all subject types; source-side definition.';
COMMENT ON COLUMN bsk.subject.origin_event_id IS 'The capture event this subject was promoted from; NULL unless promoted.';

-- Directed edges between subjects.
CREATE TABLE IF NOT EXISTS bsk.relation (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    from_subject uuid        NOT NULL REFERENCES bsk.subject (id),
    relation     text        NOT NULL
                             CHECK (relation IN (
                                 'serves', 'results_in', 'evidences', 'violates',
                                 'concerns', 'supersedes', 'promoted_from')),
    to_subject   uuid        NOT NULL REFERENCES bsk.subject (id),
    provenance   text        NOT NULL
                             CHECK (provenance IN ('declared', 'observed', 'derived')),
    created_at   timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE bsk.relation IS 'Directed, provenance-tagged edges between subjects.';
