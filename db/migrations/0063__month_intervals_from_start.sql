-- 0063__month_intervals_from_start.sql
-- Code review CR61: bsk.recurrence_occurrences counts an every-N-months repeat from its start.
--
-- Two changes to the expander, the rest of it as 0042 left it:
--   1. Every N months: the k-th date is the anchor plus k*N months, each counted from the
--      anchor. generate_series stepped from the date before, so a repeat started on the 31st
--      clamped to February's 28th and stayed there: Jan 31, Feb 28, Mar 28, Apr 28. Now it's
--      Jan 31, Feb 28, Mar 31, Apr 30, as addMonthsClamped in the web always meant.
--   2. Monthly on a day, or on the last day: a date before the anchor isn't one. The months
--      started at the anchor's month, so a Task series starting Dec 20 on the 5th, read from
--      Nov 13, gave Dec 5. Habits and Appointments read from their start, so only a window
--      opening before the anchor saw it.
-- tests/rules/recurrence.json holds the cases; RecurrenceRuleTests checks this function gives
-- them and rule-parity.test.ts checks the web's isOccurrenceDate does.

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
        IF recur->>'unit' = 'months' THEN
            -- Each date is counted from the anchor, not from the one before (CR61), so a day the
            -- short months clamp comes back in the long ones.
            RETURN QUERY
            SELECT occ
            FROM generate_series(
                     0,
                     ((extract(year FROM win_end) - extract(year FROM anchor)) * 12
                      + extract(month FROM win_end) - extract(month FROM anchor))::int / every) k,
                 LATERAL (SELECT (anchor + make_interval(months => every * k))::date) AS x(occ)
            WHERE occ BETWEEN win_start AND win_end;
        ELSE
            step := CASE recur->>'unit'
                        WHEN 'days'  THEN make_interval(days  => every)
                        WHEN 'weeks' THEN make_interval(weeks => every)
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
        END IF;

    ELSIF freq = 'monthly' THEN
        IF recur->>'on' = 'last' THEN
            RETURN QUERY
            SELECT last_day::date
            FROM generate_series(
                     date_trunc('month', greatest(anchor, win_start))::timestamp,
                     win_end::timestamp, interval '1 month') m,
                 LATERAL (SELECT (date_trunc('month', m) + interval '1 month' - interval '1 day')::date) AS x(last_day)
            WHERE last_day BETWEEN win_start AND win_end
              AND last_day >= anchor;
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
              AND occ >= anchor
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
    'D8: expands a structured recurrence (attributes.recurrence) into occurrence dates within [win_start, win_end], anchored at the item start: no date before the anchor counts, except that once each week names the anchor''s week by its Monday. Every N months counts each date from the anchor, so the 31st clamps to a short month''s last day and comes back (CR61). Once each week (freq each, period week) gives each week''s Monday for the weeks that overlap the window (D16). An every-N counted from the last completion, a trigger, and anything unknown yield nothing.';
