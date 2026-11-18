-- 0075__recurrence_cast_guards.sql
-- Code review CR146 (nightly review 2026-11-18): bsk.recurrence_occurrences reads a pattern's
-- numbers and weekdays without checking them. `(recur->>'every')::int` and `(recur->>'day')::int`
-- throw on 1.5 or "2nd", and jsonb_array_elements_text throws when 'on' isn't an array, so one
-- such pattern, written by a raw insert or anything that skips Recurrence.Canonicalize, would make
-- every read of the habits and their days fail for good. Each now reads a value it can't use as no
-- dates, as it already did a missing one; a whole number, as a JSON number or as text, reads as
-- before. Replaces the function only: no data moves.

CREATE OR REPLACE FUNCTION bsk.recurrence_occurrences(recur jsonb, anchor date, win_start date, win_end date)
 RETURNS SETOF date
 LANGUAGE plpgsql
 STABLE PARALLEL SAFE
AS $function$
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
            FROM jsonb_array_elements_text(
                     CASE WHEN jsonb_typeof(recur->'on') = 'array' THEN recur->'on' ELSE '[]'::jsonb END) x
        );

    ELSIF freq = 'interval' THEN
        -- Counted from the last completion: the dates aren't known from the pattern.
        IF recur->>'from' = 'completion' THEN
            RETURN;
        END IF;
        -- A whole number only: one bad value would otherwise make every read through this throw.
        every := CASE WHEN recur->>'every' ~ '^\s*-?[0-9]{1,9}\s*$' THEN (recur->>'every')::int END;
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
            day_n := CASE WHEN recur->>'day' ~ '^\s*-?[0-9]{1,9}\s*$' THEN (recur->>'day')::int END;
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
$function$;
