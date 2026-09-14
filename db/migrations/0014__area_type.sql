-- 0014__area_type.sql
-- GEN-2 / D1: Area of Focus becomes a durable subject type (Trading, Family,
-- Health, Dev Career…). It is the first net-new type the Pilot adds (11 -> 12),
-- and costs almost no DDL: the single typed-jsonb subject table just admits one
-- more `type`. Name is the title; Description and Notes are attributes.
--
-- An item names its Area with a soft reference in `attributes.area` — the Area
-- subject's urn — kept as a *property*, deliberately NOT an alignment edge, so the
-- serves/results_in graph stays about work, not classification. This is also kept
-- separate from the existing `focus`/Season axis (the overlap is noted in D1 but
-- not fused for the Pilot). Setting an item's Area needs no new verb: `bsk set
-- <item> area=<area-urn>` already works; `bsk new … --area` (this phase) is the
-- create-time convenience.

-- ---------------------------------------------------------------------------
-- 1. Admit the Area subject type
-- ---------------------------------------------------------------------------
-- The type CHECK is an inline column constraint; discover it by its definition
-- (only the type check mentions 'Season'), drop it, and re-add the widened set.
-- Adding a type never invalidates existing rows.
DO $$
DECLARE
    check_name text;
BEGIN
    SELECT conname INTO check_name
    FROM pg_constraint
    WHERE conrelid = 'bsk.subject'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ILIKE '%Season%';

    IF check_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE bsk.subject DROP CONSTRAINT %I', check_name);
    END IF;
END
$$;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area'));

-- ---------------------------------------------------------------------------
-- 2. Expose the item -> Area reference to readers
-- ---------------------------------------------------------------------------
-- CREATE OR REPLACE VIEW may only append columns, so `area` lands at the end
-- (after `archived`, 0012). Consumers group/filter items by their Area with it.
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
    s.attributes->>'statement'                       AS statement,
    bsk.is_archived(s.id)                            AS archived,
    s.attributes->>'area'                            AS area
FROM bsk.subject s;

-- ---------------------------------------------------------------------------
-- 3. The master Areas list
-- ---------------------------------------------------------------------------
-- Name / Description / Notes flattened for the Areas screen. Areas are permanent
-- (GEN-2), so there is no archived state to filter here.
CREATE OR REPLACE VIEW bsk.v_area AS
SELECT
    s.id,
    s.urn,
    s.title                          AS name,
    s.attributes->>'description'     AS description,
    s.attributes->>'notes'           AS notes,
    s.created_at
FROM bsk.subject s
WHERE s.type = 'Area';

GRANT SELECT ON bsk.v_area TO bsk_reader;
