-- 0022__artifact_blob.sql
-- Managed binary-artifact storage (CAP-2 / CAP-4 / JOURNAL-2).
--
-- Today `bsk.artifact` holds text `content` only. This migration lets an artifact
-- ALSO carry a binary payload — audio for a voice note, a PDF or image for a
-- document capture — without touching the text column. The bytes live in a
-- dedicated `bsk.artifact_blob` table keyed 1:1 to the artifact, so:
--   * the text `content` column (and every existing query/view over it) stays clean;
--   * an artifact may have text, bytes, or both (a voice note = audio bytes + the
--     transcript as `artifact.content`; a document = the file bytes + a description);
--   * the append-only guarantee extends to the bytes with the same trigger the rest
--     of the source layer uses (epic invariants 3 and 5) — bytes are never mutated.
--
-- Where the bytes live is a deliberate, confirmed decision: Postgres `bytea`, in the
-- append-only source layer, behind a swappable application port (IArtifactBlobStore).
-- This keeps the single authoritative append-only store, commits the bytes
-- transactionally with their event, and works identically in local Docker and
-- Supabase with no extra infra. If it ever outgrows `bytea`, the port's adapter is
-- swapped for object storage — a localized change, no schema churn. See
-- docs/pilot/binary-artifacts.md.

-- Binary payload + metadata for an artifact. Immutable once written, exactly like
-- the artifact it belongs to.
CREATE TABLE IF NOT EXISTS bsk.artifact_blob (
    artifact_id  uuid        PRIMARY KEY REFERENCES bsk.artifact (id),
    bytes        bytea       NOT NULL,
    content_type text        NOT NULL,
    byte_size    bigint      NOT NULL CHECK (byte_size >= 0),
    -- Content hash (lowercase hex sha256). Drives content-addressed idempotency:
    -- the event that references this artifact carries the same hash as its
    -- external_id, so a re-captured identical file collapses to one event via the
    -- existing (source_id, external_id) unique index (epic invariant 7).
    sha256       text        NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    -- Original filename, for restore-on-export and OS "open with". Optional.
    filename     text,
    recorded_at  timestamptz NOT NULL DEFAULT now(),
    -- The stored size must match the stored bytes: metadata cannot drift from payload.
    CONSTRAINT artifact_blob_size_matches CHECK (octet_length(bytes) = byte_size)
);

COMMENT ON TABLE bsk.artifact_blob IS
    'Binary payload + metadata for an artifact (1:1); immutable, append-only, never mutated.';

-- Extend the append-only guarantee to the bytes. Reuses bsk.deny_mutation() from
-- migration 0002 — UPDATE and DELETE are denied at the database level.
CREATE OR REPLACE TRIGGER artifact_blob_deny_update
    BEFORE UPDATE ON bsk.artifact_blob
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

CREATE OR REPLACE TRIGGER artifact_blob_deny_delete
    BEFORE DELETE ON bsk.artifact_blob
    FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();

-- Look up a blob by its content hash within a source (the dedup probe the write
-- path uses; mirrors the (source_id, external_id) idempotency scope).
CREATE INDEX IF NOT EXISTS artifact_blob_sha256 ON bsk.artifact_blob (sha256);

-- ---------------------------------------------------------------------------
-- Reader view: artifact METADATA (never the raw bytes in a list view)
-- ---------------------------------------------------------------------------
-- Exposes id, the referencing event's kind, and the blob metadata (content_type,
-- byte_size, sha256, filename, has_bytes) so a consumer can list attachments
-- cheaply. The bytes themselves are fetched deliberately — either a reader SELECT
-- of bsk.artifact_blob.bytes, or `bsk artifact get <id> --out <path>` — so a list
-- query never drags multi-MB payloads through the view.
CREATE OR REPLACE VIEW bsk.v_artifact AS
SELECT
    a.id,
    a.recorded_at,
    (b.artifact_id IS NOT NULL) AS has_bytes,
    b.content_type,
    b.byte_size,
    b.sha256,
    b.filename,
    e.id                        AS event_id,
    e.kind                      AS kind
FROM bsk.artifact a
LEFT JOIN bsk.artifact_blob b ON b.artifact_id = a.id
LEFT JOIN LATERAL (
    -- One referencing event per artifact (normally exactly one); earliest wins so
    -- the view is deterministic even if an artifact were ever shared.
    SELECT ev.id, ev.kind
    FROM bsk.event ev
    WHERE ev.artifact_id = a.id
    ORDER BY ev.recorded_at, ev.id
    LIMIT 1
) e ON true;

-- The reader role (migration 0005) is SELECT-only and may read both the metadata
-- view and the raw bytes; it can never write them (append-only + single writer,
-- invariant 9). ALTER DEFAULT PRIVILEGES in 0005 already covers new relations, but
-- grant explicitly so the intent is legible at the point the objects are created.
GRANT SELECT ON bsk.artifact_blob TO bsk_reader;
GRANT SELECT ON bsk.v_artifact    TO bsk_reader;
