-- 0055__review_date_tolerant.sql
-- A Decision's next_review_at is free text (`bsk new decision --review-at`, `bsk set`), and
-- bsk.v_subject cast it with a bare ::timestamptz since 0005. One Decision whose review date
-- isn't a date ("soon-ish", "2026-02-30") made every read of the view raise, so the API's
-- subject list, search and detail all failed, not just that Decision.
--
-- bsk.try_to_timestamptz turns a value that doesn't parse into NULL, as bsk.try_to_date (0009)
-- does for dates. The raw text stays readable in `attributes`. The view keeps its columns, in
-- order, so CREATE OR REPLACE is enough.

CREATE OR REPLACE FUNCTION bsk.try_to_timestamptz(value text)
    RETURNS timestamptz
    LANGUAGE plpgsql
    STABLE
    PARALLEL SAFE
    STRICT
AS $$
BEGIN
    RETURN value::timestamptz;
EXCEPTION
    WHEN invalid_datetime_format OR datetime_field_overflow THEN
        RETURN NULL;
END;
$$;

COMMENT ON FUNCTION bsk.try_to_timestamptz(text) IS
    'Parses text to a timestamptz, returning NULL instead of raising when the value is malformed or out of range. NULL input returns NULL.';

GRANT EXECUTE ON FUNCTION bsk.try_to_timestamptz(text) TO bsk_reader;

CREATE OR REPLACE VIEW bsk.v_subject AS
SELECT
    s.id,
    s.urn,
    s.type,
    s.title,
    s.attributes->>'expected_cadence'                    AS expected_cadence,
    bsk.try_to_timestamptz(s.attributes->>'next_review_at') AS next_review_at,
    s.attributes->>'scope'                                AS scope,
    s.origin_event_id,
    s.created_at,
    s.attributes,
    s.attributes->>'statement'                            AS statement,
    bsk.is_archived(s.id)                                 AS archived,
    s.attributes->>'area'                                 AS area
FROM bsk.subject s;
