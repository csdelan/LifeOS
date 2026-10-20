-- 0027__habit_lifecycle.sql
-- REWARD-2, decision D11 (rewards plan R3): the habit lifecycle and habit earning.
--
-- A Habit gains a status (D7, mirrored in LifeOs.Domain.StatusVocabulary):
--   Building (the default; no status reads as Building) -> Established | Abandoned.
-- Established joins the terminal set. A habit that reaches a terminal status
-- graduates: its scheduled occurrences stop there, so it leaves daily tracking.
-- Recorded history stays, and moving it back to Building resumes tracking.
--
-- Habits then earn through the same reconcile as completed work (migration 0026):
--   day:<habit>:<date>:followed   the latest adherence for that date is followed
--   day:<habit>:<date>:partial    ... is partial (pays half the followed bounty)
--   streak_<n>:<habit>:<start>    a run of n consecutive followed occurrences,
--                                 n in (7, 30, 100), starting on <start>
--   established:<habit>           its current status is Established
-- Each award is due from the moment its fact was recorded (triggered_at), so the
-- wallet's start line works exactly as it does for completions. A correction is a
-- newer adherence for the same date, so it moves the due set and reconcile follows:
-- followed -> partial reverses the day and pays the half; a miss that splits a run
-- reverses its milestone. Archiving never takes a reward back.

-- ---------------------------------------------------------------------------
-- 1. Established is terminal
-- ---------------------------------------------------------------------------
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
        -- Added for the per-type vocabularies (D7, 0011): Commitment Fulfilled/Missed,
        -- Appointment Missed, Idea Promoted/Rejected.
        'fulfilled', 'missed', 'promoted', 'rejected',
        -- Habit Established (0027). Habit Abandoned is already above.
        'established'
    );
$$;

COMMENT ON FUNCTION bsk.is_terminal_status(text) IS
    'True when a status string means the subject is finished/dead (the union of every type''s terminal statuses — D7). NULL status is active, not terminal.';

GRANT EXECUTE ON FUNCTION bsk.is_terminal_status(text) TO bsk_reader;

-- ---------------------------------------------------------------------------
-- 2. Occurrences, archived habits included, stopping at graduation
-- ---------------------------------------------------------------------------
-- The 0018 fold, extended: every habit (archived ones too, so archiving never
-- reverses an award), with when each adherence was recorded and when the habit
-- graduated (the date of its terminal status change, or NULL while Building).
-- Scheduled dates stop the day before graduation, so the graduation day is never
-- auto-missed; recorded dates always show. The CTEs are NOT MATERIALIZED so a
-- filter on one habit reaches the expansion instead of folding every habit.
CREATE OR REPLACE VIEW bsk_derived.habit_occurrence_source AS
WITH habits AS NOT MATERIALIZED (
    SELECT
        h.id,
        h.urn,
        h.attributes->'recurrence' AS recurrence,
        coalesce(bsk.try_to_date(h.attributes->>'start'), h.created_at::date) AS start_date,
        g.graduated_on,
        least(current_date,
              coalesce(bsk.try_to_date(h.attributes->>'end'), current_date),
              coalesce(g.graduated_on - 1, current_date)) AS end_cap,
        bsk.is_archived(h.id) AS archived
    FROM bsk.subject h
    LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
    CROSS JOIN LATERAL (
        SELECT CASE WHEN bsk.is_terminal_status(c.status) THEN c.status_occurred_at::date END AS graduated_on
    ) g
    WHERE h.type = 'Habit'
),
scheduled AS NOT MATERIALIZED (
    SELECT hb.id AS habit_id, occ.d AS occurrence_date
    FROM habits hb
    CROSS JOIN LATERAL bsk.recurrence_occurrences(
        hb.recurrence, hb.start_date, hb.start_date, hb.end_cap) AS occ(d)
),
recorded AS NOT MATERIALIZED (
    SELECT DISTINCT hb.id AS habit_id, (e.payload->>'occurrence')::date AS occurrence_date
    FROM habits hb
    JOIN bsk.event e
      ON e.kind = 'activity'
     AND e.payload->>'subject_id' = hb.id::text
     AND jsonb_exists(e.payload, 'occurrence')
),
dates AS NOT MATERIALIZED (
    SELECT habit_id, occurrence_date FROM scheduled
    UNION
    SELECT habit_id, occurrence_date FROM recorded
)
SELECT
    d.habit_id,
    hb.urn AS habit_urn,
    d.occurrence_date,
    CASE
        WHEN latest.result IS NOT NULL   THEN latest.result
        WHEN d.occurrence_date < current_date THEN 'missed'
        ELSE 'unrecorded'
    END AS state,
    latest.event_id    AS adherence_event_id,
    latest.occurred_at AS adherence_at,
    hb.graduated_on,
    hb.archived
FROM dates d
JOIN habits hb ON hb.id = d.habit_id
LEFT JOIN LATERAL (
    SELECT e.payload->>'result' AS result, e.id AS event_id, e.occurred_at
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND e.payload->>'subject_id' = d.habit_id::text
      AND e.payload->>'occurrence' = to_char(d.occurrence_date, 'YYYY-MM-DD')
    ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
    LIMIT 1
) latest ON true;

COMMENT ON VIEW bsk_derived.habit_occurrence_source IS
    'GEN-3 / REWARD-2: every habit''s occurrences (archived included) with resolved state, when the deciding adherence was recorded, and the graduation date. Scheduled dates stop at graduation.';

-- The public read keeps its 0018 shape: archived habits hidden.
CREATE OR REPLACE VIEW bsk.v_habit_occurrence AS
SELECT o.habit_id, o.habit_urn, o.occurrence_date, o.state, o.adherence_event_id
FROM bsk_derived.habit_occurrence_source o
WHERE NOT o.archived;

COMMENT ON VIEW bsk.v_habit_occurrence IS
    'GEN-3: derived habit occurrences (recurrence UNION recorded dates) with resolved state; unrecorded past dates read as missed; a graduated (Established / Abandoned) habit stops scheduling. Pure read — corrections recalculate automatically.';

-- ---------------------------------------------------------------------------
-- 3. Establishment: is a Building habit ready to graduate?
-- ---------------------------------------------------------------------------
-- attributes.establish_target = {min_adherence, window_days}, default 0.8 over 60
-- days (a min_adherence written as a percentage, e.g. 80, is read as 0.8). Over the
-- concluded occurrences in the last window_days days, adherence counts a partial as
-- half. target_met needs that adherence and a habit at least window_days old. It
-- only suggests: confirming is an ordinary status change to Established.
CREATE OR REPLACE VIEW bsk.v_habit_establishment AS
WITH habits AS (
    SELECT
        h.id,
        h.urn,
        h.title,
        coalesce(bsk.try_to_date(h.attributes->>'start'), h.created_at::date) AS start_date,
        CASE WHEN jsonb_typeof(h.attributes->'establish_target'->'min_adherence') = 'number'
             THEN (h.attributes->'establish_target'->>'min_adherence')::numeric END AS min_raw,
        CASE WHEN jsonb_typeof(h.attributes->'establish_target'->'window_days') = 'number'
             THEN (h.attributes->'establish_target'->>'window_days')::numeric END AS window_raw
    FROM bsk.subject h
    LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
    WHERE h.type = 'Habit'
      AND NOT bsk.is_terminal_status(c.status)
      AND NOT bsk.is_archived(h.id)
),
targets AS (
    SELECT
        id, urn, title, start_date,
        CASE WHEN min_raw > 0 AND min_raw <= 1 THEN min_raw
             WHEN min_raw > 1 AND min_raw <= 100 THEN min_raw / 100
             ELSE 0.8 END AS min_adherence,
        CASE WHEN window_raw >= 1 THEN floor(window_raw)::int ELSE 60 END AS window_days
    FROM habits
),
counts AS (
    SELECT
        o.habit_id,
        count(*) FILTER (WHERE o.state = 'followed') AS followed,
        count(*) FILTER (WHERE o.state = 'partial')  AS partial,
        count(*) FILTER (WHERE o.state = 'missed')   AS missed
    FROM bsk.v_habit_occurrence o
    JOIN targets t ON t.id = o.habit_id
    WHERE o.occurrence_date > current_date - t.window_days
      AND o.occurrence_date <= current_date
    GROUP BY o.habit_id
)
SELECT
    t.id                                        AS habit_id,
    t.urn                                       AS habit_urn,
    t.title                                     AS habit_name,
    t.min_adherence,
    t.window_days,
    current_date - t.window_days + 1            AS window_start,
    coalesce(c.followed, 0)                     AS followed,
    coalesce(c.partial, 0)                      AS partial,
    coalesce(c.missed, 0)                       AS missed,
    coalesce(c.followed + c.partial + c.missed, 0) AS concluded,
    CASE WHEN coalesce(c.followed + c.partial + c.missed, 0) > 0
         THEN round((c.followed + 0.5 * c.partial) / (c.followed + c.partial + c.missed), 4)
    END                                         AS adherence,
    current_date - t.start_date                 AS age_days,
    coalesce(c.followed + c.partial + c.missed, 0) > 0
        AND (c.followed + 0.5 * c.partial) / (c.followed + c.partial + c.missed) >= t.min_adherence
        AND current_date - t.start_date >= t.window_days AS target_met
FROM targets t
LEFT JOIN counts c ON c.habit_id = t.id;

COMMENT ON VIEW bsk.v_habit_establishment IS
    'REWARD-2: per Building habit, adherence over its establish_target window (partial = half) and whether the target is met — the "ready to establish" suggestion. Read-only.';

-- ---------------------------------------------------------------------------
-- 4. The habit awards
-- ---------------------------------------------------------------------------
-- Occurrences on or before graduation count; anything recorded after a habit
-- graduated shows in its history but does not pay. A streak run is a maximal
-- sequence of consecutive followed occurrences (a partial or a miss ends it, as in
-- v_habit_streak); it reaches n at its n-th day, and is due from when the last of
-- those n days was recorded.
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
  AND lower(btrim(c.status)) = 'completed'

UNION ALL

SELECT
    c.subject_id,
    c.type,
    'established:' || c.subject_id::text,
    'established',
    ARRAY[c.status_event_id],
    c.status_occurred_at
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
    o.adherence_at
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
    m.triggered_at
FROM (
    SELECT
        r.habit_id,
        n.n,
        r.run_start,
        array_agg(r.adherence_event_id ORDER BY r.occurrence_date) AS source_event_ids,
        max(r.adherence_at)                                        AS triggered_at
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
    'REWARD-1/2 (D11): every award the record supports, one row per award key, with the events that make it due — Task/Project/Goal completed; Habit established, days followed / partial, and 7 / 30 / 100 streaks. Not yet filtered by the wallet''s start line; see v_reward_due.';

GRANT SELECT ON bsk_derived.habit_occurrence_source TO bsk_reader;
GRANT SELECT ON bsk.v_habit_occurrence TO bsk_reader;
GRANT SELECT ON bsk.v_habit_establishment TO bsk_reader;
GRANT SELECT ON bsk.v_reward_trigger TO bsk_reader;
