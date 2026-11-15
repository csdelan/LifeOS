-- 0067__subject_current_live.sql
-- Code review CR110 (fifth review R3): bsk.v_subject_current reads the live fold.
--
-- v_subject_current read bsk_derived.subject_current, the table only `bsk rebuild` writes, so a
-- status set on the web, by an agent or by a timer's Complete didn't show in the Pilot's Browse,
-- Tasks or detail, nor to a Python or BI reader, until something ran a rebuild. The Pilot ran a
-- full rebuild after each of its own status writes to hide that, which on staging folds every
-- subject again. The API has always read bsk_derived.subject_current_source, the fold straight
-- from the state_change events, and the view now reads it too, with the same columns.
--
-- The table stays: `bsk rebuild` still writes it and `bsk rebuild --verify` still checks it against
-- the fold, as an integrity check. Nothing reads it for status any more.

CREATE OR REPLACE VIEW bsk.v_subject_current AS
 SELECT subject_id,
    urn,
    type,
    title,
    status,
    status_event_id,
    status_occurred_at
   FROM bsk_derived.subject_current_source;

COMMENT ON VIEW bsk.v_subject_current IS
    'Each subject''s current status, folded live from its state_change events (0067). No rebuild needed.';
