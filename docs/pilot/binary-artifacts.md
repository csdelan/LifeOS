# Managed Binary Artifacts — where the bytes live

A design note for the Pilot kernel. It records the storage decision behind
[migration 0022](../../db/migrations/0022__artifact_blob.sql) and the write/read paths that
close the "Managed binary artifact storage" and unreachable `voice` items in the
[kernel build plan](kernel-build-plan.md) (Band E), serving CAP-2 (voice), CAP-4
(documents/attachments), and — later, on the same infra — JOURNAL-2 (inline media). It does
not re-open the append-only source spine or the single-writer invariant; it extends both to
bytes.

**Approved by Chris 2026-09-20.** Option (a) — Postgres `bytea` in the append-only source
layer, behind a swappable port — chosen as recommended. Object storage (Supabase Storage /
S3) is explicitly *not* built now; the port is the seam that lets it be added later.

## The problem

Today `bsk.artifact` holds text `content` only. A voice note, a PDF, or an image has no home:
the `voice` event kind exists in the ontology but has no write path, and CAP-4 attachments
have nowhere to put the file. We need an artifact to be able to carry a **binary payload**
with metadata (content type, size, content hash, original filename), while keeping every
source-layer guarantee: append-only, provenance, bitemporality, idempotency, and `bsk` as the
only writer.

## The decision — bytea, in the source layer, behind a port

The bytes are stored as Postgres `bytea` in a dedicated `bsk.artifact_blob` table, keyed 1:1
to `bsk.artifact`. The alternatives considered were (b) object storage with only a key +
metadata in Postgres, and (c) a local file directory + stored path. We chose (a) because:

- **One authoritative, append-only store.** The whole ontology thesis is a single immutable
  Postgres source of truth. Bytes in `bytea` stay inside it and inherit the exact same
  append-only trigger as `event` and `artifact`.
- **Transactional with the event.** The event, its artifact (text sidecar), and its bytes
  commit together or not at all — impossible with object storage, where the bytes live in a
  separate system.
- **Zero extra infra, dev = prod.** It works identically in local Docker and Supabase with no
  buckets, credentials, or lifecycle rules, and is trivial to test with Testcontainers.
- **Modest scale.** Personal audio notes and PDFs are small. If it ever outgrows `bytea`, the
  swap is localized (see the port, below) rather than a schema migration.

(c) is rejected outright: a stored path is desktop-only and breaks the moment a second device
or the web API needs the bytes.

### The swap seam — `IArtifactBlobStore`

`IArtifactBlobStore` (`src/LifeOs.Application/Abstractions/Ports.cs`) is the port that isolates
*where the bytes live* from everything above it:

- `PutAsync(NewBinaryCapture)` — writes the event + artifact + payload as one atomic unit,
  deduplicated by content hash (below). Returns the event/artifact ids.
- `GetAsync(artifactId)` / `GetMetadataAsync(artifactId)` / `ExistsAsync(artifactId)` — the
  read/export side.

The default adapter, `NpgsqlArtifactBlobStore`, implements this against `bytea` in one
transaction. A future **Supabase-Storage / S3 adapter** implements the *same* interface —
writing bytes to the object store and the artifact / blob-metadata / event rows to Postgres —
with no change to the Application services, CLI, or reader views above it, and no schema churn.
That adapter is deliberately **not** built yet.

## The schema (migration 0022)

`bsk.artifact_blob`:

| Column | Meaning |
| --- | --- |
| `artifact_id` (PK, FK → `bsk.artifact`) | 1:1 with the artifact; keeps the text `content` column clean. |
| `bytes` (`bytea`) | The payload. |
| `content_type` | MIME type. |
| `byte_size` | Size in bytes; a CHECK asserts `octet_length(bytes) = byte_size` so metadata can't drift from payload. |
| `sha256` | Lowercase-hex content hash; a CHECK enforces `^[0-9a-f]{64}$`. |
| `filename` | Original filename (optional), for export and OS "open with". |
| `recorded_at` | When the bytes were stored. |

An artifact may therefore have **text, bytes, or both** — a voice note is audio bytes *plus* the
transcript as `artifact.content`; a document is the file bytes *plus* an optional description.

**Append-only** is extended to the bytes with the same `bsk.deny_mutation()` trigger the rest of
the source layer uses: UPDATE and DELETE on `artifact_blob` are denied at the database level.

**Reader.** `bsk.v_artifact` exposes *metadata only* — `id`, the referencing event's `kind`,
`has_bytes`, `content_type`, `byte_size`, `sha256`, `filename` — so a list query never drags a
multi-MB payload through the view. The SELECT-only `bsk_reader` role can read both the metadata
view and (deliberately, for a future streaming API) the raw `artifact_blob.bytes`, but can never
write them — append-only and single-writer hold below the door.

## Idempotency — content-addressed, aligned with the existing invariant

Re-capturing an identical file must not duplicate bytes. The payload's `sha256` is used as the
event's `external_id`, so the write is deduplicated by the *existing* `(source_id, external_id)`
unique index (epic invariant 7) — no new mechanism. `PutAsync` first probes for an event with the
same `(source_id, sha256)`; on a hit it returns the pre-existing ids with `Deduplicated = true`
and inserts nothing. Dedup is therefore scoped per source, exactly like every other idempotent
ingest.

## The write/read paths (all `bsk`, the only writer)

- `bsk attach --file <path> [--content-type <ct>] [--description <text>]` — a document/attachment
  capture (CAP-4 / D4 reference flavor): a `note` event whose artifact carries the file's bytes,
  copied into the managed store, with the description as the text sidecar. Flagged into the Inbox
  for triage (INBOX-1).
- `bsk voice --audio <path> [--transcript <text> | --transcript-file <path>] [--content-type <ct>]`
  — a voice capture (CAP-2): a `voice` event + audio artifact, with the transcript (if any) as the
  text sidecar. Closes the previously unreachable `voice` kind. Transcription itself is app-side;
  the kernel stores audio + text. Flagged into the Inbox.
- `bsk artifact get <id> --out <path>` — exports an artifact's bytes back out byte-for-byte. The
  supported read path for payloads (used by tests and, later, the web API).

The content type is inferred from the file extension when not given; the caller's explicit
`--content-type` always wins. A configurable size guard (`BSK_MAX_ARTIFACT_BYTES`, default 25 MiB)
rejects oversized payloads before any bytes are written.

## Out of scope

- The object-storage adapter (the port seam is left clean for it).
- Transcription (app-side; the kernel stores whatever transcript it is given).
- The web API that will stream bytes to a browser.
- CAP-5 (URL capture) — a URL is plain text, not a binary artifact.
