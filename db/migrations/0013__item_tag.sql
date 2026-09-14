-- 0013__item_tag.sql
-- GEN-1: tags as a ubiquitous, universal classification mechanism, deliberately
-- kept separate from the alignment relations (subject_relation / subject_event).
-- Tags classify; relations connect. Keeping them in their own table with their own
-- verb (`bsk tag`) is what makes the UI's "Tag" vs "Relate to" split real.
--
-- Tags attach to *items* — subjects AND events — so a raw capture can be tagged
-- before it is ever promoted to a subject. A normalized link table (one row per
-- item-tag) is chosen over a per-row array so the "tag universe" (every tag in use)
-- and autocomplete are trivial reads, and add/remove is set-based.
--
-- Unlike the event stream, tags are mutable interpreted classification: adding a
-- tag inserts a row, removing one deletes it (the same way `bsk set key=` removes
-- an attribute). The append-only rule governs source events, not classifications.

CREATE TABLE IF NOT EXISTS bsk.item_tag (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Exactly one of these is set — the item is a subject or an event, never both.
    subject_id uuid        REFERENCES bsk.subject (id),
    event_id   uuid        REFERENCES bsk.event (id),
    -- Tags are stored normalized (lowercased, trimmed) so "Trading" and "trading"
    -- are one tag; the CHECK makes the store reject anything un-normalized.
    tag        text        NOT NULL CHECK (tag = lower(trim(tag)) AND length(tag) > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT item_tag_exactly_one_item
        CHECK ((subject_id IS NOT NULL) <> (event_id IS NOT NULL))
);

COMMENT ON TABLE bsk.item_tag IS 'GEN-1: universal tags on items (subjects or events). Classification, distinct from relations. Mutable (add=insert, remove=delete).';

-- A tag appears at most once per item. Two partial unique indexes because the item
-- is polymorphic (one column is always NULL).
CREATE UNIQUE INDEX IF NOT EXISTS item_tag_subject_key
    ON bsk.item_tag (subject_id, tag) WHERE subject_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS item_tag_event_key
    ON bsk.item_tag (event_id, tag) WHERE event_id IS NOT NULL;

-- "Every item carrying tag X" and the tag universe / autocomplete.
CREATE INDEX IF NOT EXISTS item_tag_tag ON bsk.item_tag (tag);

-- ---------------------------------------------------------------------------
-- Reader views
-- ---------------------------------------------------------------------------

-- Every tag assignment, with the subject's urn/type joined for readability (NULL
-- for event items — an event carries kind, not a subject urn).
CREATE OR REPLACE VIEW bsk.v_item_tag AS
SELECT
    it.id,
    CASE WHEN it.subject_id IS NOT NULL THEN 'subject' ELSE 'event' END AS item_kind,
    coalesce(it.subject_id, it.event_id) AS item_id,
    s.urn  AS subject_urn,
    s.type AS subject_type,
    it.tag,
    it.created_at
FROM bsk.item_tag it
LEFT JOIN bsk.subject s ON s.id = it.subject_id;

-- The live tag universe: every tag currently on at least one item, with its usage
-- count. Drives autocomplete; a tag with no items is not in the universe.
CREATE OR REPLACE VIEW bsk.v_tag_universe AS
SELECT tag, count(*) AS item_count
FROM bsk.item_tag
GROUP BY tag;

GRANT SELECT ON bsk.item_tag, bsk.v_item_tag, bsk.v_tag_universe TO bsk_reader;
