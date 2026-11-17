# Focus Plan: a Primary focus you choose, and Goals under Goals

The design and build order for choosing your Primary focus in a review (TODAY-1, D6) and for
sub-goals, short-term milestones of a longer Goal (GEN-7, GEN-8), in
[ui-requirements.md](ui-requirements.md). The requirements say *what*; this doc says *how* and
*in what order*, written so an agent with no session context can pick up any phase.

**Why now.** On 2026-11-17 Chris asked how to change his Focus goal. The review's *Keep focus*
switch didn't seem to do anything, and it doesn't: it saves `keep_focus` on the review, and nothing
reads it. Focus has no setting of its own. `FocusReader.DashboardAsync` picks the Active Goal with
the nearest target date (ties go by title), so the only way to change the focus is to give a Goal
the soonest date. Goals also can't be nested: from a Goal, the app creates Projects and Tasks, not
Goals, so a short-term milestone of a long-term Goal has nowhere to go. To focus on one for a week,
Chris would have to date it ahead of every other Goal.

## 0. Read these first

- [ui-requirements.md](ui-requirements.md): TODAY-1 (Primary focus), D6 (two focus levels, each a
  Goal, set in a review, kept as events), REVIEW-0 ("Focus determines which Goals should be
  prominent for the current period"), REVIEW-1 (the daily review's planning area reassesses the
  focus), REVIEW-2 (weekly planning starts with keep or change), GEN-7 (parent-first creation),
  GEN-8 (the Goal form and its activation gate), GEN-16 (long-term Goals and Vision).
- [reviews-plan.md](reviews-plan.md) §1 and §4.1: the keep-focus switch, and how a reflection
  link was kept as an event concerning the Review. The focus copies that shape.
- `src/LifeOs.Api/Read/FocusReader.cs`, `web/src/components/reviews/review-editor.tsx`,
  `src/LifeOs.Domain/ParentChildRelations.cs` and `PARENT_MAP` in
  `web/src/lib/production-ui-types.ts`.

## 1. The rules

| Question | Decision |
|---|---|
| What a milestone is | **A sub-Goal**: a Goal that `serves` its parent Goal (Chris, 2026-11-17). It is a Goal like any other, with its own target date, status and Projects. The other option was a Project with a date. A milestone is an outcome ("Run a 5K"), and the ontology calls an outcome a Goal and a body of work a Project. D6's rule that the weekly focus advances the monthly one is also read from the links between Goals. |
| What the focus is | **A Goal you choose**, kept until you change it (TODAY-1, D6). Not free text: D6 settled the focus as Goal-linked, and with sub-goals a small Goal is quick to make. |
| How many | **One for now**, D6's weekly focus. The monthly focus comes with monthly reviews (REVIEW-3). |
| Where it's chosen | **In the daily and the weekly review**, and with `bsk focus`. REVIEW-1 has the daily review's planning area reassess the focus, and REVIEW-2 starts weekly planning with keep or change. The Focus page links to today's review to change it rather than holding a second picker. |
| Which Goals | **An Active, unarchived Goal**, a sub-goal or not. A New Goal is still being developed (GEN-8). |
| When none is chosen | **Today's rule is the suggestion**: the Active Goal with the nearest target date. The page says it's a suggestion, as TODAY-1 asks ("clearly distinguish priorities I selected from suggestions"). |
| When the chosen Goal ends | Completed, Abandoned or archived: the focus reads as none, the page shows the suggestion and says the old focus ended, and the next review asks for a new one. Nothing writes that on a schedule. |
| Keeping it | The switch stays and stays on by default. On keeps the focus and writes nothing. Off opens a Goal picker; saving writes the new focus and `keep_focus` false on the review, so the review records the change. |
| Clearing it | The picker has *No focus*, which writes a focus of none. The page then shows the suggestion. |
| A sub-goal's date | **May fall after its parent's**; the form warns and still saves ("guide, don't hard-validate", REVIEW-0). |
| Loops | **Refused.** No `serves` or `results_in` link may close a loop (a Goal under its own sub-goal). The kernel refuses it in every door. Today the Relationships tab can already link a Goal to a Goal, and nothing stops a loop; a loop with no Value above it has no root, so it drops off the Map. |

## 2. The model (G1)

### 2.1 The focus is an event

- Choosing a focus writes a `note` event with the payload
  `{"focus": {"level": "weekly", "goal_id": "<uuid>" | null, "review_id": "<uuid>" | null}}`. It `concerns` the Goal and, when
  chosen in a review, the Review. A null `goal_id` is *No focus*. The source is the door's, and
  the newest by `recorded_at` wins, so every change stays in the log (D6: "record changes as
  events so history falls out of the append-only log").
- `bsk.v_focus` (migration 0074): the newest focus per level (`level`, `goal_id`, `review_id`,
  `chosen_at`, `event_id`), reading the ids through `bsk.is_uuid_text` (`CastGuardTests`).
- `event_focus_idx` on `((payload->'focus'->>'level'), recorded_at)` where `kind = 'note' AND
  jsonb_exists(payload, 'focus')`, with its read in `EventIndexTests`. The view ends with
  `level` as text, its partition key.
- `bsk.event_record` names a focus by its level, and the migration rebuilds
  `event_record_idx` (`VersionedRecordTests`). `recent_changes` then shows each change once.
- `McpAreas` claims the `focus` payload key. The scale seed gets a focus stream (a change a
  week for three years), checked in `SeedTests`.
- Migration: the next free number when G1 is built. Look, don't guess (AGENTS.md).

### 2.2 Service, reads and doors

- `FocusService` (Application/Focus): `SetAsync(goal?, reviewId?)` refuses anything but an
  Active, unarchived Goal, in words (a 400; an id that matches nothing is a 404); choosing the
  focus you already have writes nothing. Chosen in a review, it turns that review's
  `keep_focus` off.
- `FocusReader.PrimaryAsync` reads `v_focus` first, for Focus and `life_snapshot`.
  `primaryFocus` gains `chosen` (true when you chose it), `chosenAt`, `chosenIn` (the review's
  title, "Weekly review, Sun Nov 22"), and `lapsedTitle` and `lapsedAs` (a chosen Goal that has
  since ended, and how: Completed, Abandoned, Archived).
- `bsk focus [show] | set <goal> [--review <id>] | clear`, with `--json` in the API's shape
  (`FocusChoiceRow`, `BskJsonTests`).
- `POST /api/focus/primary {goalId: uuid | null, reviewId?}`, through `KernelRefusals`.
  Regenerate the web client.
- `/mcp`'s `life_snapshot` adds the focus Goal (it reads `FocusReader.DayAsync` today, which has
  none), so Chris's agents know it. Agents can't set it: it's beyond the `capture` level.

### 2.3 Web

- `lib/features/focus/` holds the focus's types, reads, writes, effects and query keys, and
  `lib/mock/focus-store.ts` its mock (the `features/measurements` pattern).
- The review's Primary focus card shows the focus and whether you chose it or it's suggested.
  Turning *Keep focus* off shows a picker of the Active Goals Focus already reads, with their
  target dates, and *No focus*. (Not a `SubjectSearchCombobox`: it lists nothing until you type
  and can't keep to Active Goals, and there are only a handful.) Save chooses the focus, and the
  kernel turns the review's `keep_focus` off; off with nothing picked saves the switch alone.
  Its `writeEffects` entry refreshes Focus and the reviews.
- The Focus page's card says "Chosen in your weekly review, Sun Nov 22." or "Suggested: the
  Active Goal with the nearest target date.", says when a chosen focus has ended ("Your focus,
  Run a half-marathon, is completed."), and links to Reviews, which opens today's review, to
  change it. `lib/features/focus/describe.ts` says both, for both pages.

## 3. Sub-goals (G2)

- `ParentChildRelations` and the web's `PARENT_MAP` gain Goal under Goal by `serves`. A Goal's
  page and the Map then offer *Goal* as a child (*Add sub-goal*).
- The Goal form's "Connect this Goal to an Identity Statement" rule (`subject-forms.ts`) also
  takes a parent Goal: a sub-goal reaches a Value through its parent. It stays a form rule
  (GEN-8).
- `RelationService` refuses a `serves` or `results_in` link that closes a loop, for any types.
  The refusal says which link closes it. A test runs it through `bsk link`, the API and parent-first
  creation.
- A Goal's page lists its sub-goals with their target dates and statuses, and a sub-goal's page
  shows its parent Goal (GEN-7's grouped children; check the grouping names them *Sub-goals*).
- Focus's projects (`Objectives`) are the Active Projects that result in the focus Goal or in
  any of its Active sub-goals, nearest target date first, up to three, or any Active Projects
  when none do. The card also lists the focus Goal's Active sub-goals and their dates as
  milestones.
- Vision is unchanged: a Goal shows there when its target date is more than two years out
  (GEN-16), so most sub-goals don't.
- The drift diagnostic needs no change: a Project under a sub-goal results in a Goal.

## 4. Phases

| Phase | What lands | Migration | Pushable alone |
|---|---|---|---|
| **G0** | This plan | — | Yes |
| **G1** | A Primary focus you choose: the focus event and `v_focus`, `FocusService`, `bsk focus`, `POST /api/focus/primary`, Focus and the review read it, `life_snapshot` shows it | Next free | Yes |
| **G2** | Sub-goals: Goal under Goal, the loop refusal, the form rule, sub-goals on a Goal's page, Focus's projects and milestones from the sub-goals | — | Yes (the Focus parts after G1) |

- Checks before each commit: `./test.ps1` (.NET), and from `web/`: `npm run lint`,
  `npm run typecheck`, `npm test`, and the reviews and Focus e2e and a11y specs.

## 5. How this was settled

- **2026-09-14, Chris (D6):** the Primary focus is two Goal pointers, monthly and weekly, each
  set in its review and kept as events. Not daily, not free text.
- **2026-11-17, Chris:** asked how to change his focus, found the switch did nothing, and
  pointed at the real gap: "I can't create subgoals from those (short term goals as milestones of
  long term goals)", so focusing on one for a week meant dating it ahead of every other Goal.
  The two problems were split: the focus is guessed from dates (G1), and milestones have no
  home (G2). He chose **sub-Goal** over a Project with a date.
- **Filled in here:** one focus for now (the weekly one), set in the daily and the weekly review;
  only an Active Goal; the date rule as a labelled suggestion when none is chosen; an ended
  focus reads as none; *No focus* clears it; the Focus page links to the review rather than
  holding its own picker; a `note` event concerning the Goal and the Review; a sub-goal's later
  date warns; loops refused in the kernel; Focus's projects drawn from the sub-goals too.
- **Built (G1, 2026-11-17):** the review's picker is a short list of the Active Goals rather
  than the server search (§2.3). Choosing the focus you already have writes nothing and leaves
  the review's switch as it was, since the focus was kept.
- **Built (G2, 2026-11-17):**
  - The loop check walks `serves` and `results_in` together, so a Goal can't serve a Project
    that results in it either. It runs in `RelationService.LinkResolvedAsync`, the one path that
    links two items already there (`bsk link`, `POST /api/link`, a Decision's links), under one
    advisory lock (`AdvisoryLocks.Alignment`), so two links at once can't close a loop between
    them. Parent-first creation can't close one, since the new item has no edges yet, so it isn't
    checked. A Goal linked to itself is refused too. The refusal names the path: "'Run a half
    marathon' can't serve 'Run a 5K': 'Run a 5K' already sits under 'Run a half marathon' (…)".
  - §3 said the drift diagnostic needed no change, but the wishes check (`30__wishes.sql`) asked
    for an open Project or Task under each Goal, so a long-term Goal worked on only through its
    sub-goals would have been a wish. An open sub-goal now counts as work toward its parent, and
    the finding lands on the sub-goal with nothing under it.
  - The relations read (`RelationEdge`) carries each item's status and target date, so a Goal's
    Relationships tab shows its sub-goals' and every other row's. The group is named
    *Sub-goal*, singular like the tab's other groups. The Focus card's milestones are the focus
    Goal's direct Active sub-goals; its projects come from the sub-goals at any depth, through
    Active ones only, so a finished sub-goal's projects drop out with it.
  - The New dialog and the Map call a Goal made under a Goal a *Sub-goal*. The Goal form's
    parent picker offers Identity Statements and Goals, never the Goal itself, and a Goal that
    serves no Identity Statement shows its parent Goal there when it's edited. A target date
    after the parent Goal's is said under the field, and saving still works.

## 6. Later

- **The monthly focus**, with monthly reviews (REVIEW-3), and D6's soft check that the weekly
  focus advances it, read from the Goal graph.
- **Goals due in the coming week** called out in weekly planning (REVIEW-2, reviews plan §7).
  Sub-goals make this more useful.
- **Daily objectives** (TODAY-1, TODAY-2): the one to three per day, and an unfinished one
  going to the Inbox at day's end.
- **Rank milestones** (xp-domains plan §6): a rank that needs a Goal completed, which a
  sub-goal could be.

## Status

- [x] G0 — This plan
- [x] G1 — A Primary focus you choose (migration 0074)
- [x] G2 — Sub-goals
