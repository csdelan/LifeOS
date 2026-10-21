-- 0030__credited_day.sql
-- REWARD-6 (xp-domains plan X1, §3.3): each earn records the local day it counts on —
-- the day it happened, not the day it was logged.
--
-- v_reward_trigger (and v_reward_due over it) gain `occurred_on`, the trigger's own date
-- where it has one:
--
--   day:<habit>:<date>:<state>     the occurrence date, even when it was logged later
--   streak_<n>:<habit>:<start>     the date of the run's last (nth) day
--   every other award              NULL: a status change has no date of its own, so the
--                                  reader files it on its instant's date in the home zone
--                                  (Rewards:HomeTimeZone), coalesce(occurred_on,
--                                  (triggered_at AT TIME ZONE tz)::date)
--
-- Reconcile stamps that date on each earn as payload `credited_on`. Nothing else
-- changes: the rows and keys are 0029's, and `occurred_on` is appended last, as CREATE OR
-- REPLACE VIEW requires.

CREATE OR REPLACE VIEW bsk.v_reward_trigger AS
SELECT
    c.subject_id,
    c.type                              AS subject_type,
    'completed:' || c.subject_id::text  AS award_key,
    'completed'                         AS trigger,
    ARRAY[c.status_event_id]            AS source_event_ids,
    c.status_occurred_at                AS triggered_at,
    NULL::bigint                        AS timed_seconds,
    NULL::date                          AS occurred_on
FROM bsk_derived.subject_current_source c
WHERE c.type IN ('Task', 'Project', 'Goal')
  AND lower(btrim(c.status)) = 'completed'

UNION ALL

SELECT
    c.subject_id,
    c.type,
    'timed:' || c.subject_id::text,
    'timed',
    ARRAY[c.status_event_id] || t.event_ids,
    c.status_occurred_at,
    t.seconds,
    NULL::date
FROM bsk_derived.subject_current_source c
CROSS JOIN LATERAL (
    SELECT
        floor(sum(extract(epoch FROM coalesce(s.next_at, c.status_occurred_at) - s.occurred_at))
              FILTER (WHERE s.action IN ('start', 'resume')))::bigint AS seconds,
        array_agg(s.id ORDER BY s.occurred_at, s.recorded_at, s.id) AS event_ids
    FROM (
        -- The timer actions up to the completion; the last running segment ends there.
        SELECT
            e.id,
            e.payload->>'timer' AS action,
            e.occurred_at,
            e.recorded_at,
            lead(e.occurred_at) OVER (ORDER BY e.occurred_at, e.recorded_at, e.id) AS next_at
        FROM bsk.event e
        WHERE e.kind = 'activity'
          AND jsonb_exists(e.payload, 'timer')
          AND e.payload->>'subject_id' = c.subject_id::text
          AND e.occurred_at <= c.status_occurred_at
    ) s
) t
WHERE c.type = 'Task'
  AND lower(btrim(c.status)) = 'completed'
  AND t.seconds > 0

UNION ALL

SELECT
    c.subject_id,
    c.type,
    'established:' || c.subject_id::text,
    'established',
    ARRAY[c.status_event_id],
    c.status_occurred_at,
    NULL::bigint,
    NULL::date
FROM bsk_derived.subject_current_source c
WHERE c.type = 'Habit'
  AND lower(btrim(c.status)) = 'established'

UNION ALL

SELECT
    o.habit_id,
    'Habit',
    'day:' || o.habit_id::text || ':' || to_char(o.occurrence_date, 'YYYY-MM-DD') || ':' || o.state,
    o.state,
    ARRAY[o.adherence_event_id],
    o.adherence_at,
    NULL::bigint,
    o.occurrence_date
FROM bsk_derived.habit_occurrence_source o
WHERE o.state IN ('followed', 'partial')
  AND (o.graduated_on IS NULL OR o.occurrence_date <= o.graduated_on)

UNION ALL

SELECT
    m.habit_id,
    'Habit',
    'streak_' || m.n || ':' || m.habit_id::text || ':' || to_char(m.run_start, 'YYYY-MM-DD'),
    'streak_' || m.n,
    m.source_event_ids,
    m.triggered_at,
    NULL::bigint,
    m.last_date
FROM (
    SELECT
        r.habit_id,
        n.n,
        r.run_start,
        array_agg(r.adherence_event_id ORDER BY r.occurrence_date) AS source_event_ids,
        max(r.adherence_at)                                        AS triggered_at,
        max(r.occurrence_date)                                     AS last_date
    FROM (
        SELECT
            f.*,
            min(f.occurrence_date) OVER (PARTITION BY f.habit_id, f.island) AS run_start,
            row_number() OVER (PARTITION BY f.habit_id, f.island ORDER BY f.occurrence_date) AS run_day
        FROM (
            -- Gaps and islands: consecutive followed occurrences share an island.
            SELECT
                s.*,
                s.seq - row_number() OVER (PARTITION BY s.habit_id ORDER BY s.occurrence_date) AS island
            FROM (
                SELECT
                    o.habit_id, o.occurrence_date, o.state, o.adherence_event_id, o.adherence_at,
                    row_number() OVER (PARTITION BY o.habit_id ORDER BY o.occurrence_date) AS seq
                FROM bsk_derived.habit_occurrence_source o
                WHERE o.graduated_on IS NULL OR o.occurrence_date <= o.graduated_on
            ) s
            WHERE s.state = 'followed'
        ) f
    ) r
    CROSS JOIN (VALUES (7), (30), (100)) AS n(n)
    WHERE r.run_day <= n.n
    GROUP BY r.habit_id, n.n, r.run_start
    HAVING count(*) = n.n
) m;

COMMENT ON VIEW bsk.v_reward_trigger IS
    'REWARD-1/2/6 (D11): every award the record supports, one row per award key, with the events that make it due — Task/Project/Goal completed; Task timed (timer time before the completion, in timed_seconds); Habit established, days followed / partial, and 7 / 30 / 100 streaks. occurred_on is the award''s own local date (a habit day''s date, a streak''s last day), NULL when it has none. Not yet filtered by the wallet''s start line; see v_reward_due.';

-- v_reward_due's column list was fixed when it was created; recreate it so it carries
-- occurred_on too.
CREATE OR REPLACE VIEW bsk.v_reward_due AS
SELECT t.*
FROM bsk.v_reward_trigger t
WHERE t.triggered_at >= bsk.ledger_opened_at();

COMMENT ON VIEW bsk.v_reward_due IS
    'REWARD-1/2 (D11): the awards due right now — v_reward_trigger rows on or after bsk.ledger_opened_at(). Reconcile earns each due key the ledger has not paid and reverses each paid key that is no longer due.';

GRANT SELECT ON bsk.v_reward_trigger TO bsk_reader;
GRANT SELECT ON bsk.v_reward_due TO bsk_reader;
