-- 0049__planned_habits.sql
-- A habit can be Planned: made, but not tracked yet (mirrored in
-- LifeOs.Domain.StatusVocabulary). Its lifecycle is now
--   Planned -> Building -> Established | Abandoned.
-- A new habit is born Planned: creating one writes Planned as its first state_change
-- (SubjectService). A habit with no status still reads as Building, so every habit made
-- before Planned existed keeps being tracked exactly as it was.
--
-- While a habit is Planned it isn't tracked:
--   * no day is scheduled, so no day reads as missed (a day recorded anyway still shows)
--   * it has no establishment progress, and the days it spent Planned don't count toward
--     its age for the establish window
--   * the kernel refuses to record its days (AdherenceService)
-- Moving it to Building starts tracking that day. Planned is not terminal, so it never
-- graduates and earns nothing on its own.
--
-- The days a habit spent Planned are left out of its schedule whenever they were, so a
-- habit moved back to Planned and then on to Building again keeps the misses from its
-- first spell of tracking. Those days hold no occurrence, so they don't break a run of
-- followed days either, as a weekly habit's off days don't.

-- ---------------------------------------------------------------------------
-- 1. When each habit was Planned
-- ---------------------------------------------------------------------------
-- One row per spell: from the day the habit became Planned up to (not including) the day
-- its next status change took it out, or open while it's still Planned. Days are in the
-- home zone. Moving it in and out on the same day leaves an empty spell, so that day is
-- tracked.
CREATE OR REPLACE VIEW bsk_derived.habit_planned_source AS
SELECT
    s.habit_id,
    s.planned_from,
    s.planned_until
FROM (
    SELECT
        h.id AS habit_id,
        lower(btrim(e.payload->>'status')) AS status,
        (e.occurred_at AT TIME ZONE bsk.home_zone())::date AS planned_from,
        ((lead(e.occurred_at) OVER (
            PARTITION BY h.id ORDER BY e.occurred_at, e.recorded_at, e.id))
            AT TIME ZONE bsk.home_zone())::date AS planned_until
    FROM bsk.subject h
    JOIN bsk.event e
      ON e.kind = 'state_change'
     AND e.payload->>'subject_id' = h.id::text
    WHERE h.type = 'Habit'
) s
WHERE s.status = 'planned';

COMMENT ON VIEW bsk_derived.habit_planned_source IS
    'Each spell a habit was Planned (not tracked yet, 0049): planned_from up to, not including, planned_until (NULL while still Planned), as dates in the home zone.';

-- ---------------------------------------------------------------------------
-- 2. Occurrences (0033's view, skipping the days a habit was Planned)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk_derived.habit_occurrence_source AS
WITH habits AS NOT MATERIALIZED (
    SELECT
        h.id,
        h.urn,
        h.attributes->'recurrence' AS recurrence,
        coalesce(bsk.try_to_date(h.attributes->>'start'), (h.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date,
        g.graduated_on,
        least(bsk.home_today(),
              coalesce(bsk.try_to_date(h.attributes->>'end'), bsk.home_today()),
              coalesce(g.graduated_on - 1, bsk.home_today())) AS end_cap,
        bsk.is_archived(h.id) AS archived
    FROM bsk.subject h
    LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
    CROSS JOIN LATERAL (
        SELECT CASE WHEN bsk.is_terminal_status(c.status) THEN (c.status_occurred_at AT TIME ZONE bsk.home_zone())::date END AS graduated_on
    ) g
    WHERE h.type = 'Habit'
),
scheduled AS NOT MATERIALIZED (
    SELECT hb.id AS habit_id, occ.d AS occurrence_date
    FROM habits hb
    CROSS JOIN LATERAL bsk.recurrence_occurrences(
        hb.recurrence, hb.start_date, hb.start_date, hb.end_cap) AS occ(d)
    -- Not while it was Planned: it wasn't tracked then.
    WHERE NOT EXISTS (
        SELECT 1
        FROM bsk_derived.habit_planned_source p
        WHERE p.habit_id = hb.id
          AND occ.d >= p.planned_from
          AND (p.planned_until IS NULL OR occ.d < p.planned_until)
    )
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
        WHEN latest.result IS NOT NULL            THEN latest.result
        WHEN d.occurrence_date < bsk.home_today() THEN 'missed'
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
    'GEN-3 / REWARD-2: every habit''s occurrences (archived included) with resolved state, when the deciding adherence was recorded, and the graduation date. Scheduled dates stop at graduation and skip the days the habit was Planned (0049). Today, graduation and a default start are dates in the home zone (bsk.home_today(), 0033).';

COMMENT ON VIEW bsk.v_habit_occurrence IS
    'GEN-3: derived habit occurrences (recurrence UNION recorded dates) with resolved state; unrecorded past dates read as missed; a Planned habit has no scheduled dates (0049) and a graduated (Established / Abandoned) one stops scheduling. Pure read — corrections recalculate automatically.';

-- ---------------------------------------------------------------------------
-- 3. Establishment (0033's view, Building habits only, aged by days tracked)
-- ---------------------------------------------------------------------------
-- A Planned habit has no progress. A habit's age is the days since its start less the days
-- it spent Planned, so a habit that waited 60 days as Planned isn't ready to establish after
-- one tracked day, while one started earlier than it was made keeps those tracked days.
CREATE OR REPLACE VIEW bsk.v_habit_establishment AS
WITH habits AS (
    SELECT
        h.id,
        h.urn,
        h.title,
        st.start_date,
        pd.planned_days,
        CASE WHEN jsonb_typeof(h.attributes->'establish_target'->'min_adherence') = 'number'
             THEN (h.attributes->'establish_target'->>'min_adherence')::numeric END AS min_raw,
        CASE WHEN jsonb_typeof(h.attributes->'establish_target'->'window_days') = 'number'
             THEN (h.attributes->'establish_target'->>'window_days')::numeric END AS window_raw
    FROM bsk.subject h
    LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
    CROSS JOIN LATERAL (
        SELECT coalesce(bsk.try_to_date(h.attributes->>'start'), (h.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date
    ) st
    -- The days from its start up to today it spent Planned. Only finished spells: a habit
    -- still Planned is left out below.
    CROSS JOIN LATERAL (
        SELECT coalesce(sum(greatest(0,
                   least(p.planned_until, bsk.home_today()) - greatest(p.planned_from, st.start_date))), 0)::int AS planned_days
        FROM bsk_derived.habit_planned_source p
        WHERE p.habit_id = h.id
          AND p.planned_until IS NOT NULL
    ) pd
    WHERE h.type = 'Habit'
      AND NOT bsk.is_terminal_status(c.status)
      AND lower(btrim(coalesce(c.status, ''))) <> 'planned'
      AND NOT bsk.is_archived(h.id)
),
targets AS (
    SELECT
        id, urn, title, start_date, planned_days,
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
    WHERE o.occurrence_date > bsk.home_today() - t.window_days
      AND o.occurrence_date <= bsk.home_today()
    GROUP BY o.habit_id
)
SELECT
    t.id                                        AS habit_id,
    t.urn                                       AS habit_urn,
    t.title                                     AS habit_name,
    t.min_adherence,
    t.window_days,
    bsk.home_today() - t.window_days + 1            AS window_start,
    coalesce(c.followed, 0)                     AS followed,
    coalesce(c.partial, 0)                      AS partial,
    coalesce(c.missed, 0)                       AS missed,
    coalesce(c.followed + c.partial + c.missed, 0) AS concluded,
    CASE WHEN coalesce(c.followed + c.partial + c.missed, 0) > 0
         THEN round((c.followed + 0.5 * c.partial) / (c.followed + c.partial + c.missed), 4)
    END                                         AS adherence,
    bsk.home_today() - t.start_date - t.planned_days AS age_days,
    coalesce(c.followed + c.partial + c.missed, 0) > 0
        AND (c.followed + 0.5 * c.partial) / (c.followed + c.partial + c.missed) >= t.min_adherence
        AND bsk.home_today() - t.start_date - t.planned_days >= t.window_days AS target_met
FROM targets t
LEFT JOIN counts c ON c.habit_id = t.id;

COMMENT ON VIEW bsk.v_habit_establishment IS
    'REWARD-2: per Building habit (not Planned, 0049), adherence over its establish_target window (partial = half) and whether the target is met — the "ready to establish" suggestion. Its age is the days since its start less the days it spent Planned (0049). The window ends today in the home zone (0033). Read-only.';

GRANT SELECT ON bsk_derived.habit_planned_source TO bsk_reader;
GRANT SELECT ON bsk_derived.habit_occurrence_source TO bsk_reader;
GRANT SELECT ON bsk.v_habit_establishment TO bsk_reader;
