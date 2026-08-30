# LifeOS — Life Kernel

Stage 1 of the Life Kernel: the minimum system needed to test whether the Life
ontology survives contact with real daily use. It delivers a PostgreSQL schema,
a `bsk` command-line interface, and a set of diagnostic queries — **no UI, no
agent, no integrations**. See [epic #1](https://github.com/csdelan/LifeOS/issues/1)
for the hypothesis under test and the full scope.

> This repo is an explicit hypothesis test and may be discarded. It is
> deliberately standalone so that discarding it is `rm -rf`, not an untangling
> exercise.

## High-level goals

> **A note on the name.** "BlueSkies" is also the name of an existing personal
> **trading system**, and that is no coincidence: BlueSkies Life Intelligence
> (this project, with the Life Kernel as Stage 1) **builds upon** the trading
> system's architecture. The trading system executes and manages market
> activity; the same event-sourced, continuously-learning foundations are
> carried forward and generalized here to manage a life rather than a portfolio.
> New readers should treat this as an evolution of that architecture, not an
> unrelated project that happens to share a name.

**The BlueSkies Life Intelligence vision.** BlueSkies is a continuously
learning personal intelligence system that helps me live and work in alignment
with my chosen identity, values, goals, projects, and commitments.

- **Builds a trustworthy, evolving model of my life.** It develops its
  understanding from configurable data sources, direct interaction, observed
  events, reflection, and the outcomes of earlier decisions.

- **Protects my attention.** It surfaces what matters now, filters out
  irrelevant noise, flags neglected commitments and conflicts, and presents only
  the most useful next actions for my current focus.

- **Acts as an adaptive coach and thinking partner.** It helps me establish the
  rules, habits, and strategies that move me toward who I want to become. It may
  intervene when I drift, but its interventions stay explainable, configurable,
  respectful of my autonomy, and bounded by explicit limits. It learns the
  timing, communication style, and coaching techniques that help me most —
  without deception, coercion, or unhealthy optimization.

- **Is my launchpad for delegating work** to people and AI agents. It picks the
  right resources, supplies context, controls permissions, tracks progress,
  triages their questions, and escalates only the decisions that genuinely need
  me.

- **Works across every interface** — mobile, desktop, voice, text, wearables,
  and whatever comes next. These are different doors into the same authoritative
  personal intelligence, not separate systems.

- **Accumulates a governed history** of my events, goals, decisions,
  commitments, habits, projects, observations, and outcomes. As AI and
  human-computer interfaces advance, this history enables increasingly ambient
  and proactive assistance.

The long-term destination is a trusted cognitive companion that understands how
I think and what I care about deeply enough to guide me with minimal effort —
even through future interfaces such as BCI — while leaving me in control of my
identity, choices, attention, and life.

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/) (pinned in `global.json`)
- [Docker](https://www.docker.com/) — for local Postgres and for the
  Testcontainers-based integration tests
- PowerShell 7+ (for `run.ps1` / `test.ps1`)

## Layout

```
db/migrations/       Versioned SQL migrations, named NNNN__name.sql
src/
  LifeOs.Domain          Domain model (entities, rules) — filled in by later issues
  LifeOs.Application      Transport-neutral application services
  LifeOs.Infrastructure   Npgsql + Dapper data access and the migration runner
  LifeOs.Cli              The `bsk` executable (System.CommandLine)
tests/
  LifeOs.Tests            xUnit v3 + Testcontainers integration tests
run.ps1              Build the solution (and optionally run `bsk`)
test.ps1             Run the test suite
docker-compose.yml   Local persistent Postgres for interactive use
```

The layers depend inward only: `Cli → Infrastructure → Application → Domain`.

## Build and test

From a clean clone:

```powershell
./run.ps1        # restore + build the whole solution
./test.ps1       # run the test suite (needs Docker running)
```

`run.ps1` also forwards arguments to the CLI, e.g. `./run.ps1 migrate --json`.
`test.ps1` forwards arguments to the test runner, e.g.
`./test.ps1 --filter-class LifeOs.Tests.SmokeTests`.

Tests are xUnit v3 on Microsoft.Testing.Platform. `test.ps1` runs the test
project directly (xUnit's recommended way to launch an MTP test app) rather than
via `dotnet test`.

## Local database

The test suite manages its own throwaway Postgres via Testcontainers and needs
no setup beyond a running Docker daemon. For interactive local use, start the
persistent development database and apply migrations:

```powershell
docker compose up -d
./run.ps1 migrate
```

This uses the local development connection string
`Host=localhost;Port=5432;Database=lifeos;Username=lifeos;Password=lifeos`.
Override it with `--connection` or the `BSK_CONNECTION_STRING` environment
variable.

## Migrations

Migrations are plain SQL under `db/migrations`, named `NNNN__name.sql` (e.g.
`0001__baseline.sql`) and applied in ascending version order. They are embedded
into `LifeOs.Infrastructure` so the runner works from any working directory.

The runner (`bsk migrate`, and `MigrationRunner` in code):

- creates a `public.schema_migrations` history table if needed,
- applies each not-yet-recorded migration inside its own transaction,
- is safe to re-run — already-applied migrations are skipped, and one whose SQL
  has changed since it was applied is rejected rather than silently re-run.

Migrations are immutable once applied. To change the schema, add a new migration
with the next version number.

## Source and derived layers

Source-of-truth tables live in the `bsk` schema; derived, rebuildable
projections live in `bsk_derived`. Nothing in `bsk_derived` is canonical — it is
regenerated from `bsk` on demand.

`subject_current` (the one Stage 1 projection) folds `state_change` events to
the current status of each subject. A status changes only by appending a
`state_change` event whose payload names the subject and new status:

```json
{ "subject_id": "<uuid>", "status": "<text>" }
```

`bsk rebuild` truncates and repopulates every derived table from source inside a
transaction. Its output is deterministic and byte-comparable across runs. Use
`bsk rebuild --verify` to diff the materialized state against source and report
any drift without changing anything:

```powershell
./run.ps1 rebuild            # regenerate derived tables
./run.ps1 rebuild --verify   # report drift (exit 1 if drifted), change nothing
```

## Diagnostics (`bsk check`)

`bsk check` runs the deterministic SQL diagnostics and prints an evidence-bearing
report — every finding names the subject it concerns, the rule that fired, and
the evidence rows (event/relation ids) that triggered it. Explaining itself is
built in: no finding is unexplained.

```powershell
./run.ps1 check                 # run every diagnostic, grouped by kind
./run.ps1 check --only neglect  # run one
./run.ps1 check --json          # machine-readable, for downstream consumers
```

Findings are advisory, not a pass/fail gate: a completed run exits 0 whether or
not it flagged anything, so `bsk check` composes in scripts. `--json` carries the
findings (and their evidence arrays) as data.

Each diagnostic is a plain SQL file under `db/diagnostics`, embedded into
`LifeOs.Infrastructure` and run inside a single read-only transaction — the
runner never involves a model. The individual Stage 1 diagnostics land in
M4.2–M4.6; until then `bsk check` reports that none are configured. See
[`db/diagnostics/README.md`](db/diagnostics/README.md) for the file-naming and
result contract every diagnostic must satisfy.

## Read-only access (`bsk_reader`)

`bsk` is the only write path. Every other consumer reads Postgres directly
through the `bsk_reader` role, which has `USAGE` + `SELECT` and no write grants.
Flattened views unpack common jsonb attributes into columns for convenience:
`bsk.v_subject`, `bsk.v_event`, `bsk.v_subject_relation`, `bsk.v_subject_event`,
and `bsk.v_subject_current`.

Point read-only consumers (Python, BI, ad-hoc `psql`) at `bsk_reader`; never at
the owner used by `bsk migrate` / `bsk rebuild`. The role's password in
migration 0005 is a **local-development** credential (matches
`docker-compose.yml`); production supplies its own.

A Python script proves the door end-to-end (reads succeed, writes are rejected):

```bash
pip install "psycopg[binary]"
python scripts/verify_reader.py     # uses BSK_READER_DSN, or the local dev default
```
