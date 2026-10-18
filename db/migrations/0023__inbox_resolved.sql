-- 0023__inbox_resolved.sql
-- The way back out of a resolved Inbox item (see docs/pilot/inbox-attention-vs-status.md).
--
-- v_inbox shows only what needs attention. Once an item is dismissed, promoted or
-- dropped it leaves that view — and a raw note capture, which is an event and not a
-- subject, then had no other surface at all. This view is the mirror image: every
-- item that has ever been triaged and is no longer in the inbox, newest first, so the
-- UI can list it and offer "Back to Inbox" (a fresh `flagged` marker, the re-flag the
-- design note already describes).
--
-- An item leaves the inbox one of two ways, and `resolution` says which:
--   * its newest triage marker stopped being `flagged` — `dismissed`, `promoted`, or
--     the retired `filed` / `dropped` states, passed through as written; or
--   * it is still flagged but is a subject whose Status went terminal (Drop-as-status,
--     0021) — reported as `status`, with the status itself in `subject_status`.
-- `subject_status_terminal` tells the UI a plain re-flag would surface nothing: the
-- subject must be re-opened first, because v_inbox hides terminal-status subjects.
--
-- Archived subjects stay hidden, as in every default view (D9). Status is read from
-- the live fold (bsk_derived.subject_current_source), the same idiom as v_inbox, so a
-- Drop or re-open shows here on the next read with no `bsk rebuild` lag.

CREATE OR REPLACE VIEW bsk.v_inbox_resolved AS
WITH latest_triage AS (
    SELECT DISTINCT ON (e.payload->>'item_id')
        e.payload->>'item_id'   AS item_id,
        e.payload->>'item_kind' AS item_kind,
        e.payload->>'state'     AS state,
        e.id                    AS triage_event_id,
        e.occurred_at           AS triaged_at
    FROM bsk.event e
    WHERE e.kind = 'triage'
    -- Deterministic newest-wins, tie-broken like v_inbox and the status/archive folds.
    ORDER BY e.payload->>'item_id', e.occurred_at DESC, e.recorded_at DESC, e.id DESC
)
SELECT
    (lt.item_id)::uuid  AS item_id,
    lt.item_kind,
    lt.triage_event_id,
    lt.triaged_at,
    CASE WHEN lt.state = 'flagged' THEN 'status' ELSE lt.state END AS resolution,
    -- When it left the inbox: the marker, or the later of flag and terminal status.
    CASE WHEN lt.state = 'flagged'
         THEN greatest(lt.triaged_at, sc.status_occurred_at)
         ELSE lt.triaged_at
    END                 AS resolved_at,
    sc.status           AS subject_status,
    bsk.is_terminal_status(sc.status) AS subject_status_terminal,
    s.urn               AS subject_urn,
    s.type              AS subject_type,
    s.title             AS subject_title,
    ev.kind             AS event_kind,
    a.content           AS event_content
FROM latest_triage lt
LEFT JOIN bsk.subject  s  ON lt.item_kind = 'subject' AND s.id  = (lt.item_id)::uuid
LEFT JOIN bsk.event    ev ON lt.item_kind = 'event'   AND ev.id = (lt.item_id)::uuid
LEFT JOIN bsk.artifact a  ON a.id = ev.artifact_id
LEFT JOIN bsk_derived.subject_current_source sc
       ON lt.item_kind = 'subject' AND sc.subject_id = (lt.item_id)::uuid
WHERE (s.id IS NULL OR NOT bsk.is_archived(s.id))
  -- Exactly the complement of v_inbox's attention rule.
  AND (lt.state <> 'flagged' OR bsk.is_terminal_status(sc.status));

COMMENT ON VIEW bsk.v_inbox_resolved IS
    'Items that were triaged and have left the inbox (dismissed, promoted, or a terminal Status), minus archived subjects (D9). The complement of v_inbox; drives the web Inbox''s Resolved view and Back to Inbox.';

GRANT SELECT ON bsk.v_inbox_resolved TO bsk_reader;
