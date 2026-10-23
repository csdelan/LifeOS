# Activity Plan — Oura steps, exercise, sleep, readiness and activity XP

The design and build order for bringing Oura Ring data into LifeOS: an **Activity** page with
a steps trend, an exercise log, and sleep and readiness (phase 1, read-only), then Health XP
from that data (phase 2). The requirements are ACTIVITY-1 to ACTIVITY-5, and the decision
behind them is **D14**, both in [ui-requirements.md](ui-requirements.md). The requirements
say *what*; this doc says *how* and *in what order*, written so an agent with no session
context can pick up any phase.

**Phase 1 agreed with Chris on 2026-10-22.** He gave a brief and answered three questions,
then added sleep and readiness, and an optional one-line note on each workout, to phase 1
(§5). Phase 2 waits for his formulas.

**Keep it small.** As with the Food Log, the thing under test is whether seeing steps,
exercise and sleep beside the rest of LifeOS changes what Chris does. Phase 1 builds only
what the page needs, but stores the data so that phase 2 is rules and reconcile, with no
re-import.

## 0. Read these first

- [food-log-plan.md](food-log-plan.md): the closest prior art.
  - §3.1: versioned events, where the newest by `recorded_at` wins.
  - §3.3 and F2 as built: item-less award keys, re-priced when their day changes.
  - §3.4: settling when a page opens.
- [xp-domains-plan.md](xp-domains-plan.md): the credited day, the multiplier and the
  successful day (60 base XP since 2026-10-22). Phase 2 only.
- [ontology.md](../ontology.md):
  - §4.4 provenance: `observed` means read off an external source.
  - §5 idempotency: `(source_id, external_id)` is unique, built for "configurable data
    sources".
- `SupabaseStorageOptions` and `SupabaseObjectStorage` (Infrastructure/Storage): the pattern
  for an outbound HTTP client behind a port. Its `Section:Key` settings fail startup when
  malformed and never print a secret.
- [deploy.md](deploy.md) and `scripts/fly-setup.ps1`: how a server-only secret travels from
  `.env` to Fly.
- §7 below: the Oura API facts this plan relies on, with sources.

## 1. What Oura gives us

Checked against Oura's API v2 docs and OpenAPI spec 1.42 on 2026-10-22 (§7).

- **Connecting needs OAuth.** Oura retired personal access tokens in December 2025.
  - Chris registers LifeOS once as an app in Oura's developer portal, then signs in to Oura
    from LifeOS once (§3.1).
  - An app serves up to 10 users before Oura has to approve it, which doesn't matter here.
- **An active Oura Membership is required** for Gen3 and Ring 4. Without one the API answers
  403. Chris has one.
- **Daily activity** (`daily_activity`) is one record per Oura day:
  - steps, active and total calories, walking-equivalent distance
  - seconds at high, medium and low activity, plus sedentary, resting and non-wear time
  - the activity score, and per-minute series

  How it behaves:
  - **Oura's activity day starts at 4 a.m.** local time, so steps between midnight and
    4 a.m. count on the day before.
  - Today's record is partial and updates as the phone app syncs.
  - **Past days change too.** A workout added, edited or deleted on an earlier day changes
    that day.
  - Record ids aren't documented as stable, so LifeOS keys a day's record on its `day`.
  - **There's no step goal in the API**, only calorie and distance targets. LifeOS keeps
    Chris's own (§2).
- **Workouts** (`workout`) carry:
  - an activity: free text such as `walking`, `running` or `tableTennis`
  - an optional label, a start and an end
  - active calories and distance
  - an intensity: `easy`, `moderate` or `hard`
  - a **source**:

  | Source | Meaning |
  |---|---|
  | `manual` | Entered in the Oura app. |
  | `autodetected` | Detected by Oura, not confirmed yet. Very likely returned before it's confirmed. |
  | `confirmed` | Detected by Oura, then confirmed. |
  | `workout_heart_rate`, `live_oura_heart_rate`, `live_third_party_heart_rate` | Recorded live in the app. |

  Workouts can be added, edited or deleted on any day. A deleted one simply stops appearing,
  so a sync compares ids across its window.
- **Sleep comes in three collections:**
  - `daily_sleep`: the sleep score (1–100) and its seven contributors (deep sleep,
    efficiency, latency, REM, restfulness, timing, total sleep).
  - `daily_readiness`: the readiness score (1–100), its nine contributors (activity
    balance, body temperature, HRV balance, previous day's activity, previous night,
    recovery index, resting heart rate, sleep balance, sleep regularity) and the temperature
    deviation in °C.
  - `sleep`: each sleep period. Fields:
    - when you went to bed and got up
    - time in bed, total sleep, and time in deep, light and REM sleep and awake
    - latency and efficiency
    - average HRV and average and lowest heart rate
    - a **type**:

    | Type | Meaning |
    |---|---|
    | `long_sleep` | The night: over 3 hours, counts toward the daily scores. |
    | `sleep` | A confirmed nap, 15 minutes to 3 hours. |
    | `late_nap` | A nap ending after 6 p.m.; counts toward the next day. |
    | `rest` | A detected sleep the user rejected. |
    | `deleted` | Deleted by the user. |

  How sleep behaves:
  - **A night counts on the day you wake up.** Oura's sleep day changes at 6 p.m., and
    readiness belongs to the same morning.
  - **Sleep and readiness appear only after the Oura app syncs the ring**, usually when it's
    opened in the morning. Steps and workouts also update in the background.
- **Chris's assumption holds for what counts (§2).** Workouts entered or recorded live in
  the Oura app reach the API. Workouts Oura imports from Apple Health, Health Connect or
  Strava aren't confirmed to; if any exercise arrives that way, A6 checks it against the real
  account.
- **Quirks the client handles:**
  - Date filters behave differently per collection. Some exclude the end date, and workouts
    filter by UTC date. So each request widens its window by two days, and LifeOS filters on
    `day` itself.
  - Every list pages through `next_token`.
  - The rate limit, 5,000 requests per 5 minutes, is far beyond one person's use.
- **Webhooks exist** (ids only, signed, with subscriptions to renew). Polling is simpler and
  enough for phase 1 (§6).
- **Oura's API agreement** (in effect since 2026-06-08) has clauses on how long data is kept,
  on deleting it within 72 hours of a user's request, and on AI. It was read for A2; §7 has
  what it says and what LifeOS does about each clause.

## 2. Phase 1 rules (agreed 2026-10-22)

| Question | Decision |
|---|---|
| What's pulled | **Daily activity, workouts, daily sleep, daily readiness and sleep periods** (scopes `daily workout`). Heart rate, stress and the rest wait until a phase needs them; Oura keeps the history, so waiting loses nothing. |
| Read-only | LifeOS never writes to Oura. A workout is added, edited or deleted in the Oura app, and the change arrives with the next sync. The one thing LifeOS adds is its own note on a workout (below). |
| **Workout notes** | **Any workout can carry an optional one-line note**, written on the Activity page ("Hill repeats, left knee fine"). Up to 140 characters, no line breaks. It lives only in LifeOS, can be changed or cleared at any time, and stays with the workout through every sync. |
| Which day | **Oura's own day.** Activity days start at 4 a.m.; a night counts on the morning it ends. Steps can't be re-cut to LifeOS's midnight, because Oura gives a day's total, not steps by the hour. |
| Today | Today's steps and workouts are "so far", and last night's sleep and readiness appear once the Oura app has synced. LifeOS is never fresher than the Oura app. |
| When it syncs | When the Activity page opens (at most once every 5 minutes), on **Sync now**, and from `bsk oura sync`. No background job in phase 1 (§3.3). |
| History | On first connect, everything Oura has. A few MB at most. |
| **Step thresholds** | **A minimum of 5,000 and a goal of 7,500.** More is better; less is worse. A day reads as under the minimum, at the minimum, or at the goal. |
| Steps chart | 30 days by default, or 90: a bar per day, the 7-day average as a line, and dashed lines at 5,000 and 7,500. Bars are toned by band (under the minimum, the minimum met, the goal met). 1 year: a bar per week (Monday to Sunday) of the average a day, since 365 bars don't fit on a phone. Today is a lighter "so far" bar. |
| **What counts as exercise** | **Only workouts Chris records himself**: entered in the Oura app (`manual`) or recorded live (`workout_heart_rate`, `live_oura_heart_rate`, `live_third_party_heart_rate`). Not counted: workouts Oura detected, confirmed or not, and **any walk** (`walking`), since its steps already count. A source Oura adds later isn't counted until it's classified. |
| Exercise view | This week's sessions, minutes and calories against last week; minutes per week for 12 weeks; and the counted workouts grouped by week. The ones not counted sit behind "Show N not counted", each with its reason. |
| Sleep and readiness | Last night's sleep score, readiness score, time asleep, and bed and wake times. A chart of sleep score, readiness or time asleep over 30 days, 90 days or 1 year, with the 7-day average. Oura's own bands (85 and up optimal, 70 to 84 good, under 70 pay attention) are lines on the score charts. |
| The night | The day's `long_sleep` period (the longest, if there are two). Naps add up separately as "naps". Rejected (`rest`) and deleted periods are left out. |
| Ring off | A day with a lot of non-wear time says so ("ring off 6 h"), so a low step count has its reason beside it. |
| Where | The web app, phone first like the Food Log. The WinForms Pilot gets nothing. |

## 3. Design

### 3.1 Connecting to Oura (OAuth)

**One-time setup, by Chris, before A1 can be tried for real:**

1. In Oura's developer portal (developer.ouraring.com), create an app called LifeOS.
2. Register the redirect URIs exactly as they'll be sent:
   - `https://lifeos-mz55-staging.fly.dev/activity/connected`
   - `http://localhost:5173/activity/connected`, if the portal accepts localhost. That isn't
     documented. If it doesn't, local dev runs on the mock store and recorded fixtures, and
     only staging connects.
3. Put these in `.env`:
   - the client id and secret, as `Oura__ClientId` and `Oura__ClientSecret`
   - a new `Integrations__TokenKey` (A1 prints a command that makes one)

   `fly-setup.ps1` imports all three as Fly secrets. They're server-only, never `VITE_`.

**The flow** (server-side, with PKCE):

1. **Connect Oura** on the Activity page calls `POST /api/oura/authorize`. It answers Oura's
   authorize URL with:
   - `response_type=code`
   - the scopes `daily workout`
   - a `state` and a PKCE challenge, kept on the connection row for 10 minutes
2. Chris signs in to Oura and approves.
3. Oura sends the browser back to `/activity/connected?code=…&state=…`. That's an SPA route:
   the API authenticates by the bearer token the SPA sends, and a redirect straight to the
   API wouldn't carry it.
4. The page posts `{ code, state }` to `POST /api/oura/connect`. The API:
   - checks the state
   - trades the code for tokens, using the client secret
   - stores them (§3.5)
   - runs the first sync, which backfills history

**Tokens:**

- **Access tokens expire.** LifeOS reads `expires_in`; the docs say 24 hours in one place
  and 30 days in another.
- **Refresh tokens are single-use.** Each refresh returns a new one and spends the old one.
  So a refresh runs under `SELECT … FOR UPDATE` on the connection row and saves the new pair
  in the same transaction. The API and the CLI can't both spend it, and a crash can't lose
  it.
- **A real refresh is tried on the first day of A6.** One third-party report says apps made
  in Oura's new portal must refresh at a different, undocumented token URL. *Tried
  2026-10-22: a new-portal app refreshes at `api.ouraring.com/oauth/token` as documented.*
- **A spent or revoked token** (`invalid_grant`, or a 401) marks the connection as needing
  to reconnect. The page then shows **Reconnect Oura**.
- **Disconnect** revokes the token at Oura and clears it. Data already synced stays, since
  the record is append-only.
- **Scopes come back prefixed** (`extapi:daily`), so they're compared without the prefix.
  The spec doesn't map scopes to collections. The plan assumes `daily` covers sleep and
  readiness, and A1's first real call checks it.
- **Dev and staging are separate databases**, so each connects on its own.
- **The CLI never connects.** `bsk oura status` and `bsk oura sync` use the stored
  connection. They read `Oura__*` and `Integrations__TokenKey` from the environment, the way
  the Storage key works today.

### 3.2 Storage: observed events, versioned by content (D14)

Every Oura record LifeOS keeps is an event. No subject type and no new event kind.

- **`source_id` is `oura`** (a new `KernelSources.Oura`), whichever door ran the sync, so the
  CLI and the API dedupe against each other.
- **Provenance is `observed`**, read off an external source. This is its first use.
- **Kinds:** a workout is an `activity` event (something Chris did). Everything else is a
  `measurement` event: a day's activity, sleep score and readiness, and a sleep period. That
  follows the same split as D13.
- **Dates:** `occurred_at` is the start of the Oura day for a daily record, and the start of
  a workout or sleep period. `recorded_at` is when LifeOS synced it.
- No `subject_id` and no edge, like food events. So the inbox, diagnostics, the habit and
  timer views, and the ledger never see them. A2 checks every reader that scans events by
  kind, as F1 did.

**Identity:**

- The daily records (activity, sleep score, readiness) are identified by their `day`,
  because Oura's ids for them aren't documented as stable.
- Workouts and sleep periods are identified by Oura's `id`.

**Payloads are trimmed** to what LifeOS reads. `activity`, `source`, `intensity` and the
sleep `type` stay plain strings, not enums, because Oura keeps adding values.

```json
{ "oura_activity": { "day": "2026-10-21", "oura_id": "…", "steps": 9234,
    "active_calories": 512, "total_calories": 2650, "walking_m": 7400,
    "high_s": 600, "medium_s": 2400, "low_s": 12000, "sedentary_s": 30000,
    "resting_s": 25000, "non_wear_s": 1800, "score": 82 } }

{ "oura_workout": { "id": "<oura id>", "day": "2026-10-21", "activity": "running",
    "label": null, "start": "2026-10-21T07:02:00-07:00", "end": "2026-10-21T07:41:00-07:00",
    "calories": 420, "distance_m": 5100, "intensity": "moderate", "source": "manual" } }

{ "oura_sleep_day": { "day": "2026-10-21", "oura_id": "…", "score": 82,
    "contributors": { "deep_sleep": 90, "efficiency": 85, "latency": 70, "rem_sleep": 88,
                      "restfulness": 75, "timing": 95, "total_sleep": 80 } } }

{ "oura_readiness": { "day": "2026-10-21", "oura_id": "…", "score": 76,
    "temperature_deviation": -0.1, "temperature_trend_deviation": 0.05,
    "contributors": { "activity_balance": 80, "body_temperature": 98, "hrv_balance": 70,
                      "previous_day_activity": 85, "previous_night": 78,
                      "recovery_index": 60, "resting_heart_rate": 90,
                      "sleep_balance": 75, "sleep_regularity": 82 } } }

{ "oura_sleep": { "id": "<oura id>", "day": "2026-10-21", "type": "long_sleep",
    "start": "2026-10-20T23:05:00-07:00", "end": "2026-10-21T06:40:00-07:00",
    "in_bed_s": 27300, "asleep_s": 25200, "deep_s": 5400, "light_s": 13800, "rem_s": 6000,
    "awake_s": 2100, "latency_s": 600, "efficiency": 92, "average_hrv": 48,
    "average_hr": 54.2, "lowest_hr": 47 } }
```

Oura's series stay in Oura. That covers MET by the minute, the 5-minute activity class, and
sleep's heart-rate, HRV, movement and hypnogram strings. They're kilobytes a day, and the
activity ones change on every sync. A later view that needs one can fetch it.

**Versions:**

- **A sync writes a new version only when the trimmed record differs** from the newest one
  LifeOS holds.
  - Today's steps grow all day, so today gets a version for each sync that saw a change: a
    few to a few dozen a day.
  - Past days and past nights rarely change.
  - All told, about 5 to 10 MB a year.
- **The newest version** by `recorded_at`, then `id`, wins (the D13 rule).
- **A deleted workout or sleep period:** one that LifeOS holds but Oura's answer no longer
  has, inside a window fetched in full, gets a voided version, such as
  `{ "oura_workout": { "id": "…", "voided": true } }`. If it comes back, it gets a new
  version. A sleep period Oura marks `deleted` reads the same as a voided one.
- **Idempotency:** `external_id` is
  `<collection>:<day or id>:<first 16 hex of the trimmed record's sha256>:<the version it replaces, or "first">`.
  - The unique index then refuses a duplicate from a retried or racing sync.
  - A value that flips back (A → B → A) still records, because its predecessor differs.

**Workout notes are Chris's own events**, not Oura's:

- **Event shape:** a `note` event, provenance `declared`, `source_id` the door that wrote it
  (`api` or `cli`).
- **Payload:** `{ "workout_note": { "workout": "<oura id>", "text": "Hill repeats, left knee fine" } }`.
- **Versions:** each change writes a newer version. The newest by `recorded_at`, then `id`,
  wins, and an empty `text` clears the note.
- **Safe from syncs:** the note is keyed by the workout's Oura id, so re-syncing the
  workout never touches it. A note on a workout later deleted in Oura stays in the record
  but isn't shown. If the workout comes back, so does its note.
- **Kept out of other views:** no triage flag, no subject and no artifact, so it never
  reaches the inbox or the captures. A2 tests that, alongside the Oura events.
- **Refused in words, with nothing written:**
  - a note on a workout LifeOS doesn't hold
  - a note over 140 characters, or with a line break

**Why events, not a table each sync overwrites:**

- It's the kernel's model for an external source. Invariant 7 was built for this.
- It keeps "what LifeOS knew, and when" for free.
- Phase 2's awards cite the exact versions they were priced from (`derived_from`). So a day
  Oura revises later is re-priced exactly as a corrected food day is (D13), with no new
  machinery.

### 3.3 Syncing

- **`OuraSyncService`** (Application) sits behind an **`IOuraClient`** port.
  `OuraHttpClient` (Infrastructure) implements it with a small hand-written client:
  - five collections and the OAuth calls, so no generated SDK
  - tested against recorded JSON fixtures, with shapes taken from Oura's sandbox
- **The window:** a sync covers the last 14 days through today, for every collection. That
  catches a ring that synced late, and edits made in the Oura app.
  - Each request asks for two extra days on each side, and LifeOS filters on `day` itself.
  - The first sync after connecting fetches all history, a few months per request.
- **One sync at a time:** a sync runs under a transaction-scoped advisory lock. A second
  sync waits, then finds nothing new. That covers the page opening while Sync now is
  pressed, and the API racing the CLI.
- **Throttle:** the page-open sync is skipped when the last sync is under 5 minutes old.
  Sync now and the CLI always run.
- **The outcome** is kept on the connection row (`last_synced_at`, `last_sync_error`, §3.5).
  The page can then say "Synced 3 min ago", or "Oura refused the sign-in: Reconnect".
- **Errors**, each in words:
  - 401 after a refresh: "Reconnect Oura".
  - 403: the membership has lapsed or a scope is missing. Oura's `detail` says which, and
    the page passes it on.
  - 429: wait as long as `Retry-After` says, and say when to try again.
  - Oura is down: the page still shows what LifeOS already has.
- **No background job in phase 1.**
  - The Fly machine stops when idle (`min_machines_running = 0`), so a timer inside the API
    wouldn't run reliably.
  - The page syncs whenever it's looked at, and that's all phase 1 needs.
  - If phase 2 needs XP to land without a page open, a scheduled sync comes then (§3.8).

### 3.4 What counts as exercise (in code)

`ExerciseRules` in Domain holds the rule in §2, like `DietRules`:

- `CountedSources`: `manual`, `workout_heart_rate`, `live_oura_heart_rate`,
  `live_third_party_heart_rate`.
- `ExcludedActivities`: `walking`. Steps already count it.
- `Counts(source, activity)` answers counted, or not counted with a reason:
  - "Detected by Oura"
  - "A walk: it's in your steps"
  - "Unknown source"
- A TypeScript port, `lib/exercise-rules.ts`, is tested on the same table, so the mock and
  the API agree.

Phase 2 is expected to pay on the same set, so the rule lives where XP rules live. Changing
it (counting confirmed detections, or excluding another activity) is one line plus its
tests.

### 3.5 Migrations and views

**Migration 0036, the connection (A1):**

- **A new schema, `bsk_ops`,** holds operational state that isn't part of the life record.
  It's mutable and **not granted to `bsk_reader`**. Every table in `bsk` is readable by the
  reader role by default (0005's default privileges), so tokens can't live there.
- **`bsk_ops.integration`** has one row per connected service, with these columns:
  - `service` (the key)
  - `access_token_enc`, `refresh_token_enc`, `expires_at`, `scopes`
  - `pending_state`, `pending_verifier`, `pending_until`, for a connect in progress
  - `connected_at`, `needs_reconnect`, `last_synced_at`, `last_sync_error`, `history_from`
- **Tokens are encrypted** with AES-GCM under `Integrations:TokenKey` (32 random bytes,
  base64), held in `.env` and as a Fly secret. A database dump alone doesn't reveal them.

**Migration 0037, the views (A2).** All are read-only and granted to `bsk_reader`. Each folds
the newest live version.

| View | One row per | Columns |
|---|---|---|
| `bsk.v_activity_day` | Oura day | `day`, `steps`, `active_calories`, `total_calories`, `walking_m`, `high_s`, `medium_s`, `low_s`, `sedentary_s`, `resting_s`, `non_wear_s`, `score`, `synced_at` (the newest version's `recorded_at`), `versions`, `event_id` |
| `bsk.v_workout` | live workout | `workout_id`, `day`, `activity`, `label`, `started_at`, `ended_at`, `seconds`, `calories`, `distance_m`, `intensity`, `source`, `note`, `note_at` (null when there's none), `synced_at`, `event_id` |
| `bsk.v_sleep_period` | live sleep period (not `rest` or `deleted`) | `period_id`, `day`, `type`, `started_at`, `ended_at`, the durations, `latency_s`, `efficiency`, `average_hrv`, `average_hr`, `lowest_hr`, `synced_at`, `event_id` |
| `bsk.v_sleep_day` | day with a sleep score or a sleep period | `day`, `score`, `contributors`; the night (the longest `long_sleep`): `bed_at`, `woke_at`, `in_bed_s`, `asleep_s`, `deep_s`, `light_s`, `rem_s`, `awake_s`, `efficiency`, `average_hrv`, `lowest_hr`; `naps`, `nap_s`; `synced_at`, `event_ids` |
| `bsk.v_readiness_day` | day with a readiness score | `day`, `score`, `contributors`, `temperature_deviation`, `temperature_trend_deviation`, `synced_at`, `event_id` |

Partial indexes keep these reads off a full scan of the event stream, as 0034 did for food.
There's one per payload key: on `day` for the daily records, and on `id` for workouts and
sleep periods, each with `recorded_at`.

### 3.6 Reads, API and CLI

`ActivityReads` (Application) serves both `bsk oura` and the API's `ActivityReader` (on the
reader connection), as `FoodReads` does.

| Endpoint | Answers |
|---|---|
| `GET /api/activity/days?from&to` | One row per day, up to 731 days: steps and their band, calories, activity minutes, non-wear time; the sleep score, readiness score, time asleep, bed and wake times. |
| `GET /api/activity/workouts?from&to` | Live workouts, newest first, each with `counted` and, when not counted, its reason, plus its note. |
| `POST /api/activity/workouts/{id}/note` | `{ text }`; an empty text clears it. Answers the workout. LifeOS's one write. |
| `GET /api/activity/day?date=` | One day in full, for the detail sheet: the contributors, the night's stages, naps and the day's workouts. |
| `GET /api/activity/summary` | Today so far and last night. This week against last week: average steps, days at the goal, days under the minimum, counted sessions and minutes, average sleep and readiness. The connection state and the last sync. |
| `POST /api/activity/sync` | `{ force? }`. Answers what changed and `lastSyncedAt`. The page calls it on open, and Sync now forces it. |
| `GET /api/oura/status` | The connection state. Never answers a token. |
| `POST /api/oura/authorize`, `POST /api/oura/connect`, `POST /api/oura/disconnect` | The connect flow (§3.1). |

```
bsk oura status
bsk oura sync [--from <date>]
bsk oura steps [--days N] [--json]
bsk oura workouts [--days N] [--all] [--json]
bsk oura note <workout> "Hill repeats, left knee fine" | --clear
bsk oura sleep [--days N] [--json]
```

A workout is named by its Oura id or the first 8 characters of it, as `bsk food` names
entries. The note is written by `WorkoutNoteService` (Application).

The step thresholds (5,000 and 7,500) live in Domain beside `ExerciseRules`
(`ActivityTargets`). The API answers them with the days, so the page never hard-codes them.

Run `npm run gen:api` after the endpoints land.

### 3.7 The Activity page (web)

- **`/activity`**, called "Activity", sits in the nav after Food Log and in the command
  palette. Phone first: one column, widening to two on a desktop.
- **Banner (A11):** "Activity" over a drawn scene, teal hills under a dawn sun with a trail of
  dotted steps, or a crescent moon and stars in the dark theme. "Synced from Oura 3 min ago"
  and **Sync now** sit on a frosted strip in it. It scrolls with the page.
  - Before connecting, a Connect Oura card says what LifeOS will read, and that it's
    read-only.
  - `/activity/connected` finishes the connect (§3.1), then shows the page.
- **Card titles (A11):** each card's name is large (the page's serif, 24 px) beside its icon
  on a tile of the card's colour, and that colour runs along the card's top: Today (amber,
  a sun), Steps (teal, footprints), Tags (magenta, a tag), Sleep and readiness (indigo, a
  moon), Exercise (ember, a dumbbell).
- **Today card:**
  - **Steps so far** as the big number, with progress past the 5,000 minimum toward the
    7,500 goal.
  - **Last night:** the sleep score, readiness score and time asleep. Or "Open the Oura app
    to sync last night" when it hasn't arrived.
  - Today's counted workouts, and today's Health XP (§3.8).
  - **A day at a time (A11):** arrows step back a day, as far as the history read from Oura
    (`historyFrom`); **Today** comes back. The title says the day ("Yesterday", "Wed, Oct
    21") over the date or how long ago. An earlier day shows its steps (not "so far"), the
    night before it, its workouts, and its Health XP ("Health XP yesterday"), read from
    `GET /api/activity/day`, the same read its sheet uses. On a phone the arrows take their
    own row, one at each end.
- **Steps card:**
  - The chart (§2): 30 days, 90 days or 1 year, the 7-day average line, the two threshold
    lines, bars toned by band, and today's "so far" bar.
  - Hover or arrow keys read a day ("Tue Oct 21: 6,240 steps, over the minimum, 1,260 short
    of the goal"), with non-wear time when it's high. A Table view lists every day, as on the
    Character page.
  - Tiles: the 7-day average, this week against last week, days at the goal, days under the
    minimum, and the best day.
- **Exercise card:**
  - This week: counted sessions, minutes and calories, against last week.
  - Minutes per week for the last 12 weeks.
  - The counted workouts, newest first and grouped by week (Monday to Sunday). Each row
    shows:
    - the activity's icon and name: Oura's label when there is one, otherwise the activity
      made readable ("tableTennis" → "Table tennis"), with a generic icon for anything
      unknown
    - the day and time, duration, calories, and distance when there is one
    - an intensity chip
    - the note, when there is one, on its own line under the row
  - "Show N not counted" opens the rest, muted, each with its reason (§3.4).
  - **Tapping a row opens a sheet** with every field, the source included. The sheet also
    has the **note**:
    - a one-line box with "Add a note" as its placeholder and a 140-character counter
    - it saves on Enter or when the box loses focus, then shows "Saved"
    - Clear removes the note
    - Oura's fields are shown, not edited
  - A note can go on any workout, counted or not.
- **Sleep and readiness card:**
  - Last night in full: the sleep score and the readiness score, each with Oura's band in
    words; time asleep against time in bed; bed and wake times; deep, light and REM; average
    HRV and lowest heart rate.
  - A chart with a switch: Sleep score, Readiness or Time asleep. It has the same ranges, the
    7-day average, and, on the scores, lines at 70 and 85. Hover reads a day, and the Table
    lists them.
  - Tapping a day opens its detail sheet: both scores' contributors as small meters, the
    night's stages, and naps.
- **One chart component.** The steps chart and the sleep chart share a daily-metric chart,
  grown from `DomainDailyChart`: bars, a rolling-average line, reference lines, band tones,
  a range switch, readouts and a Table view.
- **Mock-first:** the mock store gets an activity half, seeded with about 120 days of
  activity, sleep and readiness, and 40 workouts of every source, walks included. Its sync
  changes nothing.
- **Checks:**
  - The charts and the band tones go through the dataviz validator in both themes. Numbers
    and words always say the band too, so colour is never the only cue.
  - `npm run a11y` runs in both themes and on a phone viewport.
  - The e2e golden path:
    1. Open the page.
    2. Switch steps to 90 days and open the Table.
    3. Show the workouts not counted.
    4. Add a note to a run and see it under the row.
    5. Switch the sleep chart to Readiness.

### 3.8 Phase 2: activity XP

Settled on 2026-10-22 from Chris's planning notes (§5). The machinery is D13's: only the
rules are new. Activity pays **Health XP only**, into the same balance, levels and ranks the
Food Log feeds. There's no new currency, balance or level curve, and the Food Log's rules
don't change.

**The rules (`ActivityRules`, A7).** Up to 80 base Health XP a day from three parts. Each
part is its own award, rounded to whole XP, halves up (`DietRules.Round`), before the
multiplier.

| Part | Most a day | How it pays | Pays nothing when |
|---|---:|---|---|
| Steps | 25 | A straight line through 0 → 0, 2,500 → 5, 5,000 → 12, 7,500 → 20 and 10,000 → 25. More than 10,000 pays 25. | Oura has no step count for the day |
| Exercise | 35 | The day's counted exercise, in tiers: under 10 minutes 0, 10–19 minutes 8, 20–29 minutes 15, 30–44 minutes 25, 45 minutes or more 35. | No workout counts |
| Sleep | 20 | Oura's sleep score on a straight line through 50 → 0, 60 → 5, 70 → 10, 80 → 15 and 85 → 20. 50 or less pays 0, 85 or more pays 20. | Oura has no sleep score for the day |
| Readiness | 0 | Never pays. It measures recovery, and a hard workout can lower it. | Always |

Some worked examples:

- 6,000 steps pay 15 (15.2), and 3,750 pay 9 (8.5, halves up).
- A sleep score of 75 pays 13 (12.5), and 82 pays 17.
- Two runs of 12 and 20 minutes make 32 minutes, which pays 25.

Each part works like this:

- **Exercise counts per Oura day, not per workout.**
  - It adds up the day's counted workouts to the second, merging any times where they
    overlap. 10 minutes means 600 seconds.
  - So splitting a workout in two pays no more, and two overlapping records count once.
  - Only the duration counts: not calories, intensity, heart rate or the kind of exercise.
  - What counts is §3.4's rule, unchanged: recorded by Chris, never a walk, never detected.
- **Missing isn't zero.**
  - A day with no step count, or no sleep score, pays nothing for that part. It shows as
    missing, never as 0.
  - A day where Oura measured 0 steps also pays 0.
- **No penalties.** A low day pays less, never less than 0.
- **XP from 2026-10-22** (`ActivityRules.XpFrom`, question 7). Earlier days are shown but
  never pay. It's a rules value like the others, so a retune or a test can move it.
- **Oura's Activity Score never pays.** It overlaps steps and workouts.
- **A drift test** keeps the numbers honest:
  - the parts add up to 80
  - the curves and tiers rise
  - the currency is a life domain
  - the worked examples still hold
- **A TypeScript port** (`lib/activity-score.ts`) is tested on the same table, so the mock
  and the page's projections pay what the kernel would.

**On the ledger (A8).** Each day has three award keys. None belongs to an item, like the
Food Log's `diet:` and `fast:` (food-log plan §3.3):

| Key | Due | Dated at | Cites |
|---|---|---|---|
| `steps:<day>` | Once Oura's day is over, at 4 a.m. the next morning in the home zone. Oura's day runs from 4 a.m. to 4 a.m. local. | When LifeOS last stored the day's steps | The day's activity event |
| `exercise:<day>` | The same | The last counted workout's end | The counted workouts' events |
| `sleep:<day>` | Once the day has a sleep score | The night's wake time, or else when LifeOS stored the score | The day's sleep-score event |

The "Dated at" column is what A8 built. The first plan dated steps and exercise at the day's
end; see A8 under As built.

- **The wallet's opening.** Activity never opens the wallet. It pays the Oura days from the
  one the wallet opened on, whole: a night that ended, or a run that finished, earlier that
  morning pays like the rest of the day, dated at the opening instead of its trigger, so no
  earn predates the wallet. A day before the opening never pays, whenever it syncs. (A8
  judged by the trigger's instant; see the fix after A11.)
- **Payment and credit.** Each award pays Health XP and is credited to its Oura day
  (`credited_on`).
- **The multiplier** is that day's, as it stands when the award is written. `base` and
  `multipliers` are stamped on it (xp-domains plan).
- **Re-priced like the Food Log's:**
  - A paid key that still reads the event versions it was priced from is left alone, so a
    retune never rewrites a past day.
  - When Oura revises a day, the key's net is reversed and the new base earned, in one
    batch. Revisions include more steps synced after 4 a.m., a workout added, edited,
    deleted or restored, and a revised sleep score.
  - A key whose day no longer pays, such as a deleted day's only workout, is reversed.
- **Settling:** on every Activity page sync, even one the throttle skips, so a day that
  ended at 4 a.m. pays when the page next opens. Also after `bsk oura sync`, from
  `POST /api/domains/settle` (the Character page), and in `bsk rewards reconcile`. Award keys
  are unique, so a repeated sync or settle never pays twice.
- **Toasts** are grouped as the Food Log's are, for example "👟 Steps Thu Oct 22: +20
  Health XP".

**On the page (A9).** The Steps, Exercise and Sleep cards each show the day's XP:

- **Paid:** read from the ledger, multiplier included.
- **Projected:** worked out by `lib/activity-score.ts` from the day so far, and labelled as
  projected, never as earned.

The Today card adds the three up. Nothing else on the page changes.

**The questions, answered (2026-10-22):**

1. **Activity's share of the successful day:** activity adds to the day rather than
   raising the bar, and **the bar comes down from 70 to 60** (settled 2026-10-22, see the
   calibration below).
2. **The steps curve:**
   - The straight line above, with partial pay under 5,000 and no penalty.
   - It tops out at 10,000 with 25.
3. **When steps pay:** once Oura's day is over. Until then they're projected.
4. **Workouts:**
   - They pay on the day's counted duration, in tiers, up to 35.
   - A manual workout is still on Chris's honor (GEN-5).
5. **Sleep and readiness:** sleep pays on the score. Readiness never pays.
6. **Double counting:**
   - No item pays Health XP today, on staging or dev, so nothing pays twice.
   - **One to watch:** the staging habit "Gym three times a week" has no bounty. Giving it
     a Health XP bounty would pay twice with exercise.
   - LifeOS never changes a habit's bounty itself.
   - Letting Oura tick off a habit stays in §6.
7. **History:** settled, XP from 2026-10-22.
8. **Other rewards:**
   - Health XP only. Activity pays no Gems, Freedom or Life XP of its own.
   - Health's rank-up Gems still come as Health ranks up.
9. **A background sync:**
   - Not yet. XP lands when a sync brings the data in.
   - The Activity page syncs when it opens (throttled), and A9 has the Food Log and
     Character pages ask for the same throttled sync when Oura is connected.
   - Add a scheduled sync (§6) if days still wait to pay.
10. **Days with the ring off:**
    - They're not excused. A part with no data pays nothing, and the consistency rules
      don't change.
    - **This is a known limit:** a day with the ring off can fall short of the 60-XP bar
      and count as missed.

**Calibration (2026-10-22).** The planning notes aim for about 100 paid Health XP a day and
Level 100 in about 7 years for a healthy routine. Chris's notes ask to report a gap rather
than change the level curve or the rewards.

The simulation used the Health rules as built: the curve, the ranks, a 70-XP successful day
and the multiplier. Its routine was close to the planning notes' example:

- food at 35 a day, logged 95% of days
- steps around 7,500, with a spread of 2,000
- a sleep score around 80, with a spread of 6
- the ring worn 97% of days
- four workouts a week of about 36 minutes, nine in ten done

Each case below is the median of 20 runs over 10 years:

| Routine | Base a day | Successful days | Paid a day | Level at 10 years | Grandmaster |
|---|---:|---:|---:|---:|---|
| The routine above | 78.5 | 70% | 91 | 59 | never |
| Food at the Food Log's good day (31) | 74.8 | 60% | 83 | 49 | never |
| No regular exercise | 66.5 | 47% | 69 | 39 | never |
| Lower adherence: food 80% at 28, 6,000 steps, sleep 75, 2 workouts | 54.6 | 19% | 55 | 19 | never |
| High activity: food 37, 11,000 steps, sleep 84, 6 workouts of 45+ | 101.2 | 95% | 132 | 100 | 5.3 years |
| The routine plus 20 a day of Health habits | 98.5 | 94% | 128 | 100 | 5.5 years |

`bsk domains simulate --avg 78.5 --sd 14 --off 3%` agrees: 68% successful days, level 59,
and never past Elite.

**What the simulation found.** The economy runs short of the target, not over it.

- The reason is the bar, not the curve.
- A good day without exercise adds up to about 70: food 35, steps around 19, and sleep
  around 15. So roughly half of those days miss the bar.
- At 70% successful days, the consistency bars stop the ranks at Veteran, and the level cap
  holds the levels with them.
- The planning notes' food average of 35 is also optimistic. The Food Log's own good day
  pays 31, and an ordinary day pays 15.

The same routine at other bars:

| Successful day | Successful days | Paid a day | Level at 10 years | Grandmaster |
|---|---:|---:|---:|---|
| 70 base XP (now) | 70% | 91 | 59 | never |
| 65 | 82% | 97 | 89 | never |
| 60 | 89% | 101 | 100 | 7.5 years |

**The recommendation is 60.** It's a one-line change to `LifeDomains.Health`, plus the
simulator's regression bands.

- It's easy to undo: ranks and successful days are re-read from history, and only an
  issued multiplier stays as it was.
- After a month of real days, `bsk domains simulate --replay` can confirm the choice or
  move it.
- **The bar is Chris's call.** A7 didn't change it.

**Settled (2026-10-22):** Chris said "Lower the Health successful-day bar to 60 base XP".
It was lowered after A7, ahead of A8 (xp-domains plan §2.4). The simulator's regression
scenarios are now set against the bar, so they keep their meaning when it moves.

### 3.9 Tags from the Oura app (A10, ACTIVITY-6)

Chris asked whether the API has the tags he adds in the Oura app ("I can apply a tag to a
specific time which adds metadata that can correlate to the automatic health data"), then:
"Yes I'd like to support recording tags too. Please implement" (2026-10-22).

**What Oura gives** (spec 1.42, §7):

- `GET /v2/usercollection/enhanced_tag`, under the `tag` scope ("User entered tags"). It
  takes `start_date` / `end_date` and pages through `next_token`, like the other
  collections.
- Each tag has:
  - `id`
  - `tag_type_code`: the tag Chris picked, `custom` for one he named himself, or null for a
    text-only tag
  - `custom_name`: the name of a custom tag
  - `start_time` and `start_day`: when it happened, or when it started if it has a duration
  - `end_time` and `end_day`: when it ended, or null without a duration
  - `comment`: his free text on it
- The older `/v2/usercollection/tag` is deprecated in favour of Enhanced Tag, so LifeOS
  doesn't read it.
- Oura doesn't publish its list of tag codes. The sandbox answers `generic_happy` and
  `generic_excited`.

**Decided here, because one option was clearly better:**

- **The scope.** LifeOS asks for `daily workout tag`.
  - The connection made before A10 was granted `daily workout`, so the page asks to
    reconnect ("Oura didn't allow tags"). Until then, the sync reads the other five
    collections as before and skips tags. Nothing breaks, and no sync fails for it.
  - Tags are read only when the stored grant includes `tag`. A token without the scope is
    never sent to the collection, so a refusal can't be mistaken for a bad token.
- **Their history.** A connect that grants a scope the stored grant didn't have starts the
  history backfill over, so the tags' past comes in. The collections LifeOS already holds
  are read again and write nothing new.
- **Storage (D14), as for workouts:**
  - Each tag is an `observation` event: something Chris noted about himself. Provenance
    `observed`, source `oura`, no subject and no edge.
  - It's keyed by Oura's id and versioned by content. A tag deleted in the Oura app, on a
    day in the window, gets a voided version; one that comes back is restored.
  - `occurred_at` is its start, and its day is Oura's `start_day`.
  - The payload:

    ```json
    { "oura_tag": { "id": "<oura id>", "day": "2026-10-21", "end_day": null,
        "type_code": "generic_happy", "custom_name": null,
        "start": "2026-10-21T20:30:00-07:00", "end": null,
        "comment": "Good day at work" } }
    ```
- **Migration 0039:** `bsk.v_oura_tag`, one row per live tag: `tag_id`, `day`, `end_day`,
  `type_code`, `custom_name`, `started_at`, `ended_at`, `comment`, `synced_at`,
  `event_id`. It has a partial index on the id and is granted to `bsk_reader`.
- **Names** (`OuraTags.Name`, Domain):
  - A custom tag reads as its own name, or "Custom tag" without one.
  - A text-only tag reads as "Note", and its comment carries it.
  - Otherwise the code is made readable: Oura's `tag_` and `generic_` prefixes come off,
    words split on `_` and on capitals, in sentence case. So `tag_generic_late_meal` reads
    "Late meal" and `generic_happy` reads "Happy".
  - Since Oura's codes aren't published, they're made readable rather than looked up. A
    code that reads badly can be given its own wording when one turns up.
  - The API answers the name, so the CLI and the page never word a tag differently.
- **Which day:** Oura's `start_day`. A tag at 9 p.m. sits on that day, and the night after
  it on the next day's sheet, as Oura puts nights on the morning they end.
- **Not GEN-1 tags.** GEN-1's tags label items in `bsk.item_tag`. Oura's are moments in a
  day, and they don't become LifeOS tags.
- **Comments** are Chris's own words, kept as Oura has them. Like the rest of Oura's data,
  they never go to an AI model, and the purge (§7) covers them, since they're `oura`
  events.
- **No XP.** Tags pay nothing, and the activity rules don't read them.

**Reads, API and CLI:**

| Where | What |
|---|---|
| `ActivityReads.GetTagsAsync(from, to)` | Live tags starting on those days, newest first. |
| `GET /api/activity/tags?from&to` | `{ from, to, today, tags }`. With no range it's the 30 days through today, at most 731 days. |
| `GET /api/activity/day?date=` | Adds `tags`: the day's tags, earliest first. |
| `bsk oura tags [--days N] [--json]` | The tags by day, newest first. |

Each tag row has `id`, `day`, `endDay`, `typeCode`, `customName`, `name`, `startedAt`,
`endedAt`, `seconds` (its length, or null without one), `comment` and `syncedAt`.

**The page:**

- **A Tags card**, in the left column under Steps:
  - the range: 30 days, 90 days or 1 year, remembered in the browser
  - how often each tag came up in the range ("Caffeine 12 · Alcohol 5"), most used first
  - the tags newest first, grouped by day. Each row shows the name, the time (or start to
    end, with the length) and the comment on its own line. A day's heading opens the day
    sheet.
  - Before reconnecting, it says that tags need the reconnect.
- **The day sheet** gets a Tags section with the day's tags, so a tag sits beside that day's
  sleep, readiness, steps and workouts.
- **The missing-scope banner** names scopes in words ("Oura didn't allow tags").
- **The mock store** seeds tags over its 120 days: a daily coffee, evening drinks, a sauna
  with a length, a custom tag, a text-only note, a few days of being sick, and comments on
  some.

**Later (§6):** reading a tag against the night after it, for example the average sleep
score after nights tagged Alcohol against the rest.

## 4. Phases

Each phase is one commit:

- a migration where needed, the `bsk` verb, views, and tests (`./test.ps1`), with a clean
  strict build
- web phases go mock-first, then live, with `npm run gen:api` after API changes, plus
  `npm run test:e2e` and `npm run a11y` (both themes)
- each phase updates the Build fields of the ACTIVITY requirements it advances

**Phase 1 (read-only):**

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| A0 | Requirements + plan | ACTIVITY-1 to ACTIVITY-5, D14, the backlog line, this doc. | — |
| A1 | Oura connection | §3.1. Migration 0036 (`bsk_ops.integration`); the Oura settings, `IOuraClient` + `OuraHttpClient` (OAuth calls), token encryption, refresh under a row lock; the `/api/oura` endpoints; `bsk oura status`. Tests: malformed settings fail startup and secrets never print; tokens round-trip encrypted; a wrong or expired state is refused; two refreshers spend a refresh token once; `invalid_grant` marks the connection for reconnecting; `bsk_reader` can't read `bsk_ops`. | — |
| A2 | Sync and storage | `OuraSyncService` for the five collections, `KernelSources.Oura`, the events in §3.2, `ExerciseRules` and `ActivityTargets` with their table tests, `WorkoutNoteService`. Migration 0037 (the five views). `bsk oura sync / steps / workouts / note / sleep`. Note tests: set, change and clear (the newest wins); a re-sync leaves the note alone; a deleted and restored workout keeps it; over 140 characters, a line break, or an unknown workout is refused; it stays out of the inbox and the captures. Tests on recorded fixtures: paging through `next_token`; the widened window filtered on `day`; an unchanged re-sync writes nothing; today's growing steps write versions; the newest wins; a deleted workout or sleep period is voided and can come back; A → B → A records; two racing syncs write once; the night is the longest `long_sleep`, and naps, `rest` and `deleted` are handled; walks and detections aren't counted; unknown activity, source and type values are kept; the events stay out of the inbox, timeline, diagnostics and ledger. | A1 |
| A3 | API | `ActivityReads` / `ActivityReader`, the §3.6 endpoints including the note, the page-open throttle, `gen:api`. Tests: shapes, the range limit, the throttle, `counted` and its reasons, the note's refusals as 400s, Oura's errors as clear messages. | A2 |
| A4 | Activity page: steps and exercise | §3.7's header, connect card and return route, the Today card's steps and workouts, the shared daily-metric chart, the Steps card, the Exercise card with its workout sheet and note; `lib/exercise-rules.ts`; the mock store's activity half, notes included. Vitest, e2e, a11y in both themes and on a phone. | A3 (mock-first can start after A2) |
| A5 | Activity page: sleep and readiness | §3.7's Sleep and readiness card, the Today card's last night, the day detail sheet. Vitest, e2e, a11y. | A4 |
| A6 | Live on staging | `fly-setup.ps1` imports the Oura settings; Chris connects on staging; a real refresh on day one; the history backfills; a week of comparing the page with the Oura app, imported workouts included if there are any. | A5 |

**Phase 2 (activity XP), settled 2026-10-22 (§3.8):**

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| A7 | Rules | §3.8's answers and calibration in this plan. `ActivityRules` (steps, exercise, sleep, `XpFrom`) with its drift test and the worked examples; `lib/activity-score.ts` tested on the same table. No migration and no ledger change. | A5 (A6's week of comparing runs alongside) |
| A8 | XP on the ledger | `ActivityAwards` (`steps:`, `exercise:`, `sleep:`) and `ActivityRewards`, settling after a sync, in `POST /api/domains/settle` and in `bsk rewards reconcile`, re-pricing on the cited versions as `FoodRewards` does; rank-ups and `progress` on the sync's answer. Tests: each key's due time and date; credited to the Oura day at its multiplier, the base kept; nothing before `XpFrom`; a repeated sync pays once; more steps after 4 a.m., a workout added, edited, deleted and restored, and a revised sleep score each re-price; a retune leaves paid days alone; readiness alone pays nothing; missing steps or a missing score pay nothing. | A7 |
| A9 | XP on the page | Paid and projected XP on the Steps, Exercise and Sleep cards, and the Today card's total; the sync's earn toasts; the Food Log and Character pages ask for the throttled sync when Oura is connected; the mock store pays as the kernel does. Vitest, e2e, a11y. | A8 |

- **A1 and A2 are kernel only.** From A2, `bsk oura sync` works end to end, so the numbers
  can be checked from the CLI before the page exists.
**Tags (added 2026-10-22, §3.9):**

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| A10 | Tags | §3.9. The `tag` scope, the history read again after a connect that adds a scope, `enhanced_tag` in the client and the sync, `OuraTags`, migration 0039 (`v_oura_tag`), `GET /api/activity/tags` and the day's tags, `bsk oura tags`, the Tags card and the day sheet's tags, the mock's tags. Tests: the client's fields and paging; tags read only with the scope; versions, voiding and restoring; the history read again after a new scope; names; the API's shapes and range; the events kept out of every view about items; Vitest, e2e, a11y. | A9 |

**Page polish (added 2026-10-23):**

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| A11 | Banner, titles, a day at a time | §3.7's banner, the cards' titles and colours, and the Today card's arrows. Web only: no API change and no migration. Vitest, e2e, a11y in both themes and on a phone. | A10 |

- **A4 and A5 touch the same page,** so they run one after the other.
- **Migrations** continue from 0036. Merging to `main` promotes them to staging through CI.

## 5. How this design was settled

**Chris's brief (2026-10-22)**, in his words:

- "Pull Oura ring data into my app, and add a page for showing health related stuff from
  Oura ring, including workouts and steps."
- "Pull in the actual Oura data and summarize in an Activity page. Steps is a key indicator
  that I want to actually show a trend chart for. Logged exercise is another key thing to
  record and track also. This is read only. Assumption: All of my 'exercise' activity will
  be logged through the Oura app, so it should show up in the API."
- "Assigning health XP (or other rewards) automatically based on the data. I don't know
  formulas yet, but this will come next."

**The three questions (2026-10-22):**

1. **Oura Membership:** yes, active.
2. **What counts as exercise:** "Only workouts I record explicitly." This was stricter than
   the recommendation, which would also have counted confirmed detections. Chris also said
   walks shouldn't count: "That's a good point about a walking workout double counting
   steps. I have been counting walks with an actual 'workout', but I think it makes sense to
   only log Oura exercises that are beyond just walking… like jogging, running, or other
   cardio exercises."
   - So `walking` is excluded by rule, which also covers the walks he logged in the past.
   - A run Oura detected and Chris only confirmed doesn't count. If that turns out to lose
     real exercise, counting `confirmed` is a one-line change (§3.4).
3. **Step goal:** "Daily ideal step goal should be 7500. More is better. But minimum should
   be 5000. Less is worse. I'm not sure about Life XP yet, but those are my 2 threshold
   levels." The XP side is phase 2's question 2.

**Added to phase 1:** "I forgot about sleep altogether!!! It's important. I want to track
sleep score also, and readiness. Let's add it into phase 1."

- In planning, time asleep and bed and wake times were added beside the two scores. A score
  alone doesn't say how long the night was, and both come from the same sync.

**Added to phase 1:** "I would like 1 more feature, a quick text note for each workout that
is optional. Oura doesn't have this, but I'd like to be able to optionally add a 1 line
note in the LifeOS activity page."

- It's LifeOS's own `note` event, keyed by the workout's Oura id, so syncs never touch it
  (§3.2).
- "One line" became up to 140 characters with no line breaks.

**Decided in planning, because one option was clearly better:**

- **OAuth.** Personal tokens no longer exist.
- **Oura's day.** Steps can't be re-cut to midnight, and a night belongs to its morning.
- **Events, versioned by content, not an overwritten table.** It's the kernel's model for
  external sources, and phase 2's re-pricing needs the versions.
- **Sync when the page opens, plus Sync now.** No background job until phase 2 shows it
  needs one.
- **All history on first connect.** It's a few MB. Whether past days pay XP is phase 2's
  question 7.

**XP starts on 2026-10-22 (settled at the start of A6).** Chris asked: "Do A6 next. But I would like to put in a cut-off begin date that only starts today, because today is when I started logging food, and I want to start using both at the same time to avoid half life XP contributions missing."
That was first read as a cut-off on reading Oura, and built that way. He then said: "I should clarify my earlier comment about starting Oura data today. I meant I don't want Life XP to be awarded before today. You can still collect data from before today and display it."

- So all history is still read and shown, as above.
- XP from activity, when phase 2 builds it, starts on 2026-10-22, so activity and food feed
  Health XP from the same day (§3.8, question 7).
- **The night is the longest `long_sleep`.** Naps add up separately.
- **Oura's own score bands** (70 and 85) on the sleep and readiness charts, since Chris
  hasn't set his own.

**Phase 2's rules (2026-10-22).** Chris's planning agent wrote up the decisions for
activity XP, and Chris said: "Let's work on the A6+ (Health XP scoring) in the meantime. I
am pasting a generated prompt from my planning agent that summarizes the decisions for
Activity rewards. … Use these decisions as input to the remaining phase 2 content."

- **Taken as decided:**
  - the three formulas and their caps
  - readiness and Oura's Activity Score paying nothing
  - Health XP only, with no new balance
  - steps and exercise paying once Oura's day is over, and sleep once it has a score
  - missing data not counting as zero
  - ring-off days not excused, and written down as a limit
  - overlaps with habits reported, not fixed
  - the rules in one place, with a TypeScript port
  - a calibration that reports a gap rather than changing the curve
- **Filled in here, from how LifeOS already works:**
  - the award keys' dating and re-pricing (the Food Log's, D13)
  - exercise counted to the second, with overlaps merged
  - Health pages asking for a sync, rather than a background job
- **Came back as a question:** the calibration found the 70-XP bar holds a healthy routine
  well short of Level 100 (§3.8). Chris chose 60: "Lower the Health successful-day bar to
  60 base XP, then start A8."

## 6. Later

Each of these has to earn its place through use.

- **More Oura data on the page:** heart rate, stress, resilience, SpO2, VO2 max or
  cardiovascular age.
- **A sleep target:** hours, or a bedtime.
- **Oura's webhooks**, for near-real-time sync:
  - They need a public endpoint on Fly, signature checks, subscriptions renewed before they
    expire, and one subscription per data type and event.
  - They send ids only, so the poll stays as the reconcile.
- **A scheduled sync.**
- **A workout's heart-rate curve**, or a night's hypnogram.
- **Activity by time of day**, from the 5-minute classes.
- **Other sources** (Apple Health, Strava) through the same `source_id` pattern.
- **A step goal that adapts** to recent weeks.
- **Exercise as evidence for a Habit:** an Oura run checking off a "Run 3× a week" habit.
- **A tag against the night after it** (§3.9): the sleep score or readiness after days with
  a tag, against days without it.

## 7. Oura API notes

Checked 2026-10-22. Facts from Oura's docs unless marked otherwise.

- **Docs and spec:** [API v2 docs](https://cloud.ouraring.com/v2/docs), which load OpenAPI
  spec 1.42 (`/v2/static/json/openapi-1.42.json`). The file name changes with each version,
  and old ones are removed. There's no official SDK.
- **OAuth** ([authentication](https://cloud.ouraring.com/docs/authentication)):
  - Authorize at `https://cloud.ouraring.com/oauth/authorize`. The new portal's
    `https://developer.ouraring.com/authorize` redirects there, adding `prompt=consent` and
    `acr_values=urn:se:curity:authentication:username:default_ext` (seen 2026-10-22).
    LifeOS signs in through the portal's URL (A6).
  - Get and refresh tokens at `https://api.ouraring.com/oauth/token`.
  - Revoke at `https://api.ouraring.com/oauth/revoke?access_token=…`.
  - PKCE (S256) is supported. Codes last 10 minutes. Client credentials go in the body or in
    HTTP Basic auth.
  - The client-side flow has no refresh token, so use the server-side flow
    (`response_type=code`).
  - Refresh tokens are single-use, and their lifetime isn't documented.
  - **[third-party]** Apps made in the new portal may need
    `https://moi.ouraring.com/oauth/v2/ext/oauth-token` to refresh.
- **Scopes:** `email personal daily heartrate workout tag session spo2`, plus `heart_health`
  (spec 1.42).
  - Leaving `scope` blank asks for all of them, and the user can untick any.
  - **[third-party]** Since about September 2026, granted scopes come back as
    `extapi:<scope>`.
- **Personal access tokens:** "deprecated in December 2025 and are no longer available for
  use" (spec 1.42).
- **Membership:** "Gen3 and later users without active Oura Membership can't access their
  data through the Oura API"
  ([support](https://support.ouraring.com/hc/en-us/articles/4415266939155-The-Oura-API)).
  Without one, the API answers 403.
- **Collections:** `GET /v2/usercollection/daily_activity`, `/workout`, `/daily_sleep`,
  `/daily_readiness` and `/sleep`, and from A10 `/enhanced_tag` (§3.9).
  - They take `start_date` / `end_date` and answer `{ data, next_token }`. Paging continues
    until `next_token` is null.
  - **[third-party, measured 2026-08]** `daily_activity`, `sleep` and `workout` exclude the
    end date, while `daily_sleep` and `daily_readiness` include it. `workout` filters by UTC
    date but reports a local `day`.
  - The sandbox, `/v2/sandbox/usercollection/…`, works with any non-empty `Authorization`
    header. It's useful for JSON shapes, not for date behaviour.
- **Days:**
  - Activity: "a 24-hour period starting at 4 a.m."
    ([how Oura measures steps](https://support.ouraring.com/hc/en-us/articles/360025576833-How-Oura-Measures-Steps-Activity)).
  - Sleep: a `late_nap` "ended after sleep day change (6 pm), contributes to next days daily
    scores" (spec 1.42, `PublicSleepType`).
  - Sleep and readiness "only sync when the user opens the Oura app" (spec 1.42, FAQ).
- **Workouts:**
  - Calories are active calories only.
  - When the same workout comes from several places, Oura shows it once, taking manual
    first, then third-party, then detected
    ([automatic activity detection](https://support.ouraring.com/hc/en-us/articles/360063022993-Automatic-Activity-Detection)).
  - Strava imports cover the current day only.
- **Errors** ([error handling](https://cloud.ouraring.com/docs/error-handling)):
  - 401: the token is expired, revoked or malformed.
  - 403: the membership has lapsed, or a scope is missing.
  - 429: rate limited, with `Retry-After`.
  - OAuth errors add `error`, such as `invalid_grant` for a spent refresh token.
- **Webhooks:**
  - `/v2/webhook/subscription`, using the app's `x-client-id` and `x-client-secret`.
  - A GET challenge verifies the endpoint. Events are HMAC-SHA256 signed and carry ids only.
  - Oura retries for about an hour.
  - Subscriptions expire and are renewed (**[third-party]** 90 days).
  - It isn't documented whether an unapproved app can use them.
- **API agreement** ([agreement](https://cloud.ouraring.com/legal/api-agreement)), in effect
  since 2026-06-08. Read for A2 (2026-10-22); in summary, with what LifeOS does:
  - **Keeping data:** data from the API may not be stored or kept longer than strictly
    necessary, and only what each request needs should be asked for. LifeOS keeps the
    history, because the page's trends and phase 2's XP read it. It trims each record to
    what it reads and leaves Oura's series in Oura. For one person's own data in his own
    app, the risk was accepted (D14).
  - **Deleting on request:** a user can ask for their data to be deleted at any time, and it
    must be gone within 72 hours, with nothing kept afterwards. The same applies when the
    agreement ends. LifeOS's event store is append-only (UPDATE and DELETE are refused by
    trigger), so honouring a request takes a deliberate purge, run as the database's owner,
    of the `oura` events and the workout notes. **Not built yet:** a `bsk oura forget`
    command is the follow-up, best done before A6 puts real data on staging.
  - **Consent:** revoking consent stops collection at once. Disconnect does that: it revokes
    the token and nothing calls Oura afterwards.
  - **AI:** data from the API may not be given to any AI model or AI platform, including as
    a prompt; LLM use goes through Oura's own MCP server instead. LifeOS has no AI features.
    Two consequences:
    - Phase 2 and anything later must keep Oura data out of any LLM feature.
    - Agents building LifeOS work against Oura's sandbox and recorded fixtures, not Chris's
      real data. In A6, Chris compares the page with the Oura app himself.
  - **Privacy policy:** an app must explain how it collects, keeps and deletes data. For a
    single-user app this is the plan and this section.
  - **Revocation:** Oura can suspend an app's access at its discretion.

## Status

- [x] A0 — Requirements (ACTIVITY-1 to ACTIVITY-5, D14) + this plan
- [x] A1 — Oura connection (migration 0036)
- [x] A2 — Sync and storage (migration 0037)
- [x] A3 — API
- [x] A4 — Activity page: steps and exercise
- [x] A5 — Activity page: sleep and readiness
- [ ] A6 — Live on staging
- [x] A7 — Phase 2 rules (`ActivityRules` and its TypeScript port)
- [x] A8 — Activity XP on the ledger
- [x] A9 — Activity XP on the page
- [x] A10 — Tags from the Oura app (migration 0039)
- [x] A11 — Banner, card titles, and the Today card a day at a time

### As built

**A1 (2026-10-22).** As planned, plus:

- **The connection row has three more columns:** `pending_redirect_uri` (the code exchange
  must repeat it), `reconnect_reason` (what the page says) and `updated_at`. Checks keep the
  two tokens and their expiry together, and the four pending fields together.
- **The page sends its own redirect URI** (its origin plus `/activity/connected`), so dev
  and staging each connect with their own registration. LifeOS accepts only that path, over
  https (http only on localhost), with no query; Oura checks it against the registered list.
- **A state is good once.** Taking it clears it, even when it has expired or Oura then
  refuses the code.
- **Which refusals ask to reconnect:** `invalid_grant`, or a 401 that isn't about the client.
  `invalid_client` and other 400s leave the tokens alone, since the refresh token wasn't
  spent. A 429, a 5xx, a timeout or no connection leaves everything as it was.
- **Tokens that won't open under this host's key** (the key changed) mark the connection for
  reconnecting too, in words that name `Integrations:TokenKey`.
- **Oura's endpoints are settings** (`Oura:AuthorizeUrl`, `Oura:TokenUrl`, `Oura:RevokeUrl`,
  and `Oura:RefreshUrl`, which defaults to the token URL). If refreshing fails on the first
  day of A6, as the third-party report suggests it might for a new-portal app, the refresh
  moves to `moi.ouraring.com` with a Fly secret and no release.
- **Status codes:** Oura not set up, not connected, needing to reconnect, or refusing a
  sign-in is a 409 in words; Oura down is a 502; a malformed request is a 400.
- **A host without Oura still runs:** no `Oura:ClientId` answers `not_configured`. A client
  id without `Integrations:TokenKey`, or a malformed setting, fails startup.
- **The CLI reads `Oura__*` without regard to case**, so `Oura__ClientID` works.
- **`bsk oura refresh`** was added beside `bsk oura status`, so A6 can try a real refresh
  from the command line.
- **Left for later phases:** the first sync after connecting (A2 adds it to
  `POST /api/oura/connect`), the web's typed client (regenerated in A3 with the activity
  endpoints), and `fly-setup.ps1` importing the three secrets (A6).
- **Tests:**
  - the settings, and that no secret prints
  - sealing round-trips, and refuses another column, another key or a changed byte
  - what the HTTP client sends, and how each refusal and outage surfaces
  - against Postgres: connecting, a wrong, used or expired state, PKCE, refreshing when
    stale, two racing refreshers spending the token once, a spent grant asking to
    reconnect, an outage keeping the tokens, a changed key, and disconnecting while Oura is
    down
  - the endpoints, including startup failing without a token key
  - `bsk_reader` refused on `bsk_ops.integration`

**A2 (2026-10-22).** As planned, plus:

- **Migration 0037** also adds `history_done` to the connection row, and two helpers,
  `bsk.json_int` and `bsk.json_num`, that read a jsonb number (anything else reads as null).
  Each view has a partial index on its payload key.
- **Versions are stamped with `clock_timestamp()`**, under the sync lock, not with the
  transaction's start. A sync that waited for the lock can't stamp its versions older than
  the ones it waited on.
- **The hash leaves out Oura's id for a daily record.** Those ids aren't documented as
  stable, and a new id alone isn't worth a version. The id is still kept in the payload.
- **`occurred_at`:** a daily record's Oura timestamp (4 a.m. for activity), or midnight UTC
  of its day when there's none; a workout's or sleep period's start. A voided version keeps
  the date of the version it replaces.
- **The window** is the last 14 days through tomorrow, so a night that already counts for
  tomorrow (a `late_nap` after 6 p.m.) isn't missed. Each request reads two days more on
  each side.
- **What's withdrawn:** a workout or sleep period whose newest version is on a day in the
  window and that Oura's answer, padding included, no longer has. A record held on a day
  outside the window isn't touched, so a recent sync never withdraws history.
- **History:** after the window, each sync reads back 180 days at a time, four windows per
  sync by default; `bsk oura sync` reads until it's done. It stops at a window with nothing
  in any collection, or at 2015-01-01. A gap of more than 180 days without the ring would
  stop it early; `bsk oura sync --from <date>` reads further back. Where it got to is on the
  connection (`history_from`, `history_done`) and in `GET /api/oura/status`.
- **One window, one transaction:** each window is read and written under the advisory lock,
  so a second sync waits, then reads Oura itself and finds nothing new. What earlier windows
  wrote stays if a later one fails.
- **A token Oura refuses (401)** is refreshed once per sync and the read tried again. A
  second refusal marks the connection for reconnecting. A 403 (membership or scope) or an
  outage is kept as the last sync error and leaves the connection as it was.
- **`POST /api/oura/connect` runs the first sync** (`OuraSyncService.ConnectAsync`). A first
  sync that fails doesn't undo the connection; the status carries its error.
- **Workout notes** can't hold a line break or any other control character, and are trimmed.
  A note the same as the current one writes nothing. A workout is named by its Oura id, or
  by 8 or more characters from its start.
- **`ActivityReads`** answers the CLI now and the API from A3: days, workouts (all, or only
  those that count) and sleep with readiness, up to 731 days at a time.
- **CLI:** `bsk oura sync [--from]`, `steps [--days]`, `workouts [--days] [--all]`,
  `sleep [--days]` and `note <workout> "text" | --clear`, each with `--json`.
  `bsk oura status` adds the history line.
- **The API agreement was read** (§7). What it means for LifeOS: a purge for deletion
  requests is still to build, and Oura data never goes to an AI model.
- **Tests:**
  - the HTTP client against fixtures recorded from Oura's sandbox
    (`tests/LifeOs.Tests/OuraFixtures`): the request, the bearer token, every field read,
    paging through `next_token` and stopping on a repeated token, records without an id or
    day skipped, unknown values kept, 401, 403, 429 and outages
  - table tests for `ExerciseRules` and `ActivityTargets`; the payloads' JSON and hash, the
    `external_id`, and which collections can be withdrawn
  - against Postgres, each on a database of its own: the first sync's events and views, the
    widened window, an unchanged re-sync and a new Oura id writing nothing, growing steps,
    A → B → A, a deleted workout voided and restored while daily records stay, nothing
    withdrawn outside the window, two racing syncs writing once, the night and naps with
    `rest`, `deleted` and an unknown type, walks and detections not counted, the history
    backfill and its floor, `--from`, a refused token refreshed once, reconnecting after a
    second refusal, Oura down or refusing access, a sync with no connection, connect running
    the first sync, and the events and notes staying out of the inbox, the timeline, the
    ledger, the timers, the food views and the diagnostics while `bsk_reader` reads every
    new view
  - notes: set, changed and cleared with the newest winning, re-syncs leaving them alone, a
    deleted workout refusing one and bringing its note back with it, and the refusals

**A3 (2026-10-22).** The §3.6 endpoints under `/api/activity`, on `ActivityReader` (the
reader connection), with no migration. As planned, plus:

- **`GET /days?from&to`** answers every day of the range, oldest first, with nulls where
  LifeOS holds nothing, so a chart shows the gap. Each row has the steps and their band, the
  calories, high, medium and low activity time, non-wear time, Oura's activity score, the
  sleep and readiness scores, time asleep and in bed, bed and wake times, and the naps
  (`napCount`, `napAsleepS`). The thresholds come with the days. With no range it's the 30
  days through today; more than 731 days, or a range running backwards, is a 400 in words.
- **`GET /workouts?from&to`** answers every live workout, counted or not, newest first, each
  with `counted` and its `reason`. With no range it's the last 12 whole weeks, Monday to
  Sunday, so the Exercise card's weekly minutes need one call.
- **`POST /workouts/{id}/note`** takes the workout's whole Oura id (the CLI's short ids aren't
  taken here) and `{ text }`, which is required: `""` clears the note, and a body without
  `text` is a 400, so a page bug can't clear a note by accident. A note LifeOS refuses (a line
  break, over 140 characters) is a 400 in words; a workout it doesn't hold is a 404.
- **`GET /day?date=`** (default today): the day's row, both scores' contributors by Oura's
  names, the temperature deviation and trend, the night with its stages and heart rate, every
  other period as a nap, and the day's workouts, earliest first. The night is the period
  `v_sleep_day` took its bed and wake times from. A new read, `GetSleepPeriodsAsync` over
  `v_sleep_period`, serves it.
- **`GET /summary`:** today so far — its steps, and last night's sleep and readiness, which
  Oura puts on the morning they end — today's workouts, and this week against last, Monday to
  Sunday. Each week has the average steps, days at the goal, days under the minimum, counted
  sessions with their time and calories, and the average sleep and readiness scores (rounded,
  over the days with one). **Today is still going:** it counts toward days at the goal as
  soon as it gets there, but isn't averaged or counted under the minimum until it's over, so
  this week isn't dragged down by a half-finished day; on a Monday, this week has no average
  yet. A day with no record isn't under the minimum. The summary also carries the Oura
  connection (`/api/oura/status`'s shape), so the page needs one call to start.
- **`POST /sync { force? }`** is `OuraSyncService.SyncForPageAsync`. Opening the page skips
  it when Oura is connected and last synced under 5 minutes ago; a connection that needs
  reconnecting is never skipped, so the page always hears it. `force` (Sync now) always runs.
  It answers `ran`, `changed` (something was written, so the cards should be read again), the
  counts, the window, and `lastSyncedAt`. A failed sync doesn't move `lastSyncedAt`, so the
  next page open tries again. Oura not connected, refusing the sign-in, or refusing access
  (403, with Oura's words) is a 409; Oura down or limiting requests, a 502. The reads never
  call Oura, so the page still shows what LifeOS holds.
- **`gen:api`** regenerated `web/src/api/schema.d.ts` with the new routes and shapes.
- **Tests:**
  - over HTTP, each on a database of its own with Oura connected the way the page connects
    it: the days with their bands, sleep and readiness, the empty days and the default range;
    the 731-day limit and a backwards range; only the allowlisted user; workouts counted or
    not with their reasons, the fields, and the default 12 weeks; the note set, refused (a
    line break, 141 characters, no `text`, an unknown or shortened id) and cleared, with only
    two note events written; a day in full with its contributors, night, nap and workouts in
    order, and an empty day; the summary's two weeks against hand-worked numbers; the
    throttle skipping under 5 minutes and running after, Sync now always running, and a
    later sync picking up changed steps and a new workout; and Oura not connected, down,
    refusing access, and refusing the token twice, each in words, with the reads still
    working and the throttle not hiding a reconnect
  - on `ActivityReads`: today not under the minimum until it's over, and a Monday with
    nothing averaged yet

**A4 (2026-10-22).** The Activity page's header, Connect card, return route, Today card,
Steps card and Exercise card, with the workout sheet and its note. Mock first, then live
against the API. As planned, plus:

- **Opening the page syncs once** when Oura is connected (the API skips it under 5 minutes),
  and Sync now forces one. The header says "Synced from Oura 3 min ago", refreshed every
  minute.
- **Banners, in words:** Oura needs reconnecting (with Reconnect Oura), a scope Oura didn't
  allow, a sync that failed now, and the last sync's error. The data still shows under them.
- **The Connect card** says what LifeOS reads and that it's read-only. On a host without
  Oura set up, it names `Oura__ClientId` and `Oura__ClientSecret` instead of offering the
  button. Data synced before a disconnect stays on the page under the card.
- **`/activity/connected`** posts the code once, so a second render can't spend it. Then it
  says "Oura connected" and replaces itself with `/activity`. A refusal, Oura's own error or
  a missing code shows "Oura isn't connected" with Try again and Back to Activity.
- **The Today card:** steps so far as the big number, on a meter toward the goal with the
  minimum marked, and the verdict as a sentence ("1,188 under the minimum."). Last night's
  sleep and readiness each have Oura's band in words (85 and up Optimal, 70 Good, below that
  Pay attention). Time asleep is shown against time in bed. Today's counted workouts open
  their sheet.
- **The Steps card:**
  - The range (30 days, 90 days, 1 year) is remembered in the browser.
  - The year is a bar per week, Monday to Sunday (the average a day), with no average line.
  - The tiles say "of N days with steps" and "of N full days", so a gap or today isn't
    counted against him.
  - A day with 2 hours or more off the ring says so in its readout and in the Table.
- **One chart component**, `components/charts/daily-metric-chart.tsx`, on
  `DomainDailyChart`'s pattern. The Character page's chart is left as it is. It has:
  - bars toned by band, and today's (or this week's) bar hatched as "so far"
  - the 7-day average line, broken where there's no average
  - the dotted minimum and dashed goal, each labelled on the axis
  - a legend
  - readouts on hover and with the arrow keys, Home and End
  - Enter or a click opens a bar when the card gives it somewhere to go (A5's day sheet)
  - `MetricTable`, the Table view for both cards
- **Colour:** the three step bands are one green ramp, light to dark. It was checked with the
  dataviz validator against the card in both themes, with its own dark steps. The average
  line is the ember chart colour. Its colour-blind separation from the bars is 6–8, which is
  only allowed with a second cue, so the line also has a 2px halo in the card colour and is a
  line, not a bar. Every bar's band is also said in words. In forced-colours mode, bars draw
  in `CanvasText`.
- **The Exercise card:**
  - Sessions, minutes and calories against last week.
  - Minutes per week over 12 weeks, as a chart or a Table, with this week hatched.
  - The counted workouts grouped by week. "Show N not counted" opens the rest, muted, each
    with "Not counted:" and its reason.
- **The workout sheet** opens on the right on a desktop and from the bottom on a phone. It
  opens on the sheet rather than the note box, so a phone's keyboard doesn't jump up. It
  says "Counts as exercise." or "Not counted as exercise:" and the reason, then every field
  with the source and when it synced. The note:
  - saves on Enter, when the box loses focus, and when the sheet closes, one save after
    another
  - shows "Saving…", then "Saved", with a counter out of 140
  - has Clear
  - shows a refusal in words under the box
- **`lib/exercise-rules.ts`** mirrors `ExerciseRules` and is tested on the kernel's 14-row
  table. **`lib/activity.ts`** holds the bands, the words and the weeks, tested on the
  kernel's hand-worked weeks.
- **The mock store's activity half** (`lib/mock/activity-store.ts`):
  - 120 days, with two gaps, days off the ring, naps, and today at 60%
  - 40 workouts of every source, and a note on a run
  - Oura connected and synced two hours ago
  - It refuses what the API refuses, in the same words, and skips a page sync under 5
    minutes the same way.
- **Checked live** against the API on a throwaway database, with a fake Oura on localhost
  serving synthetic records in Oura's shape (no real Oura data), and every call answered
  200:
  - the connect round trip: Oura's sign-in, the code exchange, the first sync with its
    history
  - the page-open sync skipped under 5 minutes
  - Sync now
  - a note saved and shown under its row
  - the 1-year view
- **Tests:** Vitest for the rules, the words and weeks, the mock store, the chart and the
  page (StrictMode included, so the connect posts once). The e2e golden path covers steps
  1–4; step 5, the sleep chart, comes with A5. The a11y scan covers the page in both themes
  and on a phone, with the year view, the tables and the sheet open.
- **Left for later:** a Disconnect button (the API has `POST /api/oura/disconnect`), and the
  Sleep and readiness card with the day sheet (A5).

**A5 (2026-10-22).** The Sleep and readiness card, the day sheet, and "In full" on the Today
card. Mock first, then live against the API. No migration and no API change. As planned,
plus:

- **Last night on the card:**
  - The sleep score and readiness, each with a swatch and Oura's band in words, and time
    asleep against time in bed.
  - The stages as one bar split by share (deep, light, REM, awake; deepest first, with a 2px
    gap between), listed under it with each one's time and share.
  - Bed to wake, average HRV and the lowest heart rate.
  - "Last night in full" opens the day. When the night hasn't synced: "Open the Oura app to
    sync last night."
- **This week against last:** two tiles, the sleep score and readiness averaged over this
  week's nights, each with its band and last week's number.
- **The chart** is the Steps card's chart. Measure switches between Sleep score, Readiness
  and Time asleep, and is remembered in the browser with the range.
  - The scores have a fixed 0–100 axis with Oura's lines at 70 (dotted, Good) and 85 (dashed,
    Optimal), and bars toned by band. Time asleep is in hours, on an axis of at least 10 h,
    in one tone with no lines.
  - **The 7-day average counts today:** a night is over by morning. (The steps average
    doesn't count today, which is still going.)
  - The year is a bar per week (the average a night), with this week hatched as "so far" and
    no average line, as for steps.
  - Readouts say the night in words ("Tue, Oct 20: sleep score 76, good; 7-day average 74").
    Time asleep adds time in bed and the naps. A night not synced yet says so.
  - Enter or a click opens a night (not on the year view).
- **The Table** lists every night, newest first: the day (a button that opens it), both
  scores with their band in words, and time asleep. The year's Table lists weeks with all
  three averages.
- **The day sheet** (`components/activity/day-sheet.tsx`) opens on the right on a desktop and
  from the bottom on a phone. It's titled by day ("Today", "Tue, Oct 20"), with "The night
  ending that morning: 11:52 PM to 8:19 AM" under it. It shows:
  - both scores with their bands
  - the night: the stage bar, then asleep of in bed, bed to wake, how long it took to fall
    asleep, efficiency, average HRV, and average and lowest heart rate
  - what made each score: Oura's contributors as small meters toned by band, each with its
    number and word. They're in the Oura app's order, any new one after by name; one Oura
    didn't give says "Not given".
  - the body temperature against the usual, and its trend ("+0.3 °C")
  - the naps, each with its times and time asleep
  - A day Oura has nothing for says so.
- **The Today card** got "In full" beside Last night, opening today's sheet. Its scores use
  the card's swatch-and-words figure.
- **Colour:** one blue-indigo ramp for the score bands and another for the stages. Each was
  checked with the dataviz validator against the card in both themes, with dark steps of its
  own: on dark, the stages flip so deep is brightest. Both pass the ordinal check. The ember
  average line against the blue bars is well apart for colour-blind readers (ΔE 20.6 light,
  21.2 dark). Every band is also said in words.
- **Shared pieces:**
  - The range helpers (`METRIC_RANGES`, `rangeFrom`, `rangeStart`) moved into
    `lib/activity.ts` for both cards.
  - The chart takes a fixed `top` and a `formatValue` for its axis (hours).
  - `components/activity/sleep-parts.tsx` holds the stat tile, the score figure, the stage
    bar, the contributor meter and the night's facts.
- **Reads:** `useActivityDay(date)` on `GET /api/activity/day`, normalized like the other
  reads. The mock store answers it with:
  - a night split into stages that add up to the time asleep
  - contributors around each score (the eight readiness contributors the sandbox gives,
    without `sleep_regularity`)
  - the temperature
  - a nap on nap days
  - the day's workouts
- **Checked live** against the API on a throwaway database. The fake Oura's nights were given
  realistic stages, heart rates and varied contributors (no real Oura data). Last night, the
  chart, Readiness and a night's sheet all read from the API, in both themes, with no console
  errors. A day without the ring answers no night and no contributors.
- **Tests:**
  - Vitest for the bands' edges (69, 70, 84, 85), the 7-day mean, the weeks, the words, the
    contributors' order and names, the stages, the temperature, the ranges and the
    normalizer
  - the mock's day, and the chart's fixed top and hours
  - the page: last night; switching the measure and remembering it; reading a night and
    opening it with Enter; the Table opening a night with its meters; a nap; the Today
    card's In full; the year; and a night not synced
  - the e2e golden path's step 5: the sleep chart switched to Readiness, a night opened from
    the Table, and last night from the Today card
  - the a11y scan: the sleep card's chart and Table and a night's sheet in both themes, and
    last night's sheet on a phone
- **Found in passing:** `GET /api/activity/day?date=nope` answers ASP.NET's own binding
  message ("Failed to bind parameter…") as its 400, not a 400 in words. The page never sends
  a bad date. Left for later.

**A6 (started 2026-10-22; the week of comparing is still to come).** No migration.

- **A sync cut-off, built and withdrawn the same day** (§5): at the start of A6, Chris's ask
  read as "read nothing from Oura before 2026-10-22". It went out as `Oura:StartDate`
  (`30cb9f1`, deployed). He then said he meant XP, not data, so it was reverted: the history
  is read and shown as planned, and phase 2's XP starts on 2026-10-22 (§3.8, question 7).
  Nothing had synced in between.
- **`fly-setup.ps1`** imports `Oura__ClientId`, `Oura__ClientSecret` and
  `Integrations__TokenKey` from the env file.
  - It takes all three or none, and checks that the key is 32 bytes of base64.
  - Names match without regard to case, so `.env`'s `Oura__ClientID` is found.
  - Its summary says Oura is on or off without printing a value.
  - When Oura is on, the steps left by hand add the redirect URI to register with Oura.
- **On staging:**
  - The three secrets were staged on `lifeos-mz55-staging` from `.env` with
    `fly secrets import --stage`, values on stdin. The whole script wasn't re-run, so the
    other secrets and the deploy token were left as they were.
  - Staging seals tokens with `.env`'s `Integrations__TokenKey`, so `bsk` run with `.env`
    loaded opens the same tokens.
  - Main was pushed (A0–A6), and CI ran the tests, applied 0036–0037 to staging and
    deployed.
  - Checked from outside without signing in: `/health/ready` answers ready (it can't
    without a well-formed token key), the three secrets show as deployed, `/api/oura/status`
    asks for sign-in, and Fly's logs show no errors.
- **The first connect (2026-10-22):** Chris registered the redirect URI and connected. Oura
  granted `daily` and `workout` with 30-day tokens. The first sync's read was refused (401),
  the refresh worked, and the read was refused again, so the connection asked to reconnect.
  - LifeOS had dropped Oura's reason, so the cause isn't known yet.
  - A made-up token gets Oura's `{"detail": "The access token provided is expired, revoked,
    malformed, or invalid for other reasons."}`, with or without a `User-Agent`, so the
    missing headers weren't it.
  - Third-party reports say Oura also answers 401 when a collection needs a scope the token
    lacks, and names the scope. But the five collections LifeOS reads all need `daily` or
    `workout`, which were granted.
  - Agents don't read Chris's real Oura data (§7), so his token wasn't used to probe Oura.
    LifeOS now keeps what Oura says instead.
  - **The cause (confirmed when the reconnect worked):** LifeOS sent the browser straight to
    `cloud.ouraring.com/oauth/authorize`. The new portal's documented
    `developer.ouraring.com/authorize` redirects there, but adds `prompt=consent` and
    `acr_values=…default_ext`: by its name, the sign-in for the external API. A token signed
    in without it is one the data API won't take. Checked with a made-up client id, the
    redirect keeps every parameter LifeOS sends, PKCE included. Oura's token URL stays
    `api.ouraring.com/oauth/token`; the portal has none of its own.
- **What changed after it:**
  - `Oura:AuthorizeUrl` defaults to `https://developer.ouraring.com/authorize`. Without a
    release, a host can go back with the `Oura__AuthorizeUrl` secret.
  - A 401 on a collection carries the collection and Oura's `detail`, or the
    `WWW-Authenticate` challenge when there's no body
    (`OuraAccessDeniedException.Collection`, `.Detail`).
  - The reconnect reason names both. For example: "Oura refused LifeOS's access token for
    daily_activity even after refreshing it: “…”".
  - Requests send `User-Agent: LifeOS/1.0` and `Accept: application/json`, as Oura's
    documented clients do.
  - The page's banner no longer ends a reason that has its own period with a second one
    (`asSentence`).
- **Working on staging (2026-10-22):**
  - Chris reconnected through the portal's sign-in, and the Activity page filled in.
  - A forced refresh (`bsk oura refresh`, with `.env` loaded) worked at
    `api.ouraring.com/oauth/token`, so the `moi.ouraring.com` fallback isn't needed.
  - A sync on the refreshed token worked, and the history is all in, back to 2023-04-27.
    The sync's output was discarded, so no agent saw the data (§7).
- **Left for Chris:** for a week, compare the page with the Oura app, imported workouts
  included if there are any. Then A6 is ticked.
- **Not built yet:** the `bsk oura forget` purge (§7). Real data is on staging now, so a
  deletion request would have to wait for it to be built.

**A7 (2026-10-22).** As planned in §3.8, plus:

- **The rules are data.** `ActivityRules` holds the steps and sleep curves as points and
  exercise as tiers, so a retune is a change to a list.
  - `StepsPart` and `SleepPart` give the unrounded parts for showing the working.
  - `ExerciseSeconds` adds up a day's counted workouts with overlaps merged, through
    `MergedSeconds`.
- **The facts carry no readiness.** `ActivityDayFacts` has steps, exercise seconds and the
  sleep score only, and a test fails if anything is added.
- **The TypeScript port** (`lib/activity-score.ts`) takes each workout's `counted` from the
  API, rather than judging it again, and reuses the diet score's halves-up rounding.
- **Tests:** `ActivityXpRulesTests` and `activity-score.test.ts` run the same table: the
  drift checks, the worked examples, each curve's points and midpoints, the exercise tier
  edges to the second, overlaps, splits, walks and detections, missing data, and
  `XpFrom`.
- **Not changed:** the ledger, the sync, the Activity page, the Food Log's rules, the level
  curve and the 70-XP bar.

**A8 (2026-10-22).** `ActivityAwards` (the keys) and `ActivityRewards` (settling), with no
migration. As planned in §3.8, plus:

- **When Oura's day ends:** the home-zone date 4 hours earlier
  (`ActivityRewards.OuraDayAtAsync`). Steps and exercise for a day pay once that date has
  moved past it. Oura's own day start isn't stored, so 4 a.m. is a constant
  (`ActivityAwards.DayStartHour`).
- **Dates on the earns** (the §3.8 table):
  - Steps are dated when LifeOS last stored the day's steps, and exercise at the last counted
    workout's end. Both are known without the day start, and both are on or before the
    moment the award became due.
  - A re-price is dated when it's written, always after the key's last earn, as the Food
    Log's are.
  - An earn whose date falls before the wallet opened doesn't pay. (Changed on 2026-10-23
    to go by the Oura day; see the fix after A11.)
- **The sleep score's event:** `v_sleep_day` lists the score's event first, so
  `SleepDay.ScoreEventId` reads `event_ids[1]` when the day has a score. No view changed.
- **The notes carry no Oura numbers:** "Steps Thu Oct 22", "Exercise Thu Oct 22", "Sleep Thu
  Oct 22". The ledger is append-only, so purging Oura's data (§7) never has to touch it.
  - The amount, base and multiplier are on the award as usual.
  - Each earn cites the Oura events it was priced from, so the numbers can be found while
    LifeOS holds them.
- **Where it settles:**
  - `POST /api/activity/sync` settles after every call, even a skipped one, and answers
    `awards` and `progress` beside the sync (`gen:api` regenerated).
  - `POST /api/domains/settle` settles the food days, then activity, then the ranks.
  - `bsk oura sync` settles after the sync and lists what it paid (`awards` in `--json`).
    When running it against staging, keep discarding its output (§7).
  - `bsk rewards reconcile` settles activity too.
- **Tests:**
  - `ActivityRewardsTests`, each on a database of its own, with XP starting 10 days before
    the real date, since the domains read the real clock:
    - each part pays once due and is credited to its day
    - overlapping counted workouts are merged, and a walk or a detection adds nothing
    - today's sleep pays, but today's steps and exercise wait for 4 a.m. (3:59 pays nothing,
      4:00 pays both)
    - days before `XpFrom` never pay; readiness and an unscored night pay nothing
    - a repeat settle or sync pays nothing twice
    - re-pricing: more steps, a second session, a workout deleted then restored, and a
      revised score; a new score version at the same number writes nothing
    - a retune leaves a paid day alone until Oura revises it
    - the ×1.3 multiplier, rounded halves up (26 + 33 + 20 on a 60-base day), with the day
      counted as successful on activity alone
    - nothing pays before the wallet opens, and activity never opens it
    - the kernel's rules start on 2026-10-22
  - Over HTTP (`ActivityEndpointTests`): a skipped page sync pays last night's sleep, a
    second pays nothing, and settling pays the day's steps and exercise at 4:00, not at 3:59,
    then nothing more.
- **Not changed:** the web, beyond the regenerated types (A9 shows the XP), the Food Log, the
  level curve and the views.

**A9 (2026-10-22).** The Activity page shows each day's activity XP, paid or projected, with
no migration. As planned in §3.8, plus:

- **The reads carry what was paid.** `ActivityPaidReads` (Application) reads the ledger's
  `steps:`, `exercise:` and `sleep:` keys over the reader connection, as `FoodReader` does.
  - Each day row (`GET /api/activity/days`, the day detail and the summary's `todaySoFar`)
    has `paid`: each part's base, what it paid and its multiplier (null at ×1.0), with the
    day's total and base total.
  - `gen:api` regenerated.
- **Paid or projected** (`lib/activity-xp.ts`):
  - A paid part shows what the ledger paid, multiplier included: "26 (20 ×1.3)".
  - A part not paid yet is projected from the day so far by `lib/activity-score.ts`, as base
    XP, and always says "projected", never "earned". It says when it pays: "Pays when the
    day ends, at 4 a.m." for today's steps and exercise, "Pays at the next sync" once
    they're due, and for sleep.
  - A part that pays nothing says why: no steps or no score from Oura, under 10 minutes of
    exercise, a sleep score of 50 or less. Days before 2026-10-22 show "—".
- **Where it shows:**
  - **Today card:** a "Health XP today" section with the three parts and the total in words
    ("46 Health XP: 12 earned, 34 projected").
  - **Steps card:** a Steps XP tile (earned in the range) and a Health XP column in both
    Tables (by day, and by week for a year).
  - **Sleep card:** last night's sleep XP in words, and a Health XP column in both Tables.
  - **Exercise card:** Exercise XP this week against last, a Health XP column by week (this
    week adds today's projected), and each week's heading adds its XP. It reads
    `/api/activity/days` for the 12 weeks.
  - **The day sheet:** the day's three parts, also when Oura has no night for it.
- **Toasts:** an activity award's toast names its note alone ("Sleep Thu Oct 22"), with the
  working at a multiplier ("❤️ 25 × 1.3").
  - The icon is a moon for sleep alone, a shoe for the rest of activity, a plate for diet XP,
    and a coin for a mix.
  - A day Oura revised is one toast: "Steps Thu Oct 22 re-scored: 20 → 25 Health XP".
  - In the rewards history, the trigger reads "from Oura".
- **The sync from other pages:** the Food Log and Character pages read `GET /api/oura/status`
  and, when Oura is connected, ask once for the throttled sync (`useOuraSyncOnOpen`). A
  failed sync is left to the Activity page to report. The sync's paid awards refresh the
  wallet, the ledger and the domains.
- **The mock pays as the kernel does,** from 2026-10-22 by the real date: the food
  planner's keyed-award plan is shared, and the page sync, the domain settle and the reads
  carry the activity keys.
  - Tests about anything else empty the mock's Oura days (`clearMockActivity`) so they stay
    the same on every date.
  - The e2e paths that read Health's numbers run on 2026-10-21 (Playwright's clock). The
    Character page's a11y scans run with reduced motion, so the sync's toasts are scanned
    settled, as the Food Log's are.
- **Tests:**
  - .NET: the summary, day rows and day detail carry `paid` over HTTP; `ActivityPaidReads`
    gives each part's base, paid and ×1.3 multiplier.
  - `activity-xp.test.ts`: the 4 a.m. day end; projected, paid and nothing, each with its
    words; days before `XpFrom`; the totals.
  - The mock store, at a fixed date: each part pays once due and once, not before the wallet
    opens, steps wait for 4 a.m., and the page sync (even skipped) and the domain settle pay.
  - The Activity page at a fixed date: the Today card, the Tables, the tiles and the day
    sheet. The Character page asks for the sync only when Oura is connected. Activity
    toasts in `earn-feedback.test.ts`.
  - An e2e path at a fixed date for the Today card and the Steps Table.
- **Checked live** against a throwaway database, the fake Oura and a test sign-in. The
  page's first sync paid last night's sleep ("🌙 Earned +12 Health XP, Sleep Thu Oct 22"),
  the Today card showed it earned beside the steps and the run projected, the Tables showed
  "—" before Oct 22, and the Food Log and Character pages each asked for the sync.
- **Not changed:** the rules, the ledger keys, the level curve, the Food Log's own XP and the
  views.

**A10 (2026-10-22).** Tags from the Oura app, as planned in §3.9, with migration 0039. Plus:

- **The scope:**
  - `OuraConnectionService.Scopes` is `daily workout tag`.
  - The sync reads `enhanced_tag` only when the stored grant includes `tag`, and reads it
    last in each window. Without it the other five collections sync as before, and the
    status's `missingScopes` is `["tag"]`.
- **The history read again:** `SaveConnectionAsync` clears `history_from` and
  `history_done` when the new grant isn't a subset of the stored one
  (`EXCLUDED.scopes <@ scopes`). A connect with the same scopes, or fewer, carries on where
  it was.
- **The client:** `IOuraClient.GetTagsAsync` reads `/v2/usercollection/enhanced_tag` with
  the other collections' paging. A record needs `id`, `start_day` and `start_time`; one
  without them is skipped, as for the others.
- **The domain:**
  - `OuraCollections.Tag` is `enhanced_tag`, payload key `oura_tag`, keyed by `id`, kind
    `observation`, voidable. `OuraTag` is what Oura sent.
  - `ActivityTag` is the held tag, with its `Name` from `OuraTags.Name` and its length in
    `Seconds`, null without an end.
- **The reads:**
  - `IActivityRepository.GetTagsAsync` reads `v_oura_tag` newest first.
  - `ActivityReads.GetTagsAsync` checks the range as the other reads do (at most 731 days,
    not backwards).
  - The day detail carries the day's tags, earliest first.
- **API:** `GET /api/activity/tags` (`ActivityTagsRead`, 30 days through today by default)
  and `tags` on `GET /api/activity/day`. `gen:api` regenerated.
- **CLI:** `bsk oura tags [--days N] [--json]`, days newest first and each day's tags
  earliest first. Each line shows the time, or start to end with the length, then the name,
  with the comment on its own line. With no tags and the scope missing, it says to
  reconnect.
- **Tests (.NET):**
  - The client reads the sandbox's shape (`OuraFixtures/enhanced_tag.json`), a custom tag
    with a length and a text-only tag, and skips broken records.
  - The payload and its hash; the names table; a tag's length.
  - The sync: versions, a changed comment, withdrawn and restored; tags read only with the
    scope; the history read again after a connect that adds a scope, and not after one that
    doesn't.
  - The events stay out of every view about items, and `bsk_reader` can read `v_oura_tag`.
  - The API's shapes, order, default range and refusals; the day's tags.
- **The page:**
  - **The Tags card** (`components/activity/tags-card.tsx`), in the left column under
    Steps:
    - the range (30 days, 90 days or 1 year, remembered in the browser)
    - the counts line, most used first ("Caffeine 24 · Alcohol 8 · Sauna 5")
    - the tags by the day they start, newest day first and each day's newest first. Each
      day's heading is a button that opens the day sheet, with "3 tags" beside it.
    - The latest 7 days with tags show first, then "Show 21 more days", so a year of
      daily coffees doesn't make the card endless. The counts always cover the whole
      range.
  - **Each tag** reads its name and when: "7:45 AM" for a moment, "7:00 PM–7:25 PM · 25 min"
    with a length, or "9:00 AM – Sat, Oct 4, 9:00 AM · 3 days" when it ends on another
    day. A comment sits on its own line under it.
  - **Without the scope**, the card says Oura didn't allow tags and to reconnect, or, with
    tags already held, shows them and says new ones won't arrive until then. The
    missing-scope banner words the scopes ("Oura didn't allow workouts and tags",
    `scopeWords`).
  - **The day sheet** has a section headed "A tag" or "3 tags", earliest first, also on a
    day Oura has no night for.
  - `lib/activity.ts`: `normalizeTag(s)`, `tagWhen`, `tagLength`, `tagCounts`, `tagDays`,
    `tagRangeWords` and `scopeWords`.
  - The tags query sits under `["activity"]`, so a sync or a connect refreshes it.
- **The mock:**
  - It grants `daily workout tag`.
  - It seeds about 120 days of tags: a coffee most mornings, drinks some evenings, the odd
    late meal, a 20-minute sauna, a 5-minute custom "Cold plunge", a text-only note, three
    days of "Sick" with a comment, and comments on a few others.
  - `ouraTagName` copies `OuraTags.Name`, tested on the kernel's 16 cases.
  - `clearMockActivity` clears tags too.
- **Tests (web):**
  - Vitest: the tag helpers, the mock's tags and names, and 8 Activity page tests (the
    counts and order with "Show more days", a length and a note, a day's tags in its sheet
    with and without a night, 90 days remembered, the empty and not-allowed states, and the
    banner's words for one scope and two).
  - An e2e path through the card and a day's sheet.
  - a11y scans of the card and the day sheet with tags, in both themes. Neither pins the
    clock, since tags pay nothing and the seed counts back from today.
- **Checked live** against a throwaway database, the fake Oura and a test sign-in.
  - Connected without `tag`: the sync never asked for tags, and the status named the
    missing scope. The page's banner read "Oura didn't allow tags", and the card said new
    ones need the reconnect.
  - Reconnected with it: the banner went, and the history brought in 255 synthetic tags and
    nothing else new. A second reconnect wrote nothing.
  - The card, a day's sheet and `bsk oura tags` read them back, also at 375 px wide.

**A11 (2026-10-23).** Chris asked for "a much more prominent title to identify the different
cards", "a title bar or banner too, and bonus points if it has an image in it", and arrows on
the Today card "for going back and forth by 1 day at a time. I want to see my Health XP from
yesterday." Web only:

- **The banner** (`activity-banner.tsx`) replaces the page header and scrolls with the page.
  Its picture is inline SVG drawn from theme tokens (`--banner-*` in `index.css`), so there's
  no image file and it follows the theme. It's in three layers so each part keeps its place at
  any width: the stars (dark only), the sun or moon at the top right, and the hills along the
  bottom 62%, drawn 2,400 × 160 and cropped only from the left. The words keep to the sky; the
  tagline is the page's foreground colour, so it reads at 7.3:1 even where it reaches the
  middle hill. The picture is `aria-hidden` and dropped in high-contrast mode.
- **The titles** (`activity-card.tsx`): `ActivityCard` and `ActivityCardTitle`, with five
  `--activity-*` colours. Each icon reaches 3.4:1 or more on its 12% tile in both themes. The
  colours are identity, not data: the title always names the card.
- **The Today card** keeps the day it shows as null for today, so it moves to a new day by
  itself at 4 a.m. An earlier day reads `useActivityDay`, so stepping back and then opening the
  day in full reads once. The previous arrow stops at `historyFrom`.
- **Tests:** Vitest for `dayTitle` and `dayWhen`, the five titles in order with their
  colours, stepping back two days and forward again, the day in full from an earlier day, the
  stop at `historyFrom`, and yesterday's XP all earned on Oct 25 (and Oct 21 saying when
  activity XP started). The golden path steps back to yesterday's earned XP and returns; the
  phone a11y scan steps back too and checks the arrows are 44 px and the page stays 375 px
  wide.

**Fix (2026-10-23): activity pays from the day the wallet opened.** Stepping back to Oct 22,
Chris saw "Sleep 19 of 20 projected · Pays at the next sync" a day after the night, beside
steps that were earned. He asked: "Shouldn't it have locked in that XP 24 hours ago after my
first sync after sleeping?"

- **Why:** the staging wallet opened at 11:00 on Oct 22, with his first Food Log entry. A8
  paid only triggers on or after that instant. Sleep's trigger is the wake time, which was
  earlier that morning, so the night could never pay. The steps triggered at the day's last
  sync, after the opening, so they did. The page's projection doesn't know the wallet rule,
  so it kept saying "Pays at the next sync". Only Oct 22's sleep was affected; the wallet
  opens once.
- **Decided:** count activity from the Oura day the wallet opened on (§3.8). `ActivityRewards`
  compares each award's day with `OuraDayAtAsync(openedAt)`, and a first earn whose trigger
  is before the opening is dated at the opening. A day before the opening still never pays,
  even if it syncs later. Before, Oct 21's steps and sleep would have paid had they synced
  after 11:00 on Oct 22.
- **The mock** does the same for activity only. Food keeps the instant, since the first food
  award opens the wallet at its own trigger.
- **Tests:** `ActivityRewardsTests` (a night and a run before the opening on its day pay,
  dated at the opening; the steps at the sync; the day before pays nothing, though synced
  after) and the mock store (the opening day's night pays, dated at the opening; the day
  before doesn't). No migration and no API change. On the next sync after the deploy, Oct
  22's sleep pays.
