-- 0042__task_series.sql
-- D16 / TASKS-3 (recurring-tasks plan RT2): repeating Tasks.
--
-- A repeating Task is a series: a Task carrying attributes.recurrence (D8) and
-- attributes.start. It holds the pattern and the price and never shows in the Task lists.
-- Each repetition is an ordinary Task carrying attributes.series (the series' urn) and
-- attributes.occurrence (the date it stands for). TaskSeriesService makes them: the first
-- when the pattern is set, the next when one closes, and for calendar dates also when one's
-- date passes. Nothing here changes a status.
--
-- This migration adds:
--   1. two shapes to bsk.recurrence_occurrences:
--        {"freq":"each","period":"week"}   once each week on any day: each week's Monday,
--                                          from the anchor's week, for weeks that overlap
--                                          the window;
--        {"freq":"interval",…,"from":"completion"}   every N after the last one closed:
--                                          no dates of its own (they come from completions);
--   2. bsk.is_task_series(type, attributes);
--   3. one repetition per (series, occurrence): a unique index;
--   4. bsk.v_task_repetition and bsk.v_task_series, status folded from source.

-- ---------------------------------------------------------------------------
-- 1. The expander (0016's, plus the two shapes)
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION bsk.recurrence_occurrences(
    recur jsonb, anchor date, win_start date, win_end date)
    RETURNS SETOF date
    LANGUAGE plpgsql
    STABLE
    PARALLEL SAFE
AS $$
DECLARE
    freq  text := recur->>'freq';
    every int;
    step  interval;
    day_n int;
BEGIN
    IF recur IS NULL OR freq IS NULL OR win_start > win_end THEN
        RETURN;
    END IF;

    IF freq = 'daily' THEN
        RETURN QUERY
        SELECT d::date
        FROM generate_series(greatest(anchor, win_start), win_end, interval '1 day') d;

    ELSIF freq = 'weekly' THEN
        -- Match by day-of-week number (locale-independent); Postgres dow: 0=Sun..6=Sat.
        RETURN QUERY
        SELECT d::date
        FROM generate_series(greatest(anchor, win_start), win_end, interval '1 day') d
        WHERE extract(dow FROM d)::int = ANY (
            SELECT CASE lower(x)
                       WHEN 'sun' THEN 0 WHEN 'mon' THEN 1 WHEN 'tue' THEN 2
                       WHEN 'wed' THEN 3 WHEN 'thu' THEN 4 WHEN 'fri' THEN 5
                       WHEN 'sat' THEN 6 END
            FROM jsonb_array_elements_text(recur->'on') x
        );

    ELSIF freq = 'interval' THEN
        -- Counted from the last completion: the dates aren't known from the pattern.
        IF recur->>'from' = 'completion' THEN
            RETURN;
        END IF;
        every := (recur->>'every')::int;
        IF every IS NULL OR every < 1 THEN
            RETURN;
        END IF;
        step := CASE recur->>'unit'
                    WHEN 'days'   THEN make_interval(days   => every)
                    WHEN 'weeks'  THEN make_interval(weeks  => every)
                    WHEN 'months' THEN make_interval(months => every)
                    ELSE NULL
                END;
        IF step IS NULL THEN
            RETURN;
        END IF;
        -- Phased from the anchor; keep only those inside the window.
        RETURN QUERY
        SELECT d::date
        FROM generate_series(anchor::timestamp, win_end::timestamp, step) d
        WHERE d::date >= win_start;

    ELSIF freq = 'monthly' THEN
        IF recur->>'on' = 'last' THEN
            RETURN QUERY
            SELECT last_day::date
            FROM generate_series(
                     date_trunc('month', greatest(anchor, win_start))::timestamp,
                     win_end::timestamp, interval '1 month') m,
                 LATERAL (SELECT (date_trunc('month', m) + interval '1 month' - interval '1 day')::date) AS x(last_day)
            WHERE last_day BETWEEN win_start AND win_end;
        ELSE
            day_n := (recur->>'day')::int;
            IF day_n IS NULL OR day_n < 1 OR day_n > 31 THEN
                RETURN;
            END IF;
            RETURN QUERY
            SELECT occ::date
            FROM generate_series(
                     date_trunc('month', greatest(anchor, win_start))::timestamp,
                     win_end::timestamp, interval '1 month') m,
                 LATERAL (SELECT (date_trunc('month', m) + make_interval(days => day_n - 1))::date) AS x(occ)
            WHERE occ BETWEEN win_start AND win_end
              -- Skip months that have no such day (e.g. day 31 in February).
              AND extract(month FROM occ) = extract(month FROM m);
        END IF;

    ELSIF freq = 'each' THEN
        -- Once each week, Monday to Sunday (date_trunc('week') is the Monday): each week
        -- from the anchor's, named by its Monday, for the weeks that overlap the window.
        IF recur->>'period' IS DISTINCT FROM 'week' THEN
            RETURN;
        END IF;
        RETURN QUERY
        SELECT w::date
        FROM generate_series(
                 date_trunc('week', greatest(anchor, win_start))::timestamp,
                 win_end::timestamp, interval '1 week') w;

    ELSE
        -- 'trigger' and anything unknown: no scheduled dates.
        RETURN;
    END IF;
END;
$$;

COMMENT ON FUNCTION bsk.recurrence_occurrences(jsonb, date, date, date) IS
    'D8: expands a structured recurrence (attributes.recurrence) into occurrence dates within [win_start, win_end], anchored at the item start. Once each week (freq each, period week) gives each week''s Monday for the weeks that overlap the window (D16). An every-N counted from the last completion, a trigger, and anything unknown yield nothing.';

GRANT EXECUTE ON FUNCTION bsk.recurrence_occurrences(jsonb, date, date, date) TO bsk_reader;

-- ---------------------------------------------------------------------------
-- 2. Which subjects are series
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION bsk.is_task_series(type text, attributes jsonb)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    PARALLEL SAFE
AS $$
    -- coalesce: false, never null, for a Task with no pattern (NOT null would drop it).
    SELECT coalesce(
        type = 'Task'
        AND jsonb_typeof(attributes->'recurrence') = 'object'
        AND (attributes->>'series') IS NULL,
        false);
$$;

COMMENT ON FUNCTION bsk.is_task_series(text, jsonb) IS
    'D16: true for a repeating Task''s series (a Task carrying a recurrence object and no series of its own). Series hold the pattern and price and stay out of the Task lists.';

GRANT EXECUTE ON FUNCTION bsk.is_task_series(text, jsonb) TO bsk_reader;

-- ---------------------------------------------------------------------------
-- 3. One repetition per (series, occurrence)
-- ---------------------------------------------------------------------------
-- TaskSeriesService also takes an advisory lock per series; this is the store's guarantee.
-- It doubles as the index for "the repetitions of this series".
CREATE UNIQUE INDEX IF NOT EXISTS subject_task_repetition_key
    ON bsk.subject ((attributes->>'series'), (attributes->>'occurrence'))
    WHERE type = 'Task' AND (attributes->>'series') IS NOT NULL;

-- ---------------------------------------------------------------------------
-- 4. Views
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW bsk.v_task_repetition AS
SELECT
    r.id,
    r.urn,
    r.title,
    sr.id                                         AS series_id,
    r.attributes->>'series'                       AS series_urn,
    bsk.try_to_date(r.attributes->>'occurrence')  AS occurrence,
    bsk.try_to_date(r.attributes->>'due')         AS due,
    bsk.try_to_date(r.attributes->>'scheduled')   AS scheduled,
    c.status,
    bsk.is_terminal_status(c.status)              AS closed,
    CASE WHEN bsk.is_terminal_status(c.status)
         THEN (c.status_occurred_at AT TIME ZONE bsk.home_zone())::date
    END                                           AS closed_on,
    bsk.is_archived(r.id)                         AS archived,
    r.created_at
FROM bsk.subject r
LEFT JOIN bsk.subject sr ON sr.urn = r.attributes->>'series'
LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = r.id
WHERE r.type = 'Task'
  AND (r.attributes->>'series') IS NOT NULL;

COMMENT ON VIEW bsk.v_task_repetition IS
    'D16: each repetition of a repeating Task, with the date it stands for, its dates, its status folded from source, and the home-zone day it closed (Completed, Missed or Cancelled).';

CREATE OR REPLACE VIEW bsk.v_task_series AS
SELECT
    s.id,
    s.urn,
    s.title,
    s.attributes->'recurrence'                         AS recurrence,
    bsk.try_to_date(s.attributes->>'start')            AS start_date,
    c.status,
    bsk.is_archived(s.id)                              AS archived,
    (bsk.is_terminal_status(c.status) OR bsk.is_archived(s.id)) AS stopped,
    coalesce(r.open_count, 0)                          AS open_count,
    coalesce(r.completed_count, 0)                     AS completed_count,
    coalesce(r.missed_count, 0)                        AS missed_count,
    coalesce(r.cancelled_count, 0)                     AS cancelled_count,
    r.last_completed_on,
    r.next_due,
    r.latest_occurrence
FROM bsk.subject s
LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
LEFT JOIN LATERAL (
    SELECT
        count(*) FILTER (WHERE NOT x.closed AND NOT x.archived)               AS open_count,
        count(*) FILTER (WHERE lower(x.status) = 'completed')                 AS completed_count,
        count(*) FILTER (WHERE lower(x.status) = 'missed')                    AS missed_count,
        count(*) FILTER (WHERE lower(x.status) = 'cancelled')                 AS cancelled_count,
        max(x.closed_on) FILTER (WHERE lower(x.status) = 'completed')         AS last_completed_on,
        min(x.due) FILTER (WHERE NOT x.closed AND NOT x.archived)             AS next_due,
        max(x.occurrence)                                                     AS latest_occurrence
    FROM bsk.v_task_repetition x
    WHERE x.series_urn = s.urn
) r ON true
WHERE bsk.is_task_series(s.type, s.attributes);

COMMENT ON VIEW bsk.v_task_series IS
    'D16: each repeating Task''s series: its pattern and start, whether it is stopped (Cancelled or archived), and its repetitions counted: open, completed, missed, cancelled, the last completed day, the next open due date and the latest date made.';

GRANT SELECT ON bsk.v_task_repetition TO bsk_reader;
GRANT SELECT ON bsk.v_task_series TO bsk_reader;
