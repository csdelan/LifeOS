-- 0015__triage_marker.sql
-- INBOX-1: the Inbox is a source-agnostic triage queue, not a capture log.
-- Membership is *asserted* ("this needs a decision"), not inferred from an item's
-- kind and the absence of a promote/relate. That is the seam a future email-reading
-- agent plugs into: it flags an item; it need not be a `bsk capture`.
--
-- The triage marker mirrors the D9 archive flag (Option A, chosen with Chris): an
-- append-only `triage` event carries {item_kind, item_id, state} for a subject OR an
-- event (an event capture is immutable, so the flag cannot live on its row), and the
-- newest marker per item wins. state ∈ flagged | dropped | filed:
--   * flagged — in the inbox, awaiting a decision
--   * dropped — "I looked, it's nothing" (the outcome pure inference could not express)
--   * filed   — retained as reference, resolved out (INBOX-4)
-- Promote resolves too, but that is wired into the promote path later (Phase 7);
-- relating / tagging deliberately do NOT resolve (INBOX-4) — they organize only.
--
-- `v_inbox` = items whose newest marker is `flagged`. This runs ALONGSIDE the
-- Pilot's existing inbox query; the UI switches over on Chris's schedule.

-- ---------------------------------------------------------------------------
-- 1. Admit the triage event kind
-- ---------------------------------------------------------------------------
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
        'activity', 'measurement', 'interaction', 'state_change', 'archive_change', 'triage'));

-- Supports the newest-marker-per-item fold.
CREATE INDEX IF NOT EXISTS event_triage_item
    ON bsk.event ((payload->>'item_id'), occurred_at DESC)
    WHERE kind = 'triage';

-- ---------------------------------------------------------------------------
-- 2. The inbox projection: flagged and not resolved
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_inbox AS
WITH latest_triage AS (
    SELECT DISTINCT ON (e.payload->>'item_id')
        e.payload->>'item_id'   AS item_id,
        e.payload->>'item_kind' AS item_kind,
        e.payload->>'state'     AS state,
        e.id                    AS triage_event_id,
        e.occurred_at           AS triaged_at
    FROM bsk.event e
    WHERE e.kind = 'triage'
    -- Deterministic newest-wins, tie-broken like the status/archive folds.
    ORDER BY e.payload->>'item_id', e.occurred_at DESC, e.recorded_at DESC, e.id DESC
)
SELECT
    (lt.item_id)::uuid  AS item_id,
    lt.item_kind,
    lt.triage_event_id,
    lt.triaged_at,
    s.urn               AS subject_urn,
    s.type              AS subject_type,
    s.title             AS subject_title,
    ev.kind             AS event_kind,
    a.content           AS event_content
FROM latest_triage lt
LEFT JOIN bsk.subject  s  ON lt.item_kind = 'subject' AND s.id  = (lt.item_id)::uuid
LEFT JOIN bsk.event    ev ON lt.item_kind = 'event'   AND ev.id = (lt.item_id)::uuid
LEFT JOIN bsk.artifact a  ON a.id = ev.artifact_id
WHERE lt.state = 'flagged'
  -- An archived subject is hidden from every default view (D9), the inbox included.
  AND (s.id IS NULL OR NOT bsk.is_archived(s.id));

GRANT SELECT ON bsk.v_inbox TO bsk_reader;
