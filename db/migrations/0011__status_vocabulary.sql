-- 0011__status_vocabulary.sql
-- D7: per-type status vocabularies + terminal classification.
--
-- Each status-bearing subject type now has its own documented status set, moved
-- only by state_change events (invariant 8). The vocabulary itself is validated
-- app-side (the Pilot UI offers each type's set; the kernel does not add DB enums
-- for it), keeping the single typed-jsonb subject table free of per-type DDL. What
-- the kernel *does* own is the terminal predicate the diagnostics rest on:
-- bsk.is_terminal_status (migration 0008), which decides "active vs finished".
--
-- The authoritative per-type status map (mirrored in
-- src/LifeOs.Domain/StatusVocabulary.cs):
--
--   Goal         developing | Active | Completed | Abandoned      terminal: Completed, Abandoned
--   Project      developing | Active | Completed | Abandoned      terminal: Completed, Abandoned
--   Task         Not started | In progress | Waiting |
--                Completed | Cancelled                            terminal: Completed, Cancelled
--   Commitment   Open | Fulfilled | Missed | Cancelled            terminal: Fulfilled, Missed, Cancelled
--   Decision     Open | Implementing | Cancelled | Closed         terminal: Cancelled, Closed
--   Problem      Open | Working | Resolved                        terminal: Resolved
--   Appointment  Scheduled | Completed | Cancelled | Missed       terminal: Completed, Cancelled, Missed
--   Idea         New | Promoted | Rejected                        terminal: Promoted, Rejected
--
-- Identity Statement (Value) has no status at all — it uses the archive flag (D9)
-- instead. Area / Person / Constraint / Season are status-less in the Pilot. Habit
-- and Review earn their own status maps when their types land (plan phases 9, E);
-- Appointment is listed now (D7 fixes its vocabulary) though the type is created in
-- phase 12.
--
-- Terminal words new in this migration, folding the per-type terminal sets above
-- into the flat predicate: fulfilled, missed, promoted, rejected. (Statuses do not
-- collide across types — no type uses any of these as a non-terminal state — so a
-- single word-set predicate stays correct; it is the design 0008 chose and this
-- migration extends it exactly as 0008 anticipated.)

CREATE OR REPLACE FUNCTION bsk.is_terminal_status(status text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    PARALLEL SAFE
AS $$
    SELECT lower(trim(coalesce(status, ''))) IN (
        -- Original set (0008).
        'done', 'completed', 'resolved', 'closed', 'cancelled',
        'abandoned', 'dropped', 'archived', 'superseded',
        -- Added for the per-type vocabularies (D7): Commitment Fulfilled/Missed,
        -- Appointment Missed, Idea Promoted/Rejected.
        'fulfilled', 'missed', 'promoted', 'rejected'
    );
$$;

COMMENT ON FUNCTION bsk.is_terminal_status(text) IS
    'True when a status string means the subject is finished/dead (the union of every type''s terminal statuses — D7). NULL status is active, not terminal.';

-- Function ownership carries the existing grant; restate it so the reader''s
-- "is this still active?" queries keep working after the replace.
GRANT EXECUTE ON FUNCTION bsk.is_terminal_status(text) TO bsk_reader;
