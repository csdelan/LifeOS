# Food Log Plan — meals, fasts and diet XP

The design and build order for the Food Log (FOOD-1 to FOOD-7 in
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

**Added on 2026-10-22 (§7):** drinks flagged as alcoholic, a cut to the day's diet XP above
two drinks, a sobriety streak, a count of drinks over any range, identical entries combined
on the day's page, and the day's entries listed oldest first.

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
- Photos (F6) are an artifact with bytes (in Supabase Storage on staging; see F6 as built).
  The payload carries `meal.photo` (the artifact id) forward on every version, so editing the
  calories never drops the photo. The version that adds the photo doesn't set the event's
  `artifact_id`: the photo's own `observation` event does (F6 as built).

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
| `GET /api/food/day?date=` | The day's entries (oldest first since F7, §7), fasts ending that day, the running fast if any, the last entry's time (for Begin fast), and the summary: calories, `S`, the three parts, the two awards projected and paid (base, paid, multiplier), closed and how. |
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
- **Gallery:** a card per entry, oldest first (newest first until F8, §7):
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
| F7 | Drinks in the kernel | §7: migration 0038 (`alcoholic` on `v_meal`; `drinks` and `drink_event_ids` on `v_food_day`); the score locked at 1 and the drink meal type; the drinks cut in `DietRules`; the fast award cut once the day closes; `Sobriety`; the drinks count over a range; the day's entries oldest first; `bsk food log/edit --alcohol` and `bsk food drinks`; the API fields and `GET /api/food/drinks`; `gen:api`. Tests: the drinks table; the score lock; the cut on both awards and when each applies; removing a drink re-scores back; the streak and its edges; the range count by days and by times. | F6 |
| F8 | Drinks on the page | §7: the Alcoholic drink switch and the greyed-out score; the drink tile and label; the summary's drinks line and streak; drinks and the streak in Week and Month; the gallery oldest first; `lib/diet-score.ts` and `lib/sobriety.ts` on the same tables as the C#; the mock store. Vitest, e2e, a11y in both themes. | F7 |
| F9 | Combined entries | §7.4: identical entries on a day as one "×N" card with summed calories and nutrients and a time range; tapping it lists its copies, with Have another. Vitest, e2e, a11y. | F8 |

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
- **Water intake** as its own line. (Alcohol landed in F7–F9, §7.)
- **Saved meals** that can be edited, beyond "Log again" from history.
- **Estimating calories and a score from a photo** (Production, with AI).
- **Long fasts crediting every day they span**, instead of only the end day.
- **Closing the food day inside the daily review** (REVIEW-1).
- **The running fast on the Focus page.**
- **Fasting streaks, or Life XP and other currencies for food.**
- **A diet-aware success rule** for Health, such as "70 XP and a diet score of at least 20"
  (xp-domains plan §6, richer success rules).

## 7. Drinks, the sobriety streak and combined entries (2026-10-22)

Chris asked for four changes on 2026-10-22, in his words:

1. "I would like to optionally flag any logged "food" as "alcoholic". This can be a beer,
   glass of wine, or any other roughly equivalent alcoholic drink. An alcoholic entry
   contributes zero Health XP, and it will automatically lock in lowest rating health rank...
   in fact you can grey out the star rating."
2. "All alcoholic drinks in excess of 2 for that day will decrease the diet's health XP
   contribution by 10% per drink above 2. So theoretically, 12 drinks in a single day will
   ensure zero Health XP from diet is contributed that day."
3. "If I copy a food log and apply it to the same day, it will record the new log with the
   new timestamp, but since it is on the same day, it will combine the display into 1 entry
   with (x2) and combine the calories/nutrients. You can also convert the time of day display
   to a range (eg 12:14 PM-2:45 PM). This feature only affects the display to combine
   identical food logs for a single day."
4. "I would like some way to count up all the alcoholic drinks I've had between 2 dates ...
   Just have the capability to sum up the number of alcoholic drinks in a datetime range. Add
   this information to the week or month view."

He took the three recommendations below and added two things: "Add sobriety streak on the
Food page, and on weekly, monthly views when applicable. Then sort the food entries from
oldest to newest instead of newest to oldest."

### 7.1 The three questions

| Question | Options | Chosen |
|---|---|---|
| Do the first two drinks cost anything? | **A.** A drink is a score of 1 in the calorie-weighted average, like any other 1. **B.** Drinks are left out of the average. | **A.** Under B, 300 calories of beer would score better than 300 calories of fries. |
| Does the cut above two drinks reach fasting XP? | **A.** The whole day: the fast pays in full when it ends and is re-scored at the close. **B.** The food part only. | **A.** It matches "12 drinks ... zero Health XP from diet", and logging a third drink never takes XP back on the spot. |
| What does tapping a combined card do? | **A.** It lists the copies with their times, each opening to change or delete, plus Have another. **B.** It opens the newest copy. | **A.** |

### 7.2 The rules

| Question | Decision |
|---|---|
| What's a drink? | A food entry flagged **alcoholic**: a beer, a glass of wine, a shot, or roughly the same. **One entry is one drink**; a double is logged twice (Log again and the combined card make that quick). Wine with dinner is its own entry, since the flag makes the whole entry score 1. |
| Its score | Always **1**. The service stores 1 whatever is sent; the page greys the picker out at 1. Turning the flag off frees the score again. |
| Its meal type | **Drink** when none is given, instead of the guess from the time. The page switches to Drink when the flag goes on; it can still be changed. |
| Its calories | Count like any entry's: toward the day's calories, and a drink with calories ends a running fast. |
| In the quality score | A 1 in the calorie-weighted average (§7.1), so even one drink costs a little. |
| The cut | With `n` drinks on a day, its diet XP is multiplied by `max(0, 1 − 10% × max(0, n − 2))`: 3 drinks −10%, 4 −20%, 12 or more leave nothing. |
| Which awards | Both. The food award is `round((Quality + Calories part) × cut)` and the fasting award `round(Fasting × cut)`, each rounded halves up as before. |
| When | The food award already pays at the close, so it carries the cut then. **The fasting award pays in full when the fast ends**; once the day is closed (by hand, or because it's over) with more than two drinks, it's re-scored with the cut. Before the close, no drink takes back fasting XP. "On track for" shows the cut on both. |
| Days | A drink counts on the home-zone day it was drunk, midnight to midnight, like every entry. A night out across midnight splits across two days. |
| Past days | Nothing logged before 2026-10-22 has the flag, so no paid day changes. Flagging an old entry re-scores its day, as any edit does. |

**Examples**, in base Health XP: the salmon day of §1 (1,200 calories at 5 and a 23-hour
fast, 39 of 40) plus beers of 150 calories each.

| Beers | Quality | Calories | Fasting | Cut | Food + fast |
|---|---|---|---|---|---|
| 0 | 28 | 4 | 7.3 | none | 32 + 7 = **39** |
| 2 | 22.4 | 4 | 7.3 | none | 26 + 7 = **33** |
| 3 | 20.4 | 4 | 7.3 | −10% | 22 + 7 = **29** |
| 4 | 18.7 | 4 | 7.3 | −20% | 18 + 6 = **24** |
| 12 | 11.2 | 0 | 7.3 | −100% | 0 + 0 = **0** |

### 7.3 The sobriety streak and the drinks count

- **A drink-free day** is a day with no alcoholic entry. A day with nothing logged counts as
  drink-free: only a logged drink breaks the streak (scout's honor, GEN-5).
- **The streak on a day** is the drink-free days in a row ending on it, counted back to the
  day after the last drink, or to the Food Log's first day (its earliest entry, fast or
  close) when there's been no drink. A day with a drink has a streak of 0. Today counts from
  its start, so a drink today makes it 0.
- **The best streak** is the longest run of drink-free days from the Food Log's first day to
  that day. Before the first day there's no streak.
- **Where it shows:**
  - The Day view's summary: the streak as of that day and the best.
  - Week and Month: the drinks in the period, the drink-free days among its days so far (from
    the Food Log's first day), and the streak as of its last day so far (today, when the
    period holds today). A period wholly before the first day shows none of this: that's the
    "when applicable".
- **Drinks over any range:** `FoodReads.GetDrinksAsync(from, to)` counts the drinks drunk in
  `[from, to)`, the days with a drink and the most in one day.
  - `GET /api/food/drinks?from=&to=`: each end is a home-zone day (`yyyy-MM-dd`, both days
    included) or an ISO instant (`to` excluded). The last 30 days, today included, by
    default. There's no range limit, since it's one count, so a 30-, 90- or 365-day look-back
    is ready for a later report.
  - `bsk food drinks [--days N | --from … --to …]`, the last 30 days by default.
- Each day's summary carries its drinks and its cut; the day and range reads carry the streak.

### 7.4 Combined entries (display only)

- On one day, entries with the same **name** (ignoring case and surrounding spaces),
  **score**, **calories**, **protein**, **carbs**, **fat** and **alcohol flag** show as one
  card. The meal type and the photo don't count, since Log again re-guesses the meal type
  from the time.
- The card reads "Beer ×3", with the calories and nutrients summed and the time as a range
  ("7:10 PM–9:30 PM"). It shows the first copy's photo that has one, and the inside-a-fast
  flag when any copy has it.
- The record keeps each entry with its own time, and the XP is the same either way. Editing
  one copy so it differs splits it back out. It applies to any identical entries, not only
  those made with Log again.
- **Tapping it** opens a short list of the copies with their times; tapping one opens it to
  change or delete. **Have another** opens the Log food sheet filled in with that entry, as
  Log again does: the time is now on today, or the last copy's time on a past day.
- The gallery lists cards oldest first; a combined card sits at its first copy's time.

### 7.5 Storage and code

- **Payload:** `meal.alcoholic: true`, left out when false, carried on every version like the
  photo. An edit keeps it unless told otherwise (`alcoholic` left out of `POST
  /api/food/meals/{id}`; `bsk food edit` without `--alcohol` or `--no-alcohol`).
- **Migration 0038:** `bsk.v_meal` gains `alcoholic`; `bsk.v_food_day` gains `drinks` and
  `drink_event_ids` (the drinks' event versions).
- **`DietRules`** gains `DrinksFree` (2) and `DrinkCutPercent` (10); `DietDayFacts` gains
  `Drinks`. `DietScore` carries `Drinks`, `DrinkFactor` and `FastAwardUncut` (what the fast
  pays before the close). The drift test checks the cut reaches 0 at
  `DrinksFree + 100 / DrinkCutPercent` drinks (12).
- **`FoodRewards`:** the fast award's base is `FastAwardUncut` while the day is open and
  `FastAward` once it's closed. A closed day with any drinks also cites the drinks' versions
  (and the close), so adding, removing or flagging a drink re-scores the fast. Any drinks,
  not more than `DrinksFree`, keeps what's cited free of the rules' numbers, so a retune
  still leaves a paid fast alone. A day without drinks cites the fast's own version alone, as
  before, so no paid fast changes.
- **`Sobriety`** (Domain) computes the streak from the Food Log's first day and the days with
  a drink; `lib/sobriety.ts` ports it, tested on the same table.
- **Oldest first:** `FoodReads.GetDayAsync` returns the day's entries by the time they were
  eaten, so the page and `bsk food` agree.

## Status

- [x] F0 — Requirements (FOOD-1 to FOOD-5, D13) + this plan
- [x] F1 — Food entries and fasts (migration 0034)
- [x] F2 — Diet XP on the ledger (no migration)
- [x] F3 — API
- [x] F4 — Day view
- [x] F5 — Week and month views
- [x] F6 — Photos (migration 0035, Supabase Storage)
- [x] F7 — Drinks in the kernel (migration 0038)
- [x] F8 — Drinks on the page
- [x] F9 — Combined entries

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

**F5 (2026-10-20).** As planned, plus:

- **One switch, one URL.** Day / Week / Month sit under the title; `?view=week|month&date=`
  names the period, and ‹ › step a day, a week (from its Monday) or a month (from its first).
  The date is dropped from the URL once the period holds today. In the Week and Month views,
  Log food logs to today.
- **The parts' colours** (quality teal, calories orange, fasting violet) are categorical
  tokens validated on the card across all pairs in both themes; dark fasting steps down to
  L 0.66 to sit in the dark band. Segments are 2 px apart, the data end rounded, the rest of
  the 40 a muted track. Today's segments are lighter and read "on track"; numbers always show.
- **Each day's button says it in words**, e.g. "Sun, Oct 18, 2026: 46 Health XP paid (39 base)
  · 2 entries, 1,200 cal, average score 5, fasted 23 h 00 m", and a tooltip breaks it into its
  parts. Days ahead are disabled; the Table leaves them out.
- **The month calendar** shades by base in four fixed steps of 10 (not relative to the
  month's best), in the Rewards History's teal.
- **Totals:** Health XP paid, the average a closed day (empty closed days count), average
  calories a logged day, and days closed of the days so far.

**F6 (2026-10-21).** As planned, plus:

- **The bytes go to Supabase Storage, not the database.** We're on the Supabase free plan
  (500 MB of database, 1 GB of Storage), so Chris asked for photos in Storage. Migration 0035
  adds `bsk.artifact_object`, and the blob store uploads to a private bucket
  (`lifeos-artifacts`) when the host has `Storage__SupabaseSecretKey`, then writes the rows in
  one transaction. At about 150 KB a photo, three a day is about 165 MB a year, so 1 GB is
  roughly six years. Details in [binary-artifacts.md](binary-artifacts.md#object-storage-migration-0035).
  Without the key the API refuses photos outside Development, in words, rather than filling
  the database. Local dev and tests keep `bytea`.
- **How a photo links to its entry.** The photo is its own `observation` event (payload
  `{"food_photo": {"meal": "<id>"}}`, no inbox flag) whose `artifact_id` is the photo. The
  entry's next version carries `meal.photo`; its event doesn't set `artifact_id`, since one
  event already owns the artifact and a later version would point at it a second time.
  Picking the same photo again writes nothing; Remove photo writes a version without it, and
  the bytes stay in the record.
- **Refusals** (400, in words): no field named `photo`, an empty file, over 5 MB, or bytes
  that aren't JPEG, PNG or WebP (read from the bytes, not the name or the content type).
  The request limit is 5 MB plus 64 KB for the form. Storage down is a 502.
- **Serving:** `GET /api/artifacts/{id}/content` with `Cache-Control: private,
  max-age=31536000, immutable` and the sha256 as its ETag; `If-None-Match` answers 304 before
  any download. The SPA fetches with its bearer token and shows an object URL, kept per
  artifact, since an `<img>` can't send the token.
- **The browser** draws the picked image onto a white canvas no more than 1,200 px on its long
  side and sends a JPEG at 0.8. Re-encoding drops EXIF, so a photo's location never leaves the
  phone. On a new entry the photo is held and sent once the entry is logged (if only the photo
  fails, a toast says the entry is in and how to add it again); on an entry already logged it
  saves the moment it's picked.
- **Take photo and Choose photo.** On Android, `capture="environment"` opens the camera with
  no way to the gallery, so a phone gets both buttons and a desktop only Choose photo.
  `Permissions-Policy` now allows the camera for the app's own origin (`camera=(self)`).
- **The gallery's thumbnail is its own button** ("Show the photo of Grain bowl full size"),
  beside the button that opens the entry, and opens the photo in a dialog.
- **CLI:** `bsk food photo <entry> --file <path>` and `bsk food photo <entry> --remove`. The
  CLI reads the same `Storage__*` environment variables.
- **Not reused yet:** the live client's multipart write is built to serve document capture,
  but `captureDocument` is still a stub.

**F7 (2026-10-22).** As planned in §7, plus:

- **The cut is applied before rounding.** Each award is `round(part × cut)`, so a 23-hour
  fast (7.3) with three drinks still pays 7 (6.6 rounds up), and with four pays 6. The
  salmon-day table in §7.2 is a test (`The_drinks_table_holds`).
- **The fast cites the drinks once the day is closed with any drinks**, not only more than
  two (§7.5), so the citations don't depend on the rules' numbers. Taking a drink back
  re-prices only when the base changes: from four drinks to three, the fast goes 6 → 7 and
  the food 18 → 22; from three to two, only the food moves (22 → 26).
- **A drink needs no score anywhere.** `POST /api/food/meals` with `"alcoholic": true` and no
  `score` stores 1; with a score, 1 is stored whatever it is. `bsk food log --alcohol` refuses
  any score but 1 in words ("A drink's score is always 1: leave --score out."). Turning the
  flag off (`"alcoholic": false`, `bsk food edit --no-alcohol`) needs a score again.
- **Reads:** each day summary carries `drinks` and `drinkFactor` (1 with no cut); each meal
  `alcoholic`; `GET /api/food/day` and `GET /api/food/days` carry `sobriety` (`asOf`, `days`,
  `bestDays`, `since`, `lastDrinkOn`, `firstDay`), null before the Food Log's first day.
  The range's streak is as of its last day, or today when the range runs past it.
- **`GET /api/food/drinks`** answers `{from, to, drinks, drinkDays, mostInADay, mostOn}` with
  `from` and `to` as the instants counted between. An end that's neither a day nor an
  instant with an offset (`2026-10-22T19:00` alone) is a 400 in words, as is `to` before
  `from`.
- **`bsk food`** marks drinks in the day with "(drink)", prints
  the drinks line with its cut ("4 drinks, 2 over 2: 20% off the day's diet XP") and the
  streak ("Sober 12 days (best 30)"); `--week` / `--month` add a drinks column and a totals
  line ("3 drinks; drink-free 5 of 7 days; Sober 2 days (best 9) as of Sun Oct 25");
  `bsk food drinks` prints the count and today's streak, `--json` everywhere.
- **Oldest first** reached `bsk food` and the API at once, since both read
  `FoodReads.GetDayAsync`; the page's gallery follows in F8.

**F8 (2026-10-22).** As planned in §7, plus:

- **The Log food sheet's Alcoholic drink switch** sits just above the score, with what a drink
  is and what more than two cost. On, the score greys out at 1 and the meal type reads "Drink
  (it's alcoholic)" on a new entry, or Drink on one already logged. Off, the score picked
  before comes back. Log again carries the flag, and its chip says "drink" in place of the
  score ("IPA · drink · 200 cal").
- **The gallery** shows a drink as a glass of beer on its score tile, with "1 · alcoholic" where
  other entries name their score's word. The entry's button says "(alcoholic drink)".
- **The Diet XP card** adds the drinks under the parts ("4 drinks · 20% off the day's diet
  XP"; "2 drinks · the cut starts above 2"). While the day is open with a cut and a paid fast,
  it adds that the fast is re-scored when the day closes. "On track for" is already cut.
- **The Sobriety streak card** sits under the Diet XP card on the Day view. It shows "12 days
  sober" or "A drink today", then "Drink-free since Oct 14 · best 30 days", and a line on the
  first day and unlogged days. Its heading names the day when it isn't today, and it's hidden
  before the Food Log's first day.
- **Week and Month** add two totals from the Food Log's first day:
  - **Drinks:** "4 drinks", with "Drink-free 2 of 3 days".
  - **Sobriety streak:** "0 days", with "A drink today · best 6 days". It's labelled with its
    day ("on Oct 25") for a past period.

  The totals grid goes to three columns when they show. Each day's bar is shortened by its
  cut, like its XP. Its facts and its button add the drinks, and its tooltip adds the cut. A
  calendar day with drinks shows a glass and the count, and the Table has a Drinks column.
- **The page adds up the range's days** for the period's drinks; it doesn't call
  `GET /api/food/drinks`, which waits for a later report.
- **Ports:**
  - `lib/diet-score.ts` gains `drinksFree`, `drinkCutPercent`, `drinkFactorFor`,
    `DRINKS_TO_NOTHING` and `fastAwardUncut`. The cut is applied as whole percent before
    rounding, so it matches the kernel's decimals.
  - `lib/sobriety.ts` ports `Sobriety`.
  - Both are tested on the kernel's tables.
- **The mock** follows the kernel:
  - the flag on the entry's payload, kept through an edit or a photo
  - a drink stored at 1 and typed as a drink
  - the fast paid in full until the close, then cut, citing the drinks
  - entries oldest first
  - the streak on the day and range reads (`firstDay` is the earliest folded day)
- **Tests:**
  - Vitest: the drinks table and the rounding in `diet-score.test.ts`, `sobriety.test.ts`, the
    mock's drinks (as the kernel's `FoodDrinksTests`), and the totals' drinks.
  - An e2e path: four IPAs with the score locked, the cut, the streak, the week's count and
    the Table.
  - An a11y scan in both themes: the sheet with the switch on, a day with drinks, the week
    and the month.

**F9 (2026-10-22).** As planned in §7.4, display only (no kernel, API or mock change), plus:

- **`lib/meal-groups.ts`** groups a day's entries by name (ignoring case and surrounding
  spaces), score, calories, protein, carbs, fat and the alcohol flag. Each card sits at its
  first copy's time.
- **What the card shows:**
  - the first copy's name with the count ("IPA ×4") and its meal type
  - the calories and nutrients summed
  - the time as a range ("7:10 PM–9:30 PM"), or one time when the copies share the minute
  - the first copy's photo that has one
  - "One was eaten during a recorded fast" when any copy was
- **Its button** reads "IPA ×4 (alcoholic drink), 7:10 PM–9:30 PM: show the 4 entries".
  It opens a dialog listing each copy by time, meal type and calories; picking one opens it to
  change or delete. There's no chevron on the card: in three columns it squeezed the text, and
  the "×4" already says there's more.
- **Have another** opens Log food filled in from the last copy: the name, the score or the
  drink flag, the calories and the nutrients. The meal type is re-guessed. The time is now on
  today, or the last copy's time on a past day. The sheet's description reads "Another IPA,
  filled in from the last one: change anything that's different."
- **Tests:**
  - `meal-groups.test.ts`: the key, the order, the sums, the range and the fast flag.
  - `meal-gallery.test.tsx`: the combined card, a single entry, the copies and Have another.
  - The drinks e2e path: Have another adds a fifth IPA, and deleting a copy takes it back.
  - An a11y scan of the copies dialog in both themes.
