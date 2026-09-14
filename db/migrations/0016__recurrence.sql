-- 0016__recurrence.sql
-- D8: a recurrence representation richer than the free-text `expected_cadence`,
-- shared by Habit / Appointment / Review. It lives in `attributes.recurrence` as a
-- structured JSON object (zero DDL, extendable), and is ADDITIVE — `expected_cadence`
-- and the neglect diagnostic that reads it are untouched. Canonical shapes:
--   {"freq":"daily"}
--   {"freq":"weekly","on":["sun","wed"]}          -- weekdays (sun..sat)
--   {"freq":"interval","every":10,"unit":"days"}  -- unit: days|weeks|months
--   {"freq":"monthly","on":"last"}                -- last calendar day of the month
--   {"freq":"monthly","day":15}                   -- the Nth day of the month
--   {"freq":"trigger","cue":"after lunch"}        -- cue-based; no scheduled dates
--
-- The expansion is defined once here, in the database, so read-only projections
-- (habit occurrences, appointment instances) can compute occurrences directly. It
-- returns the occurrence dates in [win_start, win_end], anchored at `anchor` (the
-- item's start date — needed to phase an interval recurrence). A trigger recurrence
-- has no scheduled dates and yields nothing.

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

    ELSE
        -- 'trigger' and anything unknown: no scheduled dates.
        RETURN;
    END IF;
END;
$$;

COMMENT ON FUNCTION bsk.recurrence_occurrences(jsonb, date, date, date) IS
    'D8: expands a structured recurrence (attributes.recurrence) into occurrence dates within [win_start, win_end], anchored at the item start. Trigger/unknown recurrences yield nothing.';

GRANT EXECUTE ON FUNCTION bsk.recurrence_occurrences(jsonb, date, date, date) TO bsk_reader;
