# LifeOS — Life Kernel

Stage 1 of the Life Kernel: the minimum system needed to test whether the Life
ontology survives contact with real daily use. It delivers a PostgreSQL schema,
a `bsk` command-line interface, and a set of diagnostic queries — **no UI, no
agent, no integrations**. See [epic #1](https://github.com/csdelan/LifeOS/issues/1)
for the hypothesis under test and the full scope.

> This repo is an explicit hypothesis test and may be discarded. It is
> deliberately standalone so that discarding it is `rm -rf`, not an untangling
> exercise.

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
