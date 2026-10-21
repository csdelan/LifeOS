-- 0031__maintained_habits.sql
-- REWARD-2 revision (xp-domains plan X2, §3.9): an established habit keeps earning through
-- logged maintenance.
--
-- Until now a habit that graduated stopped paying for days recorded after the graduation
-- date. Those days were already in bsk_derived.habit_occurrence_source; only the day rows'
-- filter in v_reward_trigger kept them out. Now day rows also pass for a habit whose
-- current status is Established, under the same day:<habit>:<date>:<state> key and price:
--
--   Building      every recorded followed / partial day pays (unchanged)
--   Established   days up to graduation pay (unchanged), and so do days logged after it
--   Abandoned     days after abandoning don't pay (unchanged)
--
-- What stops at establishment stays stopped: scheduled dates still end the day before
-- graduation, so an unlogged day is never a miss; streak rows keep the graduation filter, so
-- no milestone pays after it. Moving the habit back to Building clears graduated_on, so
-- every recorded day pays as before; moving it to Abandoned dates graduated_on at the
-- abandoning, so the maintained days logged before it keep what they paid.
--
-- The columns are 0030's, so v_reward_due (SELECT t.* over this view) needs no change.

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
LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = o.habit_id
WHERE o.state IN ('followed', 'partial')
  AND (o.graduated_on IS NULL
       OR o.occurrence_date <= o.graduated_on
       -- Maintenance: an Established habit keeps paying for the days logged after it
       -- graduated. An Abandoned one doesn't.
       OR lower(btrim(c.status)) = 'established')

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
    'REWARD-1/2/6 (D11): every award the record supports, one row per award key, with the events that make it due — Task/Project/Goal completed; Task timed (timer time before the completion, in timed_seconds); Habit established, days followed / partial (up to graduation, and after it while the habit is Established), and 7 / 30 / 100 streaks (up to graduation). occurred_on is the award''s own local date (a habit day''s date, a streak''s last day), NULL when it has none. Not yet filtered by the wallet''s start line; see v_reward_due.';

GRANT SELECT ON bsk.v_reward_trigger TO bsk_reader;
