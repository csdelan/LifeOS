-- 0010__value_statement.sql
-- Values become identity statements. A Value now has two parts: the title is a
-- short handle (it drives the URN slug and is what edges reference), and a full
-- first-person statement — "who I've chosen to be" (§4.1) — lives alongside it.
-- e.g. handle "Learns from mistakes" →
--      urn:bsk:value:learns-from-mistakes-<shortid>
--      statement "I am a person who learns from mistakes rather than letting them
--                 define my worth."
-- This makes the data match the definition a Value always had: an enduring
-- principle, not a one-word label.
--
-- The statement lives in the existing attributes jsonb — no new column, so the
-- single typed-jsonb subject table is preserved. A CHECK makes it mandatory for
-- Values, so a Value can never regress to a bare handle with nothing behind it.

-- Backfill first: any pre-existing Value without a usable statement adopts its own
-- title, so the constraint below can be added without rejecting existing rows.
UPDATE bsk.subject
SET attributes = jsonb_set(attributes, '{statement}', to_jsonb(title), true)
WHERE type = 'Value'
  AND length(trim(coalesce(attributes->>'statement', ''))) = 0;

-- A Value must carry a non-empty statement; every other type is unaffected. The
-- coalesce guards the NULL-passes-a-CHECK trap: a missing key would make the
-- length test NULL (unknown), which a bare CHECK treats as satisfied. Folding a
-- missing/whitespace value to '' makes the predicate a hard false instead.
ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_value_has_statement
    CHECK (
        type <> 'Value'
        OR length(trim(coalesce(attributes->>'statement', ''))) > 0
    );

COMMENT ON CONSTRAINT subject_value_has_statement ON bsk.subject IS
    'A Value carries a full identity statement in attributes.statement; its title is the short handle.';

-- Expose the statement as a flattened column for readers. CREATE OR REPLACE VIEW
-- can only append columns (never rename or reorder existing ones), so statement
-- is added at the end rather than beside scope where it belongs conceptually.
CREATE OR REPLACE VIEW bsk.v_subject AS
SELECT
    s.id,
    s.urn,
    s.type,
    s.title,
    s.attributes->>'expected_cadence'               AS expected_cadence,
    (s.attributes->>'next_review_at')::timestamptz   AS next_review_at,
    s.attributes->>'scope'                           AS scope,
    s.origin_event_id,
    s.created_at,
    s.attributes,
    s.attributes->>'statement'                       AS statement
FROM bsk.subject s;
