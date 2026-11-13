# Code review fixes plan

**Status:** built (2026-11-12). CR1 to CR26 are one commit each, whose message names it
(`code review CR<n>`), so `git log --grep "code review CR"` shows them. They reach staging at the
next push.

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
- **CR17:** `useWriteAction` (`web/src/lib/write-action.ts`) keeps its own pending and error
  state rather than `useMutation`'s, because forms also say problems of their own beside a
  write's (a photo that didn't upload). A form says a failure inline and runs one write at a
  time; a row's button says it in a toast and lets clicks run side by side. A promise nothing
  waited on is said in a toast too.
- **CR18:** the container builds each kernel service from its constructor. A service that takes
  a setting (the coin value, the home time zone, whether photos can be stored) is built by hand
  in `ServiceCollectionExtensions`, so the setting can't fall back to a parameter default unseen;
  `KernelCompositionTests` checks that every registered service can be built.
- **CR21:** the domain fold carries a neutral day `Score`, and `IConsistencyRule` says whether
  it reads one and works the scores out (`ReadsScore`, `DayScores`, `ScoreBar`, `Average`), so
  the fold, `/api/domains` and `bsk domains --json` never ask which rule they hold. The API's
  and the CLI's field names (`homeScore`, `averageHomeScore`) stay, since the web reads them.
  The Home simulator and the CLI's written Home reading still name `HomeConditionRule`: they
  are the home's own.
- **CR22:** the database stays one of two places the home zone is set, rather than the only
  one: making it the only source would change every reader that takes a zone. Instead the API
  refuses to start, and the calendar to answer, while `Rewards:HomeTimeZone` and `bsk.home_zone`
  disagree. The web counts "today" in the zone `public-config.js` names, so a check-off or
  capture made while travelling lands on the home day; the ledger's chart stays in the viewer's
  zone (REWARD-5).
- **CR24:** a rename or attribute change appends an `attribute_change` event in the same
  transaction, so "what was this habit's recurrence on a date" can be answered later.
- **CR25:** the ontology's invariant 9 now reads "one write path": every write goes through the
  Application services, and `bsk`, the API and `/mcp` are doors into them. It said `bsk` was the
  only writer, which stopped being true when the API began to write. The ontology's Stage 1
  scope is marked as history, and its type and event-kind lists match the CHECK constraints.
- **CR26:** the services keep refusing with a plain `ArgumentException` or
  `InvalidOperationException` where they already do. Rewriting those hundreds of throws would
  change nothing a caller sees. They count as refusals only when LifeOS's own code threw them, so
  the same types from Npgsql or the framework are faults. The Application layer's own exceptions
  derive from `KernelRefusalException`, which carries its kind. A food write that raced another is
  now a 409, not a 400, like a timer's; a ledger race is a 409, not a 500. A fault's 500 no longer
  repeats the exception's text.

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

## Found along the way

Defects the fixes turned up, each fixed in a commit of its own that says where it was found.

- **While doing CR12:** one Decision whose review date isn't a date ("soon-ish") made every read
  of `bsk.v_subject` raise, so the subject list, search and detail all failed. Migration 0055
  reads such a date as none.
