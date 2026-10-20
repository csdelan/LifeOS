# Rewards Plan — currencies, redeeming, and reward history

The design and build order for the rewards requirements (REWARD-1..5 in
[ui-requirements.md](ui-requirements.md)) and the decision behind them (**D11**, in the same
doc). The requirements say *what*; this doc says *how* and *in what order*, written so an
agent with no session context can pick up any phase.

**Scope agreed with Chris on 2026-10-20.** The rules in §1 are his answers to the design
questions. R0 (the requirements and this plan), R1 (the ledger), R2 (earning on work), R3
(habit earning), R4 (the reward catalog), R5 (the wallet and Redeem page), R6 (earning in
the UI) and R7 (history and report) are done.

## 0. Read these first

- [ontology.md](../ontology.md): events, provenance (a `derived` event must cite its sources
  in `derived_from`), status-only-by-event, append-only.
- [ui-requirements.md](ui-requirements.md): REWARD-1..6, D7 (status vocabularies), D11.
- [kernel-build-plan.md](kernel-build-plan.md): how a phase lands (migration + `bsk` verb +
  view + tests via `./test.ps1`, strict build).
- Prior art to copy:
  - `AdherenceService` + migration 0018: activity events folded into occurrences and streaks.
  - `TimerService` + migration 0024: an advisory lock with an expected-state check, and a fold
    over append-only actions.

## 1. The rules (agreed 2026-10-20)

| Question | Decision |
|---|---|
| How are amounts set? | **Per item, by Chris.** Every Task / Project / Goal / Habit carries its own bounty in all six currencies. Forms pre-fill the type default. An unpriced item pays the *current* default; an explicit empty bounty pays nothing. |
| Karma and Resolve? | Two more amounts in the item's bounty (a good deed, or a task he's resisting, carries them). No automatic or standalone awards. |
| Coins? | **Real money at a rate** (default $0.50 per coin). Redeeming coins into a savings goal means actually moving those dollars; the log keeps the coins, the dollars and the rate. |
| When is a habit established? | Each habit has a target (default: 80% adherence over 60 days). When it's met LifeOS **suggests**, and Chris **confirms**. |
| After a habit is established? | It **graduates** and leaves daily tracking. |
| Streak milestones? | **Yes**: bonuses at 7 / 30 / 100 in a row, editable per habit. |
| Overdraft? | **No.** Redeem is blocked until the balance covers it. |
| Starting balance? | **Zero** on launch day; nothing completed earlier pays. |
| Penalties? | **None for now.** Later, a penalty is a ledger entry with negative amounts, with no schema change. |
| Undo? | Reopen → the reward is reversed; complete again → paid once. Habit-day corrections work the same way. |
| Retuning? | Each earn records its amounts at that moment; changing a bounty or a default never rewrites history. |
| Feedback? | A small chime and a toast on every earn. Confetti and a fanfare on redemptions, Gem awards, streak milestones and establishing a habit. One mute switch. |

## 2. Currencies

| Key | Label | Spendable | Earned for | Spent on |
|---|---|---|---|---|
| `coins` | Coins | yes | completing work, habits | real money moved into savings goals |
| `freedom` | Freedom | yes | completing work, habits | entertainment minutes (1 Freedom = 1 minute) |
| `gems` | Gems | yes | projects, goals, establishing a habit | significant events: vacations, road trips |
| `karma` | Karma | yes | good deeds (in the item's bounty) | catalog rewards |
| `xp` | XP | no (tracked) | everything | nothing yet (levels later) |
| `resolve` | Resolve | no (tracked) | overcoming fear, procrastination, resistance | nothing |

In Domain this is a `Currencies` class: keys, labels, the spendable set and `All`. It is
validated app-side as D7 does for statuses, with no DB enum, so adding a currency is one
line. Bounty amounts are non-negative integers; ledger amounts are signed.

## 3. Design

### 3.1 The ledger event

A new event kind, `ledger`. The migration widens the `bsk.event.kind` CHECK, as 0012 and
0015 did. Payload:

```json
{
  "entry": "open | earn | reversal | redeem",
  "amounts": { "xp": 10, "coins": 2 },
  "award_key": "completed:<subject_id>",
  "trigger": "completed",
  "subject_id": "<the item that earned, or the Reward redeemed>",
  "dollars": 25.00,
  "coin_rate": 0.50,
  "note": "Movie night with the kids"
}
```

| Entry | Provenance | Amounts | Edge | Meaning |
|---|---|---|---|---|
| `open` | declared | none | none | The start line. Triggers before it never pay. Written once, timestamped at or before the first trigger it lets through. |
| `earn` | derived; `derived_from` = the triggering event(s) | positive | `concerns` the item | An award the record says is due. |
| `reversal` | derived; `derived_from` = the event that undid it | negative, exactly the key's net | `concerns` the item | An award the record no longer supports. |
| `redeem` | declared | negative | `concerns` the Reward, if any | Chris spent currency, with his note. |

A future `penalty` entry is declared or derived with negative amounts and needs no migration
(§6).

### 3.2 Award keys

| Trigger | Key | Due when |
|---|---|---|
| Task / Project / Goal completed | `completed:<subject>` | its current status is Completed |
| Habit day followed | `day:<habit>:<date>:followed` | the latest adherence for that date is followed |
| Habit day partial | `day:<habit>:<date>:partial` | the latest adherence is partial; pays half of the `followed` bounty, rounded down |
| Streak milestone, n ∈ {7, 30, 100} | `streak_<n>:<habit>:<run start date>` | a run of consecutive followed occurrences starting on that date reaches n |
| Habit established | `established:<habit>` | its current status is Established |

Every key also requires its triggering fact to fall on or after the `open` entry. A key's
fact is the moment it became true in the record (`triggered_at` in `v_reward_trigger`): the
status change for `completed` and `established`, the recording for a day, and for a
milestone the recording of the last of its n days. So a day pays when it is recorded after
the opening, whatever its date (a backfill pays, as REWARD-2 asks), and dates never meet
timestamps across time zones. Days and runs only count up to graduation (§3.5).

### 3.3 Reconcile, not react

`RewardService.ReconcileAsync(subject, causeEventId?)`:

1. Read the subject's due keys from `v_reward_due`. SQL decides *which* awards are due, as a
   pure function of the record.
2. Read the net paid per key from `v_ledger`.
3. **Due, net zero** → append an `earn`, priced now from `attributes.bounty[trigger]`, or
   `BountyDefaults` when unset. Pricing happens in C#, so SQL never duplicates the defaults.
   The earn is dated at the trigger, so a sweep that heals it later still files it on the
   right day. A trigger priced to pay nothing (`{}`) writes nothing; if it is given a price
   later, the next reconcile of that item pays it.
4. **Not due, net non-zero** → append a `reversal` of exactly the net, citing the cause (or,
   in a sweep, the item's newest event).
5. **Due and already paid** → nothing. Reconcile is presence-based, so a bounty edited after
   payment never re-prices a past earn.
6. Append everything in one transaction under the wallet lock. Each earn and reversal names
   its award key and whether it expects that key unpaid or paid; if another reconcile changed
   it first, nothing is written and reconcile re-reads and retries (up to three attempts).
   Two windows completing the same item can't both pay it.
7. Return the entries written; the API turns them into REWARD-4's toast.

**Opening the wallet.** Before any `open` entry exists, nothing is due. A cause (a status
change) that makes an award pay opens the wallet itself, dated at that trigger
(`v_reward_trigger` is the same rows without the start-line filter), then pays it. A change
that pays nothing, a trigger priced at `{}`, and a sweep never open it, so history from before
launch stays unpaid. `bsk wallet open` still opens it by hand.

Hooks:

- `StatusService.ChangeStatusAsync`. This covers the web's Change status, `bsk status` and
  `TimerService.CompleteAsync`.
- `AdherenceService.RecordAsync`.

Each reconciles the affected subject after its write. Reconcile is idempotent, so a crash
between the trigger and the reconcile is healed by the next reconcile of that subject. It can
also be healed by `bsk rewards reconcile [--subject <ref>]`, which sweeps every subject with an
award due or paid.

Ledger entries `concern` the item they paid for, but they are bookkeeping, not tending: the
neglect diagnostic ignores them, so a reversal never resets an item's neglect clock.

Read current status from the live fold `bsk_derived.subject_current_source`, as the API's
`SubjectReader` does. Never use the materialized `subject_current`, which lags until
`bsk rebuild`.

### 3.4 Bounties

`attributes.bounty` maps each trigger to its amounts:

```json
{ "completed": { "xp": 10, "coins": 2, "freedom": 10, "karma": 3 } }
```

```json
{ "followed": {}, "streak_7": {}, "streak_30": {}, "streak_100": {}, "established": {} }
```

- A missing trigger → the type default. An explicit `{}` → pays nothing.
- It is named `bounty` because a Habit's `attributes.reward` is already the cue / routine /
  reward text (`v_habit.reward`).
- It is written by `bsk bounty <subject> [--on <trigger>] xp=10 karma=3` and by
  `POST /api/bounty`. The CLI sets currencies on what the trigger pays now (its default when
  never priced): `karma=3` adds Karma, `xp=0` drops XP. `--nothing` stores `{}`; `--clear`
  puts the trigger (or, without `--on`, every trigger) back on the default. With no amounts it
  shows each trigger's price and whether it is the default. `--on` may be left out when the
  type pays on one trigger.
- Reading is lenient: a bounty in another shape, an unknown currency or an amount that isn't
  a whole number above zero is skipped (the trigger then pays the default or what remains),
  so a hand-edited bounty never fails a status change. `BountyService` is the strict writer.
- Forms show the type default and save only what Chris changes, so later retuning of a default
  still reaches items he never priced.

Starter defaults (`BountyDefaults`), placeholders to tune by feel:

| Trigger | XP | Coins | Freedom | Gems |
|---|---|---|---|---|
| Task completed | 10 | 2 | 10 | – |
| Project completed | 50 | 10 | 30 | 1 |
| Goal completed | 150 | 25 | 60 | 3 |
| Habit day followed (partial = half) | 5 | 1 | 5 | – |
| Habit 7-day streak | 25 | 5 | 15 | – |
| Habit 30-day streak | 75 | 15 | 30 | – |
| Habit 100-day streak | 200 | 40 | 90 | 1 |
| Habit established | 100 | 25 | 60 | 1 |

Karma and Resolve default to 0; Chris adds them per item.

### 3.5 Habit lifecycle

- **Status vocabulary (D7):** Habit = Building, Established, Abandoned. Building is the
  default (first in the list), and a habit with no status reads as Building.
  `DismissStatusFor(Habit)` = Abandoned.
- **Terminal set:** `established` joins `bsk.is_terminal_status` and `StatusVocabulary`'s
  terminal set (`abandoned` is already there). Update the Pilot's `PilotVocab` mirror too, or
  the drift-guard test fails.
- **Graduation:**
  - `v_habit_occurrence` (0018) stops expanding at the date of the terminal status change;
    recorded history stays. (Built, 0027: scheduled dates stop the day *before* graduation, so
    the graduation day is never auto-missed; a day recorded on it still counts.)
  - Moving the habit back to Building resumes tracking and reverses the establishment award.
  - Days recorded after graduation show in the history but don't pay. Abandoning pays nothing
    and keeps what the days earned.
  - Occurrences are folded from `bsk_derived.habit_occurrence_source`, which keeps archived
    habits, so archiving never takes a reward back; `v_habit_occurrence` hides them as before.
- **Target:** `attributes.establish_target` = `{ "min_adherence": 0.8, "window_days": 60 }`,
  the default when absent.
  - `v_habit_establishment` gives, per Building habit:
    - the concluded occurrences in the window
    - adherence (partial = 0.5)
    - `target_met`: adherence ≥ the minimum, and the habit is at least `window_days` old
  - It is read-only. It drives a "Ready to establish" badge; confirming is an ordinary status
    change.
  - Built (0027): the window is the last `window_days` days up to today; today counts once
    recorded. A `min_adherence` written as a percentage (80) reads as 0.8. A target that isn't a
    number falls back to the default.
  - Written by `HabitTargetService` (`bsk habit target <habit> [--adherence 80%|0.8]
    [--days 60] | --clear`, `POST /api/habits/target`). Each part is optional, so setting one
    keeps the other; a part never set stays absent and reads as the default. The writer is
    strict where the view is lenient: Habits only, numbers only, an adherence above 0 and up to
    1 (a number above 1 and up to 100 is a percentage, stored as the fraction), and a window of
    1 to 3650 whole days (a larger one would push the window start outside Postgres's dates).

### 3.6 Reward catalog and redemption

- **`Reward` subject type** (the 15th): status-less, archivable, renamable.
  - `attributes.kind` = `instant`, with `price` (currency → amount)
  - or `savings`, with an optional `target_usd`
- **`v_reward`:** the catalog rows. For savings goals it adds the coins and dollars deposited
  (the sum of redeem entries concerning the goal) and the percentage of target.
- **`RewardService.RedeemAsync(reward?, amounts, note)`:**
  - It refuses tracked currencies and non-positive amounts.
  - It requires a note when no Reward is given.
  - Under one wallet-wide advisory lock, it reads `v_wallet` and refuses if any balance would
    go negative ("You have 40 Freedom; this needs 120.").
  - For a savings deposit, it stamps `coin_rate` and `dollars`. The rate comes from
    configuration: `Rewards:CoinValueUsd`, default 0.50.
- `bsk redeem [<reward>] coins=100 --note "…"`.
- Built (0028):
  - An instant reward costs its catalog price; amounts given override it for that one
    redemption (45 minutes of gaming). A savings goal takes Coins only, and needs the amount.
    An instant reward with no price needs amounts. An archived Reward refuses redemptions.
  - The note is optional against a Reward and required without one.
  - Every redemption that spends Coins stamps `dollars` and `coin_rate`, not only savings
    deposits: Coins are real money wherever they go, so the log always says what money moved.
    `v_reward` sums those stamped dollars, so a rate change never moves a goal's progress.
  - The redeem entry names the Reward in its payload `subject_id` and has a declared
    `concerns` edge to it.
  - The rate: the API reads `Rewards:CoinValueUsd` from its configuration (environment
    `Rewards__CoinValueUsd`); `bsk` reads the same environment variable. Blank means $0.50; a
    value that isn't a dollar amount above zero fails startup rather than mis-pricing deposits.
    `Wallet.CoinValueUsd` carries it to readers; `bsk wallet` shows the Coins' dollar value.
  - Catalog verbs: `bsk rewards list [--archived]`, `bsk rewards add <title> [currency=n…]
    [--savings [--target <usd>]] [--description …]`, `bsk rewards price <reward> currency=n…`
    (replaces the price), `bsk rewards target <reward> <usd> | --clear`. Rename and archive
    are the ordinary verbs. `RewardCatalogService` is the strict writer; reading a hand-edited
    price or target is lenient (a text target like "$1,250.50" reads; a price keeps only
    spendable whole amounts above zero).

### 3.7 Reads

| View / endpoint | Shape |
|---|---|
| `v_ledger` | One row per ledger entry × currency: event id, occurred_at, entry, currency, amount, award_key, trigger, the item or Reward (id, urn, type, title), note, dollars, provenance. |
| `v_wallet` | Per currency: earned, reversed, spent, balance. |
| `v_reward_trigger` | Every award the record supports, whatever the start line: subject_id, subject_type, award_key, trigger, source_event_ids, triggered_at. |
| `v_reward_due` | `v_reward_trigger` rows on or after `bsk.ledger_opened_at()`. |
| `v_habit_establishment` | Per Building habit: min_adherence, window_days, window_start, followed / partial / missed / concluded, adherence, age_days, target_met. |
| `v_reward` | Per Reward: name, kind (instant unless savings), price, target_usd, redemptions, coins_redeemed, dollars_redeemed, last_redeemed_at, percent_of_target and remaining_usd (savings goals with a target), description, notes, archived. |
| API | `GET /api/wallet`, `GET /api/ledger?from&to`, `GET /api/ledger/daily?from&to&tz`, `GET /api/rewards`, `POST /api/redeem`, `POST /api/bounty`. Built in R5: the wallet, ledger (`?from&to&limit`), catalog and redeem reads and writes, plus `POST /api/rewards`, `/api/rewards/price` and `/api/rewards/target`; `/api/ledger/daily` was built in R7. Built in R6: `POST /api/bounty` and `GET /api/bounty/defaults`; the subject list and detail reads carry the bounty, and `GET /api/habits` carries each habit's status and establishment progress. After R6: `POST /api/habits/target` (`{habit, minAdherence?, windowDays?}` or `{habit, clear}`), answering the target as the view reads it and which parts are the habit's own. |

- **Time zones:** daily totals bucket by `occurred_at AT TIME ZONE @tz`, using the browser's
  IANA zone. The database runs in UTC, so an evening completion would otherwise land on
  tomorrow. Habit days already use the occurrence date the client sent.
- **Earn responses:** the endpoints that can earn (`POST /api/status`, `/api/adhere`,
  `/api/timer`) add an `awards` array to their responses: the entries the reconcile wrote.

## 4. Phases

Each phase is one commit: a migration (where applicable), the `bsk` verb, views, tests
(`./test.ps1`), and a clean strict build. Web phases go mock-first (`web/src/lib/mock`), then
live, with `npm run gen:api` after API changes. Each phase also updates the Build fields of
the REWARD requirements it advances.

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| R0 | Requirements + plan | REWARD-1..6, D11, backlog lines, this doc. **Done.** | — |
| R1 | Ledger + wallet | Migration: `ledger` kind, `v_ledger`, `v_wallet`. `Currencies`; `RewardService` with `open` and custom-only redeem under the wallet lock; `bsk wallet`, `bsk ledger`, `bsk redeem --note`. Tests: balances, tracked currencies can't be spent, overdraft refused, two concurrent redeems can't overspend. | Timer work committed (§5) |
| R2 | Bounties + earning on work | `attributes.bounty`, `BountyDefaults`, `bsk bounty`; `v_reward_due` (completed rows); reconcile plus the `StatusService` hook; `bsk rewards reconcile`. Tests: completing pays; reopening reverses; re-completing pays once; an edited bounty doesn't re-price; completions before `open` never pay; the timer's Complete pays. | R1 |
| R3 | Habit lifecycle + habit earning | Migration: Habit vocab and `established` terminal, occurrence expansion stops at graduation, `v_habit_establishment`, the habit rows of `v_reward_due`. The `AdherenceService` hook. Tests: day, partial and corrections; a correction that splits a run reverses its milestone; establishing pays and graduates; back to Building reverses. | R2 |
| R4 | Reward catalog | Migration: `Reward` type, `v_reward`. Redeem against a catalog item, and savings deposits with the rate stamped. Tests: savings progress; a rate change doesn't rewrite past deposits. | R1 |
| R5 | Wallet + Redeem page | API endpoints, and `awards` on the earning responses. A Rewards tab: wallet, catalog cards, a Redeem dialog with a comment, add / edit catalog items. Celebration (confetti + a Web Audio fanfare), a mute switch, reduced-motion support. Vitest plus one e2e golden path. | R1, R4 |
| R6 | Earning in the UI | A bounty editor in create / edit for Task / Project / Goal / Habit (defaults pre-filled, only changes saved). Bounty chips on lists and in detail. Toast and chime from `awards`. The "Ready to establish" badge and its confirm. Graduated habits move off the daily list into their own section. | R2, R3, R5 |
| R7 | History + report | Rewards History: a month calendar of daily earnings, week / month earned-vs-spent bars per currency, and a filterable ledger linking each entry to its item or Reward. | R5 |

- R4 touches only the catalog, so it can run alongside R2–R3.
- The WinForms Pilot gets no rewards UI, but it still earns: it writes through `bsk` into the
  same Application services.
- Merging to `main` promotes the new migrations to staging through CI as usual.

## 5. Sequencing constraint

As of 2026-10-20 the task-timer work (TASKS-2, migration 0024) is uncommitted in the working
tree. It touches files R1–R2 need: `Vocabulary.cs`, `Ports.cs`, `WriteEndpoints.cs`, the DI
wiring, and the web query and mock layer. Start R1 after it is committed. Rewards migrations
number from 0025.

## 6. Later (outside R1–R7)

- **Penalties:** a ledger entry with negative amounts (for example, a missed Commitment).
  Nothing to migrate; off for now by decision.
- **Tracked currencies and life balance** (REWARD-6, drafting): more unspendable currencies
  like XP, shown as horizontal bars or compared on a radar / spider chart. Currencies are data
  with a spendable flag, so the ledger doesn't change. One shape to weigh when it is specified
  is a tracked currency per Area; not decided.
- **Levels** from XP.
- **More earning sources:** Reviews, Inbox Zero, resolved Problems, fulfilled Commitments.
  Each is a new trigger in `v_reward_due` plus a default, not a rebuild.
- **Editing the defaults and the coin rate in the UI.** v1 keeps them in code and
  configuration.

## Status

- [x] R0 — Requirements (REWARD-1..6, D11) + this plan
- [x] R1 — Ledger + wallet (migration 0025). `ledger` event kind; `v_ledger`, `v_wallet`,
  `bsk.ledger_opened_at()`; `Currencies` / `LedgerEntries` in Domain; `RewardService` (wallet,
  ledger, open, custom redeem) over `NpgsqlLedgerRepository`, whose writes share one
  advisory lock and re-check every currency a redemption touches before committing;
  `bsk wallet`, `bsk wallet open`, `bsk ledger`, `bsk redeem`. Notes for R2: for now the
  wallet is opened explicitly by `bsk wallet open`; R2's reconcile should open it itself,
  dated at the first trigger it pays, when no opening exists. Only redemptions are guarded,
  so reversing something already spent can take a balance below zero, and the wallet shows
  that as it is.
- [x] R2 — Bounties + earning on work (migration 0026). `v_reward_trigger` / `v_reward_due`
  (Task / Project / Goal `completed`); `BountyTriggers`, `BountyDefaults`, `AwardKeys` in
  Domain; `Bounty` (lenient read) and `BountyService` (strict write); `RewardService`
  reconcile and sweep, with award-state checks under the wallet lock; `StatusService` reconciles
  after every status change, so `bsk status`, the web and the timer's Complete all earn, and
  `StatusResult` / `TimerResult` carry the awards; `bsk bounty`, `bsk rewards reconcile`;
  `bsk status` / `bsk timer` print what they earned. The neglect diagnostic now ignores ledger
  entries. Notes for R3: add the habit rows to `v_reward_trigger` (and the triggers to
  `BountyTriggers` / `BountyDefaults` — the drift test checks they match), and call
  `ReconcileAsync(habit, adherenceEventId)` from `AdherenceService`. The API's `awards` on
  responses is still R5.
- [x] R3 — Habit lifecycle + habit earning (migration 0027). Habit vocabulary Building →
  Established | Abandoned in `StatusVocabulary` and the Pilot mirror; `established` terminal;
  `bsk_derived.habit_occurrence_source` (archived included, graduation-capped) behind
  `v_habit_occurrence`; `v_habit_establishment`; the habit rows of `v_reward_trigger` (days,
  7 / 30 / 100 runs, established); habit triggers and defaults in `BountyTriggers` /
  `BountyDefaults`, partial priced as half of followed; `AdherenceService` reconciles after
  every recording and `bsk adhere` prints what it earned. Notes for R6: the web's
  `STATUS_BY_TYPE` / `TERMINAL_STATUSES` (and the copy in `docs/pilot/production-ui-types.ts`)
  still treat Habit as status-less; add the Habit vocabulary and `established` with the
  "Ready to establish" badge. The Pilot already offers Change status on habits. Graduated
  habits already drop off the web's Today list, because their occurrences stop.
- [x] R4 — Reward catalog (migration 0028). The `Reward` subject type (Domain, the API and
  `bsk new` type lists, and the web's `SubjectType` with an icon, colour and graph backdrop,
  so a Reward reaching the UI renders; it is not in global New); `v_reward`;
  `RewardService.RedeemAsync(reward?, amounts, note)` with the coin rate stamped;
  `RewardCatalogService` and the `bsk rewards list / add / price / target` verbs. Notes for R5:
  the catalog reads are `ILedgerRepository.GetCatalogAsync` / `GetCatalogRewardAsync`; the web's
  `ATTR_KEYS_BY_TYPE.Reward` leaves out `price`, an object the rewards endpoints write; a
  redemption returns the Reward's progress afterwards for the dialog.
- [x] R5 — Wallet + Redeem page. API: `RewardEndpoints` (`GET /api/wallet`, `GET /api/ledger`
  newest first with `limit` 1..500 (default 100) and a `from` < `to` check, `GET /api/rewards`
  with `includeArchived`, `POST /api/redeem`, `POST /api/rewards`, `/api/rewards/price`,
  `/api/rewards/target`) reading through `RewardReader` on the reader connection; rename,
  describe and archive a Reward with the ordinary endpoints. `/api/status` and `/api/adhere` now
  answer `{ awards }`, and `/api/timer` adds `awards`, each the entries the reconcile wrote.
  Web: a Rewards tab (`/rewards`, in the nav and the command palette) with the wallet (Coins
  shown in dollars), Treats and Savings goals cards (a treat the wallet can't cover is blocked
  with the kernel's reason; savings show progress), recent activity, and archived rewards with
  Restore. The Redeem dialog pre-fills the price (editable for this time), takes only Coins
  into a savings goal and shows the dollars that move, needs a note without a Reward, and
  checks the kernel's rules as you type. Add / edit catalog items (kind fixed once created;
  edits save only what changed). `lib/celebrate.ts`: DOM confetti (none under reduced motion)
  and a synthesized Web Audio fanfare behind a per-device Sound switch. The mock folds a seeded
  ledger the way the views do and refuses what the kernel refuses, in the same words. Notes for
  R6: the web's `setStatus` / `adhere` still return nothing; read `awards` from their responses
  (and `TimerActionResult.awards`) for the toast and chime, and give the mock store an earning
  reconcile so mock mode pays. Status, adherence and timer writes already refresh the wallet.
- [x] R6 — Earning in the UI. API: `POST /api/bounty` (`BountyService`: amounts, `nothing`
  or `clear`, the kernel's refusals as 400s) and `GET /api/bounty/defaults` (every earning
  type's triggers at their defaults, for create forms); `SubjectListItem.bounty` (what the
  headline trigger pays: completed, or a followed habit day), `SubjectDetail.bounty` (every
  trigger with its price, `isCustom` and `default`), and `HabitRow.status` / `bounty` /
  `establishment` (from `v_habit_establishment`, while Building). Prices are computed in C#
  from `attributes->>'bounty'` with `Bounty.Price`, so the web never duplicates the live
  defaults. Web: a Bounty section in the New dialog and the detail pane for Task / Project /
  Goal / Habit — a summary per trigger, and Change opens a box per currency pre-filled with
  what it pays now; `lib/bounty.ts` `bountyWrites` saves only what changed (an untouched or
  unchanged trigger writes nothing, one edited back to its default is cleared so retuning
  still reaches it, an emptied one pays nothing, otherwise the whole price is stored), and a
  bounty is written before any status change in the same save. Bounty chips (`BountyChip`,
  "🪙2 ⏳10 ⭐10", words for screen readers) on list rows (Tasks, Focus) while the item is open,
  on habit rows ("/day") and in the detail header. `setStatus` / `adhere` answer `Award[]` and
  the timer's result carries `awards`; `decorateWrites` hands them to
  `lib/earn-feedback.ts`: a toast of what was earned and a chime (`playChime`, behind the same
  Sound switch) on every earn, the full celebration for Gems, streak milestones and
  establishing, and a quiet "Gave back" toast for a reversal. Habit vocabulary in the web's
  `STATUS_BY_TYPE` / `TERMINAL_STATUSES` (and the docs copy); the Habit edit form has Status.
  Habits page: Building habits with the adherence buttons, a "Ready to establish" button when
  the target is met (an AlertDialog with the progress and what establishing pays; confirm =
  status Established), and Established and Abandoned sections without daily tracking.
  `todayHabitRows` drops terminal habits, so a graduated habit leaves Today in live mode too.
  The detail pane shows a habit's progress toward its target. The mock store earns: a status
  change settles the item's completed / established award and a habit day settles its
  followed / partial awards and the streak milestones of the runs around it, priced from the
  item's bounty with the kernel's refusals for `setBounty`; seeded history is never swept.
  Follow-up (REWARD-2 now `built`): the establish-target editor. `HabitTargetService` writes
  `attributes.establish_target` as numbers (§3.5); `bsk habit target` and
  `POST /api/habits/target` call it. Web: `setHabitTarget` (live, mock and `decorateWrites`,
  which refreshes the habits); while editing a Building habit, the detail pane's Establishing
  panel shows "Establish when followed __% over __ days" pre-filled from
  `HabitRow.establishment`, and `lib/establish.ts` `targetWrite` sends only the part that
  changed, the percentage as a fraction. The mock store's `mockSetHabitTarget` refuses what
  the service refuses, in its words.
- [x] R7 — History + report. No migration: `NpgsqlLedgerRepository.GetDailyTotalsAsync` totals
  `v_ledger` by `(occurred_at AT TIME ZONE @tz)::date` over the half-open local range
  [from, to), per currency: earned, reversed and spent as positive totals, and the signed net.
  `LedgerDays.Validate` refuses a range that doesn't end after it starts, one longer than 731
  days, and a blank or malformed zone; Postgres refuses an unknown zone (22023), answered as
  "Unknown time zone 'X'." `LedgerDays.Fold` makes one `LedgerDay` per date, zeros dropped,
  currencies in display order. `RewardService.GetDailyAsync`, `bsk ledger --daily [--from]
  [--to] [--tz]` (`--to` inclusive, the last 30 days by default, in this machine's zone: the
  solution runs with invariant globalization, so common Windows zone ids are mapped to IANA
  in the command and any other zone reads in UTC with a note) and
  `GET /api/ledger/daily?from&to&tz` (`LedgerDayRow[]`; a missing date or unknown zone is a
  400; tz defaults to UTC). Web: Rewards has Wallet & rewards and History tabs
  (`/rewards?view=history`, plus `selected` for the detail peek); "See all history" on Recent
  activity opens it. History reads one daily range covering the month and both charts
  (`lib/rewards-history.ts` `historyRange`), so switching weeks / months never waits.
  - A month calendar (Sunday first) for one currency at a time, shaded by what each day earned
    net of what it gave back (one hue, four steps), with spending below; future days are
    disabled and each day says its totals in words.
  - Earned-vs-spent bars for the last 8 weeks or 6 months (tracked currencies show earned
    only), with a tooltip per period, the last period labelled, and a Table view. New
    `--chart-earned` / `--chart-spent` tokens per theme, checked with the dataviz validator.
  - Picking a day, week or month narrows the report below: its totals (Spent with the real
    dollars that moved), and its ledger entries grouped by day, filterable by kind (earned /
    given back / spent) and currency, each linking to its item or Reward in the detail peek.
    A reversal is its own entry on the day it was made, so a past day never changes.
  The mock store's `mockLedgerDaily` files by the viewer's zone and refuses what the kernel
  refuses, in its words. The shared Tabs trigger now uses `text-muted-foreground`
  (`text-foreground/60` failed AA contrast in light mode).
