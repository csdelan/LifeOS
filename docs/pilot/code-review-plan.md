# Code review fixes plan

**Status:** CR1 to CR26 built (2026-11-12). CR27 to CR56, the second review's P0, P1 and P2, built
(2026-11-13), and CR57 for its choice A; choices B and C wait for Chris
([Second review](#second-review-2026-11-13)). The third review scored the code 6.1 of 10, and Chris
chose its P0 and P1, CR58 to CR67, built (2026-11-13), then its P2, CR68 to CR90, built
(2026-11-13) ([Third review](#third-review-2026-11-13)).
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
- **C.** A non-owner database role for the API, so it can't drop the append-only triggers.

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
- **C:** unchanged.

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

### P1: built so far (CR92, CR93)

| ID | Review # | Fix |
|----|----------|-----|
| CR92 | 8 | Discard in "Unsaved edits" asks once: the router's blocker reads the flag where Discard has already cleared it. Discard throws the edit away when the move keeps the same item open (the panel and the review editor key themselves by it), so the shell and what holds the edit agree. Escape is Stay, and lets the router go: before, the move stayed blocked and every later link did nothing. The guard has a module of its own (`components/shell/unsaved-edits.ts`), and three golden paths cover it. |
| CR93 | 9 | A page opened before a deploy copes with the files the deploy removed. Every lazily loaded piece (the pages, the palette, the dialogs, the Map's graph, the journal) reloads once when its file is gone (`lib/new-build.ts`, Vite's `vite:preloadError`), and each page has its own error view inside the shell, which says "LifeOS was updated" with a Reload button when a reload didn't help. A removed `/assets` file answers 404; it got the sign-in's 401, not the app's page as the review said. Every copy of the app's page says not to cache it: only a literal `/index.html` did, not `/` or a link to a page. |

### P2 and P3: built so far (CR94 to CR100)

| ID | Review # | Fix |
|----|----------|-----|
| CR94 | P3 | Lint warnings fail: `npm run lint` is `oxlint --deny-warnings`, which CI runs. The 25 warnings went first. Shared hooks and helpers left the component files that exported them (`useAuth` to `auth/auth-context.ts`, `usePersistedWidth` to `shell/use-persisted-width.ts`, `oversizedFile` to `lib/artifacts.ts`), so fast refresh works there; the shadcn `*Variants` exports are allowed by name. State worked out from other state is no longer set in an effect (the phone's navigation, the Unsaved edits dialog, the Relationships target); refs are no longer written during render (the panel width, the graph's edges); a file's icon is drawn by `ContentTypeIcon`; the forms read a field with `useWatch`. Six effects stay as they are, each with a disable comment that says why: the restores on opening Browse and the Map, Expand all, the voice dialog's recording, the New dialog's reset and the graph's layout. |
| CR95 | 33 | Tab leaves the Map's inline create as it does anywhere else; it used to change the type, so the keyboard couldn't reach Add or anything past it. The type buttons are a group named "New item's type", each saying with `aria-pressed` whether it's chosen. A golden path tabs to Add and chooses a type from the keyboard. |
| CR96 | 31 | The seven form and record sheets (food, readings, lab visits, photos, the walk-through, workouts, days) open through one `ResponsiveSheet` (`components/primitives`): a bottom sheet on a phone, a side panel on a desktop. Once something in it is typed or chosen, Escape, the close button or a tap outside asks "Unsaved edits" first, with Stay and Discard; its own Save or Cancel closes it without asking. The workout and day sheets don't ask: a day only shows, and a workout's note saves as the sheet closes. A test lists the sheets that still open shadcn's sheet themselves, each with why, so a new form sheet can't skip the guard; a golden path covers Log food. |
| CR97 | First review's P3 | The pages are one table, `components/shell/pages.ts`, which the navigation and the command palette list; before, each kept its own list, and the router and the a11y spec two more. `pages.test.ts` checks the table names every route the router serves (Oura's return page listed apart, with why) and no other, and that an accessibility spec opens each page, so a page can't be added to one and missed in another. |
| CR98 | 32 | The bar chart, the trend chart and the rank chart read their points through one `useChartCursor` (`components/charts`): hover reads the point under the pointer, focus starts at the newest, the arrow keys, Home and End move it, Enter or a click opens it where the chart can, and leaving or blurring clears it. The copies had drifted: the bar and rank charts read point -1 when focused with nothing to show. The charts' class check now finds them by the hook and fails on any component that handles the arrow keys and the pointer itself. |
| CR99 | 14 | The Tasks page reads only what its view shows: Active its open Tasks as before, a view of one status that status alone (Not started the open ones, since a Task with no status yet is Not started), and All alone every Task. It groups and sorts the list once per change to it, not on every render, and the new-task entry holds its own text, so typing a title no longer redraws every Task under it. The list draws a page at a time across its groups, with Show more, through the paging Browse used, now shared (`usePaged` and `ShowMore` in `components/primitives`). A page test checks each, and fails on the old page all three ways. |
| CR100 | 29 | The Inbox draws its list as plain rows (`InboxRow`), and `@tanstack/react-table`, which it used for one column, is gone from the app. The Inbox's chunk is 13.5 kB, where the review measured 124 kB. |

### P1: proposed

| # | Area | Problem and fix | Finishes | Size |
|---|------|-----------------|----------|------|
| 2 | Kernel | A write that commits can still fail its request, when a follow-up after the commit throws, and the refusal then says nothing was written. `bsk` has no zone check at start. Follow-ups run best effort and say what failed, the wording says what was written, and `bsk` checks the zone as the API does. | CR88 | M |
| 3 | Kernel | Filing an Inbox item runs in three transactions, so a retry after a failure makes a second Idea; the voice door's duplicate check is a rule in a door. File in one transaction, with the check in Application. | CR68 | M |
| 4 | Web data | The web works out habit due days itself (`todayHabitRows`, `tracksOn`, the streak grid), and they differ from the kernel's occurrences, so Missed is offered on days the kernel doesn't schedule. Writing it is a permanent event that lowers adherence and XP. Use the kernel's occurrences, with a golden path on a habit that isn't daily. | | M |
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
| 13 | Web data | Reads still unscoped: the shell's Inbox badge reads the whole Inbox (`inboxCount` exists, unused); the Relate picker, the New dialog and the Relationships tab read every subject; the Map reads every repetition. | CR76 | M |
| 14 | Web screens | Built as CR99. The Tasks views read and draw every Task, and regroup them on every render. | CR76 | M |
| 15 | API | The alignment tool reads every subject and edge on each call, and `recent_changes` builds its whole window before its limit. | | M |
| 16 | Web data | The live Focus page is five reads, every edge among them, worked out in the browser with a copy of `life_snapshot`'s rule: `GET /api/focus`. | | M |
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
| 30 | Web screens | The whole `supabase-js` client loads with the first page, about 100 kB, for auth alone. | | S–M |
| 31 | Web screens | Built as CR96. Seven copies of the side or bottom sheet, none guarding typed input when it closes: `ResponsiveSheet`. | | M |
| 32 | Web screens | Built as CR98. Three copies of the chart cursor: `useChartCursor`. | | M |
| 33 | Web screens | Built as CR95. The Map's inline create traps Tab, and its type buttons don't say which is pressed. | CR87 | S |

### P3: when working in that area

Day-award sources as one `IDayAwardSource` list; stale Dapper comments and seven `ParseDay` copies;
rating a space reads every rating; a lab visit's range sync reads every reading and makes metrics in
transactions of their own; fast overlap uses the database's clock; a missing URN answers 400 in
`ResolveOrCreateAsync`, 404 elsewhere; `ReadFailed` over shown data when a refetch fails; the e2e
specs on a preview build, with one timeout, one scan helper and named asides (lint warnings fail
since CR94); the mock clients' wrapper boilerplate and the `bump` and `emit` nobody hears; the review recap
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
- **C:** unchanged.
