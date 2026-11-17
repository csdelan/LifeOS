# Code review fixes plan

**Status:** CR1 to CR26 built (2026-11-12). CR27 to CR56, the second review's P0, P1 and P2, built
(2026-11-13), and CR57 for its choice A; Chris chose B and C on 2026-11-15 (CR124, and CR126, whose
staging switch waits for his steps in deploy.md) ([Second review](#second-review-2026-11-13)). The third review scored the code 6.1 of 10, and Chris
chose its P0 and P1, CR58 to CR67, built (2026-11-13), then its P2, CR68 to CR90, built
(2026-11-13) ([Third review](#third-review-2026-11-13)). The fourth review scored 6.4, and CR91
to CR107 are built from it ([Fourth review](#fourth-review-2026-11-13)). The fifth scored 6.5 and
carries every proposal the fourth left open. R1 to R11 and R14 to R18 are built as CR108 to CR123
(2026-11-14 and 2026-11-15), its choice D as CR125 (2026-11-15), R13 and R18's query budget as
CR127 and R12 as CR128 (2026-11-15), and R19 to R35 are still proposed
([Fifth review](#fifth-review-2026-11-14)). From 2026-11-17 a review runs every night and builds
up to ten of its own fixes as CRs ([Nightly reviews](#nightly-reviews)).
Each CR is one commit whose message names it (`code review CR<n>`), so
`git log --grep "code review CR"` shows them. They reach staging at the next push.

Each full review also scores the code from 1 to 10 against `docs/agents/quality-scorecard.md`,
which keeps the scores and measured numbers over time; the target is 7 to 8 in every area.

On 2026-11-12 a full review of the repo, weighted toward maintainability, scalability and
extendibility, produced 31 proposals in four tiers. Chris chose P0 to P2 (CR1 to CR26), one
commit each, and skipped P3 (listed under the second review, so it isn't lost).

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
- **CR42:** the kernel's today is `IHomeCalendar.TodayAsync`, the home-zone date at `IClock`'s
  now. The review proposed backing it with `bsk.home_today()`, but that reads the database's
  clock, which no test can move, and the review, repeating-Task and activity tests set today with
  a `TestClock`. A view's `bsk.home_today()` still reads the database's clock.
- **CR46:** `SettlementService.SettleAsync` heals every item first only when asked (`items:
  true`), since that reads every item ever paid. `bsk rewards reconcile` asks; the Character
  page's settle, which runs each time the page opens, doesn't, and leaves each item to the
  reconcile its own write runs.
- **CR51:** a change that follows from another (a repetition redated by its series' new pattern,
  an open repetition or upcoming occurrence taking its series' new title or field) is recorded on
  the item it changed as `derived`, citing the series' own change, because the ontology requires
  a derived event to cite its sources (the `event_derived_has_sources` CHECK). A rename's follow
  cites the change made in its transaction; a redate or a followed field runs apart from it and
  cites the series' newest `attribute_change` on those keys, or is `declared` when there is none.
- **CR54:** `life_snapshot` reads only open items now, but still lists every unarchived value,
  whatever its status, as it did.
- **CR57 (choice A):** the API catches up as it starts, in `StartupCatchUp`, before it answers
  anything: the repetitions repeating Tasks are owed, then `SettlementService` (food days,
  activity days already synced, rank-ups). Fly stops the machine when idle, so the first request
  of most days starts it, and agents reading `/mcp` see today's chores and yesterday's XP. It
  doesn't sync Oura (the activity plan's "no background job in phase 1" stands) or heal every
  item (CR46). A catch-up that fails or runs past 20 seconds is logged and the API starts anyway;
  the pages still catch up when they open, for a machine that stayed up past midnight.
  `CatchUp:OnStart` false turns it off; test hosts start with it off.
- **CR61:** every N months counts each date from the start: the k-th date is the start plus k×N
  months, its day clamped to the month's last. A repeat started Jan 31 falls on Feb 28, Mar 31,
  Apr 30. This is what `addMonthsClamped` meant, not a new choice: the SQL stepped from the
  date before and stuck on the 28th, and the web copied it. A monthly day before the start no
  longer counts either. A series already running on a drifted date (one started on the 29th to
  31st, every N months, not from completion) gets its next dates on the true day, and a date
  already made on the drifted day stays. Before the push that deploys migration 0063, this,
  run in the Supabase SQL editor, lists any such series on staging:

  ```sql
  SELECT s.type, s.title, s.attributes->'recurrence' AS recurrence,
         coalesce(s.attributes->>'start', s.attributes->>'start_date') AS start
  FROM bsk.subject s
  WHERE s.attributes->'recurrence'->>'freq' = 'interval'
    AND s.attributes->'recurrence'->>'unit' = 'months'
    AND s.attributes->'recurrence'->>'from' IS DISTINCT FROM 'completion'
    AND extract(day FROM coalesce(bsk.try_to_date(s.attributes->>'start'),
                                  bsk.try_to_date(s.attributes->>'start_date'),
                                  (s.created_at AT TIME ZONE bsk.home_zone())::date)) >= 29;
  ```

- **CR65:** an Appointment occurrence carries `attributes.occurrence`, the date of the pattern
  it stands for, beside `attributes.date`, the day it's on, as a Task repetition does (D16), and
  migration 0064 keys (series, occurrence). Occurrences made before have no `occurrence` and
  aren't given one: a migration writing attributes would change items with no event saying so.
  Materialize treats each as standing for its `date`, which is what it did before, and their
  (series, NULL) pairs never clash, so the index builds on what staging holds. One run reaches
  two years past today at home and makes at most 750 (two years of a daily series); a series
  started long ago whose backlog is bigger is made in steps, an earlier day first.
- **CR66:** a `bsk` older than the database refuses every verb but `migrate` and `check`, not
  only the ones that write. Sorting fifty verbs into readers and writers is a list someone would
  have to keep, and a reader built before a view changed can misread it too. The Pilot reads
  through Npgsql, not `bsk`, so it only loses its writes until `install-bsk.ps1` runs again, and
  shows `bsk`'s reason when one is refused. The review's other half, the Pilot comparing the
  installed copy's commit with `origin/main`, isn't built: it needs a fetch at startup, and the
  schema check covers the case that does harm (new rules in a migration). A C#-only rule change
  still reaches the Pilot only through `install-bsk.ps1`, as before.
- **CR124 (choice B):** the mock's copy of the reward economy (about 3,200 lines in `lib/mock`,
  with about 1,800 of tests) is no longer kept in step with the kernel. Nothing is rewritten now:
  the next time a rule it copies changes in the kernel, that part of the mock becomes canned reads,
  and the exact amounts are checked in the .NET tests only. This extends CR10's thin mock from new
  features to the old ones. AGENTS.md says so beside the thin-mock rule.
- **CR125 (choice D):** a Goal can't be Active without a target date (GEN-8), and a Habit serves
  at least one Goal or Identity Statement (GEN-3), refused by the services every door shares
  (`SubjectGates`, `SubjectGateException`, an invalid request): a status of Active
  (`StatusService`), clearing an Active Goal's date (`AttributeService`), a Habit made any way but
  under a Goal or Identity Statement it serves, promotion's included (`SubjectService`), and
  unlinking its last one (`RelationService`, which locks the Habit while it counts). Only the
  change that breaks a gate is refused, so items made before it are left alone. The web's form
  keeps its copy, so focus still moves to the field; `tests/rules/gates.json` ties it to the
  kernel's, and `subject-detail.tsx`'s copy that couldn't run is gone. A Goal's "≥1 Value" stays a
  form rule, as GEN-8 calls it provisional. The Pilot is frozen and keeps its own checks, but makes
  a Habit with its first `serves` link as the parent, since `bsk new` now refuses one without.
  To count what was there before, in the Supabase SQL editor:

  ```sql
  SELECT 'Active Goals without a target date' AS what, count(*) AS how_many
  FROM bsk.v_subject_current c
  JOIN bsk.subject s ON s.id = c.subject_id
  WHERE s.type = 'Goal' AND c.status = 'Active'
    AND btrim(coalesce(s.attributes->>'target_date', '')) = ''
  UNION ALL
  SELECT 'Habits serving no Goal or Identity Statement', count(*)
  FROM bsk.subject h
  WHERE h.type = 'Habit'
    AND NOT EXISTS (
        SELECT 1 FROM bsk.subject_relation r
        JOIN bsk.subject p ON p.id = r.to_subject
        WHERE r.from_subject = h.id AND r.relation = 'serves' AND p.type IN ('Goal', 'Value'));
  ```

- **CR126 (choice C):** the API writes as `bsk_writer` (migration 0071), a role that reads and
  writes every table in `bsk`, `bsk_derived` and `bsk_ops` but owns none and can create nothing, so
  it can't alter or drop a table or switch off the append-only triggers. On `bsk.event` and the
  artifact tables it can only add. It's NOLOGIN, as `bsk_backup` is: each project gives it a password
  by hand, and only the Fly app gets its string (`BSK_WRITER_CONNECTION_STRING`); `bsk`, the Pilot,
  the backup and CI's `bsk migrate` keep the owner's. The API takes the writer from
  `ConnectionStrings:Writer` or that variable, and the owner's only while neither is set, so the
  push that makes the role can't take staging down; `fly-setup.ps1` puts exactly one of the two on
  Fly. The tests' server, `ApiTestHost` and `live-check.ps1` connect as the role, so every endpoint
  test is a check of its grants, and `WriterRoleTests` reads the catalog so a later table is
  checked the day it's added (an append-only one fails until its migration revokes UPDATE and
  DELETE). Staging switches by Chris's steps in deploy.md (Switching the API to bsk_writer).
  **Step 2, after he says staging has switched:** the API stops falling back to the owner outside
  Development, and `fly-setup.ps1` requires `BSK_WRITER_CONNECTION_STRING`.

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
- **During the final checks:** the golden path "quick capture lands in Inbox" failed about one run
  in three, on `main` too. The Inbox opens its newest item on a desktop, so the new capture could
  show in the detail pane as well as its row, and the test's text match found three. It now takes
  the first.
- **At the first push:** CR11's CI job failed three golden paths that pass on Chris's PC. They
  pin the clock with a local time such as `new Date("2026-11-07T08:30:00")`, which the test
  runner read in UTC while the page ran in Pacific, eight hours apart. `playwright.config.ts` now
  runs the runner in Pacific too. The deploy was skipped, so staging didn't change.

## First review's skipped P3

For the record, since they weren't written down when Chris skipped them:

- Adding a web screen means editing four lists (router, nav, palette, a11y spec), and the
  list-plus-detail wiring is repeated per page. The four lists are one since CR97.
- Eight or more day parsers with different strictness (a bad `from=` silently returns something).
- `v_subject_current` reads a table that lags until `bsk rebuild`; the Pilot still reads it.
- The Pilot has no retirement trigger, and its 25 SQL reader methods are untested.
- Hygiene: unused `@radix-ui` packages and an unpinned `openapi-typescript`.

## Second review (2026-11-13)

A second pass looked for what the first missed or left half done, with the same weighting. It
produced 30 proposals in three tiers, a P3 list and three choices for Chris. Chris chose P0
first, then P1, then P2, one commit each. The numbers below are the review's; a proposal gets a
CR number when Chris chooses it.

### P0: built (CR27 to CR31)

| ID | Fix |
|----|-----|
| CR27 | `migrate-staging.ps1` no longer leaves the staging connection string (or any `.env` key) in the caller's shell; `seed.ps1` needs a throwaway database on localhost. |
| CR28 | `recent_changes` leaves a switched-off area out by one table (`McpAreas`), and fails closed: an event no area claims shows while an area is off only when its payload is the core's. Food photos were leaking. |
| CR29 | `/mcp` refuses a write tool in its call filter, not only in each tool, so a new write tool can't forget. |
| CR30 | `TRUNCATE` is refused on the append-only tables, as UPDATE and DELETE are. |
| CR31 | The diagnostics count days in the home zone, not UTC. |

### P1: built (CR32 to CR40)

| ID | Review # | Fix |
|----|----------|-----|
| CR32 | 6 | The food views say the predicates their partial indexes are built on (migration 0059), and `v_fast` folds the meals once instead of once per fast. |
| CR33 | 7 | An index on `bsk.event.artifact_id` (0060); the artifact lookup's `OR` split into two keyed halves. |
| CR34 | 8 | Ledger reads by subject and by trigger use expression indexes that match the view (0061). The keyed-award read kept its query: the trigger index serves it, so `award_key = ANY(@keys)` wasn't needed. |
| CR35 | 9 | NOT VALID CHECK constraints on every payload key a view casts (0062): `archived`, `item_id`, `occurrence`, a timer's `subject_id`, and the ledger's `subject_id`, `dollars`, `coin_rate` and `amounts`. |
| CR36 | 13 | Writes refresh named groups of cached reads; Archive and Restore share one list, Habits and the Inbox among them. |
| CR37 | 12 | The palette and New dialog read subjects only while open; the Map's outline is built from the cached subject and edge reads; Habits reads the day's occurrences, not the dashboard. The dashboard itself stays one read, since the mock's carries its seeded focus. |
| CR38 | 11 | Live reads and writes pass the API's data through `fits<T>()` (`lib/live/fits.ts`), so tsc checks the generated type against the app's. `production-ui-types.ts` stays; its types become aliases of the generated ones feature by feature, as each is next worked on. |
| CR39 | 10 | Every test class runs alongside the others: 14 left the serial collection, 69 take a `ClassDatabase`, and the collection is gone. The suite takes about 40 seconds instead of 90. |
| CR40 | 14 | `scripts/live-check.ps1` and `scripts/live-check/fakes.mjs` run the throwaway-database live check in one command, and `-Stop` undoes it. `docs/agents/verifying.md` still describes the steps by hand: another session has uncommitted edits to it, so it should point at the script once those land. |

### P2: built (CR41 to CR56)

| ID | Review # | Fix |
|----|----------|-----|
| CR41 | 24 | `SubjectTypes.All`, `Creatable` and `ParseCreatable`, and `EventKinds.All`, replace the copied lists; promoting reads the type through them, so "goal" no longer reaches the CHECK. |
| CR42 | 22 | The kernel asks for today one way, `IHomeCalendar.TodayAsync` at `IClock`'s now; the food log no longer asks the database. |
| CR43 | 18 | `SubjectService.ResolveAsync` takes an id, so `ResolveWriteRefAsync` and its copies are gone; the reads' own lookups share `FindByIdOrUrnAsync`. |
| CR44 | 17 | `InboxFilingService.CaptureAsync` writes and files every capture, the web's typed quick capture included; `JournalService.AppendAsync` writes an entry and its edge in one transaction. |
| CR45 | 16 | An agent's writes go through a kernel of its own (`SourceKernels`, built by `AddLifeOsKernel`); a test checks every Application service is registered. |
| CR46 | 15 | `SettlementService.SettleAsync` is the one settle for the page and the CLI; `ActivitySyncService` syncs Oura and pays what the sync made due. |
| CR47 | 19 | The ledger repository holds only the ledger; the reward catalog and the domain credit have readers of their own, the credit read in one repeatable-read transaction. |
| CR48 | 20 | `RankUpService` folds the domains and pays rank-ups; `ItemPrices` prices an item, and a repetition by its series (D16), in one place. |
| CR49 | 21 | `ReservedAttributes` lists every key only its own verb may write; create and set check it at every door. |
| CR50 | 23 | `DomainProgress.RuleLevel` and `TodayRule`, and `DomainFigures`' rounding, so `/api/domains` and `bsk domains --json` agree. |
| CR51 | 30 | A repetition redated by its series, and a repetition or occurrence following its series' title or field, leave a derived `attribute_change` citing the series' change. |
| CR52 | 29 | `ReaderRoleTests` reads, as `bsk_reader`, every table and view `pg_class` lists. |
| CR53 | 25 | `tests/rules/diet.json` and `statuses.json`, answered by both rule-parity suites. |
| CR54 | 26 | `life_snapshot`, `get_item`, `inbox` and dismiss read only what they show; the journal and history endpoints take a limit; the review recap reads the period's repetitions in one call (`GET /api/tasks/series/due`). |
| CR55 | 27 | Success toasts are said in `decorateWrites`, for both write clients; a lint rule keeps `sonner` out of the clients. |
| CR56 | 28 | The golden-path and a11y specs are split by feature (`web/e2e/golden`, `web/e2e/a11y`), and two workers run them. |

### P3: when working in that area

Health range reads and `try_to_date`'s subtransactions; one `bsk.record_key()`; a cached
domain-credit read if 8 isn't enough; drop two unused GIN indexes; a longer migration command
timeout and `NOT VALID` CHECK widening; CR17 leftovers in ten components; small repeats left after
CR20; Infrastructure faults answered as refusals; test classes that fix "today" at load; pin the
SDK and Docker image; CI builds the Pilot; `migrate.yml`'s default Fly config; `.dockerignore`
misses `.claude/`.

### For Chris to decide

- **A.** Run the catch-up jobs (repeating Tasks, food days, rank-ups, the item sweep) when the
  API starts, reversing the recurring-tasks plan's "no background job"; or document the gap.
  *Chosen 2026-11-13: run them at start, without the item sweep or an Oura sync (CR57).*
- **B.** Stop maintaining the mock's copy of the reward economy, replacing each part with
  canned reads when its rule next changes, and check exact amounts in .NET.
  *Chosen 2026-11-15, as recommended (CR124).*
- **C.** A non-owner database role for the API, so it can't drop the append-only triggers.
  *Chosen 2026-11-15: `bsk_writer`, with the switch in two steps so staging never breaks (CR126).*

## Third review (2026-11-13)

A third pass, with the same weighting, by six reviewers working side by side on one area each,
produced 33 proposals in three tiers and a P3 list. It also made the first scores on the quality
scorecard (`docs/agents/quality-scorecard.md`): 6.1 of 10, against Chris's target of 7 to 8 in every
area. Nine of its proposals finish a CR that was left half done (CR9, CR14, CR17, CR18, CR21, CR38,
CR42, CR44, CR51). Each was a rule kept by convention with nothing that fails when it's broken, while
the CRs that came with such a check (CR29, CR52, CR55) held. So each fix below ships with the check
that would have caught it. The numbers are the review's; a proposal gets a CR number when Chris
chooses it.

### P0 and P1: built (CR58 to CR67)

Each shipped with the check that would have caught it. Where that was a test of existing
behaviour (CR60 to CR63, CR65, CR66), it was run against the code before the fix and failed.

| ID | Review # | Fix |
|----|----------|-----|
| CR58 | 1 | A page left open past midnight checks habits off on the home day it is now, not the day it loaded (`useHomeToday`, `HomeDayRollover`). |
| CR59 | 2 | `bsk` says on stderr when `BSK_CONNECTION_STRING` points it off this machine; `migrate-staging.ps1` lets `-EnvFile` win over the shell and refuses when they disagree; the docs stop telling Chris to keep the variable set. Chris checks his PCs for a saved one. |
| CR60 | 3 | The detail panel's Save and the Map's inline create run one write at a time through `useWriteAction`, and say a refusal inline. |
| CR61 | 4 | "Every N months" repeats count from their start (migration 0063, `tests/rules/recurrence.json`). Run the query in the decisions on staging before the push. |
| CR62 | 5 | Renaming an appointment series, the domain fold and exception days take today from the home calendar. |
| CR63 | 6 | `InboxFilingService` stores, files and links a document or voice note to the end, and a re-sent file never triaged is flagged; `bsk journal <item>` writes the entry and its link in one call, and the Pilot uses it. Run `install-bsk.ps1` after the push. |
| CR64 | 7 | The Connected agents page says whether agents may write, and each agent whether it does and as whom, by the rule `/mcp` applies (`McpWriteAccess.ReadOnlyBecause`). |
| CR65 | 8 | Making appointment occurrences is bounded (two years, 750 a run), locked per series, and keyed on (series, occurrence) by migration 0064. |
| CR66 | 9 | A `bsk` older than the database refuses every verb but `migrate` and `check` (`StaleBskGuard`). |
| CR67 | 10 | The Pilot's New item closes once the item exists, and names each follow-up that failed (`NewItemWrites`). |

### P2: built (CR68 to CR90)

Each shipped with the check that would have caught it, run against the code before the fix, where
it failed. CR76 took a second commit, a longer timeout for Browse's two thousand-item tests.

| ID | Review # | Fix |
|----|----------|-----|
| CR68 | 11 | Request idempotency moves into Application: the capture's text and event in one transaction, "already written" as a kernel result. Before agent access level `full`. |
| CR69 | 12 | System follow-ups (lab-visit range sync, a series' start) are recorded as derived, citing their cause; metric and space edits in one transaction each. |
| CR70 | 13 | `v_habit` reads `allows_partial` the way `AdherenceService` does, so one bad value can't break every habit read. |
| CR71 | 14 | `bsk` stores photos and reports in the database only on localhost without object storage. |
| CR72 | 15 | Space share, "a Value needs a statement" and the food-edit merge move out of the doors into Domain and Application. |
| CR73 | 16 | The measurement catalog and currency labels come from the API, not a copy. |
| CR74 | 17 | One `isOverdue`, so Browse's Overdue leaves finished items out. |
| CR75 | 18 | `usePersistedChoice` for the ten remembered view choices; a renamed option can't crash a render. |
| CR76 | 19 | `/api/subjects` filters by status and limit; Focus, Tasks, the palette and pickers read open items; Browse caps what it draws; Resolved loads older. |
| CR77 | 20 | `EventIndexTests` needs a read per index and checks the index is used for the lookup; the timer view, six newest-version views and the recap use their indexes. |
| CR78 | 21 | The Food and Activity paid reads are bounded by date in SQL, in one shared read. |
| CR79 | 22 | Oura sync fetches before it opens its transaction. |
| CR80 | 23 | Feature follow-ups on the generic verbs become required, then a hook per feature. |
| CR81 | 24 | Readers are built from a reader kernel through DI (CR18's read side). |
| CR82 | 25 | Every live call goes through `fits()`; the normalizers stop converting numbers (CR9, CR38). |
| CR83 | 26 | Generated types aliased in one line, with a ratchet; a folder per web feature; `decorateWrites` as a table of effects. |
| CR84 | 27 | The bundle: the graph and closed dialogs load lazily, mock-only code moves under `lib/mock`, unused packages go. |
| CR85 | 28 | `ReadFailed`, one way to say a read failed, with a Retry that reads it again, in every view. |
| CR86 | 29 | `DomainDailyChart` moves onto `DailyMetricChart`, with sleep stacked on top; both Character charts let a phone scroll past them. |
| CR87 | 30 | Keyboard access: the Map outline as nested lists, Enter adding only under the selected item, "+ child" shown on focus, and the resize edges as window splitters. |
| CR88 | 31 | Test kernels in their database's zone; tests of a global fold get their own database; one UTC+14 test of every home-date column. |
| CR89 | 32 | `bsk --json` emits the API's DTOs. |
| CR90 | 33 | One image built per commit and deployed by digest; `/health/ready` checked after a deploy. |

At the push that carries them:

- **CR90** is the new deploy's first run. Staging pushes CI's image to its Fly registry with the
  `FLY_API_TOKEN` it already has, and whether that token may push is known only then. A refused push
  stops the release before it migrates; `scripts/fly-setup.ps1` mints a new token, which takes
  effect at the next deploy. Production has 7 days to be approved before CI's image expires, after
  which Deploy is run again. A deploy run by hand (`migrate.yml`) now names an image's digest, and
  that is how a rollback goes. A deploy that doesn't answer `/health/ready` fails without rolling
  back. Each of these is Chris's to change.
- **CR89:** run `scripts/install-bsk.ps1`. `bsk --json` now prints the API's shapes, so the output
  of domains, food, home, metrics, reviews, oura steps and workouts, tags, recur, bounty, task
  settle and cover search changed; nothing in the repo reads the old shapes.

Left for later, by design:

- **CR80:** the follow-up hook each feature registers waits for a second feature that follows the
  generic verbs; the repeating Task is the only one today.
- **CR83:** the other web features move into `lib/features/<feature>` as they are next worked on,
  as Measurements did.
- **CR84:** 45 exports only the mock and the tests use, the kernel rules the mock ports, are counted
  exactly in `bundle.test.ts` (`MOCK_ONLY`), so a new one fails it; each goes under `lib/mock` as
  its feature moves, and the count is lowered with it.

### P3: when working in that area

One fold before and after a settle; consistency rules load their own inputs; one planner call per
catch-up; habit reads in bounded windows; MCP areas from one list; the core endpoints and
`SubjectReader` split by feature; refusal statuses in the OpenAPI document; `missed` on `/mcp`;
drift checks for the alignment tree, the Pilot's vocabulary and the edge relations; one CLI action
helper with its own exit code for refusals; the domain simulator's replay in Application; a migration
test against a database that holds data; `McpServerTests` split; the design primitives used; one
photo picker; a shared page-test harness; "in real money" from the daily read; one Fly config.

### Evidence for Chris's open choices

- **B:** the mock's copy of the reward economy is about 4,800 lines, and the live app uses only
  `barText` from it. Of 34 commits to the kernel's rule files since October, 12 also had to edit the
  web copies, and the Health and Environment fold rules have no shared cases.
- **C:** unchanged. *Chosen 2026-11-15 (CR126).*

## Fourth review (2026-11-13)

A fourth pass at 6152b61, after CR58 to CR90, with the same weighting and method: six Opus 5.5
reviewers, one area each, findings first and scores second. It scored the code 6.4, up from 6.1
(`docs/agents/quality-scorecard.md`). Its 62 findings merge into 33 proposals in three tiers and a P3
list. The P0 was checked by hand before it was written here.

Twelve of the proposals finish a CR built earlier the same day: CR68, CR70, CR74, CR76 (twice),
CR77, CR80, CR87, CR88, CR89 (twice) and CR90. Unlike the third review's half-done CRs, these came
with a check, but each check covered the one case it fixed rather than the class: CR70 guarded
`allows_partial` in `v_habit` and not the other casts of a free attribute; CR74 made one `isOverdue`
for the web while `/mcp` kept its own; CR89 paired the verbs it changed, and nothing makes a new verb
choose. So each fix below ships with a check over the class: every cast of a free attribute, every
door that answers a rule, every read that lists. The numbers are the review's; a proposal gets a CR
number when Chris chooses it.

### P0: built (CR91)

| ID | Review # | Fix |
|----|----------|-----|
| CR91 | 1 | The detail panel keys everything typed by its item, inside `SubjectDetail`, so none of its five pages can forget: an edit, a bounty or establish target, a journal entry being written and a relationship being chosen start afresh with the next item, and only the open tab stays. It says nothing is unsaved once it's gone. The Inbox preview is keyed by its item the same way, and a typed promote title now survives the item being tagged. The sheets that edit a record already keyed their forms by it. |

### P1: built so far (CR92, CR93, CR105)

| ID | Review # | Fix |
|----|----------|-----|
| CR92 | 8 | Discard in "Unsaved edits" asks once: the router's blocker reads the flag where Discard has already cleared it. Discard throws the edit away when the move keeps the same item open (the panel and the review editor key themselves by it), so the shell and what holds the edit agree. Escape is Stay, and lets the router go: before, the move stayed blocked and every later link did nothing. The guard has a module of its own (`components/shell/unsaved-edits.ts`), and three golden paths cover it. |
| CR93 | 9 | A page opened before a deploy copes with the files the deploy removed. Every lazily loaded piece (the pages, the palette, the dialogs, the Map's graph, the journal) reloads once when its file is gone (`lib/new-build.ts`, Vite's `vite:preloadError`), and each page has its own error view inside the shell, which says "LifeOS was updated" with a Reload button when a reload didn't help. A removed `/assets` file answers 404; it got the sign-in's 401, not the app's page as the review said. Every copy of the app's page says not to cache it: only a literal `/index.html` did, not `/` or a link to a page. |
| CR105 | 4 | A habit is missed only on a day it has. `AdherenceService` refuses a day that hasn't come, and a miss on a day the habit's pattern doesn't schedule unless something was already recorded on it (the miss corrects it); a session done on another day can still be logged as followed. Every door gets that from the one service. The web takes a habit's days from the kernel's occurrences rather than reckoning them: a Building habit still shows on Today when today isn't one of its days, saying "Not due today", with Followed but no Not followed; the review's check-offs and the streak grid offer Missed only on a day the kernel lists, and the grid fades every other day rather than reading the pattern itself. `AdherenceButtons` must be told whether a day can be missed, so tsc makes every screen that offers it say. A habit with no pattern, which the web had read as daily, is scheduled on no day. The mock starts tracking a habit today only when its pattern has today, and refuses as the kernel does. A golden path covers a weekdays-only habit on a Saturday. |

### P2 and P3: built so far (CR94 to CR104, CR106, CR107)

| ID | Review # | Fix |
|----|----------|-----|
| CR94 | P3 | Lint warnings fail: `npm run lint` is `oxlint --deny-warnings`, which CI runs. The 25 warnings went first. Shared hooks and helpers left the component files that exported them (`useAuth` to `auth/auth-context.ts`, `usePersistedWidth` to `shell/use-persisted-width.ts`, `oversizedFile` to `lib/artifacts.ts`), so fast refresh works there; the shadcn `*Variants` exports are allowed by name. State worked out from other state is no longer set in an effect (the phone's navigation, the Unsaved edits dialog, the Relationships target); refs are no longer written during render (the panel width, the graph's edges); a file's icon is drawn by `ContentTypeIcon`; the forms read a field with `useWatch`. Six effects stay as they are, each with a disable comment that says why: the restores on opening Browse and the Map, Expand all, the voice dialog's recording, the New dialog's reset and the graph's layout. |
| CR95 | 33 | Tab leaves the Map's inline create as it does anywhere else; it used to change the type, so the keyboard couldn't reach Add or anything past it. The type buttons are a group named "New item's type", each saying with `aria-pressed` whether it's chosen. A golden path tabs to Add and chooses a type from the keyboard. |
| CR96 | 31 | The seven form and record sheets (food, readings, lab visits, photos, the walk-through, workouts, days) open through one `ResponsiveSheet` (`components/primitives`): a bottom sheet on a phone, a side panel on a desktop. Once something in it is typed or chosen, Escape, the close button or a tap outside asks "Unsaved edits" first, with Stay and Discard; its own Save or Cancel closes it without asking. The workout and day sheets don't ask: a day only shows, and a workout's note saves as the sheet closes. A test lists the sheets that still open shadcn's sheet themselves, each with why, so a new form sheet can't skip the guard; a golden path covers Log food. |
| CR97 | First review's P3 | The pages are one table, `components/shell/pages.ts`, which the navigation and the command palette list; before, each kept its own list, and the router and the a11y spec two more. `pages.test.ts` checks the table names every route the router serves (Oura's return page listed apart, with why) and no other, and that an accessibility spec opens each page, so a page can't be added to one and missed in another. |
| CR98 | 32 | The bar chart, the trend chart and the rank chart read their points through one `useChartCursor` (`components/charts`): hover reads the point under the pointer, focus starts at the newest, the arrow keys, Home and End move it, Enter or a click opens it where the chart can, and leaving or blurring clears it. The copies had drifted: the bar and rank charts read point -1 when focused with nothing to show. The charts' class check now finds them by the hook and fails on any component that handles the arrow keys and the pointer itself. |
| CR99 | 14 | The Tasks page reads only what its view shows: Active its open Tasks as before, a view of one status that status alone (Not started the open ones, since a Task with no status yet is Not started), and All alone every Task. It groups and sorts the list once per change to it, not on every render, and the new-task entry holds its own text, so typing a title no longer redraws every Task under it. The list draws a page at a time across its groups, with Show more, through the paging Browse used, now shared (`usePaged` and `ShowMore` in `components/primitives`). A page test checks each, and fails on the old page all three ways. |
| CR100 | 29 | The Inbox draws its list as plain rows (`InboxRow`), and `@tanstack/react-table`, which it used for one column, is gone from the app. The Inbox's chunk is 13.5 kB, where the review measured 124 kB. |
| CR101 | 30 | supabase-js loads in a chunk of its own when the app first reads a session, not with the first page: `getSupabase()` imports it and answers a promise of the client, and `main.tsx` starts that as the live app loads. It had shared the agents code's chunk, which every page preloaded. What the first page fetches drops from 932 kB to 719 kB (291 to 237 kB gzipped), mock mode never fetches the client, and a deploy that leaves supabase-js alone leaves its chunk cached. `bundle.test.ts` fails while the first page or any page's chunk imports it. Signing in, signing out and the agents' grants were checked against the API with `live-check.ps1`. |
| CR102 | Web screens score | Nine screens that caught a write's failure and toasted it themselves (the cover banner and picker, renaming a tag, deleting and restoring a reading, a repetition's status, the timer, the Inbox's dismiss, Undo, drop, return, tags and resolve, and the review's check-offs and saves) run it through `useWriteAction`, whose `run` now takes a write's own fallback beside its `onDone`. What each says is unchanged. A check in `write-action.test.tsx` lists the files left that toast a failure of their own, each with why: mostly a write that went in while a step after it didn't (a meal logged, its photo not). Forms that keep a failure in their own state instead of the hook's `error`, about twenty, are left for when each is next worked on. |
| CR103 | Web screens score | The four longest pages keep the page and move their parts beside it. Browse (876 lines to 346) moves its type list, filters and sort menu to `components/browse/browse-filters.tsx` and its list, headers and rows to `browse-list.tsx`; the Inbox (705 to 315) its row and preview pane to `components/inbox/`, and what it says about an item to `lib/inbox.ts`; the Map (725 to 501) its outline's branch and inline create to `components/map/`, and its tree lookups to `lib/map-tree.ts`; Reviews (607 to 257) its recap and editor to `components/reviews/`. Browse's sort and clear rules join `lib/browse.ts`. Nothing they draw or do changes. `pages.test.ts` holds every page's file to 550 lines. |
| CR104 | P3 | The Playwright specs run against a mock-mode build that `vite preview` serves (`dist-e2e`), not the dev server, so they check the code that ships and a page loads its files as on staging; CR93's path aborts the built chunk. Every check waits `expect.timeout`, ten seconds, where 139 calls each said so; the three longer waits have names (`GRAPH_DRAWN`, and the reload in CR93's path). The accessibility scan, written out 39 times, is `expectAccessible` in `e2e/support.ts`. Vision on a phone measured its sheet mid-slide and failed about one run in three under load; it measures once the sheet settles. `src/e2e-setup.test.ts` checks the specs keep to all of it. |
| CR106 | 16 | Focus is one read, `GET /api/focus`, which the API works out in `FocusReader` (`src/LifeOs.Api/Read/FocusReader.cs`): the Active goal with the nearest target date and its cover, the Active projects that result in it (or any Active ones), the Tasks due or whose do-date has come, the ones in progress, each tracked habit's day as the kernel lists it (CR105) and the Inbox count. `life_snapshot` reads today through the same `DayAsync`, so an agent and the page can't disagree about what's due. The web had put Focus together from five reads, every open item and every edge among them, with its own copy of the rules; that copy is now the mock's (`lib/mock/dashboard.ts`), and the live read can't reach it. `DashboardRead` is the generated type with only its items restated in the app's narrower types (`Retyped`, beside `ApiSchema`), so the copy count holds. The web document marks a response's fields present only on a type reached at the top or in a list, so `FocusGoal`'s are `required` in C#; about twenty other types reached only as a property come out all-optional the same way (`CoverRead`, `DomainShown`, `TaskRepeatRead`, ...), left for #25. `SubjectReader` had two Inbox counts; one is left. |
| CR107 | 13 | Screens read what they show. The shell's Inbox badge reads the count (`GET /api/inbox/count`), not the whole Inbox. A detail's Link and the Inbox's Relate search on the server as Chris types (`SubjectSearchCombobox`, over `GET /api/subjects/search`), where each read every subject to filter it in the browser; nothing is listed until something's typed, as in the palette. The New dialog reads the chosen parent, where it read every subject to find its type. The Map asks the API to leave a repeating Task's repetitions out (`excludeRepetitions`), where it read every one ever made to drop them, and a habit's streak grid reads the month it shows, not its whole history (it had read that on every item's detail, habit or not). `src/lib/scoped-reads.test.ts` fails on each of the old reads: a read of every subject is listed with why (Browse, and the Map without repetitions), the whole Inbox is the Inbox page's, and a habit's days are read for a day or a range. The mock read an empty type list as no types, where the API reads every type; it reads it as the API does. |

### P1: proposed

Every P1 and P2 proposal below not yet built still stood at the fifth review, which carries it as
"4th #N" ([Fifth review](#fifth-review-2026-11-14)).

| # | Area | Problem and fix | Finishes | Size |
|---|------|-----------------|----------|------|
| 2 | Kernel | A write that commits can still fail its request, when a follow-up after the commit throws, and the refusal then says nothing was written. `bsk` has no zone check at start. Follow-ups run best effort and say what failed, the wording says what was written, and `bsk` checks the zone as the API does. | CR88 | M |
| 3 | Kernel | Filing an Inbox item runs in three transactions, so a retry after a failure makes a second Idea; the voice door's duplicate check is a rule in a door. File in one transaction, with the check in Application. | CR68 | M |
| 4 | Web data | Built as CR105. The web works out habit due days itself (`todayHabitRows`, `tracksOn`, the streak grid), and they differ from the kernel's occurrences, so Missed is offered on days the kernel doesn't schedule. Writing it is a permanent event that lowers adherence and XP. Use the kernel's occurrences, with a golden path on a habit that isn't daily. | | M |
| 5 | Database | `SubjectReader`'s list casts `vision_order` with an unguarded `::int`, for the API and the Pilot, so one bad value breaks every list. Guard it, and add a test that writes junk to every free attribute a view or reader casts, then reads every list. | CR70 | S–M |
| 6 | Ops | A rollback run by hand (`migrate.yml`) runs main's newest migrations before deploying the old image, and doesn't check the digest is in the registry before it migrates; each job builds its own `bsk`. Check the digest first, migrate only when asked (`bsk migrate --pending` to say what would run), and publish `bsk` from CI beside the image. | CR90 | M |
| 7 | Ops | The backup and restore scripts run the working tree's `bsk`, and the nightly `rebuild --verify` has raised false alarms since CR66. Build `bsk` from `origin/main` as `install-bsk.ps1` does, and fix the verify and the stale dev-migrate notes. | | M |
| 8 | Web screens | Built as CR92. Discard in the unsaved-edits dialog asks twice, because the blocker holds a stale dirty flag, and the page's two dirty flags can disagree. One flag. | | S |
| 9 | Web screens, API | Built as CR93. One error boundary, at the root. The lazy pieces (the palette, the dialogs, the graph, the journal tab) don't reload when a deploy has removed their chunk, and `/assets` falls back to `index.html`, so a missing chunk answers HTML. A deep link's `index.html` has no cache header. A boundary per page, a lazy import that reloads once, a 404 for a missing asset, and `no-cache` on `index.html`. | | M |
| 10 | API | With the rewards area off, the bounty still reaches agents through `get_item`'s attributes and series and `recent_changes`' attribute changes. Hide reward attributes through `McpAreas`, with a test over every tool that answers attributes. | | S–M |

### P2: proposed

| # | Area | Problem and fix | Finishes | Size |
|---|------|-----------------|----------|------|
| 11 | API | `/mcp`'s overdue uses `due ?? scheduled`; the app uses `due`. One rule in Domain, with `tests/rules/overdue.json`. | CR74 | S |
| 12 | API | `RewardReader` is still in the API and copies `RewardService`'s range check; the 731-day cap is only in the API's door; defaults differ by door (30 or 14 days, 27 or 28 reviews, no cap in `bsk`); `BskJsonTests` always passes arguments. The reader in Contracts, caps and defaults in Application, and pairs with no arguments. | CR89 | M |
| 13 | Web data | Built as CR107. Reads still unscoped: the shell's Inbox badge reads the whole Inbox (`inboxCount` exists, unused); the Relate picker, the New dialog and the Relationships tab read every subject; the Map reads every repetition. | CR76 | M |
| 14 | Web screens | Built as CR99. The Tasks views read and draw every Task, and regroup them on every render. | CR76 | M |
| 15 | API | The alignment tool reads every subject and edge on each call, and `recent_changes` builds its whole window before its limit. | | M |
| 16 | Web data | Built as CR106. The live Focus page is five reads, every edge among them, worked out in the browser with a copy of `life_snapshot`'s rule: `GET /api/focus`. | | M |
| 17 | Database, kernel | `EventIndexTests` checks one direction (each index has a read, not each lookup an index); `GetLatestSubjectEventAsync` scans the log; `v_workout`'s lookups miss its key. | CR77 | M |
| 18 | Database | Views copy C# vocabularies, limits and defaults (review kinds, a metric's panel, direction and decimals, a space's bounds, establishment's 0.8 and 60) with nothing checking them. | | M |
| 19 | Database | The newest-version view is copied 13 times and has drifted (`v_workout`, `v_sleep_period`, `v_oura_tag`). A recipe, and a test of each such view's shape. | | M |
| 20 | Database | Nothing measures cost at volume: a scale template seeded with years of data, and timed hot reads. | | L |
| 21 | Tests | No shared test support (33 copies of opening a connection, 9 of a refusal check, 7 of inserting a subject, 10 kernel bundles); `TaskSeriesTests` settles every row of a shared database. | | M |
| 22 | Kernel | Home-zone conversions outside `IHomeCalendar`: the food, metric and space repositories, and three in the API. | | M |
| 23 | Kernel | Spaces and Metrics copy their resolve and name checks, have drifted (short ids) and check then act; `ArchiveService` branches by type. CR80's wait for a second feature is over: three follow the pattern. | CR80 | M |
| 24 | Web data | `lib/features` stops at the mock, so a feature still edits eight shared files. One record per feature, spread from one index. | | M |
| 25 | Web data | The type-copy ratchet matches exact names only and misses about 52 copies with a suffix. | | S |
| 26 | Ops | `BskJsonTests`' pairs are a hand list (21 reads and 18 writes of 49 verbs); nothing makes a new verb be paired or listed as `bsk`'s own. | CR89 | S |
| 27 | Pilot | The Pilot's 30 or so `bsk` command lines are checked by nothing. One `BskVerbs.cs`, parsed by `BskRoot` in a test. | | M |
| 28 | Ops (latent) | `deploy.yml`'s concurrency group covers the whole run, so once production is on, staging waits behind the last production approval. | | S |
| 29 | Web screens | Built as CR100. `@tanstack/react-table` is about 110 kB of the Inbox's 124 kB chunk, for a one-column list. | | S |
| 30 | Web screens | Built as CR101. The whole `supabase-js` client loads with the first page, about 100 kB, for auth alone. | | S–M |
| 31 | Web screens | Built as CR96. Seven copies of the side or bottom sheet, none guarding typed input when it closes: `ResponsiveSheet`. | | M |
| 32 | Web screens | Built as CR98. Three copies of the chart cursor: `useChartCursor`. | | M |
| 33 | Web screens | Built as CR95. The Map's inline create traps Tab, and its type buttons don't say which is pressed. | CR87 | S |

### P3: when working in that area

Day-award sources as one `IDayAwardSource` list; stale Dapper comments and seven `ParseDay` copies;
rating a space reads every rating; a lab visit's range sync reads every reading and makes metrics in
transactions of their own; fast overlap uses the database's clock; a missing URN answers 400 in
`ResolveOrCreateAsync`, 404 elsewhere; `ReadFailed` over shown data when a refetch fails; the e2e
specs on a preview build, with one timeout, one scan helper and named asides (built as CR104; lint
warnings fail since CR94); the mock clients' wrapper boilerplate and the `bump` and `emit` nobody hears; the review recap
in two halves; `setAttributes` not refreshing the task series, and typed reads listed by hand;
unused seam members and a stale `TODO(api)`; `v_fast.meals_during`; an index on `subject(type)` and
`v_task_series`' aggregate; the shared SQL folds spelled again; `RuleParityTests`' file list; keyset
paging for Resolved; the extra connection pools (CR18); a declared shape for `/mcp`'s answers;
`MeasurementReader`'s re-reads; small repeats (the Monday week start five times, upload intake four,
the Inbox count, the `bounty` literal, config read twice); CR59's notice heading the Pilot's error
dialogs and printing the owner and pooler host in CI logs; `v*` tags not checked against `main`; the
Pilot's `bsk` calls on the UI thread and its unbounded polled reads; the CLI's time parsing in
`FoodCommand`; text and JSON printed from different reads.

### Evidence for Chris's open choices

- **B:** the mock's copy of the reward economy is about 3,750 lines, with about 1,700 lines of
  tests; CR84 moved it under `lib/mock`, so the live bundle no longer ships it. Since October 1, 11
  commits touched the copied folds, 5 of them alongside a kernel change. The mock keeps
  economy-aware handlers for two writes no screen calls.
- **C:** unchanged. *Chosen 2026-11-15 (CR126).*

## Fifth review (2026-11-14)

A fifth pass at 1a079d3, after CR91 to CR107, with the same weighting and method: six Opus 5.5
reviewers, one area each, findings first and scores second. It scored the code 6.5, up from 6.4
(`docs/agents/quality-scorecard.md`), and found no P0. Its findings merge into 35 proposals, eleven
P1 and twenty-four P2, a P3 list and a new choice for Chris (D). R1 to R6, R8 to R10 and R14, the
rename half of R19, the web half of R28 and choice D were checked by hand against the code before
they were written here; the rest are the reviewers' readings, each with the lines it cites.

Every proposal the fourth review left open still stands at 1a079d3 and is carried here ("4th #N").
Most of the new ones finish a CR whose check covered the case it fixed rather than its class, as the
fourth review found: CR96's unsaved-edits guard covers sheets and not dialogs (R2), CR60's one write
at a time covers the Map's inline create and not the New dialog (R1), CR85's check looks for words
(R24), CR107 scoped three read hooks (R16), CR106 moved Focus to the server without scoping its read
(R14), CR28 hides an area's payload keys and not its attribute keys (R8), and CR103 caps pages and
not components (R23). So each fix below names the check over its class that ships with it. The
numbers (R1 to R35) are the review's; a proposal gets a CR number when Chris chooses it. "Earlier"
names the CR whose check it widens, or the fourth review's proposal it carries.

### P1: proposed

| # | Area | Problem and fix | Earlier | Size |
|---|------|-----------------|---------|------|
| R1 | Web | Built as CR108; a typed capture sends a request id, and a create doesn't yet, since the kernel's create keeps none. Enter pressed twice in the New dialog or Quick capture writes twice: neither checks that its write is still running, and the web sends no request id, so each Enter makes another permanent item or capture. Both run through `useWriteAction` inline, and create and capture send a request id. CR102's check widens from `toast.error(` to any pending state or catch a component keeps around a write (twelve files, among them `materialize-panel.tsx`, `exception-days.tsx` and `redeem-dialog.tsx`), and a test presses Enter twice in each dialog. | CR60, CR102 | M |
| R2 | Web screens | Built as CR109; Quick capture and Voice capture keep Escape as discard, as their spec says, and ask on a tap outside. Nine form dialogs throw away what's typed on Escape or a tap outside: New, Quick capture, Voice capture, Mark exception, Fast, Add metric, Metric settings, Redeem and the reward form. CR96's guard lives only in `ResponsiveSheet`. A `FormDialog` (or `useCloseGuard`) carries it, and the check that lists what opens shadcn's sheet itself widens from `<SheetContent` to `<DialogContent`, each unguarded dialog listed with why (the viewers, the palette, the cover picker). | CR96 | M |
| R3 | Pilot | Built as CR110; `v_subject_current` itself now reads the live fold, so Python and BI readers see it too. The Pilot reads status from `bsk.v_subject_current`, which only `bsk rebuild` writes (`Reader/SubjectReader.cs`), so a status set on the web, by an agent or by a timer's Complete doesn't show in its Browse, Tasks or detail. Flag to Inbox then offers to re-open an item it wrongly thinks is Completed (`BrowseView.cs`), which writes Not started over the live status, and the Pilot runs a full `rebuild` on staging after its own writes (four calls). A view the reader role can read over `subject_current_source`, as the API reads, and the rebuild calls go; a test writes a status through `StatusService` and reads it back through the Pilot's reader with no rebuild. | First review's P3 | M |
| R4 | Database | Built as CR111 (migration 0068); a second link answers the edge already there. The two inserts beside a new subject keep no `ON CONFLICT`, since a new subject has no edges yet. Before the push that deploys it, staging's duplicates: `SELECT from_subject, relation, to_subject, count(*) FROM bsk.subject_relation GROUP BY 1, 2, 3 HAVING count(*) > 1;`. `bsk.subject_relation` has no unique key, and nothing checks for an existing edge (`RelationService.LinkResolvedAsync`, `POST /api/relations`): a retry or a second add stores the edge twice, each new repetition copies the duplicate, the serves lists show it twice, and unlink removes both. A migration keeps the oldest of each duplicate (the table is in the mutable layer) and adds a unique index on `(from_subject, relation, to_subject)`, and the four inserts take `ON CONFLICT DO NOTHING`. Before the push, a query lists staging's duplicates. A test links twice through `bsk`, the API and a repetition's copy and finds one edge. | | S |
| R5 | Kernel | Built as CR112; the one intake is `NewBinaryCapture.Of`, which every door builds its capture with, naming its `BinaryIntake`, and a feature's event stored under the bare hash is still found as its own. An upload that repeats one already stored is found by source and SHA-256 alone (`NpgsqlArtifactBlobStore`), across the five upload services. A room, meal, lab-visit or person photo that matches reuses the stored bytes and writes its own event, which is fine. But a file shared to the Inbox that matches a photo stored for a room or a meal gets that photo's event back, flags it into the Inbox (`InboxFilingService.FileAsync`), and links an Idea filed from it to it, in a log that can't be edited. The dedup key names the feature (the object key stays content-addressed), the five services share one intake, and a test stores the same bytes through each pair of doors. | CR68's class | M |
| R6 | Kernel, web data | Built as CR113, with the adherence service too; the answer carries `FollowUpsPending`, which `bsk` says, and the API's shapes are unchanged. A write that committed can still answer "Nothing was written". `StatusService`, `AttributeService` and `ArchiveService` run the reward reconcile and the series follow-ups after the commit, and the reconcile's last `LedgerConflictException` escapes as a refusal; the web's effect runner (`write-effects.ts`) treats a throw in `lands`, `says` or the earnings toast as the write failing. A retry writes twice. A follow-up that fails after the commit is logged and answered as pending, left to the catch-up and reconcile that already heal it, never as a refusal, and the web catches after the write lands. A test makes each service's follow-up port throw and checks the answer isn't a refusal while the event is in the log. | 4th #2 | M |
| R7 | Kernel | Built as CR114; the one write is `SubjectService.CreateFromCaptureAsync` (`ISubjectRepository.CreateFiledAsync`, and `FileExistingAsync` for a Problem reused by title), a file stored again finds its item through `IEventReader.ConcernsAsync`, and the voice door's missing item now answers 404, as every door's does. Filing an Inbox item takes three transactions (create, relate, flag), and a document or voice note re-sent as an Idea makes a second Idea: the text path's retry lookup (`FinishRetryAsync`) isn't used for files, and the voice door's `concerning` check is in the door. One write for the Idea, its edge and its flag; the lookup for every capture; the check in Application. | 4th #3 | M |
| R8 | API | Built as CR115; `McpArea` names its `AttributeKeys` and its `Tools`, the rewards area owns `bounty`, and the host lists each area's tools from `McpAreas.All`. A habit's establish target stays the core's: it says when a habit is ready for Established, a status of the core's, and the habits tool's progress reads it. With the Rewards area off, a bounty still reaches agents through `get_item`'s raw attributes and its series headline, and setting one is a core `attribute_change` in `recent_changes`. `McpArea` names its attribute keys (`bounty`, and `establish_target` if it's Rewards'), tools strip them while the area is off, `ChangeReader` treats an attribute change of an off area's key as that area's, and the host registers tools from `McpAreas.All`. A test switches each area off and reads each of its attributes through every tool. Latent while every area is on. | 4th #10 | S–M |
| R9 | Database | Built as CR116; migration 0069 reads the four views' times through `bsk.try_to_timestamptz` and leaves out a record whose required time doesn't parse (an optional end that doesn't parse reads as none), adds `event_ledger_base_check` (NOT VALID, as the other payload CHECKs) and makes `bsk.json_int` answer none past an int's range; both subject lists read a `vision_order` that isn't a whole number as none. `CastGuardTests` scans `pg_views` and the C# SQL for a cast of a JSON value to a uuid, a number, a date, a boolean or a timestamp with no guard sound for its type in its own CASE or a WHERE of the same view; the casts a CHECK guards, and the four ids a view's version filter keeps to uuids, are listed with why. It doesn't check a number past its type's range. `(attributes->>'vision_order')::int` has no guard in the API's and the Pilot's subject lists, so `bsk set <item> vision_order=1st` breaks Browse, the Map and Vision. Five payload casts are half guarded: `v_fast`, `v_oura_tag`, `v_sleep_period` and `v_workout` check a timestamp is a string but not that it parses, and `v_award_credit`'s `base` has no CHECK. Guard them, and a test over `pg_views` and the C# SQL fails on any cast of a free value without a guard or a CHECK. | 4th #5 | S–M |
| R10 | Ops | Built as CR117; `migrate.yml` takes a `migrate` input, off unless asked, which Deploy and Migrate staging pass; without it the run lists what `bsk migrate --pending` (new) would apply in its summary and applies none. Run by hand, it finds the `image_digest` in the app's registry (`skopeo inspect`) before anything migrates; the Fly config is the environment's (no `fly_config` input); and a `v*` tag must be an ancestor of `main`, on a full checkout. `DeployWorkflowTests` asserts each, and `MigrationRunnerTests` that `--pending` applies and creates nothing and refuses what migrate refuses. A rollback through `migrate.yml` runs main's newest migrations before deploying the old image, never checks the digest is in the registry, defaults `fly_config` to `fly.toml` (production's app) whatever environment is chosen, and accepts any `v*` tag without checking it's on `main`. Check the digest first, migrate on a run by hand only when asked (`bsk migrate --pending` to say what would run), take the Fly config from the environment, and check a tag's ancestry; `DeployWorkflowTests` asserts each. | 4th #6 | M |
| R11 | Ops | Built as CR118; `scripts/LifeOsBsk.psm1` holds `Get-MainBsk`, the `install-bsk.ps1` build (fetch, check the commit is on `origin/main`, publish from a temporary worktree), kept in `%USERPROFILE%\.lifeos\bin\bsk-main` until main moves; `install-bsk.ps1`, `migrate-staging.ps1` (which no longer needs HEAD to be main), `restore.ps1` and `backup.ps1` run it, and the manifest records `bskCommit`. `backup-setup.ps1` installs `backup.ps1` and the modules it loads in `%USERPROFILE%\.lifeos\bin\backup` and has the task build bsk in the main checkout (`-Repo`). `ScriptBskTests` allows a CLI build in `scripts/` only in `LifeOsBsk.psm1` (and `live-check.ps1` and `seed.ps1`, which reach only throwaway databases), checks the task installs every file `backup.ps1` loads, and parses every script. Rerun `install-bsk.ps1` and `backup-setup.ps1` after the push. `restore.ps1` migrates a new real database with the working tree's `bsk`, branch migrations and all; the nightly `rebuild --verify` raises a false alarm whenever the checkout is behind staging (since CR66); and the scheduled backup runs `backup.ps1` from whichever checkout set it up, which may be a worktree that has since gone. One `Get-MainBsk` that builds from `origin/main` as `install-bsk.ps1` does, used by restore, backup and `migrate-staging.ps1`; `backup-setup.ps1` installs the scripts under `%USERPROFILE%\.lifeos\bin`; a scan allows a CLI build in `scripts/` only there and in the throwaway-database scripts. | 4th #7 | M |

### P2: proposed

| # | Area | Problem and fix | Earlier | Size |
|---|------|-----------------|---------|------|
| R12 | Database, kernel | Built as CR128, for what R13 measured over budget or growing with history. Migration 0072: `v_fast` counts each fast's meals by a binary search of one sorted array of meal times, not a subquery per fast over every meal; `v_sleep_day` ranks each day's periods once, not an anti-join of every period against every night; `v_reward_due` asks `bsk.ledger_opened_at()` once, not of each row; `v_activity_day` and `v_readiness_day` answer `day_key`, Oura's day as text, so a week reads only its days from their indexes; `v_workout`'s `workout_id` is its window's key, so one workout is a lookup. Every view answers the rows it did. A Food Log range picks its meals' keys from the range's events first (`MealsInRangeSql`); the drink history reads `v_meal` alone; the ledger's and the domain credit's awards by key look each key up on its own (`LATERAL ... OFFSET 0`), since the planner has no statistics for an expression only a partial index holds and read the whole ledger for a week's 14 keys, and the net is summed from those entries. `EventIndexTests` lists all 30 views over `bsk.event` from `pg_depend`, each with its reads by key or why it's read whole, and has lookups for a range's meals, a week of activity and readiness days, one workout, an item's due awards, one review, one space and a subject's events. Measured on R13's template, before and after (statements, event rows read, rows scanned): a Food Log week 5, 31,899, 480,224 to 5, 16,485, 48,789; an Activity week 9, 29,665, 732,227 to 9, 5,514, 15,598; the settle 22, 55,688, 4,195,389 (3.0 s) to 22, 47,598, 128,503; a check-off 1,023, 28,396, 81,304 to 32, 20,508, 73,421; a completed Task 26, 11, 29 to 20, 5, 23; a meal 10, 19,824, 3,419,329 (2.8 s) to 10, 19,824, 51,019; the Inbox count, the domain fold, a room rating and a reading unchanged. The settle, a check-off and a meal are now within budget. Left, with why: `v_food_day` by range (each day groups meals and fasts folded by their ids, so it needs a function that picks the range's keys first) and `v_sleep_day` by range (periods folded by Oura's id, grouped by day), so both weeks stay in `Over`; the Inbox count, whose `DISTINCT ON` over every triage event needs a projection beside the log to bound (102 statements, 5,661 rows, about 30 ms, in `Over`); workouts and tags by range, a few hundred rows in three years, which would need new indexes; readings by metric and ratings and photos by space, within budget (a reading reads 2 event rows; a room rating's 16,840 are the domain fold R18 allows); kinds named in `GetLatestSubjectEventAsync`, which runs only when a change is reversed, takes 8 to 12 ms on the template, and would miss a kind that names a subject without being listed. Before: reads over a range of a versioned record fold every version ever stored (meals and fasts by day, the six Oura reads by day, readings by metric, ratings and photos by space), since each filters on a column worked out above the view's window. The Inbox count, on every page's badge, runs `DISTINCT ON` over every triage event; each reconcile's `GetLatestSubjectEventAsync` scans the log; `GetWorkoutAsync` folds every workout. `EventIndexTests` marks these `Lookup: false` and checks only that each index has a read. A day column as text for streams keyed by day, a pre-select of keys for streams keyed by id, kinds named in the latest-event lookup, and `EventIndexTests` lists every view over `bsk.event` from `pg_depend`, each bounded or listed with why. | 4th #17, CR77 | M |
| R13 | Database | Built as CR127; `Scale/seed.sql` writes three years of made-up use in the kernel's shapes (46,800 events, 35 MB: 3,806 meals and 885 fasts, 1,095 Oura days each sent several times, 1,180 sleep periods and 617 workouts, habit check-offs and 2,036 completions, 5,560 triage events, 1,404 room ratings, 1,262 readings, 5,868 awards due and paid), the kernel's settle pays its food and activity days, and `SeededTemplate` makes it once a run, in about 10 s, for `DomainTestDatabase.CreateSeededAsync` to copy in 0.1 s. `QueryMeter` loads `auto_explain` on a kernel's own data source and counts each statement's rows of `bsk.event` read, the rows every scan read (which shows a subquery run once per row) and reads of the whole log; `HotPathBudgetTests` holds each hot path to a budget of those, with 2 s for a read and 3 s for a write, and lists those over it in `Over` with what they cost and why, which they mustn't exceed. `LIFEOS_SCALE_REPORT=<file>` writes what each cost. `SeedTests` checks every stream has its rows and the ledger pays what the items are due; `MigrationOnDataTests` applies the newest three migrations to a seeded database and wants every view to answer the same rows. Measured before R12 (statements, event rows read, rows scanned, time): a Food Log week 5, 31,899, 480,224, 138 ms; an Activity week 9, 29,665, 732,227, 142 ms; the Inbox count 102, 5,661, 8,822, 30 ms; the domain fold 6, 15,312, 54,764, 100 ms; the settle 22, 55,688, 4,195,389, 3.0 s; a check-off 1,023, 28,396, 81,304, 245 ms; a completed Task 26, 11, 29, 61 ms; a meal 10, 19,824, 3,419,329, 2.8 s; a room rating 17, 16,840, 68,210, 155 ms; a reading 6, 2, 3,808, 21 ms. Over budget: both weeks, the Inbox count, the settle, a check-off and a meal. Nothing measures cost as data grows: `EventIndexTests` plans against an empty log. A template seeded with years of meals, Oura days, check-offs, the ledger and the triage history, with time budgets for the hot reads (a Food Log week, an Activity week, the Inbox count, one check-off's reconcile, the domain fold); it also gives a migration test against a database that holds data. Best done before R12 and R14 to R18, to rank them by what they cost. | 4th #20 | L |
| R14 | API | Built as CR119; `SubjectFilter.Types` names the types a list reads, Focus reads `FocusReader.Types`, and `ScopedReadsTests` lists each list of subjects in the API that can read every type (`GET /api/subjects`, `list_items`, `alignment`) with why. Focus and `life_snapshot` read every open item of every type (`FocusReader.DayAsync`), Appointment occurrences two years ahead and every status-less Person, Area, Value and Metric among them, and use only the Goals, Projects and Tasks. The filter names the three types, and a server-side twin of `scoped-reads.test.ts` checks it. | CR106 | S |
| R15 | Web data, API | Built as CR120; `GET /api/browse` (`BrowseReader`, `BrowseRules`) counts every type in SQL, reads only the type looked at, and filters, sorts and pages it on the server, a page more each Show more, and `GET /api/alignment` (`SubjectReader.GetAlignmentAsync`) reads the Identity Statements, Goals, Projects and Tasks, a repeating Task once as its series, any other item an edge links and the edges among them, for the Map's outline and graph and `/mcp`'s alignment. `GET /api/subjects` takes types, area, tag and a due range. `tests/rules/browse.json` and `alignment.json` hold the server and the mock or the web to the same pages and trees, and `scoped-reads.test.ts` wants every list of subjects to name its type, with no exceptions. The list no longer draws an open item past its first page (the panel shows it), and the Map's outline nests along serves and results in only, sorted by type and then title. Browse and the Map read every subject of every type: every repetition, a review a day, Appointment occurrences ahead, and Spaces, Metrics and Reviews the web throws away (`browsableItems`). `/mcp`'s alignment reads every subject and edge, through a C# port of the web's `buildAlignmentForest` with no shared cases. `GET /api/subjects` takes types, area, tag and due (the kernel's `SubjectFilter` already does); Browse counts and pages on the server; one alignment read serves the Map and `/mcp`; `scoped-reads.test.ts` requires a read of all subjects to name its types, with no whitelist. | 4th #15, CR76, CR107 | M–L |
| R16 | API, web | Built as CR121; the shell reads the running and paused timers (`GET /api/timers?active=true`) and the Tasks page those of the Tasks it lists (`status`), a Task's own timer and a habit's detail read one (`GET /api/subjects/{id}/timer`, `GET /api/habits/{id}`), the journal and history tabs read a first page with Show older, the repeat panel a series' newest 6 repetitions with their count (`TaskSeriesHistory.RepetitionCount`), the home reads each shot's first, newest and picked photos and counts the rest in SQL (`ISpaceRepository.GetShotEndsAsync`), and parent pickers and the Tasks page's parent filter read the open Goals and Projects and any already picked, the filter the parent's own relations in place of every edge (`GET /api/edges` is gone). `UnboundedReadsTests` lists the API's list endpoints that take no bound, with why, and `scoped-reads.test.ts` checks `useHabits`, `.timers(`, `useTimers`, `useSubjectsOfTypes` and the journal, history and series reads. More reads with no bound: `GET /api/timers` answers every timer ever, and the shell reads it on every page to find the running one; the journal and history read with no limit, though the API takes one (CR54); a series' whole history is read, then cut to 20; `EnvironmentReader` reads every photo of every space; a detail reads every habit to find its own, habit or not; parent pickers read every Goal and Project. Bounds in SQL, a running-timer read, a first page with Show older. A list of the server's unbounded reads with why, and `scoped-reads.test.ts` widened to `useSubjectsOfTypes`, `useHabits`, `useTimers` and the journal. | CR54, CR107 | S–M |
| R17 | API | Built as CR122; migration 0070 moves the record rule into `bsk.event_record`, indexed with its versions' times (`event_record_idx`), and adds `event_occurred_at_idx`; `ChangeReader` reads the log newest first by either time and stops at the page, a version showing when no newer one of its record was recorded, and only the page counts its versions (now all of them, not those since the window's first). `ChangeFeedPlanTests` plans the feed's own query over a year of made-up events, each order, after a cursor and with an area off, and wants a limit above its time's index scan with nothing between that reads the window, and pages it to the end against the plain fold. `recent_changes` builds its whole window, up to 366 days with a regex on every row, before its limit (`ChangeReader`). Page first by keyset over `recorded_at` and id, and fold newest versions only for the page; an EXPLAIN check wants a limit above an index scan. | 4th #15 | M |
| R18 | Kernel | Built as CR123, and its query budget as CR127: each write that earns (a check-off, a completed Task, a meal, a room rating, a reading) folds the domains' history once (`NpgsqlDomainCreditReader.DaysSql`), within the fold's own cost and the habit's history; the settle the same; a write reads all of the domains' history once, before itself, and `RankUpService` makes the after from that read with only what the write changed read again: its own awards' credit before and after it (`AroundAwardsAsync`, `IDomainCreditReader.GetAwardCreditAsync`, `DomainCredit.WithAwards`), or the exception days and the Home (`AroundAsync`, `RereadBesideAsync`); a rank-up due then pays from a read of all of history again, so what pays is never a patched read's; the sweep keeps the patched fold from item to item, two reads of history where CR14 left four; "Same as last time" reads only its space's ratings; and a unit change counts only its metric's readings. `DomainFoldReadsTests` counts the reads of a check-off, of one that reaches a rank and of the next, and holds what an earn, a reversal, an earn again and an exception day leave the domains to a read of all of history. Each write that earns XP runs the domain fold over all history twice (`RankUpService`'s `AroundAsync` and `PayAfterAsync`); a room rating reads every rating of every space (`SpaceRatingService`); a reading counts every metric's readings. One fold per request, shared; reads bounded to the space or metric; a query budget on R13's template. | | M |
| R19 | Kernel | There's no place for a type's own rules. The generic rename (`RenameService`) skips the name checks `SpaceService` and `MetricService` make, so two metrics can be called "Weight", and readings find a metric by name; the two services copy their resolve and free-name checks, which have drifted (short ids) and check before acting; eight verb services branch on type. One registration per type that rename, archive, restore and status consult, and a partial unique index on live spaces' and metrics' names. A table test over every type and generic verb: each is refused or reaches its type's rule. | 4th #23 | M |
| R20 | Web data | A feature with a read and a write still edits eight shared files (`reads.ts`, `live/reads.ts`, `query-keys.ts`, `production-ui-types.ts`, both write clients, `write-effects.ts`, `mock/api.ts`): People's photo edited six, CR106 five, CR107 six. `lib/features` holds two features, CR105 to CR107 didn't move theirs, and neither `MOCK_ONLY` (45) nor the copy count (75) has been lowered. One record per feature spread from `features/index.ts`; `LifeOsWriteClient` in `lib/writes.ts`; `web/src/lib/production-ui-types.ts` (2,106 lines, 200 importers) split by feature. Feature by feature, habits, food and environment first. | 4th #24 | L |
| R21 | API, web data | About 30 response types reached only as a property are all-optional in the OpenAPI document (`WebClientDocument` marks fields required only without a `JsonPropertyInfo`), so `fits()` checks nothing in them, and about 48 hand-written types match a generated schema field for field under another name (`Award` and `AwardRow`, seven `*Request` and `*Body` pairs). Required marked from the CLR type; the copies aliased; the ratchet compares fields, not names; a test fails on an all-optional response schema. | 4th #25 | S–M |
| R22 | Web screens | `subject-detail.tsx` has 21 `subject.type === …` branches in one 509-line function (the timer, repeat, materialize, person photo, establishment, streak, Decision actions, target date), so a new type with a panel edits it. A typed table of panels per type, and a check that no type literal is left in the file. | | M |
| R23 | Web screens, API, CLI | Only pages have a size cap. Six components are over the pages' 550 lines (`domain-reports.tsx` 756, `log-food-sheet.tsx` 695, `walk-through-sheet.tsx` 658, `rewards-history.tsx` 633, `subject-detail.tsx` 621, `lab-visit-sheet.tsx` 560), and in C# `SubjectReader.cs` (1,051, with four methods nothing calls), `FoodCommand.cs` (843) and `NpgsqlLedgerRepository.cs` (726). One ratchet for components and C# files listing today's oversized ones, lowered as each is split; the dead methods go. | CR103 | M (check S) |
| R24 | Web screens | Built as CR131 (nightly 2026-11-17) for the item detail only: its History, Journal, Relationships, Tags and Files say a failed read, and `components/detail/read-failures.test.ts` wants every file of the detail that reads to handle a failure; the other views, `FormError` and the workout note are still open. A failed read looks empty in 19 views: the journal says "No journal yet", the history "No status history", the palette "Nothing matches.", and the Relationships tab, repeat panel, streak grid and pickers the same. About ten write-failure or field-error paragraphs aren't announced (`role="alert"`, `aria-describedby`), and a workout note saved as its sheet closes fails silently. A source check that each read hook's result reaches `ReadFailed` or `isError`, exceptions listed with why; one `FormError` primitive; the workout note toasts when its sheet is gone. | CR85 | S–M |
| R25 | API, CLI | Defaults and caps differ by door: the domains read 30 days through the API and 14 through `bsk`, and only the API holds the 731-day cap; the review list reads 28 days with a 400-day cap through the API and no cap through `bsk`; `RewardReader` copies a service's range check. Constants in Application, doors pass null for the default, and `BskJsonTests` pairs these with no arguments. | 4th #12 | M |
| R26 | API | Overdue counts a past do-date on `/mcp` (`Due ?? Scheduled`) and not on the web or in reviews, where it's the due date alone. One `TaskDates.IsOverdue` in Domain, with `tests/rules/overdue.json`. | 4th #11 | S |
| R27 | Database | Views copy C# lists with nothing checking them: review kinds (a third kind would vanish from `v_review`), lab panels, a metric's direction and decimals, a space's weight and pace bounds. And the newest-version view is copied 13 times and has drifted: 6 of 12 end with their key as text, some use `DISTINCT ON` and some `row_number`, the uuid regex is written 14 times beside `bsk.is_uuid_text`, and keys compare as text whatever their case. A test per copied list, and a shape test over `pg_views` with a written recipe. | 4th #18, #19 | M |
| R28 | Kernel, web, Pilot | Built in part as CR134 (nightly 2026-11-17): the fast overlap reads the kernel's clock, not `now()`; the rest is open. Home-zone math outside `IHomeCalendar` in three repositories (space, food, metric), and `now()` or `clock_timestamp()` in two writes, which a `TestClock` can't move. The web builds a default noon in the browser's zone (`collectedInstant`, the food sheet's past-day default), so with the browser five or more hours east of Pacific a lab visit or a meal lands on the home day before. The Pilot's today is the PC's date (`Ui.Today`). `IHomeCalendar.InstantAtAsync`, a `homeInstant(day, time)` in `dates.ts`, the Pilot reading `bsk.home_today()`, and a source scan on each side. | 4th #22 | S–M |
| R29 | Tests | No shared test support: 182 `new NpgsqlConnection(` in 96 files, 35 private `OpenAsync`, 12 `RefusedAsync`, 31 raw subject inserts, and the newest test files add more. CR88's rule that a global read takes its own database is a convention, and slipped once (27d69aa). `Support/Db.cs` and `Support/Api.cs` with typed inserts, and a ratchet on connections opened outside them. | 4th #21 | M |
| R30 | CLI, Pilot | CI never builds the Pilot or runs its 25 SQL reads; its ~35 `bsk` command lines are parsed by nothing; four of its vocabulary tables copy the kernel's unchecked. `BskJsonTests`' pairs are a hand list, and `bsk timer --json` already prints a different shape from `POST /api/timer`. The Pilot built in CI; each reader method run once as `bsk_reader`; one `BskVerbs.cs` parsed by `BskRoot`; a walk of `BskRoot`'s verbs, each paired or listed as `bsk`'s own with why. | 4th #26, #27 | M |
| R31 | CLI | `bsk` is the door CR26 left out: every exception, refusal or fault, exits 1 with its message, `--json` errors carry no kind, and resolving the connection, `--json` and building services is copied into 77 verbs. One `Cli.Verb` helper on `KernelRefusals`' table: exit 2 for a refusal, 1 for a fault, and a JSON `kind`. | CR26 | M |
| R32 | Ops | Four `.env` parsers that disagree: `fly-setup.ps1` and `LifeOsBackup.psm1` strip only double quotes, so a single-quoted value reaches Fly and GitHub secrets with its quotes, which breaks staging at the next deploy. Also four connection-string parsers and four ways to run `bsk`. One module (`LifeOsConnection.psm1`, with R11's `Get-MainBsk`), tested on quoted values. | | S–M |
| R33 | Web screens | No accessibility scan opens the shell's overlays: the palette, Quick and Voice capture, Redeem, the reward form, either Unsaved edits dialog. Scan each, and a check that every file rendering a dialog's or a sheet's content is opened by a spec or listed with why. | CR97 | M |
| R34 | Web | Built as CR133 (nightly 2026-11-17); `tsconfig.e2e.json`, which `tsconfig.json` references, so `npm run typecheck` checks the specs. `web/e2e` is never type-checked (`tsconfig.node.json` leaves it out), and five errors are there today (`golden/tasks.spec.ts` calls `group(name: string)` with a RegExp). Include it, and `e2e-setup.test.ts` checks it stays included. | CR104 | S |
| R35 | Ops (latent) | `deploy.yml`'s concurrency group covers the whole run, so once production is on, staging waits behind the last production approval. A group per job and environment, as `migrate.yml` has. | 4th #28 | S |

The review's suggested order: R1 to R11; then R13, so R12 and R14 to R18 can be ranked by what
they cost; then R19 to R24. The cheapest are R4, R14, R26 and R34.

### P3: when working in that area

Kernel: `TaskSeries.CopiedKeys` copied into the repository's SQL; a lab visit makes its metrics
before its own write, and its range sync reads every reading; faults thrown as
`InvalidOperationException` answer 400; eleven `ParseDay` copies in the kernel and thirteen day
parsers in the API, one of which ignores a bad `from=` and reads every occurrence; the Monday week
start written five times; stale Dapper comments; a subject's first status inserted without
`EventSql`. API: MCP areas registered by hand in four places; habits on `/mcp` say both "missed" and
"not_followed"; the guide's status table and `RecordKey`'s list of versioned records are copies; 26
of 141 routes declare 404; the agent-in-use predicate written four times; status words as literals.
Database and tests: relation vocabularies not checked against their CHECKs; `RuleParityTests`' file
list; two unused indexes and none on `subject(type)`; migrations' 30-second command timeout; tests
on the real clock. CLI, Pilot and ops: `FoodCommand`'s time parsing, and a drink-score rule only
`bsk` holds; `bsk home --json` reads twice; the Pilot's `bsk` calls on the UI thread and its polled
timer read; stale notes (`verifying.md`'s `-OpenApiUrl`, "bsk is the only writer"); the two Fly
configs unchecked, floating base images, `migrate-staging.yml` repeating `migrate.yml`. Web data:
seam members no screen calls (`artifact`, `spaceRatings`, editing and deleting a rating); CR55's lint
rule tied to two file paths; `CREATABLE_TYPES`, `PARENT_MAP`, `ATTR_KEYS_BY_TYPE`, `PERSON_ROLES` and
`habitDay` without shared cases; `qk.recap` refreshed by nothing; the README, ten stale `TODO(api)`
notes, the `bump` and `emit` nobody hears, and the dead draft `docs/pilot/production-ui-types.ts`
(347 lines; deleted 2026-11-15). Web screens: `MetricFields` and one `PhotoPicker`; a shared page-test harness (Map,
Rewards, Food, People and Areas have no page test); three segmented controls, two Unsaved edits
dialogs, settle-on-open written four ways, and a "G F" shortcut the palette shows that nothing
handles; the list-plus-detail wiring five times, where Habits and Rewards don't pass
`onOpenSubject`.

### For Chris to decide

- **B:** the mock's copy of the reward economy is about 3,200 lines in `lib/mock`, with about 1,800
  lines of tests. Since 6152b61 no commit touched the copied folds, but both commits that changed
  kernel code (CR105 and the People photo) also had to edit the mock, and CR106 moved Focus's rules
  into the mock (`mock/dashboard.ts`) rather than deleting them. *Chosen 2026-11-15: each part
  becomes canned reads when its rule next changes (CR124).*
- **C:** unchanged. *Chosen 2026-11-15 (CR126).*
- **D (new):** the Goal and Habit gates (a Goal can't be Active without a target date; a Habit needs
  a Goal or an Identity Statement) live in the web twice (`lib/subject-forms.ts`, and a copy in
  `subject-detail.tsx` that can't run because the form refuses first) and in the Pilot, but not in
  Application, so `bsk` and agents skip them. `ui-requirements.md` puts them in the app layer.
  Either move them into Application, refused at every door, or record that they stay in the screens
  and delete the web's copy that can't run. *Chosen 2026-11-15: into Application, refused at every
  door (CR125).*

## Nightly reviews

From 2026-11-17 a scheduled task in Chris's Claude app (`lifeos-nightly-quality`, 2 AM) reviews the
repo the way the full reviews did: six Opus 5.5 reviewers, one per area, findings first and scores
second, except that none is shown an earlier score. It writes the review to
`docs/agents/quality-reviews/<date>.md`, adds its scores to `docs/agents/quality-scorecard.md`, and
builds up to ten of its checked findings that are small enough to finish that night with a test,
each as a CR of its own, on `main` and not pushed; then it emails Chris a summary. It leaves alone
what a plan records as his choice or as waiting on him, what needs a live system to make or check,
migrations that move data, changes to the event log's payloads or the reward economy, and anything
outside programs read that it would remove or rename. A proposal above that it builds starts "Built
as CR<n> (nightly <date>)". Each night adds a bullet here with its score, its review and the CRs it
built.

- **2026-11-17** at 58a9e53: 6.6 (+0.1). [Review](../agents/quality-reviews/2026-11-17.md).
  Built: CR129 (a food, room-rating, exception-day or Oura-sync write answers pending when its
  settle or rank-up fails after the commit); CR130 (the Food Log golden spec counts its days from its
  fixed clock, and no spec that fixes one reads the real date); CR131 (the item detail's tabs say a
  failed read, R24 in part); CR132 (the `habits` tool's description names the states it answers);
  CR133 (the e2e specs are type-checked, R34); CR134 (a running fast reaches the kernel's clock,
  R28 in part).
