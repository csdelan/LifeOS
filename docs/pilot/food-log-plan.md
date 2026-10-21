# Food Log Plan — meals, fasts and diet XP

The design and build order for the Food Log (FOOD-1 to FOOD-5 in
[ui-requirements.md](ui-requirements.md)) and the decision behind it (**D13**, in the same
doc). It builds on the rewards ledger in [rewards-plan.md](rewards-plan.md) and on Health XP
in [xp-domains-plan.md](xp-domains-plan.md). The requirements say *what*; this doc says *how*
and *in what order*, written so an agent with no session context can pick up any phase.

**Scope agreed with Chris on 2026-10-20.** He gave a first pass of the feature, then answered
seven questions by taking every recommendation (§5). The Food Log tracks what Chris eats,
how healthy it was, roughly how many calories, and his fasts. A day's diet pays up to **40
base Health XP**. Photos come last.

**Keep it small.** The thing under test is whether logging food and fasts, and seeing a daily
diet score, changes what Chris eats. Everything in §6 has to earn its place through use.

## 0. Read these first

- [xp-domains-plan.md](xp-domains-plan.md): the credited day (§3.3), daily credit and
  `v_award_credit` (§3.4), the multiplier frozen on each earn (§3.6), rank-ups (§3.7) and the
  `progress` answer (§3.8). Diet XP is ordinary Health XP on that ledger.
- [rewards-plan.md](rewards-plan.md): award keys, reconcile, the wallet opening.
- [binary-artifacts.md](binary-artifacts.md): how bytes are stored, for photos (F6).
- [ontology.md](../ontology.md): append-only events, `occurred_at` vs `recorded_at`,
  provenance (a `derived` event cites its sources).
- [kernel-build-plan.md](kernel-build-plan.md): how a phase lands (migration + `bsk` verb +
  view + tests via `./test.ps1`, strict build).
- Prior art to copy:
  - `TimerService` + migration 0024: `activity` events with a payload key of their own,
    folded by a read-only view.
  - `AdherenceService` + migration 0018: backfill, and corrections where the newest record
    wins.
  - `RewardService.PayRankUpsAsync`: award keys that belong to no item (subject-less earns).
  - `bsk.home_zone()` / `bsk.home_today()` (0033): home-zone dates in SQL. .NET can't convert
    IANA zones here (invariant globalization), so every wall-clock conversion goes through
    Postgres, as `IHomeCalendar` does.
  - `lib/domain-fold.ts`: a TypeScript port of a Domain rule, tested on the same table as the
    C# tests, so the mock store and the page agree with the API.
  - The Character page: settle once when the page opens (`POST /api/domains/settle`).

## 1. The rules (agreed 2026-10-20)

| Question | Decision |
|---|---|
| What's logged? | **Food entries** (meals, snacks, and drinks with calories) and **fasts**. Photos last (F6). |
| A food entry | **Required:** the time eaten (defaults to now), a short name, a health score from 1 to 5, and calories (a rough estimate). **Optional:** protein, carbs and fat in grams, which don't count toward XP for now. A meal type (breakfast, lunch, dinner, snack, drink) is guessed from the time and can be changed. |
| Health score | 1 to 5, whole numbers. The picker shows examples: **1** junk (ice cream, fries, candy), **2** indulgent (pizza, burger, pastry), **3** average (a sandwich, pasta), **4** good (grilled chicken and vegetables, oatmeal and fruit), **5** very healthy (a salmon salad). |
| Calories | Quick sizes to start from: snack 200, light 400, meal 700, big 1,000. Editable to any whole number from 0 to 5,000. |
| "Log again" | Recent entries are one tap away: the name, score, calories and nutrients are filled in, and the time is now. |
| Diet XP per day | Up to **40 base Health XP**: **food quality 28** (70%), **calories 4** (10%), **fasting 8** (20%). §2 has the formula. |
| Food quality | The calorie-weighted average of the day's scores, on a straight line: an average of 1 pays 0, 2 pays 7, 3 pays 14, 4 pays 21, 5 pays 28. |
| Calories | 0 XP under 500 calories. 4 XP from 500 to 2,000. Then it fades evenly to 0 at 2,500. It doesn't depend on quality: 1,500 calories of ice cream still earns the 4. |
| Fasting | Nothing up to 12 hours. Then ⅔ XP per hour, counted by the second, up to 8 XP at 24 hours. Longer fasts pay the 8. |
| Which day does a fast count on? | **The day it ends.** Only the longest fast ending on a day counts, so a day's fasting never pays more than 8. |
| When does fasting pay? | **The moment the fast ends**, or when a backfilled fast is saved. |
| When does food pay? | **When the day is closed.** Chris taps **Close day** after his last meal. If he doesn't, the day closes itself once it's over, and it pays the next time LifeOS settles (§3.4), credited to its own day. |
| Corrections | Food entries and fasts can be edited or deleted at any time. Nothing is erased: each change records a newer version. A change to a day that has already paid **re-scores it**: the old award is given back and the new one paid. |
| Days | Midnight to midnight in the home zone (America/Los_Angeles), like all Health XP. An entry counts on the day it was eaten. |
| Rounding | Each award (the fast, the food) is rounded to whole XP, halves up, before the multiplier. Rounding the two parts separately can differ from rounding the day's total by 1 XP at most. The day never pays more than 40 base. |
| Multiplier | Applies on top, as for all Health XP: each award is paid at the multiplier of the day it's credited to, so 40 base can pay up to 52. |
| The 70-XP successful day | Diet XP counts toward it. A perfect diet day is 40 of the 70, so the other 30 must come from exercise, sleep or habits. That's intended. |
| A day with nothing logged | Pays nothing and isn't marked as a failure. |
| Fasting help | **Begin fast** offers the time of the last food entry. Logging food with calories while a fast is running offers to end the fast at that time. A running fast shows a live counter: "Fasting 14 h 20 m · earning since 12 h". A 0-calorie entry (black coffee, water) never ends a fast. |
| Views | **Day** (the default): a gallery of the day's entries and a summary. **Week**, Monday to Sunday. **Month**, a calendar. Each shows diet XP for every day and in total. |
| Where | The web app, designed for the phone first (Chris logs from his phone) and fine on a desktop. The WinForms Pilot gets nothing. |
| Currencies | Diet awards pay Health XP only. |

**Examples**, all base Health XP before the multiplier. The parts are shown unrounded; the
diet XP is the food award (quality + calories, rounded) plus the fasting award (rounded):

| Day | Quality | Calories | Fasting | Diet XP |
|---|---|---|---|---|
| 3,000 calories of ice cream (all 1s), no fast | 0 | 0 | 0 | **0** |
| 1,200 calories of salmon salad (5), a 23-hour fast | 28 | 4 | 7.3 | **39** of 40 (32 + 7) |
| Good day: 1,800 calories (700 at 5, 200 at 4, 900 at 4), a 16-hour fast | 23.7 | 4 | 2.7 | **31** (28 + 3) |
| Ordinary day: 2,200 calories (oatmeal 400 at 4, sandwich 700 at 3, pizza 900 at 2, cookie 200 at 1), a 14-hour fast | 11.1 | 2.4 | 1.3 | **15** (14 + 1) |

The good day shows the rounding: one rounding of its total (30.4) would give 30.

## 2. The formula

### 2.1 Definitions

For one home-zone day, over its live food entries (the newest version of each, not
deleted):

- `C` = total calories.
- `S` = the calorie-weighted average score, `Σ(score × calories) / C`. Entries with 0
  calories weigh nothing. When `C` is 0, there's no average and quality is 0.
- **Quality** = `28 × (S − 1) / 4`.
- **Calories part**:
  - `C < 500`: 0
  - `500 ≤ C ≤ 2000`: 4
  - `2000 < C < 2500`: `4 × (2500 − C) / 500`
  - `C ≥ 2500`: 0
- **Food award** = `round(Quality + Calories part)`, halves up; 32 at most.

For the longest fast ending on the day, `h` = its length in hours (to the second):

- **Fasting award** = `round(8 × clamp(h − 12, 0, 12) / 12)`, halves up; 8 at most.

### 2.2 Rules in code

`DietRules` in Domain holds the numbers, like `LifeDomains` and `BountyDefaults`:

- `Currency`: `health_xp`.
- `Cap`: 40, split as `QualityMax` 28, `CaloriesMax` 4, `FastingMax` 8.
- `ScoreMin` 1, `ScoreMax` 5.
- `CalorieFloor` 500, `CalorieFullTo` 2000, `CalorieZeroAt` 2500.
- `FastFromHours` 12, `FastFullAtHours` 24.
- `Score(DietDay facts)` returns the three parts (unrounded), the two awards (rounded) and
  `S`, so readers can show "on track for 31 of 40".

A drift test checks that:

- the three maxima add up to the cap
- the calorie thresholds rise strictly (floor < full-to < zero-at)
- fasting starts paying before it's full (`FastFromHours < FastFullAtHours`)
- the example table above holds

Retuning means editing these numbers and deploying. Paid awards keep what they paid. A day is
re-scored only when its entries change, so a retune doesn't rewrite past days. That matches
how bounties work: changing a price never re-prices a past earn.

### 2.3 Calibration

The numbers are Chris's first pass. After 2–4 weeks of real logging, look at:

- the spread of daily diet XP: does an ordinary day sit around 15 and a great one near 35?
- whether 2,000 is the right full-credit line for his activity level
- the Health minimum: the 70-XP successful day (xp-domains plan §2.4) should be recalibrated
  with diet XP in the mix, since diet can supply up to 40 of it

## 3. Design

### 3.1 Storage: append-only events (D13)

Everything is an event. No new subject type and no new event kind. The kind CHECK already
allows both kinds used here.

**Food entries are `measurement` events** (things measured about me: calories, a self-rated
score, nutrients). This gives `measurement` its first write path. Payload:

```json
{ "meal": { "id": "<meal id>", "name": "Salmon salad", "score": 5, "calories": 650,
            "protein_g": 40, "carbs_g": 20, "fat_g": 30, "meal_type": "lunch" } }
```

- `occurred_at` is when it was eaten; `recorded_at` is when it was logged. A backfill is just
  an earlier `occurred_at`.
- `meal.id` is a uuid the service creates with the first version. Every later version
  carries the same id.
- **An edit** is a new event with the whole entry as it now stands.
- **A delete** is `{ "meal": { "id": "…", "voided": true } }`.
- **The newest version wins by `recorded_at`, then `id`, never by `occurred_at`.** An edit
  that moves the time earlier must still win, so this is unlike the habit folds, which order
  by `occurred_at`.
- Photos (F6) are an artifact whose bytes live in `bsk.artifact_blob`. The payload carries
  `meal.photo` (the artifact id) forward on every version, so editing the calories never
  drops the photo. F6 checks how artifacts link to events and decides whether the version
  that adds the photo also sets the event's `artifact_id`.

**Fasts are `activity` events** (something I do), like the task timer. Payload:

```json
{ "fast": { "id": "<fast id>", "started_at": "2026-10-19T20:00:00-07:00", "ended_at": null } }
```

- **Begin** writes a version with `ended_at` null, dated at the start.
- **End** writes a version with both times, dated at the end.
- **A backfill or an edit** writes a version with both times.
- **A delete** is `{ "fast": { "id": "…", "voided": true } }`.
- The newest version wins, by `recorded_at` then `id`, as for food entries.
- Rules (refused with a reason, nothing written):
  - only one fast runs at a time
  - a fast ends after it starts
  - neither time is in the future (a minute of clock skew is allowed)
  - two recorded fasts can't overlap ("That overlaps your fast from Mon 8:00 pm to Tue
    2:00 pm.")
- Food logged inside a recorded fast isn't refused (scout's honor, GEN-5). The day view flags
  it and offers to end the fast at that entry's time.

**Closing a day is an `activity` event**: `{ "food_day": { "on": "2026-10-20", "closed": true } }`,
dated now.

- There's no reopen. Food logged after a close simply re-scores the closed day.
- Closing a day that's already closed (by hand, or by time) writes nothing.
- A future day can't be closed.

None of these events carries a `subject_id` or an edge, so the habit views (which match
`payload->>'subject_id'`), the timer (`jsonb_exists(payload, 'timer')`) and the
neglect diagnostic never see them. **F1 checks every reader that scans events by kind** (the
inbox, the timeline / history reads, `bsk log`, diagnostics) so food events don't surface
where they don't belong.

Provenance is `declared` for all of them.

### 3.2 Views (migration 0034)

All read-only, granted to `bsk_reader`. Day dates use
`(ts AT TIME ZONE bsk.home_zone())::date`.

| View | One row per | Columns |
|---|---|---|
| `bsk.v_meal` | live food entry (newest version, not voided) | `meal_id`, `eaten_at`, `eaten_on`, `name`, `score`, `calories`, `protein_g`, `carbs_g`, `fat_g`, `meal_type`, `photo_artifact_id`, `first_logged_at`, `logged_at`, `versions`, `event_id` (newest version) |
| `bsk.v_fast` | live fast | `fast_id`, `started_at`, `ended_at` (null while running), `state` (running / ended), `seconds` (ended only), `started_on`, `ended_on`, `meals_during` (live entries with calories eaten inside it), `event_id` |
| `bsk.v_food_day` | home-zone day with a live entry, an ended fast or a close | `day`, `meals`, `calories`, `score_calories` (Σ score × calories), `longest_fast_seconds` (fasts ending that day), `closed_by_hand`, `closed_at`, `closed` (by hand, or `day < bsk.home_today()`), `last_recorded_at` (the newest version behind the day), `food_event_ids` and `fast_event_id` (the versions the day reads; the awards cite them) |

Scoring stays in C# (`DietRules.Score`), as all pricing does, so the SQL never duplicates
the numbers.

### 3.3 Diet awards on the ledger

Two award keys per day. Neither belongs to an item (no `subject_id`, no edge), like rank-ups.

| Key | Trigger | Due when | Base |
|---|---|---|---|
| `fast:<yyyy-MM-dd>` | `fast` | the day has an ended fast | the fasting award |
| `diet:<yyyy-MM-dd>` | `diet` | the day is closed and has live food entries | the food award |

- Both pay `health_xp` only, credited to the day (`credited_on` = the key's date).
- **The multiplier:** each earn goes through `DomainPricing.Price` at its credited day's
  multiplier, stamping `base` and `multipliers` as every Health XP earn does.
- A due award whose base is 0 writes nothing (like a trigger priced `{}`).
- Earns are `derived`, citing the event versions the score read (`food_event_ids` and the
  close, or `fast_event_id`).
- **Wallet opening:** the usual rule. A food write that makes an award pay opens the wallet
  if it has never opened. A fact recorded before the opening never pays.

**Re-pricing is new.** Today's reconcile is presence-based: an award key that's paid is
never priced again. A day's diet score changes when its entries change, so for `fast:` and
`diet:` keys, reconcile compares the **base** due now with the **base of the key's latest
earn** (`v_award_credit.base`):

| Due now | Paid | Writes |
|---|---|---|
| base > 0 | nothing | an earn |
| base > 0 | the same base | nothing |
| base > 0 | a different base | **a re-price**: a reversal of the key's net, then an earn at the new base |
| nothing, or 0 | something | a reversal |

- The re-price's earn is priced at the credited day's multiplier as it stands now. That's a
  new issuance, like completing a task again, and the issued earn is still never changed.
- **The ledger guard needs one change.** `NpgsqlLedgerRepository.AppendAsync` checks every
  write against the state before the batch, so a reversal and an earn on the same key in one
  batch would fail. Evaluate the guards in order, applying each earlier write in the batch to
  the key's state. Also let the reversal name the latest earn it read (`ExpectLatestEarn`),
  so two racing re-pricers can't both pass. A lost race re-reads and retries, as reconcile
  already does.
- **Dating the earn.** `v_award_credit` takes a key's latest earn by `occurred_at`, so a
  re-price's earn must be dated after the earn it replaces. Date each earn at its trigger:
  the close, the fast's end, or the change that re-scored it. For a day that closed itself,
  use `last_recorded_at`, but never earlier than the key's previous earn.
- **Toasts:** "🍽️ Food day closed: +39 Health XP". "⏱️ 23 h fast: +7 Health XP". A
  re-price shows as one change ("Food day re-scored: 32 → 29 Health XP"), not as a give-back
  and an earn.

### 3.4 When diet awards reconcile

`FoodRewards` (Application, next to `RewardService`, sharing its ledger, pricing and rank-up
code) reconciles food days:

- **After every food write**, for the days it touched. That means both days when an edit
  moves an entry across midnight, or changes a fast's end day.
- **Settling:** every food day whose awards don't match the record. Fewer than 400 rows a
  year, so check them all. Settling runs:
  - on every food write (so yesterday pays the moment today's breakfast is logged)
  - from `POST /api/food/settle`, which the Food Log page calls when it opens
  - from `POST /api/domains/settle` (the Character page)
  - in the `bsk rewards reconcile` sweep (`ReconcileAllAsync`)
- After a reconcile that wrote Health XP, pay any rank-up it reached and answer `progress`,
  exactly as status, adhere and timer writes do. Food writes answer `{ …, awards, progress }`.
- The sweep's item-by-item reversal pass never sees `fast:` / `diet:` keys: they have no
  subject. Food days are reconciled only by `FoodRewards`.

### 3.5 Write path: `FoodLogService` and `bsk food`

`FoodLogService` (Application) writes the events in §3.1 and validates, refusing in words
with nothing written:

- name: 1–80 characters
- score: a whole number from 1 to 5
- calories: a whole number from 0 to 5,000
- protein, carbs, fat: optional, whole grams from 0 to 1,000
- meal type: breakfast, lunch, dinner, snack or drink. When it's left out, it's guessed from
  the home-zone time: 04:00–10:59 breakfast, 11:00–15:59 lunch, 16:00–21:59 dinner, otherwise
  snack.
- times: not in the future (a minute of skew is allowed)

Operations:

- `LogAsync(entry)`
- `EditAsync(mealId, entry)`
- `DeleteAsync(mealId)`
- `BeginFastAsync(at?)`
- `EndFastAsync(at?)`
- `RecordFastAsync(from, to)`
- `EditFastAsync(fastId, from, to?)`
- `DeleteFastAsync(fastId)`
- `CloseDayAsync(day?)`
- `LogAsync` takes an optional `endFast` flag. Set, it ends the running fast at the entry's
  time in the same transaction (the "end your fast?" prompt).

Every operation runs `FoodRewards` and returns what it wrote plus `Earned`.

`bsk food` (times are home-zone wall time, converted through Postgres):

```
bsk food log "Salmon salad" --score 5 --cal 650 [--at 12:30 | --at "2026-10-19 19:00"]
             [--protein 40 --carbs 20 --fat 30] [--type lunch] [--end-fast]
bsk food edit <meal> [same options]        bsk food delete <meal>
bsk food fast begin [--at …]               bsk food fast end [--at …]
bsk food fast record --from … --to …       bsk food fast edit|delete <fast>
bsk food close [<date>]
bsk food [<date>] [--week | --month] [--json]
```

- A meal or fast is named by its id or its first 8 characters.
- Writes print what they earned ("+39 Health XP (food day)"), like `bsk adhere`.
- The read prints the day's entries, fasts and score parts, and the diet XP paid or
  projected. `--week` and `--month` print one line per day plus totals.

### 3.6 Reads and API

`FoodReader` (API, reader connection) serves:

| Endpoint | Answers |
|---|---|
| `GET /api/food/day?date=` | The day's entries (newest first), fasts ending that day, the running fast if any, the last entry's time (for Begin fast), and the summary: calories, `S`, the three parts, the two awards projected and paid (base, paid, multiplier), closed and how. |
| `GET /api/food/days?from&to` | One summary per day in the range (up to 62 days): the parts, awards projected and paid, calories, `S`, entries, the longest fast. The Week and Month views total these. |
| `GET /api/food/recent?limit=` | Recent distinct entries by name (the newest version of each), for "Log again". |
| `POST /api/food/meals`, `/api/food/meals/{id}`, `/api/food/meals/{id}/delete` | Log, edit, delete. |
| `POST /api/food/fasts` | `{ action: begin \| end \| record \| edit \| delete, … }`. |
| `POST /api/food/close` | `{ date? }`. |
| `POST /api/food/settle` | Settles every food day; answers `{ awards, progress }`. |

- Every write answers `awards` and `progress`.
- Dates are home-zone days (`yyyy-MM-dd`). Times are ISO instants with an offset.
- Projected and paid both come from `DietRules`, so the page shows exactly what will pay.
- Run `npm run gen:api` after the endpoints land.

### 3.7 The Food Log page (web)

- **`/food`**, called "Food Log", in the nav (after Habits) and the command palette. Search
  params: `view` (day / week / month, day by default) and `date`.
- **Phone first.** One column, big touch targets, and a sticky **Log food** button at the
  bottom on narrow screens. It widens into two columns on a desktop.

**Day view**

- **Header:** the date with ‹ ›, Today, and a Day / Week / Month switch.
- **Fasting card:**
  - Running: "Fasting 14 h 20 m · earning since 12 h · 1.6 of 8 XP" with a ring to 24 h and
    **End fast** (the time is now, editable).
  - Not running: **Begin fast**, with the start time set to the last entry's time (editable).
  - Lists the fasts that ended today, with their XP, and **Record a past fast**.
- **Summary card:**
  - Before the close: "On track for 31 of 40" with three bars (Quality 23.7 / 28, Calories
    4 / 4, Fasting 2.7 / 8), total calories and the average score ("4.4 avg").
  - **Close day** shows once the day has an entry.
  - After the close: "Closed · 31 base → 37 Health XP (×1.2)".
  - A day closed by time reads "Closed at midnight".
- **Gallery:** a card per entry, newest first:
  - the photo (F6), or a tile coloured by score with the meal type's icon
  - the name, time, calories and a score chip ("5 · very healthy"; the number is always
    shown, so colour is never the only cue)
  - nutrients when present
  - a flag when it was eaten inside a recorded fast
  
  Tapping a card opens it for editing, with Delete.
- **Log food sheet:**
  - Name, with matching recent entries under it ("Log again").
  - Score picker 1–5, with the examples from §1.
  - Calories: quick sizes, then a number box.
  - Time (now; quick picks for −30 min, −1 h, −2 h and "earlier…").
  - Meal type (guessed). Nutrients (collapsed).
  - When a fast is running and calories are above 0: "Ends your 17 h fast at 12:30" (on by
    default).

**Week view** (Monday to Sunday)

- A row per day: diet XP as a stacked bar out of 40 (quality, calories, fasting), the paid
  XP, calories, the average score and the longest fast. Today is marked as projected.
- Totals: diet XP paid, the average per day, average calories, days closed. Tapping a day
  opens it.

**Month view**

- A Monday-first calendar like the Rewards History calendar. Each cell shows the day's diet
  XP, shaded by base out of 40 (one hue, four steps), with a Table view.
- Month totals as in the week. Tapping a day opens it.

**Feedback:** `announceEarned` as on every earning write: toasts, the chime, level-ups and
rank-ups. A re-price is grouped into one "re-scored" toast (§3.3).

**Mock-first**, as before:

- `lib/diet-score.ts` ports `DietRules.Score` and is tested on the same table as the C#.
- The mock store keeps food events (the newest version wins), pays `fast:` / `diet:` awards
  through the mock earning store at the credited day's multiplier (re-pricing included), and
  answers the food reads.
- The seed has a few days of entries and fasts, so the views are populated.

**Charts:** the stacked bars and the calendar's shading go through the dataviz validator in
both themes. Score colours are a five-step scale checked the same way.

### 3.8 Photos (F6, last)

- **Upload:** `POST /api/food/meals/{id}/photo` (multipart) stores the image as an artifact
  with its bytes in `bsk.artifact_blob` (`IArtifactBlobStore`), then writes a new meal version
  carrying it. It's the live API's first multipart endpoint. The web's document capture still
  stubs one (`captureDocument`), so build it to be reused there.
- **Size:** the browser resizes before uploading, to 1,200 px on the long side as a JPEG at
  about 0.8 quality (roughly 150 KB). Three photos a day is then about 165 MB a year in
  Postgres. **Check the Supabase plan's database limit before F6** (the free plan's is
  500 MB). The blob store is a port, so moving bytes to object storage (Supabase Storage)
  later doesn't touch the event model.
- **Serving:** `GET /api/artifacts/{id}/content`, authenticated, with long cache headers. The
  live `artifact` read is still a stub (`live/reads.ts`), so this is new.
- **Phone camera:** `<input type="file" accept="image/*" capture="environment">`.
- **The gallery** shows thumbnails; tapping one shows it full size. Remove photo writes a
  version without it.

## 4. Phases

Each phase is one commit:

- a migration where needed, the `bsk` verb, views, and tests (`./test.ps1`), with a clean
  strict build
- web phases go mock-first, then live, with `npm run gen:api` after API changes, plus
  `npm run test:e2e` and `npm run a11y` (both themes)
- each phase updates the Build fields of the FOOD requirements it advances

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| F0 | Requirements + plan | FOOD-1 to FOOD-5, D13, the backlog line, this doc. | — |
| F1 | Food entries and fasts | Migration 0034 (`v_meal`, `v_fast`, `v_food_day`); `DietRules` + `DietRules.Score` in Domain with the drift test; `FoodLogService`; `bsk food` writes and reads (no XP yet). Tests: versions, edits, deletes (newest by `recorded_at` wins, even when the time moves earlier); an entry after 5 pm Pacific lands on the Pacific day; across-midnight edits; one running fast; overlap refused; backfill; 0-calorie entries; the example table; the `--end-fast` path; food events stay out of the inbox, timeline and diagnostics. | — |
| F2 | Diet XP | `FoodRewards`: `fast:` / `diet:` keys; the re-price and the ordered ledger guard (`ExpectLatestEarn`); hooks on every food write; settling from `ReconcileAllAsync` (the sweep) and `SettleDomainsAsync`; rank-ups and `progress`; `bsk food` prints what it earned. Tests: example 1 pays nothing; example 2 pays 32 + 7; a fast pays on its end day; Close day pays; a day closed by time pays at the next settle, credited to its own day; an edit after the close re-prices (the reversal, the new earn, `v_award_credit` shows the new base); deleting every entry reverses; a move across midnight re-scores both days; the multiplier stamps `base`; the 70-XP success reads base including diet; two racing re-pricers pay once; nothing before the wallet opening pays. | F1 |
| F3 | API | `FoodReader`; the endpoints in §3.6 with `awards` / `progress`; `gen:api`. Tests: endpoint shapes, refusals as 400s, the 62-day range limit. | F2 |
| F4 | Day view | `/food` (nav, palette), the fasting card with its prefills, the summary with Close day, the gallery, the Log food sheet with Log again; `lib/diet-score.ts`; the mock store's food half; toasts and the grouped re-score. Vitest; e2e golden path (log a salmon salad, record a 23 h fast, close the day: "+39 Health XP"); a11y in both themes, on a phone viewport too. | F3 (mock-first can start after F1) |
| F5 | Week and month | The Week and Month views over `GET /api/food/days`, stacked bars, the calendar with its Table view, totals. Vitest, e2e (switch to Week, open a day), a11y. | F4 |
| F6 | Photos | §3.8: the multipart upload, resize in the browser, artifact content serving, the camera input, thumbnails. Tests: an upload round-trips byte for byte; editing keeps the photo; Remove photo; oversized and non-image files refused. | F4 |

- **F1 and F2 are kernel only.** From F2, `bsk food` works end to end, so Chris could start
  logging from the CLI before the page exists.
- F5 and F6 touch different files and can run in parallel once F4 is in.
- Migrations continue from 0034. Merging to `main` promotes them to staging through CI as
  usual.

## 5. How this design was settled

**Chris's first pass (2026-10-20)**, in his words:

- Track food intake, how healthy it is, photos (last priority), fasting, and calories or
  nutrients when available, all with events.
- Begin and end fasts, with backfill. Fasting XP: none under 12 hours, a small amount per hour
  beyond, recorded at the end of the fast, capped at 24 hours.
- Each meal or snack is an entry with a 1–5 health score and a rough calorie number. At the
  end of the day, a calorie-weighted health score and total calories (a bonus under 2,000 but
  above about 500) pay Health XP, together with fasting.
- At most 40 diet XP a day: calories 10%, healthiness 70%, fasting 20%.
- Examples: 3,000 calories of ice cream pays nothing; 1,200 calories of salmon salad with a
  23-hour fast is close to the full 40.
- A daily view with a gallery and a summary, plus weekly and monthly views, each showing the
  Health XP per day and in total.

**The seven questions**, all answered with the recommendation:

1. **When food pays:** when Chris closes the day, or by itself once the day is over. Rather
   than live, where logging a cookie would show an instant loss and could discourage honest
   logging. Rather than only after midnight, which would keep today's XP out of today's
   70. Closing by hand also vouches the day is fully logged. Fasting pays the moment a fast
   ends.
2. **Above 2,000 calories:** fade to 0 at 2,500, not a cliff, because the estimates are
   rough. The calorie part stays independent of quality.
3. **The quality scale:** a straight line (an all-3 day earns 14 of 28).
4. **Fasting:** buttons, filled in from the food log, with a running counter. Not inferred
   from gaps between meals.
5. **Device:** phone first.
6. **Entry fields:** as in §1, with protein, carbs and fat optional, "Log again", and photos
   last.
7. **Diet vs the 70-XP day:** diet's 40 counts toward it, and the multiplier applies on top.
   Both intended.

**Decided in planning, because one option was clearly better:**

- **Events, not a subject type.** A food entry or a fast is something that happened, not a
  durable thing to manage.
- **Existing kinds:** `measurement` for food, `activity` for fasts and closes (D13).
- **Corrections append a newer version.** The newest by `recorded_at` wins, so the record is
  never rewritten.
- **Scoring lives in C#** (`DietRules`), with a TypeScript port tested on the same table.
- **Two awards a day**, so the fast can pay at its end and the food at the close. That's why
  rounding happens per award.
- **Re-pricing for food days only.** Every other award stays presence-based.
- **A retune never rewrites a past day.** Only a change to a day's entries re-scores it.
- **One day boundary:** midnight Pacific, like the rest of Health XP.

## 6. Later

Each of these has to earn its place through use.

- **Nutrients in the formula:** a protein target, say. They're optional and recorded from F1,
  so a rule can read them later.
- **A calorie target that follows activity**, instead of a fixed 2,000.
- **Weight and body measurements** as more `measurement` events.
- **Water intake and alcohol** as their own lines.
- **Saved meals** that can be edited, beyond "Log again" from history.
- **Estimating calories and a score from a photo** (Production, with AI).
- **Long fasts crediting every day they span**, instead of only the end day.
- **Closing the food day inside the daily review** (REVIEW-1).
- **The running fast on the Focus page.**
- **Fasting streaks, or Life XP and other currencies for food.**
- **A diet-aware success rule** for Health, such as "70 XP and a diet score of at least 20"
  (xp-domains plan §6, richer success rules).

## Status

- [x] F0 — Requirements (FOOD-1 to FOOD-5, D13) + this plan
- [x] F1 — Food entries and fasts (migration 0034)
- [x] F2 — Diet XP on the ledger (no migration)
- [x] F3 — API
- [x] F4 — Day view
- [ ] F5 — Week and month views
- [ ] F6 — Photos

### As built

**F1 (2026-10-20).** As planned, plus:

- **Times are kept to the whole second.** A fast's end is stored both as the event's date and
  inside its payload; at the second, both read back the same, so the entry that ends a fast
  never counts as eaten inside it.
- **One lock for food writes.** Begin, end, record, edit and `--end-fast` re-check "one fast
  runs at a time" and "no overlaps" under a transaction-scoped advisory lock, so two windows
  can't both start a fast. A lost race is refused in words (`FoodConflictException`, a 400).
- **The day view flags food eaten inside a fast** with the fast's id (`EatenDuringFast`), so
  the page can offer to end that fast at the entry's time. 0-calorie entries are never
  flagged.
- **`bsk food edit` changes only what it's given**; the service takes the whole entry. The
  meal type stays as it was unless `--type` is given.
- **`--at` reads** `12:30`, `7:30pm` (today, or yesterday when today's is still ahead),
  `yesterday 20:00`, `2026-10-19 19:00` (home zone), or an ISO instant with an offset.
- **`GetRecentAsync`** ("Log again") returns the newest entry of each name, ignoring case and
  surrounding spaces.
- **Food events never reach other views:** they carry no `subject_id` and no edge, so the
  inbox, the diagnostics, the habit and timer views and the ledger never see them (tested).

**F2 (2026-10-20).** As planned, plus:

- **A paid day is re-scored only when what it read changes.** Each earn cites the event
  versions its day was scored from. Settling leaves a paid key alone while its day still reads
  exactly those versions, whatever `DietRules` scores it now. So a retune never rewrites a
  past day, as §5 decided; comparing bases alone would have re-priced every past day after a
  retune. Once the versions differ, a different base re-prices; the same base writes nothing.
- **The ledger checks a batch in order.** `LedgerWrite` gained `ExpectPaid` and
  `ExpectLatestEarn`; `AppendAsync` checks each write inside the transaction after inserting
  the ones before it, so a reversal and an earn of one key pass together, and a re-price
  decided against an earn that's since been replaced is refused. `RewardService.EarnAsync`
  runs a plan, appends it, pays rank-ups and retries a lost race (3 tries), as reconcile does.
- **Dating:** the first earn of a key is dated at its trigger: the fast's end, the close, or
  for a day that closed itself its `last_recorded_at`. A re-price's reversal is dated at the
  change and its earn 1 ms later, and every earn after the key's previous one.
- **Opening the wallet:** only a food write opens it, dated at the earliest trigger among the
  days that write touched that pays. A settle with nothing behind it (the page opening, the
  sweep) never does. A fact whose trigger came before the opening never pays.
- **Settling lives in the hosts, not in `ReconcileAllAsync`.** `FoodRewards` depends on
  `RewardService`, so `bsk rewards reconcile` (without `--subject`) runs the item sweep and
  then the food settle, and `POST /api/domains/settle` runs the food settle before the domain
  one. Every food write settles all food days, not only the ones it touched, so yesterday
  pays the moment anything is logged today.
- **`bsk food` prints a re-price as one line**, "Re-scored Food day Tue Oct 20: 32 -> 18
  Health XP.", and the day read prints what's paid ("Paid: 39 Health XP (food 32 x 1.2 = 38,
  fast 7)"); `--week` / `--month` add a paid column and total.

**F3 (2026-10-20).** As planned, plus:

- **One read path.** The reads moved out of `FoodLogService` into `FoodReads` (Application),
  which only reads. `bsk food` reaches it through `FoodLogService`, and the API's
  `FoodReader` builds it on the reader connection, so the page and the CLI read a day
  identically.
- **Shapes** (`FoodModels.cs`): `FoodDayRead` (`day`, `today`, `meals` newest first with
  `duringFast`, `fasts`, `running`, `lastMeal`, `summary`), `FoodDaysRead` (`from`, `to`,
  `today`, `days`), and `FoodWriteResultRow` (`action`, `meal`, `fast`, `day`, `days`,
  `awards`, `progress`). Each summary carries the parts, both projected awards, `cap` (40),
  and `paid` (`fast` and `diet`, each `{base, paid, multiplier}`, plus `total` and
  `baseTotal`).
- **`GET /api/food/days`** defaults `to` to today and `from` to that week's Monday.
  `GET /api/food/recent` takes `limit` from 1 to 100 (20 by default).
- **`POST /api/food/meals/{id}` takes the whole entry**, as the Log food sheet sends it, so a
  nutrient left out is cleared. The entry's time and meal type stay as they were when left
  out. `{id}` may be the start of the id, as in `bsk food`.
- **`POST /api/food/fasts`** takes `{action, fast?, at?, from?, to?}`. An action missing what
  it needs is a 400 naming it.

**F4 (2026-10-20).** As planned, plus:

- **The mock's seeded food is unpaid until the page opens.** The seed's Health history is
  calibrated for the habit golden path (40 XP short of level 10), so the seeded food days
  don't sit on the seeded ledger, and the mock's domain settle leaves food alone. Opening
  `/food` settles them, as the API's settle would; in the mock that makes Health an Apprentice
  on the spot.
- **The score scale** is one hue (168), five lightness steps per theme, the dark ramp flipped
  so 5 is always the strongest; it passes the dataviz validator's ordinal checks in both
  themes. The middle steps (3 in light, 2 in dark) reach only about 4:1 against either ink:
  enough for a tile's icon, not for small text. So the gallery's tiles carry only an icon,
  and the score picker shows each score's swatch with the selected one ringed, never text on
  the fill.
- **Toasts:** diet and fasting earns show a plate (🍽️); a reversal and an earn on one key are
  grouped into one "re-scored" toast that chimes only when the score went up, and a give-back
  alone is quiet.
- **Day navigation steps from the date asked for**, not the day on screen: the last day stays
  up while the next loads, so pressing ‹ twice moves two days.
- **The Log food sheet** takes the whole entry, as the API does. Log again fills everything
  but the time. Past days start a new entry at noon on that day. End fast shows only for a new
  entry with calories, eaten after the running fast began.
- **Accessibility:** the food scans emulate reduced motion, which `index.css` already honours
  by switching transitions off, so axe reads settled colours. Opening the page stacks the
  settle's toasts, and the day buttons fade between enabled and disabled.
