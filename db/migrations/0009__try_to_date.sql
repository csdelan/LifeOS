-- 0009__try_to_date.sql
-- A NULL-on-invalid date parse, mirroring bsk.is_terminal_status (0008) as a
-- small reusable predicate. Subject attributes are free-text jsonb, so a date
-- like `ends` can be absent, the wrong shape ("soon"), or the right shape but an
-- impossible value ("2026-02-30"). A bare `(attributes->>'ends')::date` throws
-- on the last two — and inside a diagnostic that aborts the whole `bsk check`
-- run, not just the one diagnostic. This helper turns every un-parseable value
-- into NULL so the caller can decide what a missing date means.
--
-- STABLE, not IMMUTABLE: text->date parsing of non-ISO input can depend on the
-- DateStyle setting. Only the two datetime parse errors are swallowed; anything
-- else (e.g. a real fault) still propagates.

CREATE OR REPLACE FUNCTION bsk.try_to_date(value text)
    RETURNS date
    LANGUAGE plpgsql
    STABLE
    PARALLEL SAFE
    STRICT
AS $$
BEGIN
    RETURN value::date;
EXCEPTION
    WHEN invalid_datetime_format OR datetime_field_overflow THEN
        RETURN NULL;
END;
$$;

COMMENT ON FUNCTION bsk.try_to_date(text) IS
    'Parses text to a date, returning NULL instead of raising when the value is malformed or out of range (e.g. 2026-02-30). NULL input returns NULL.';

GRANT EXECUTE ON FUNCTION bsk.try_to_date(text) TO bsk_reader;
