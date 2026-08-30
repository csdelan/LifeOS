# Diagnostics

Deterministic SQL diagnostics run by `bsk check`. Each file is one diagnostic —
one rule that fires against the kernel's own data and explains itself. No model
is involved; a diagnostic is plain SQL invoked by the runner
(`LifeOs.Infrastructure.Diagnostics.DiagnosticRunner`).

The runner discovers every `*.sql` file in this directory (embedded into
`LifeOs.Infrastructure`), runs each inside a single **read-only** transaction,
and prints the findings grouped by diagnostic. `bsk check --only <name>` runs
one; `bsk check --json` emits the findings as data for downstream consumers.

The individual Stage 1 diagnostics land in M4.2–M4.6 (neglect, breach, wishes,
drift, unclosed loops, decorative identity, and the capacity-constraint check).
This directory is intentionally empty until then — M4.1 is the runner and the
output format that every one of those must satisfy.

## File naming

```
NN__<slug>.sql
```

`NN` is a two-or-more-digit ordering prefix (controls the order diagnostics are
reported in); `<slug>` is the diagnostic's stable name, the value passed to
`bsk check --only <slug>`. For example `10__neglect.sql` → `neglect`.

## The header

An optional first-comment directive gives the diagnostic its human-readable
**rule statement** — the "why you're telling me this" that prints above the
findings:

```sql
-- title: Neglected subjects (no concerning event within the expected cadence)
```

If omitted, the slug is used as the title.

## The result contract

The query must return **one row per finding**, with exactly these columns (plain
snake_case — the runner reads them by name):

| column          | type   | meaning                                                            |
| --------------- | ------ | ------------------------------------------------------------------ |
| `subject_id`    | uuid   | the subject the finding is about                                   |
| `subject_urn`   | text   | that subject's URN                                                 |
| `subject_type`  | text   | that subject's type (`Project`, `Commitment`, …)                   |
| `subject_title` | text   | that subject's title                                               |
| `summary`       | text   | one line, specific: *why this instance fired*. Must be non-blank.  |
| `evidence`      | jsonb  | a JSON **array** of the rows that triggered it (may be empty)      |

Each element of `evidence` is a JSON object citing a source row. The convention
is a `kind` and an `id`, plus whatever context helps a reader trust the finding:

```json
[
  { "kind": "event", "id": "8f3a2c11-…", "relation": "concerns", "occurred_at": "2026-07-27T09:00:00Z" }
]
```

`kind` is the source table the id points at (`event`, `subject_relation`,
`subject_event`, `subject`). The runner treats the array as opaque structured
evidence: in text mode it prints each element compactly; in `--json` mode it
passes the array through untouched.

A finding whose cause is an **absence** (a Goal with no serving Project) has no
row to cite — its `evidence` array is empty and the `summary` carries the whole
explanation. Every finding is still explained: `summary` is required and the
diagnostic's `title` states the rule.

## Read-only

Diagnostics run inside a `READ ONLY` transaction, so a diagnostic that tries to
write fails loudly rather than mutating the kernel. Keep them to `SELECT`.
