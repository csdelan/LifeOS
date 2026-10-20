-- 0028__reward_catalog.sql
-- REWARD-3 (rewards plan R4): the reward catalog. A Reward is the 15th subject type —
-- something Chris redeems currency for. It has no status (archive it to retire it, D9)
-- and two kinds, held in attributes:
--
--   instant  — a treat with a catalog price, attributes.price = {currency: n, ...}
--              (e.g. Movie night = {"freedom": 120})
--   savings  — a savings goal that Coins are deposited into, with an optional
--              attributes.target_usd (e.g. Bike fund, 500)
--
-- A redemption is a `ledger` redeem entry (0025) whose payload subject_id names the
-- Reward. Every redeem that spends Coins stamps the dollars it moved and the coin rate
-- in force (configuration Rewards:CoinValueUsd), so changing the rate later never
-- rewrites what past deposits were worth: progress toward a savings target is the sum
-- of those stamped dollars, not coins x today's rate. See docs/pilot/rewards-plan.md §3.6.

-- ---------------------------------------------------------------------------
-- 1. Admit the Reward subject type
-- ---------------------------------------------------------------------------
ALTER TABLE bsk.subject DROP CONSTRAINT IF EXISTS subject_type_check;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area', 'Habit', 'Appointment',
        'Reward'));

-- The catalog looks each Reward's redemptions up by the payload's subject_id.
CREATE INDEX IF NOT EXISTS event_ledger_subject_idx
    ON bsk.event ((payload->>'subject_id'))
    WHERE kind = 'ledger';

-- ---------------------------------------------------------------------------
-- 2. The catalog, with what has been redeemed against each Reward
-- ---------------------------------------------------------------------------
-- kind reads as 'instant' unless it says savings. price is the stored object, or NULL
-- when it is missing or not an object (readers validate its entries). target_usd is
-- read tolerantly, since `bsk set` stores text: "500", "$1,250.50" and 500 all read;
-- anything else, or a target that isn't above zero, reads as NULL. The redemption
-- totals cover every redeem entry naming the Reward: coins_redeemed and
-- dollars_redeemed are a savings goal's deposits; percent_of_target and remaining_usd
-- are only set for a savings goal with a target.
CREATE OR REPLACE VIEW bsk.v_reward AS
WITH catalog AS (
    SELECT
        r.id,
        r.urn,
        r.title,
        CASE WHEN lower(trim(r.attributes->>'kind')) = 'savings'
             THEN 'savings' ELSE 'instant' END                         AS kind,
        CASE WHEN jsonb_typeof(r.attributes->'price') = 'object'
             THEN r.attributes->'price' END                            AS price,
        CASE
            WHEN jsonb_typeof(r.attributes->'target_usd') = 'number'
                THEN (r.attributes->>'target_usd')::numeric
            WHEN regexp_replace(r.attributes->>'target_usd', '[\s$,]', '', 'g') ~ '^[0-9]+(\.[0-9]+)?$'
                THEN regexp_replace(r.attributes->>'target_usd', '[\s$,]', '', 'g')::numeric
        END                                                            AS target_usd,
        r.attributes->>'description'                                   AS description,
        r.attributes->>'notes'                                         AS notes,
        r.created_at
    FROM bsk.subject r
    WHERE r.type = 'Reward'
)
SELECT
    c.id,
    c.urn,
    c.title                                                            AS name,
    c.kind,
    c.price,
    CASE WHEN c.target_usd > 0 THEN c.target_usd END                   AS target_usd,
    d.redemptions,
    coalesce(d.coins_redeemed, 0)                                      AS coins_redeemed,
    coalesce(d.dollars_redeemed, 0)                                    AS dollars_redeemed,
    d.last_redeemed_at,
    CASE WHEN c.kind = 'savings' AND c.target_usd > 0
         THEN round(100 * coalesce(d.dollars_redeemed, 0) / c.target_usd, 1) END
                                                                       AS percent_of_target,
    CASE WHEN c.kind = 'savings' AND c.target_usd > 0
         THEN greatest(c.target_usd - coalesce(d.dollars_redeemed, 0), 0) END
                                                                       AS remaining_usd,
    c.description,
    c.notes,
    bsk.is_archived(c.id)                                              AS archived,
    c.created_at
FROM catalog c
LEFT JOIN LATERAL (
    SELECT
        count(*)                                                       AS redemptions,
        coalesce(-sum(CASE WHEN jsonb_typeof(e.payload->'amounts'->'coins') = 'number'
                           THEN (e.payload->'amounts'->>'coins')::numeric END), 0)::bigint
                                                                       AS coins_redeemed,
        coalesce(sum(CASE WHEN jsonb_typeof(e.payload->'dollars') = 'number'
                          THEN (e.payload->>'dollars')::numeric END), 0)
                                                                       AS dollars_redeemed,
        max(e.occurred_at)                                             AS last_redeemed_at
    FROM bsk.event e
    WHERE e.kind = 'ledger'
      AND e.payload->>'subject_id' = c.id::text
      AND e.payload->>'entry' = 'redeem'
) d ON true;

COMMENT ON VIEW bsk.v_reward IS
    'REWARD-3: the reward catalog — instant rewards with a price, savings goals with a target — and what has been redeemed against each. Savings progress sums the dollars stamped on each deposit, so a coin-rate change never rewrites it.';

GRANT SELECT ON bsk.v_reward TO bsk_reader;
