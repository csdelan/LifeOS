# Recurring Tasks Plan

**Status: approved and building (2026-10-24).** Chris asked on 2026-10-24 to explore
recurring Tasks and answered the exploration's questions the same day (§1.1). He then
approved RT0–RT5 ("Approved Start RT0-RT5. Go straight through, only pause when you need
additional input from me."). The requirement is **TASKS-3** and the decision **D16**, both
in [ui-requirements.md](ui-requirements.md). This doc says *how* and *in what order*,
written so an agent with no session context can pick up any phase. Progress is in §10.

## 0. Read these first

- [ontology.md](../ontology.md): subjects are interpreted truth (attributes edited in
  place); status moves only by `state_change` events; `bsk` is the one writer.
- D8 (recurrence), D10 (Appointment occurrences), D11 (rewards reconcile) and D16 in
  [ui-requirements.md](ui-requirements.md).
- Prior art: `AppointmentService.MaterializeAsync` and `NpgsqlAppointmentRepository`
  (occurrences from a series); `RenameService` (a series rename follows to its copies);
  `NpgsqlTimerRepository` (an advisory lock with a re-check); the food and domains
  settle endpoints (catching up when the app opens).
- How a phase lands: [kernel-build-plan.md](kernel-build-plan.md) (migration + verb + view
  + tests, `./test.ps1`, strict build). Web checks: `npm run lint`, `npm test`,
  `npm run test:e2e` and `npm run a11y` from `web/` (CI doesn't run Playwright).

## 1. What Chris asked

> "I probably just need simple recurrence like:
> - 'Take out trash' every Wednesday afternoon
> - 'Clean fridge' every 30 days (calculated from completion date of last instance)
>
> If I miss taking out the trash 1 week, that instance is missed, and I close out the task
> without getting rewards. Whereas the Clean Fridge instance doesn't need to be done on a
> specific day, but rather just routinely."

> "Keep currency awards consistent across each repetition."

His answers to the exploration's questions:

> "Sometimes it will be Thursday morning that I take it out. … I just need the ability to
> close out a missed task myself. It should not be automatic. The rest of the answers are
> fine."

On the other scenarios: a calendar date that stays open when late, "yes"; once a week on any
day, "yes"; "Other scenarios I don't really need at this point."

### 1.1 The rules (agreed 2026-10-24)

| Question | Decision |
|---|---|
| Does anything close by itself? | **No.** A repetition stays open until Chris completes it, cancels it or marks it **Missed**. A late one shows under Overdue. |
| When is the trash missed? | Only when Chris marks it Missed. Taking it out Thursday morning and completing it pays in full. |
| Can a Missed one be corrected? | Yes: change it to Completed and it pays, as with a habit day (GEN-5). |
| Early? | Completing the open repetition before its date counts for that date. |
| Time of day ("afternoon")? | Not kept. The date is enough; LifeOS has no reminders. |
| The fridge's first due date? | A date Chris picks, defaulting to today. |
| An overdue fridge? | Shows under Overdue, as any late Task. |
| Skipping on purpose? | Cancel that repetition: no reward, not counted as missed. A repeat counted from the last completion restarts from the day it was cancelled. |
| The price? | Every repetition pays the repeating task's price. One repetition can be given its own. |
| Editing the repeating task? | Changes the repetitions still open, except what was changed on that repetition. Never a reward already paid. |
| A calendar date paid late (rent)? | Stays open. If the next date arrives first, both are open. |
| Once a week, any day? | Yes. The week (Monday to Sunday) is the repetition: do it any day, due Sunday. |
| Where does it show during the week? | In **Today** all week. Any Task whose do-date has arrived stays in Today until it's done or its due date passes, then moves to Overdue. This applies to every Task, and TASKS-1 was amended for it (§9). |
| Not needed now | Nth weekday of a month, yearly dates, seasonal, pausing, ending after a count, streak bonuses, steps or checklists, a count per period, assigning to someone, reminders. Several days a week and every other week already work with today's patterns. |

## 2. Three kinds of repeat

Because nothing closes by itself, one question separates them: where the next date comes
from.

| Kind | Examples | A repetition is | The next one is made |
|---|---|---|---|
| **On calendar dates** | Trash every Wednesday; rent on the 1st | due on that date | when the current one closes, or when its date passes, whichever is first |
| **Once each week** | Clean the bathroom sometime each week | do-date Monday, due Sunday | when the current one closes, or when Sunday passes |
| **After the last completion** | Fridge every 30 days | due N days after the last one closed | when the current one closes |

What follows from it:

- **A calendar repeat can have more than one open.** November's rent still unpaid when
  December's is made: both show, each pays when done. Each date gets exactly one repetition.
- **After three weeks away,** each missed Wednesday has its own trash repetition under
  Overdue, to complete or mark Missed one at a time. The Pilot has no bulk actions
  (TASKS-1). Pausing, deferred for now, is the fix if this becomes a nuisance.
- **A repeat after the last completion never has two open.**

## 3. What LifeOS already has

- **A repeat pattern (D8).** `attributes.recurrence` holds daily, weekly (chosen weekdays),
  every N days/weeks/months, monthly (a day number or the last day), or trigger. Migration
  0016's `bsk.recurrence_occurrences` turns it into dates. `bsk recur` writes it. Nothing
  stops it from going on a Task already.
- **Two ways of repeating, both deliberate (D10):**
  - **Habits compute their days.** One Habit record. Each day is worked out from the
    pattern, a check-off is an event, and a past day with no check-off reads as missed. No
    record exists per day.
  - **Appointments make a copy per occurrence.** A series record holds the pattern, and each
    occurrence is its own Appointment (`attributes.series`) with its own status. Copies are
    made only by `bsk materialize --through <date>`; nothing makes them automatically.
- **Rewards pay once per record (D11).** A Task pays under `completed:<task>` and, for timer
  time, `timed:<task>`. The engine pays what the record says is due *now*: if the Task isn't
  Completed at the moment, its award is reversed. So **one Task record that is completed,
  reopened and completed again each week can only ever hold one payment.** This shapes every
  option below.
- **Nothing runs on a clock.** There's no background job, and the Fly machines stop when
  idle (`min_machines_running = 0`). Work that depends on time passing is caught up when the
  app opens: Food (`POST /api/food/settle`), Domains (`POST /api/domains/settle`) and the Oura
  sync all do this.
- **Days are home-zone days.** `bsk.home_today()` and `IHomeCalendar` give today's date in
  the home zone. "Wednesday has passed" must use them, not UTC. **Weeks run Monday to Sunday.**

## 4. The options

| | **A. Chores are habits** | **B. One Task, days computed** (the Habit way) | **C. One Task that rolls forward** (Todoist) | **D. One Task per repetition** (the Appointment way) ★ |
|---|---|---|---|---|
| How it works | A Habit with no "established" goal | The Task holds the pattern; each completion is an event for a date | Completing it moves the due date on and reopens it | A series holds the pattern and price; each repetition is an ordinary Task |
| Trash (late or missed, Chris's call) | Poorly: habits mark a past day missed by themselves | Needs that rule changed | Poorly: no record of the missed week | Fits: it stays open until he completes it or marks it Missed |
| Rent (two months open at once) | No | Needs new logic | No | Fits: one Task per date |
| Fridge (30 days from completion) | No: habit days come from a start date | Needs new logic | Fits | Fits: completing it makes the next one |
| Shows on Tasks, due and overdue | No (Habits page) | Needs a new layer on every Task screen | Yes | **Yes, unchanged** |
| Timer, status, notes per repetition | No | Timer and status need splitting by date | Mixed: the timer adds up across every week | **Yes, unchanged** |
| Same reward every time | Yes (per-day key) | Needs new per-date keys | **Breaks**: the engine reverses each payment when the Task reopens | **Yes**: each repetition pays the series' price under its own key |
| Records created | none per day | one event per completion | none | one Task per repetition (52 a year for a weekly chore) |
| Ontology change | Bends Habit (a habit you never build) | A new "occurrence" layer on Task | Status history cycles Completed → Not started | None new: D10's pattern used a second time |

**Recommendation: D.** Chris's answers didn't change it; they strengthen it, since
nothing auto-misses and rent needs two open at once. Every Task feature (the Tasks
groups, Change status, the timer, the `timed` reward, notes, the detail page) works on a
repetition because it *is* a Task. The new work sits in one place: the series, a service
that makes the next repetition, and the price coming from the series. B is the tidier store
(no copy per week), but it puts a new layer on every Task screen and on the timer. C breaks
the reward engine, the one thing Chris asked to keep consistent. A covers none of the cases
well, and chores would move off the Tasks page.

## 5. How it plays out

**Take out trash: weekly on Wednesday.**

| When | What happens |
|---|---|
| Mon 26 Oct | Chris creates "Take out trash", repeating weekly on Wednesday. This week's is due Wed 28 Oct, under Upcoming. |
| Wed 28 Oct | Under Today. He completes it: it pays the series' price, and next week's is made, due Wed 4 Nov. |
| Thu 5 Nov, 7 am | 4 Nov's isn't done and is under Overdue. 11 Nov's has been made because the 4th passed. He takes the trash out and completes 4 Nov's: it pays in full. |
| A week he skips it entirely | That week's waits under Overdue until he marks it Missed: no reward, no penalty. |

**Pay rent: monthly on the 1st.**

| When | What happens |
|---|---|
| October | 1 Nov's is under Upcoming. |
| Mon 2 Nov, unpaid | 1 Nov's is under Overdue, and 1 Dec's is made. |
| Thu 5 Nov | He pays and completes 1 Nov's: it pays the series' price. 1 Dec's is already there. |

**Clean the bathroom: once each week.**

| When | What happens |
|---|---|
| Mon 26 Oct | This week's: do-date Mon 26 Oct, due Sun 1 Nov. It shows in Today all week, with "due Sunday". |
| Wed 28 Oct | He completes it: it pays. Next week's is made: do-date Mon 2 Nov, due Sun 8 Nov. |
| Mon 9 Nov | Last week's wasn't done: it's under Overdue, and this week's is made. |

**Clean fridge: every 30 days from the last completion.**

| When | What happens |
|---|---|
| Created | The first is due on the date Chris picks (default today). |
| Done Mon 2 Nov | It pays the series' price. The next is due Wed 2 Dec. |
| Wed 2 Dec passes | It stays open, under Overdue. |
| Done Wed 9 Dec | It pays the same price. The next is due Fri 8 Jan: 30 days from the 9th, not the 2nd. Cancelling or marking it Missed on the 9th would count from the 9th the same way. |

## 6. Rewards: the same price every time

- **The price lives on the series.** A repetition has no bounty of its own, so when it's
  completed, `RewardService.ReadBountyAsync` falls back to the series' bounty (then to the
  Task default, as today). Every repetition pays the same unless the series' price changes.
- **Changing the series' price** changes the repetitions completed after the change. A past
  payment is never re-priced: the engine already freezes each earn at the moment it's paid.
- **Each repetition pays under its own key** (`completed:<repetition>`,
  `timed:<repetition>`), so the engine, undo and re-completion work unchanged, two open rent
  repetitions included. `v_reward_due` needs no change.
- **Late pays in full.** Missed and Cancelled pay nothing, with no penalty (REWARD-1: "No
  penalties for now"). Correcting Missed to Completed pays; the reverse gives it back.
- **A one-off price** is a bounty set on that repetition alone.
- **One caveat:** if a chore pays life-domain XP (say Environment XP), the multiplier frozen
  at payment (X5) can make two weeks pay different totals. The base is still the same.

## 7. Blast radius

Sizes are rough: XS is a line or two, S is under a day's phase, M is a phase of its own.

### Kernel: Domain, Application, SQL

| What | Change | Size |
|---|---|---|
| `Domain/Recurrence.cs`, `RecurrenceService`, `bsk recur` | Every N days gains `"from": "completion"`. A new once-each-week shape, `{"freq": "each", "period": "week"}`, which `bsk.recurrence_occurrences` expands to each week's Monday (a month period would be one more branch later) | S |
| `Domain/StatusVocabulary.cs` (+ the Pilot mirror) | Task gains **Missed**, a status Chris chooses, offered on every Task. `bsk.is_terminal_status` already treats `missed` as terminal | XS |
| **New `TaskSeriesService`** | Make the first repetition on creation. Make the next when one closes, and for the two calendar kinds when its date passes. **Settle** makes the calendar repetitions due by today. It never changes a status. One repetition per (series, date), serialized with an advisory lock as `TimerService` does | **M: the heart of it** |
| `StatusService` | After a repetition is Completed, Cancelled or Missed, call the series service. The timer's Complete goes through here, so it comes free | S |
| `RewardService.ReadBountyAsync` | Fall back to the series' bounty | S |
| `RenameService` | Already renames an Appointment series' upcoming copies; extend it to Task series | S |
| Migration **0042** | The once-each-week branch of `recurrence_occurrences`; a `v_task_series` view (pattern, open repetitions, next date, done / missed counts, last done); an index on `attributes->>'series'` | S |
| Archive (D9) | An archived series stops repeating; settle skips it | XS |
| Diagnostics | Neglect would flag a series if it carried `expected_cadence`; exclude series | XS |
| `AppointmentService.MaterializeAsync` | Unchanged. It could later share the new service and make appointments automatic too | none now |

### API

| What | Change | Size |
|---|---|---|
| Saving a repeat pattern | **No endpoint exists for any type** (§8.1). Add one, or accept the pattern on create and edit | S |
| `POST /api/tasks/settle` | New, called when Tasks and Focus open | S |
| `SubjectListItem`, `SubjectReader.QuerySubjects` | Expose the series and repeat label; keep series rows out of the Task lists | S |
| `GET /api/tasks/series` | A list of repeating Tasks to manage, if wanted | S |
| `openapi.json`, `schema.d.ts` | Regenerate | XS |

### Web

| What | Change | Size |
|---|---|---|
| `lib/recurrence.ts`, `RecurrenceSpec`, `RecurrenceEditor` | Switch to the kernel's format; add monthly, once each week, and "after the last completion". This also fixes Habits and Appointments (§8.1) | M |
| Task form (`lib/subject-forms.ts`, `production-ui-types.ts` Task keys) | A **Repeat** field | S |
| Tasks page, `lib/task-groups.ts` | Hide series rows; a repeat mark on repetitions; Missed hidden like Completed. TASKS-1's do-date rule (§9) ships on its own, in RT1b | S |
| Focus page, `lib/derive.ts` | Repetitions are ordinary Tasks; Missed drops out as terminal. The do-date rule applies to its Due & overdue list too (RT1b) | XS |
| Settle on open | Tasks and Focus call settle, as `use-oura-sync-on-open` does | S |
| Task detail | "Repeats every Wednesday", Edit this one or the series, Stop repeating, a done / missed history. Missed in the status list | M |
| Bounty section | Show the series' price as the repetition's price, with "this one only" | S |
| `lib/review-recap.ts` | Takes the first Completed per Task, which works per repetition. A review may want "trash: 3 of 4 weeks" | S |
| Map graph | Show the series as one node, not every repetition | S |
| Mock store and seed | A mock series service; seed one weekly, one monthly and one every-30-days chore | M |
| Tests | Unit (groups, recurrence), e2e for repeat flows, a11y in both themes | S |

### Pilot (WinForms) and CLI

- **Pilot:** frozen per [production-ui-plan.md](production-ui-plan.md). Repetitions show as
  plain Tasks there, which works. At most, hide series rows from its Tasks view.
- **CLI:** `bsk recur <task> …` already works on a Task; add `bsk tasks settle`, and maybe
  `bsk new Task … --repeat`.

### Unaffected

`v_reward_trigger` / `v_reward_due`, the ledger, wallet and redeem, `v_task_timer`, life
domains, Food, Activity and Covers.

## 8. Found while mapping

### 8.1 Repeat patterns don't reach the database from the web

- The web stores a pattern in its own shape (`{"kind":"weekly","weekdays":[3]}`), while the
  kernel expects `{"freq":"weekly","on":["wed"]}`.
- The live `recur` call is a stub that shows "Recurrence is not on the live API yet."
  (`web/src/lib/live/write-client.ts`).
- The pattern instead travels as a plain text attribute through `POST /api/subjects`, which
  stores strings. `bsk.recurrence_occurrences` reads `recur->>'freq'` and finds nothing.
- **Likely effect today:** a Habit created on the web has no scheduled days. Its unlogged
  days are never marked missed, so its adherence and "Ready to establish" may read higher
  than they should. Habits set up with `bsk recur` are fine.
- **Staging, checked 2026-10-24:** one subject had a pattern, the Habit "Clean as I go",
  stored as text. Migration 0041 turns it into the object, so from then on its scheduled
  days without a check-off read as missed. No habit-day award changes: those pay only for
  days followed.
- Recurring Tasks need this path fixed anyway, so it is phase RT1 (§10).

### 8.2 A do-date that has passed

`taskGroup` (`web/src/lib/task-groups.ts`) puts a Task in Today only when its due date or
do-date *is* today:

- A do-date that has passed, with a future due date, falls to **Upcoming**.
- A do-date that has passed, with no due date, falls to **Unscheduled**, so a one-off Task
  planned for yesterday drops out of sight.

TASKS-1 didn't say where either belonged. The Focus page's due and overdue list has the same
gap (`lib/derive.ts`). It matters here because a once-each-week repetition has a do-date of
Monday and a due date of Sunday. Settled in §9.

## 9. Decided after the rules

**Where does a "sometime this week" chore show from Tuesday to Saturday?** Chris agreed on
2026-10-24 to change the rule for every Task:

- A Task whose do-date has arrived stays in **Today** until it's done or its due date
  passes, then moves to Overdue.
- So the bathroom shows in Today all week with "due Sunday", and §8.2's one-off Task no
  longer drops into Unscheduled.
- TASKS-1 is amended (built again in RT1b). The Focus page's Due & overdue list
  follows the same rule. The frozen Pilot keeps the old one.

Calls made without asking, since one option was clearly better:

- **Missed is offered on every Task,** not only repetitions. It means "didn't happen", which
  Cancelled ("no longer needed") doesn't.
- **Every calendar date gets its own repetition,** even ones that pass while the app is
  closed. Rent needs this. The cost is the after-a-trip close-out in §2.
- **The next repetition is made as soon as the current one closes,** so it shows under
  Upcoming straight away.
- **Reopening a closed repetition doesn't remove the next one,** since nothing is deleted
  (D9). Completing it again doesn't make a second next one.

## 10. Build order

Chris approved all six phases on 2026-10-24.

1. **RT0:** TASKS-3 active with its ontology fit, D16 and the backlog line. **Done.**
2. **RT1:** repeat patterns round-trip from the web to the kernel (§8.1). Fixes Habits and
   Appointments on its own. **Done:** every door that writes a pattern checks it and
   stores the object (`Recurrence.Canonicalize`: `bsk new`/`set`/`recur`, `POST /api/subjects`,
   `/api/set` and the new `POST /api/recur`); migration 0041 repairs the patterns already
   stored as text (`bsk.recurrence_from_text`). The web's `RecurrenceSpec` is the kernel's
   shape, its editor adds every N weeks or months and monthly on a day or the last day, and
   Habits, Appointments and Commitments save the pattern through `recur` (it's no longer a
   string attribute), with "Doesn't repeat" on the last two.
   - **RT1b:** TASKS-1's do-date rule (§9) in `taskGroup` and the Focus list, with unit
     tests. Independent of everything else here; it can ship first. **Done.**
3. **RT2:** kernel: Task series and repetitions, Missed, the two new repeat shapes,
   `TaskSeriesService` with settle, the price from the series, migration 0042,
   `bsk tasks settle`. **Done:** `{"freq":"each","period":"week"}` and every N with
   `"from":"completion"` (`bsk recur --each week`, `--from completion`, `--start`); Task's
   vocabulary gains Missed; `TaskSeriesService` makes repetitions on the pattern, on each
   close (`StatusService`, so the timer's Complete too) and on settle (`bsk tasks settle`);
   a repetition copies description, area, priority, estimated duration, tags and `serves`
   links, and pays the series' price unless it has its own (`RewardService`); renames, edits
   and tags follow to the open repetitions; migration 0042 (the expander's two shapes,
   `bsk.is_task_series`, one repetition per date, `v_task_repetition`, `v_task_series`);
   neglect skips series. Tests: `TaskSeriesTests`. Calls made while building it:
   - A series' own status is **Not started** (repeating) or **Cancelled** (stopped); Completed,
     In progress and Missed are for its repetitions. Back to Not started, or restored from
     the archive, it repeats **from today**, so the weeks it was stopped aren't made.
   - A repetition can't take a pattern of its own, and a series that has made repetitions
     stops by being Cancelled, not by clearing its pattern (its history stays tied to it).
   - A Task can't repeat on a cue ("after dinner"): it has no dates, so nothing would be made.
   - Changing the pattern moves the open repetition whose date hasn't passed to the new
     pattern; one already late keeps its date.
   - An edit to the series reaches an open repetition only where it still had the series'
     value, so a field changed on that repetition keeps its own. A done one keeps the name it
     was done under.
   - A hidden (archived) repetition counts as closed, so hiding one doesn't stall the series.
   - A pattern stored without a start (made with its attributes) gets one the first time it's
     settled: its due date, else that day.
4. **RT3:** API: the settle endpoint, series fields on list items, the repeat field on
   create and edit. **Done:** `POST /api/tasks/settle`; `GET /api/tasks/series` (each
   repeating Task with its counts and price) and `GET /api/tasks/series/{id}` (its
   repetitions, latest first); `POST /api/recur` takes `start`, `period` and `from` and
   answers the repetitions it made; a Task made through `POST /api/subjects` with a pattern
   starts repeating (a cue is refused). List items carry `isSeries`, `seriesId`, `occurrence`
   and `repeat` (the series' pattern), and a repetition's price is its series' unless it has
   its own. `GET /api/subjects` leaves series out unless `includeSeries=true`, so every page
   that lists Tasks shows the repetitions only. The detail adds `repeating` (the series and its
   counts) and `bountyFromSeries`. Tests: `TaskEndpointTests`.
5. **RT4:** web: the Repeat field, Tasks and Focus, settle on open, the repetition detail,
   the mock store.
6. **RT5:** the history and review read-outs and the graph node.
