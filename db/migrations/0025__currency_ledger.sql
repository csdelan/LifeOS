-- 0025__currency_ledger.sql
-- REWARD-1 / REWARD-3 / REWARD-5, decision D11: rewards are an append-only ledger.
-- Every earn, reversal and redemption is a `ledger` event whose payload carries the
-- signed amount per currency:
--
--   {entry: open | earn | reversal | redeem,
--    amounts: {currency: n, ...},        -- earn > 0; reversal / redeem < 0
--    award_key, trigger, subject_id,     -- what earned it (earn / reversal)
--    note, dollars, coin_rate}           -- a redemption's comment and, for coins
--                                           moved to savings, the money and the rate
--
-- Earns and reversals are `derived` and cite the event that caused them in
-- derived_from; redemptions are `declared`. `open` (no amounts) is the start line:
-- nothing that happened before it ever pays. Balances and history are folded here,
-- read-only; nothing else about a wallet is stored. Currency keys are vocabulary
-- data validated app-side (LifeOs.Domain.Currencies), as D7 does for statuses, so a
-- new currency needs no migration. See docs/pilot/rewards-plan.md.

-- ---------------------------------------------------------------------------
-- 1. Admit the ledger event kind
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
        'activity', 'measurement', 'interaction', 'state_change', 'archive_change', 'triage',
        'ledger'));

-- Wallet and history reads scan the ledger by time; reconcile (rewards plan R2)
-- looks entries up by award key.
CREATE INDEX IF NOT EXISTS event_ledger_occurred_idx
    ON bsk.event (occurred_at)
    WHERE kind = 'ledger';

CREATE INDEX IF NOT EXISTS event_ledger_award_key_idx
    ON bsk.event ((payload->>'award_key'))
    WHERE kind = 'ledger';

-- ---------------------------------------------------------------------------
-- 2. The ledger: one row per entry x currency
-- ---------------------------------------------------------------------------
-- An entry with no amounts (the `open` start line) has no rows here; read it with
-- bsk.ledger_opened_at(). Non-numeric amounts are skipped rather than failing the
-- whole view.
CREATE OR REPLACE VIEW bsk.v_ledger AS
SELECT
    e.id                                         AS event_id,
    e.occurred_at,
    e.recorded_at,
    e.provenance,
    e.source_id,
    e.payload->>'entry'                          AS entry,
    a.key                                        AS currency,
    (a.value #>> '{}')::numeric::bigint          AS amount,
    e.payload->>'award_key'                      AS award_key,
    e.payload->>'trigger'                        AS trigger,
    s.id                                         AS subject_id,
    s.urn                                        AS subject_urn,
    s.type                                       AS subject_type,
    s.title                                      AS subject_title,
    e.payload->>'note'                           AS note,
    (e.payload->>'dollars')::numeric             AS dollars,
    (e.payload->>'coin_rate')::numeric           AS coin_rate,
    e.derived_from
FROM bsk.event e
CROSS JOIN LATERAL jsonb_each(
    CASE WHEN jsonb_typeof(e.payload->'amounts') = 'object'
         THEN e.payload->'amounts'
         ELSE '{}'::jsonb
    END) a
LEFT JOIN bsk.subject s ON s.id = nullif(e.payload->>'subject_id', '')::uuid
WHERE e.kind = 'ledger'
  AND jsonb_typeof(a.value) = 'number';

COMMENT ON VIEW bsk.v_ledger IS
    'REWARD-1/3/5 (D11): every ledger entry, one row per currency, with the item that earned it or the Reward it bought. Signed amounts: earn > 0, reversal and redeem < 0.';

-- ---------------------------------------------------------------------------
-- 3. The wallet: balances per currency
-- ---------------------------------------------------------------------------
-- earned / reversed / spent are positive totals; balance is the signed sum of every
-- entry, so a later entry kind (a penalty) counts without changing this view. A
-- currency never touched has no row; readers show it as zero.
CREATE OR REPLACE VIEW bsk.v_wallet AS
SELECT
    l.currency,
    coalesce(sum(l.amount) FILTER (WHERE l.entry = 'earn'), 0)::bigint      AS earned,
    coalesce(-sum(l.amount) FILTER (WHERE l.entry = 'reversal'), 0)::bigint AS reversed,
    coalesce(-sum(l.amount) FILTER (WHERE l.entry = 'redeem'), 0)::bigint   AS spent,
    sum(l.amount)::bigint                                                   AS balance,
    max(l.occurred_at)                                                      AS last_changed_at
FROM bsk.v_ledger l
GROUP BY l.currency;

COMMENT ON VIEW bsk.v_wallet IS
    'REWARD-1/3 (D11): the balance of each currency touched by the ledger, with earned, reversed and spent totals.';

-- ---------------------------------------------------------------------------
-- 4. The start line
-- ---------------------------------------------------------------------------
-- When the wallet was opened: the earliest `open` entry, or NULL before launch.
-- Awards are only due for what happened on or after it (rewards plan R2).
CREATE OR REPLACE FUNCTION bsk.ledger_opened_at()
    RETURNS timestamptz
    LANGUAGE sql
    STABLE
    PARALLEL SAFE
AS $$
    SELECT min(e.occurred_at)
    FROM bsk.event e
    WHERE e.kind = 'ledger'
      AND e.payload->>'entry' = 'open';
$$;

GRANT SELECT ON bsk.v_ledger TO bsk_reader;
GRANT SELECT ON bsk.v_wallet TO bsk_reader;
GRANT EXECUTE ON FUNCTION bsk.ledger_opened_at() TO bsk_reader;
