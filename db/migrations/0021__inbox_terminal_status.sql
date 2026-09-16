-- 0021__inbox_terminal_status.sql
-- Inbox attention vs. status (see docs/pilot/inbox-attention-vs-status.md).
--
-- The Inbox encodes ONE axis: attention ("does this need me to look at it?").
-- An item's resolution belongs in its Status field, not in the triage marker.
-- Migration 0015 already resolves an item out of the inbox when its newest triage
-- marker stops being `flagged`; this migration adds the symmetric status rule so
-- that moving a subject's Status to a terminal value ALSO clears it from the inbox
-- — for free, with no parallel `dropped` marker required.
--
-- v_inbox already hides archived subjects (D9). We add the mirror predicate for
-- terminal status (D7), reusing bsk.is_terminal_status (migration 0011). Status is
-- read from bsk_derived.subject_current_source (the live fold from state_change
-- events, not the rebuilt table) so a Drop-as-status-change is reflected the moment
-- it is written, with no `bsk rebuild` lag — the same idiom v_appointment uses (0020).
--
-- Safe for every row: an event capture and a subject with no recorded status both
-- fold to NULL, and is_terminal_status(NULL) is false, so neither is affected. The
-- existing Drop button (which still writes a `dropped` triage marker for now) keeps
-- working through the unchanged `flagged` filter; rerouting Drop to a status change
-- is later follow-up work (see the design note).
--
-- Companion vocab change (app-side, no DDL): the Problem status set gains a terminal
-- `Cancelled` — "this problem is nothing / not worth pursuing" — so Drop-as-status
-- has an honest target that is not `Resolved` ("solved"). Problem is now
-- Open | Working | Resolved | Cancelled (terminal: Resolved, Cancelled). Both words
-- are already in bsk.is_terminal_status, so this migration changes no predicate; the
-- vocabulary itself is validated app-side (D7) in StatusVocabulary.cs / PilotVocab.cs.

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
-- Live folded status for the subject (NULL for an event capture or an unstatused
-- subject); is_terminal_status(NULL) is false, so those rows are unaffected.
LEFT JOIN bsk_derived.subject_current_source sc
       ON lt.item_kind = 'subject' AND sc.subject_id = (lt.item_id)::uuid
WHERE lt.state = 'flagged'
  -- An archived subject is hidden from every default view (D9), the inbox included.
  AND (s.id IS NULL OR NOT bsk.is_archived(s.id))
  -- A resolved subject needs no attention: a terminal Status clears the inbox (D7).
  AND NOT bsk.is_terminal_status(sc.status);

COMMENT ON VIEW bsk.v_inbox IS
    'INBOX-1: items whose newest triage marker is `flagged`, minus archived (D9) and terminal-status (D7) subjects. Attention only; resolution lives in the Status field. See docs/pilot/inbox-attention-vs-status.md.';

GRANT SELECT ON bsk.v_inbox TO bsk_reader;
