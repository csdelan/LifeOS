-- 0026__reward_due.sql
-- REWARD-1 / REWARD-2, decision D11 (rewards plan R2): which awards the record says
-- are due. Reconcile (LifeOs.Application.Rewards.RewardService) compares these rows
-- with what the ledger has paid per award key, then appends an `earn` for each due
-- key not yet paid and a `reversal` for each paid key no longer due. SQL decides
-- *which* awards are due, as a pure function of the record; pricing stays in C#
-- (BountyDefaults / attributes.bounty), so the defaults live in one place.
--
-- Status is read from the live fold bsk_derived.subject_current_source, never the
-- materialized subject_current, which lags until `bsk rebuild`. The earning types
-- here must match LifeOs.Domain.BountyTriggers; a test keeps the two in step. Habit
-- rows (day, streak, established) arrive with rewards plan R3.

-- ---------------------------------------------------------------------------
-- 1. Every award the record supports, whatever the start line
-- ---------------------------------------------------------------------------
-- One row per award key. triggered_at is when the fact that makes it due happened
-- (for `completed`, the latest status change). Reconcile reads this, rather than
-- v_reward_due, only to open the wallet at the first trigger it pays.
CREATE OR REPLACE VIEW bsk.v_reward_trigger AS
SELECT
    c.subject_id,
    c.type                              AS subject_type,
    'completed:' || c.subject_id::text  AS award_key,
    'completed'                         AS trigger,
    ARRAY[c.status_event_id]            AS source_event_ids,
    c.status_occurred_at                AS triggered_at
FROM bsk_derived.subject_current_source c
WHERE c.type IN ('Task', 'Project', 'Goal')
  AND lower(btrim(c.status)) = 'completed';

COMMENT ON VIEW bsk.v_reward_trigger IS
    'REWARD-1/2 (D11): every award the record supports, one row per award key, with the events that make it due. Not yet filtered by the wallet''s start line; see v_reward_due.';

-- ---------------------------------------------------------------------------
-- 2. What is due: the triggers on or after the wallet's opening
-- ---------------------------------------------------------------------------
-- Before the wallet opens nothing is due, and nothing that happened before the
-- opening ever pays.
CREATE OR REPLACE VIEW bsk.v_reward_due AS
SELECT t.*
FROM bsk.v_reward_trigger t
WHERE t.triggered_at >= bsk.ledger_opened_at();

COMMENT ON VIEW bsk.v_reward_due IS
    'REWARD-1/2 (D11): the awards due right now — v_reward_trigger rows on or after bsk.ledger_opened_at(). Reconcile earns each due key the ledger has not paid and reverses each paid key that is no longer due.';

GRANT SELECT ON bsk.v_reward_trigger TO bsk_reader;
GRANT SELECT ON bsk.v_reward_due TO bsk_reader;
