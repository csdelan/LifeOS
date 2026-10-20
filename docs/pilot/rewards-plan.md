# Rewards Plan — currencies, redeeming, and reward history

The design and build order for the rewards requirements (REWARD-1..5 in
[ui-requirements.md](ui-requirements.md)) and the decision behind them (**D11**, in the same
doc). The requirements say *what*; this doc says *how* and *in what order*, written so an
agent with no session context can pick up any phase.

**Scope agreed with Chris on 2026-10-20.** The rules in §1 are his answers to the design
questions. R0 (the requirements and this plan) and R1 (the ledger) are done; R2–R7 are open.

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

Every key also requires its triggering fact to fall on or after the `open` entry. For a
milestone, that fact is the day that reached n.

### 3.3 Reconcile, not react

`RewardService.ReconcileAsync(subjectId)`:

1. Take the subject's advisory lock.
2. Read the subject's due keys from `v_reward_due`. SQL decides *which* awards are due, as a
   pure function of the record.
3. Read the net paid per key from `v_ledger`.
4. **Due, net zero** → append an `earn`, priced now from `attributes.bounty[trigger]`, or
   `BountyDefaults` when unset. Pricing happens in C#, so SQL never duplicates the defaults.
5. **Not due, net non-zero** → append a `reversal` of exactly the net.
6. **Due and already paid** → nothing. Reconcile is presence-based, so a bounty edited after
   payment never re-prices a past earn.
7. Return the entries written; the API turns them into REWARD-4's toast.

Hooks:

- `StatusService.ChangeStatusAsync`. This covers the web's Change status, `bsk status` and
  `TimerService.CompleteAsync`.
- `AdherenceService.RecordAsync`.

Each reconciles the affected subject after its write. Reconcile is idempotent, so a crash
between the trigger and the reconcile is healed by the next reconcile of that subject. It can
also be healed by `bsk rewards reconcile`, which sweeps every subject with triggers since
`open`.

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
- It is written by `bsk bounty <subject> --on <trigger> xp=10 coins=2 [--clear]` and by
  `POST /api/bounty`.
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
    recorded history stays.
  - Moving the habit back to Building resumes tracking and reverses the establishment award.
- **Target:** `attributes.establish_target` = `{ "min_adherence": 0.8, "window_days": 60 }`,
  the default when absent.
  - `v_habit_establishment` gives, per Building habit:
    - the concluded occurrences in the window
    - adherence (partial = 0.5)
    - `target_met`: adherence ≥ the minimum, and the habit is at least `window_days` old
  - It is read-only. It drives a "Ready to establish" badge; confirming is an ordinary status
    change.

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

### 3.7 Reads

| View / endpoint | Shape |
|---|---|
| `v_ledger` | One row per ledger entry × currency: event id, occurred_at, entry, currency, amount, award_key, trigger, the item or Reward (id, urn, type, title), note, dollars, provenance. |
| `v_wallet` | Per currency: earned, reversed, spent, balance. |
| `v_reward_due` | (subject_id, award_key, trigger, triggering event ids). |
| `v_habit_establishment` | Per habit: window, adherence, target_met. |
| `v_reward` | The catalog plus savings progress. |
| API | `GET /api/wallet`, `GET /api/ledger?from&to`, `GET /api/ledger/daily?from&to&tz`, `GET /api/rewards`, `POST /api/redeem`, `POST /api/bounty`. |

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
- [ ] R2 — Bounties + earning on work
- [ ] R3 — Habit lifecycle + habit earning
- [ ] R4 — Reward catalog
- [ ] R5 — Wallet + Redeem page
- [ ] R6 — Earning in the UI
- [ ] R7 — History + report
