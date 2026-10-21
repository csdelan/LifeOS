-- 0035__artifact_object.sql
-- Artifact bytes kept in object storage (food-log plan F6, FOOD-5; binary-artifacts.md).
--
-- Migration 0022 keeps an artifact's bytes in Postgres (`bsk.artifact_blob`, bytea). On
-- Supabase's free plan the database holds 500 MB, and food photos would fill it, so a host
-- with object storage configured (Supabase Storage) keeps the bytes there instead, and only
-- this row in Postgres: where the object is, and the same metadata `artifact_blob` carries.
--
--   * The object key is content-addressed (`artifacts/<sha256>`), so the same bytes are the
--     same object, and a reader checks what it downloads against `sha256` and `byte_size`.
--   * The row is append-only, like the artifact it belongs to: UPDATE and DELETE are denied.
--   * An artifact's bytes live in one place: a row here or in `artifact_blob`, never both.
--   * Objects are never overwritten (uploads refuse to replace) and never deleted by `bsk`.
--
-- `bsk.v_artifact` now reads both tables; `stored_in` says where the bytes are.

CREATE TABLE IF NOT EXISTS bsk.artifact_object (
    artifact_id  uuid        PRIMARY KEY REFERENCES bsk.artifact (id),
    -- Which object store holds it ('supabase').
    store        text        NOT NULL CHECK (store ~ '^[a-z][a-z0-9_-]*$'),
    bucket       text        NOT NULL CHECK (bucket <> ''),
    object_key   text        NOT NULL CHECK (object_key <> ''),
    content_type text        NOT NULL,
    byte_size    bigint      NOT NULL CHECK (byte_size >= 0),
    sha256       text        NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    filename     text,
    recorded_at  timestamptz NOT NULL DEFAULT now()
);

COMMENT ON TABLE bsk.artifact_object IS
    'Where an artifact''s bytes live in object storage, with their metadata (1:1); append-only, never mutated.';

CREATE OR REPLACE TRIGGER artifact_object_deny_update
    BEFORE UPDATE ON bsk.artifact_object
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

CREATE OR REPLACE TRIGGER artifact_object_deny_delete
    BEFORE DELETE ON bsk.artifact_object
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

CREATE INDEX IF NOT EXISTS artifact_object_sha256 ON bsk.artifact_object (sha256);

-- An artifact's bytes live in one place.
CREATE OR REPLACE FUNCTION bsk.artifact_bytes_in_one_place()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF (TG_TABLE_NAME = 'artifact_object'
            AND EXISTS (SELECT 1 FROM bsk.artifact_blob WHERE artifact_id = NEW.artifact_id))
        OR (TG_TABLE_NAME = 'artifact_blob'
            AND EXISTS (SELECT 1 FROM bsk.artifact_object WHERE artifact_id = NEW.artifact_id)) THEN
        RAISE EXCEPTION 'artifact % already has its bytes stored elsewhere', NEW.artifact_id
            USING ERRCODE = 'unique_violation';
    END IF;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE TRIGGER artifact_object_one_place
    BEFORE INSERT ON bsk.artifact_object
    FOR EACH ROW EXECUTE FUNCTION bsk.artifact_bytes_in_one_place();

CREATE OR REPLACE TRIGGER artifact_blob_one_place
    BEFORE INSERT ON bsk.artifact_blob
    FOR EACH ROW EXECUTE FUNCTION bsk.artifact_bytes_in_one_place();

-- ---------------------------------------------------------------------------
-- Reader view: metadata from wherever the bytes are (never the bytes)
-- ---------------------------------------------------------------------------
-- Same columns as 0022, in the same order, plus `stored_in`: 'database' for
-- `artifact_blob`, the store's name ('supabase') for `artifact_object`, null without bytes.
CREATE OR REPLACE VIEW bsk.v_artifact AS
SELECT
    a.id,
    a.recorded_at,
    (b.artifact_id IS NOT NULL OR o.artifact_id IS NOT NULL) AS has_bytes,
    CASE WHEN b.artifact_id IS NOT NULL THEN b.content_type ELSE o.content_type END AS content_type,
    CASE WHEN b.artifact_id IS NOT NULL THEN b.byte_size    ELSE o.byte_size    END AS byte_size,
    CASE WHEN b.artifact_id IS NOT NULL THEN b.sha256       ELSE o.sha256       END AS sha256,
    CASE WHEN b.artifact_id IS NOT NULL THEN b.filename     ELSE o.filename     END AS filename,
    e.id                        AS event_id,
    e.kind                      AS kind,
    CASE
        WHEN b.artifact_id IS NOT NULL THEN 'database'
        WHEN o.artifact_id IS NOT NULL THEN o.store
    END                         AS stored_in
FROM bsk.artifact a
LEFT JOIN bsk.artifact_blob b ON b.artifact_id = a.id
LEFT JOIN bsk.artifact_object o ON o.artifact_id = a.id
LEFT JOIN LATERAL (
    SELECT ev.id, ev.kind
    FROM bsk.event ev
    WHERE ev.artifact_id = a.id
    ORDER BY ev.recorded_at, ev.id
    LIMIT 1
) e ON true;

-- The reader reads where an object is (to serve its bytes) but never writes it.
GRANT SELECT ON bsk.artifact_object TO bsk_reader;
GRANT SELECT ON bsk.v_artifact      TO bsk_reader;
