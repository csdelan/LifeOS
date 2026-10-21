-- 0032__award_credit.sql
-- REWARD-6 / REWARD-7, D12 (xp-domains plan X3, §3.4): what each paid award credits, and to
-- which day — the read every life-domain number is folded from.
--
-- A domain's day holds what the ledger pays now, on the day it happened:
--
--   * every award key whose net in a currency is positive counts, on the credited day of
--     its latest `earn` (payload `credited_on`, migration 0030)
--   * a reversal takes the award off its day (its net falls to zero, so it has no row)
--   * earning the key again files it on the new earn's credited day
--
-- So domain days follow the record as it stands, unlike History, which files entries by
-- when they were written. Each row carries two figures:
--
--   base   what the earn would have paid without the life-domain multiplier: its payload
--          `base` for the currency when present (stamped from phase X5 when a multiplier
--          is above 1.0), else its `amounts`. Success rules read this.
--   paid   what the ledger pays now: the key's net. Levels read this.
--
-- credited_on is NULL for earns written before 0030; the reader files those on their
-- occurred_at's date in the home zone (Rewards:HomeTimeZone), as it does for due awards.
--
-- Every currency is listed; readers pick the life-domain ones. Redeems carry no award key
-- and `open` carries no amounts, so neither appears. Rank-up awards (phase X4) pay Gems,
-- not domain XP, so they never feed a domain back into itself.

CREATE OR REPLACE VIEW bsk.v_award_credit AS
WITH net AS (
    SELECT l.award_key, l.currency, sum(l.amount)::bigint AS paid
    FROM bsk.v_ledger l
    WHERE l.award_key IS NOT NULL
    GROUP BY l.award_key, l.currency
    HAVING sum(l.amount) > 0
),
latest_earn AS (
    SELECT DISTINCT ON (e.payload->>'award_key')
        e.payload->>'award_key'                   AS award_key,
        e.id                                      AS event_id,
        e.occurred_at,
        bsk.try_to_date(e.payload->>'credited_on') AS credited_on,
        CASE WHEN jsonb_typeof(e.payload->'amounts') = 'object' THEN e.payload->'amounts' END AS amounts,
        CASE WHEN jsonb_typeof(e.payload->'base') = 'object' THEN e.payload->'base' END       AS base
    FROM bsk.event e
    WHERE e.kind = 'ledger'
      AND e.payload->>'entry' = 'earn'
      AND e.payload->>'award_key' IS NOT NULL
    ORDER BY e.payload->>'award_key', e.occurred_at DESC, e.recorded_at DESC, e.id DESC
)
SELECT
    n.award_key,
    n.currency,
    coalesce(
        CASE WHEN jsonb_typeof(le.base->n.currency) = 'number' THEN (le.base->>n.currency)::numeric END,
        CASE WHEN jsonb_typeof(le.amounts->n.currency) = 'number' THEN (le.amounts->>n.currency)::numeric END,
        n.paid)::bigint                           AS base,
    n.paid,
    le.credited_on,
    le.occurred_at,
    le.event_id
FROM net n
JOIN latest_earn le ON le.award_key = n.award_key;

COMMENT ON VIEW bsk.v_award_credit IS
    'REWARD-6/7 (D12): one row per currently paid award key x currency — base (before any life-domain multiplier), paid (the key''s net), the latest earn''s credited_on (NULL before 0030), its occurred_at and event. A reversed award has no row; re-earning files it on the new earn''s day. Life-domain days total this per currency per credited day.';

GRANT SELECT ON bsk.v_award_credit TO bsk_reader;
