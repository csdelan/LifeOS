-- 0004__subject_current.sql
-- The one derived projection of Stage 1, plus the schema that holds all derived
-- state. Everything in `bsk_derived` is rebuildable from source (`bsk`) at any
-- time by `bsk rebuild`; nothing here is canonical.
--
-- Status convention: a subject's status changes only by appending a
-- `state_change` event whose payload carries the target subject and new status:
--     {"subject_id": "<uuid>", "status": "<text>"}
-- `subject_current` folds those events to the latest status per subject. It is
-- never edited directly (epic invariant 8); the only writer is `bsk rebuild`.

CREATE SCHEMA IF NOT EXISTS bsk_derived;

COMMENT ON SCHEMA bsk_derived IS 'Derived, rebuildable projections. Never canonical; regenerated from bsk by `bsk rebuild`.';

-- The materialized projection: exactly one row per subject.
-- No rebuild timestamp column — derived state must be byte-comparable across
-- rebuilds, so it contains only values folded from source.
CREATE TABLE IF NOT EXISTS bsk_derived.subject_current (
    subject_id         uuid        PRIMARY KEY,
    urn                text        NOT NULL,
    type               text        NOT NULL,
    title              text        NOT NULL,
    status             text,
    status_event_id    uuid,
    status_occurred_at timestamptz
);

COMMENT ON TABLE bsk_derived.subject_current IS 'Derived: current status per subject, folded from state_change events. Rebuilt wholesale; never edited directly.';

-- The recompute definition: what `subject_current` should contain, straight
-- from source. `bsk rebuild` truncates the table and repopulates it from this
-- view; `bsk rebuild --verify` diffs the table against this view to detect drift.
CREATE OR REPLACE VIEW bsk_derived.subject_current_source AS
SELECT
    s.id   AS subject_id,
    s.urn,
    s.type,
    s.title,
    latest.status,
    latest.status_event_id,
    latest.status_occurred_at
FROM bsk.subject s
LEFT JOIN LATERAL (
    SELECT
        e.payload->>'status' AS status,
        e.id                 AS status_event_id,
        e.occurred_at        AS status_occurred_at
    FROM bsk.event e
    WHERE e.kind = 'state_change'
      AND e.payload->>'subject_id' = s.id::text
    -- Deterministic: newest occurrence wins, tie-broken by record time then id.
    ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
    LIMIT 1
) latest ON true;
