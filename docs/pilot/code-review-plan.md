# Code review fixes plan

**Status:** in progress. Each item is one commit whose message names it (`code review CR<n>`), so
`git log --grep "code review CR"` shows what has landed.

On 2026-11-12 a full review of the repo, weighted toward maintainability, scalability and
extendibility, produced 31 proposals in four tiers. Chris chose P0 to P2 (CR1 to CR26), one
commit each, and skipped P3.

## Decisions

- **CR2:** `bsk migrate` refuses a pending migration numbered below the highest one already
  applied, unless given `--allow-out-of-order`. A branch's migration that reached staging and was
  later renumbered on `main` otherwise breaks every later deploy.
- **CR10:** new features get a thin mock: it stores rows and returns plausible results, and leaves
  refusal wording and reward reconcile to the .NET tests. This narrows "mock-first, as before"
  (xp-domains-plan.md, Phase 1): screens are still built against the mock first.
- **CR14:** reads are filtered in SQL and folded once per settle chain. Bounding a write's settle
  to the days it touched would change food-log-plan.md §3.4, so it is not part of this plan.
- **CR24:** a rename or attribute change appends an `attribute_change` event in the same
  transaction, so "what was this habit's recurrence on a date" can be answered later.

## Items

| ID | Tier | Fix |
|----|------|-----|
| CR1 | P0 | The Pilot writes to staging, and migrates dev on launch, only with the bsk installed from `origin/main`; a failover to staging is read-only. |
| CR2 | P0 | Manual migrations: `migrate-staging.ps1` needs a clean `origin/main`; the runner locks, sets `lock_timeout` and refuses out-of-order versions. |
| CR3 | P0 | Journal HTML is sanitized before it is rendered. |
| CR4 | P0 | A retried agent capture finishes filing what the first call left undone. |
| CR5a–e | P0 | Habit-day notes read back; `get_item` by URN keeps repeat and bounty; the Inbox stops swallowing Enter; `bsk new Task` checks the recurrence pattern; a failed journal append keeps the editor open. |
| CR6 | P1 | Indexes for habit check-offs, `recorded_at` and `(kind, occurred_at)` on the event log. |
| CR7 | P1 | Create, promote and capture filing move into Application services that the CLI, API and MCP share. |
| CR8 | P1 | Shared JSON fixtures check that the TypeScript copies of the C# rules agree. |
| CR9 | P1 | CI fails when `schema.d.ts` is stale; the generated types are strict enough to use. |
| CR10 | P1 | The thin-mock rule, and the mock store split by feature. |
| CR11 | P1 | Playwright and a11y in CI; one web workflow; timeouts, pinned actions, Dependabot. |
| CR12 | P1 | The test database is migrated once as a template and cloned. |
| CR13 | P1 | `db/schema/current.sql` shows every view's current definition, and CI keeps it current. |
| CR14 | P2 | Reads filter and page in SQL, run async, and fold once per settle chain. |
| CR15 | P2 | One searchable subject picker. |
| CR16 | P2 | `SubjectDetail`'s create and edit share one write plan. |
| CR17 | P2 | One write hook that reports failures. |
| CR18 | P2 | One kernel settings record and one Npgsql data source; DI builds the services. |
| CR19 | P2 | `Ports.cs` split by feature; the ledger repository split by concern. |
| CR20 | P2 | Shared helpers for day awards, entry references and event inserts. |
| CR21 | P2 | Home Score goes through `IConsistencyRule`, not type checks. |
| CR22 | P2 | One home time zone. |
| CR23 | P2 | Routes and the mock load lazily. |
| CR24 | P2 | Renames and attribute changes leave an event. |
| CR25 | P2 | Docs say what is true now, and the plans have an index. |
| CR26 | P2 | One mapping from kernel refusals to HTTP and MCP errors. |
