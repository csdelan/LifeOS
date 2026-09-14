-- 0018__habit_projection.sql
-- GEN-3 / GEN-5: the habit occurrence + streak projection. Occurrences are NOT
-- stored (D2) — they are derived here from the recurrence (expanded by
-- bsk.recurrence_occurrences, migration 0016) and the append-only adherence events
-- (migration 0017). Because it is a pure read, a correction (a newer adherence event
-- for the same date) recalculates everything automatically; nothing is rebuilt.
--
-- Occurrence state:
--   * a recorded result wins (followed / partial / missed) — latest event per date;
--   * an occurrence whose date is in the past with no record reads as 'missed'
--     (concluded and unrecorded — GEN-3's "not followed at window close");
--   * today's (and future) unrecorded occurrences are 'unrecorded', never a failure.
-- The occurrence-date set is the recurrence expansion UNION any recorded dates, so a
-- cue-based (trigger) habit — which has no scheduled dates — still shows the days it
-- was recorded, and can never be auto-missed for a cue that did not fire.

CREATE OR REPLACE VIEW bsk.v_habit_occurrence AS
WITH habits AS (
    SELECT
        h.id,
        h.urn,
        h.attributes->'recurrence' AS recurrence,
        coalesce(bsk.try_to_date(h.attributes->>'start'), h.created_at::date) AS start_date,
        least(current_date,
              coalesce(bsk.try_to_date(h.attributes->>'end'), current_date)) AS end_cap
    FROM bsk.subject h
    WHERE h.type = 'Habit'
      AND NOT bsk.is_archived(h.id)
),
-- Scheduled occurrence dates from the recurrence.
scheduled AS (
    SELECT hb.id AS habit_id, occ.d AS occurrence_date
    FROM habits hb
    CROSS JOIN LATERAL bsk.recurrence_occurrences(
        hb.recurrence, hb.start_date, hb.start_date, hb.end_cap) AS occ(d)
),
-- Dates that carry a recorded adherence (covers cue-based habits and any backfill
-- landing off the schedule).
recorded AS (
    SELECT DISTINCT hb.id AS habit_id, (e.payload->>'occurrence')::date AS occurrence_date
    FROM habits hb
    JOIN bsk.event e
      ON e.kind = 'activity'
     AND e.payload->>'subject_id' = hb.id::text
     AND jsonb_exists(e.payload, 'occurrence')
),
dates AS (
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
    latest.event_id AS adherence_event_id
FROM dates d
JOIN habits hb ON hb.id = d.habit_id
LEFT JOIN LATERAL (
    SELECT e.payload->>'result' AS result, e.id AS event_id
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND e.payload->>'subject_id' = d.habit_id::text
      AND e.payload->>'occurrence' = to_char(d.occurrence_date, 'YYYY-MM-DD')
    ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
    LIMIT 1
) latest ON true;

COMMENT ON VIEW bsk.v_habit_occurrence IS
    'GEN-3: derived habit occurrences (recurrence UNION recorded dates) with resolved state; unrecorded past dates read as missed. Pure read — corrections recalculate automatically.';

-- The current streak: consecutive fully-followed occurrences counting back from the
-- most recent CONCLUDED occurrence (partial or missed breaks the binary streak, D2;
-- today's unrecorded occurrence does not count yet).
CREATE OR REPLACE VIEW bsk.v_habit_streak AS
WITH concluded AS (
    SELECT
        habit_id, habit_urn, occurrence_date, state,
        row_number() OVER (PARTITION BY habit_id ORDER BY occurrence_date DESC) AS rn
    FROM bsk.v_habit_occurrence
    WHERE state <> 'unrecorded'
)
SELECT
    c.habit_id,
    c.habit_urn,
    -- Position of the most recent non-followed occurrence (the break), minus one; or,
    -- when there is no break, every concluded occurrence counts.
    coalesce(min(c.rn) FILTER (WHERE c.state <> 'followed'), max(c.rn) + 1) - 1 AS current_streak,
    max(c.occurrence_date) AS last_occurrence,
    (array_agg(c.state ORDER BY c.occurrence_date DESC))[1] AS last_state
FROM concluded c
GROUP BY c.habit_id, c.habit_urn;

COMMENT ON VIEW bsk.v_habit_streak IS
    'GEN-3/D2: current per-habit streak of consecutive fully-followed concluded occurrences; partial or missed breaks it.';

GRANT SELECT ON bsk.v_habit_occurrence, bsk.v_habit_streak TO bsk_reader;
