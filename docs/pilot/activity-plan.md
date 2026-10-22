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
- [xp-domains-plan.md](xp-domains-plan.md): the credited day, the multiplier and the 70-XP
  successful day. Phase 2 only.
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
| **Start date** | **Oura is read from 2026-10-22**, the day Chris started the Food Log, so activity and food feed Health XP from the same day. Nothing from before it is synced (`Oura:StartDate`, §3.3). *(Changed 2026-10-22, at the start of A6: it was everything Oura has.)* |
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
  in Oura's new portal must refresh at a different, undocumented token URL.
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
- **The start date** (`Oura:StartDate`, 2026-10-22 by default): no sync reads or keeps
  anything from before it.
  - The window starts there while it's under 14 days old, and the history counts as all in
    once it reaches it, so there's no backfill.
  - `bsk oura sync --from` can't go before it, and says so.
  - The API's status and `bsk oura status` say it, and the page says "LifeOS reads Oura from
    Thu, Oct 22" while the charts' year reaches before it. Days before it read "before
    LifeOS read Oura", not "no steps recorded".
  - Anything LifeOS read before a start date was set stays (the record is append-only).
  - It's a setting, not a constant, so a host can move it. `2015-01-01` reads Oura's whole
    history.
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
- **Header:** "Synced from Oura 3 min ago" and **Sync now**.
  - Before connecting, a Connect Oura card says what LifeOS will read, and that it's
    read-only.
  - `/activity/connected` finishes the connect (§3.1), then shows the page.
- **Today card:**
  - **Last night:** the sleep score, readiness score and time asleep. Or "Open the Oura app
    to sync last night" when it hasn't arrived.
  - **Steps so far** as the big number, with progress past the 5,000 minimum toward the
    7,500 goal.
  - Today's counted workouts.
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

### 3.8 Phase 2: activity XP (shape only)

The machinery is D13's. Only the rules are new.

- **`ActivityRules`** in Domain, like `DietRules`: the numbers, a `Score` over a day's facts,
  a drift test, and a TypeScript port (`lib/activity-score.ts`) tested on the same table.
- **Award keys** are item-less and re-priced:
  - such as `steps:<day>`, `exercise:<day>` and `sleep:<day>`, or one per workout
    (`workout:<oura id>`)
  - paid in Health XP, credited to the Oura day, at that day's multiplier
  - citing the versions they were priced from

  A paid day is re-priced only when those versions change, so a retune never rewrites a
  past day.
- **Settling:** after every sync, from `POST /api/domains/settle` (the Character page), and
  in `bsk rewards reconcile`.
- **Feedback:** earn toasts on the Activity page ("👟 9,234 steps: +12 Health XP"), and
  projected against paid per day on the page.

**Questions for the phase 2 Q&A**, to settle with the formulas:

1. **Activity's share of the 70-XP successful day.** Food pays up to 40, and habits fill the
   rest today. Steps, exercise and sleep would share what's left, or raise the bar.
2. **The steps curve around Chris's thresholds** (5,000 minimum, 7,500 goal, more is
   better, less is worse):
   - **Below 5,000:** nothing, or a penalty? There are no penalties on the ledger yet.
   - **From 5,000 to 7,500:** a straight line or a step.
   - **Above 7,500:** keeps paying, and up to what cap?

   Steps (tiers) re-price less often while a day is in progress.
3. **When steps pay:** live as the day goes (re-priced on each sync), or once the day is
   over, like the Food Log's close.
4. **How counted workouts pay** (§3.4 decides which count): duration, intensity, calories,
   or a flat amount per session, and a daily cap. A manual workout can be back-dated with
   any intensity, so it's scout's honor (GEN-5).
5. **Sleep and readiness:** do they pay, and on what? The sleep score, time asleep, a
   bedtime target, or readiness. Readiness reflects recovery more than effort.
6. **Double counting:** a Health habit that is itself exercise or sleep would pay twice.
7. **History:** XP only from the day phase 2 ships (recommended), or for past days too. A
   year of backfilled data paying at once would jump several levels and fill the
   consistency window in one go. *Partly settled (2026-10-22):* LifeOS holds nothing from
   before the start date (2026-10-22, when the Food Log began), so there's no year to pay.
   What's left is whether the days between then and phase 2 shipping pay, as the Food
   Log's do.
8. **Other rewards:** Gems for step streaks, Freedom, Life XP.
9. **A background sync:** XP lands only when something syncs. If that turns out to matter,
   add a daily scheduled sync (a GitHub Actions cron, or Oura's webhooks waking the machine).
10. **Days with the ring off:** is a day with a lot of non-wear time excused rather than
    counted as a low-step day?

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
| A6 | Live on staging | The start date (2026-10-22: nothing earlier is read); `fly-setup.ps1` imports the Oura settings; Chris connects on staging; a real refresh on day one; a week of comparing the page with the Oura app, imported workouts included if there are any. | A5 |

**Phase 2 (activity XP), after the formulas:**

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| A7 | Q&A + rules | The §3.8 questions answered; `ActivityRules` with its drift test; the plan updated. | A6 |
| A8 | XP on the ledger | The award keys, the settling hooks, rank-ups and `progress`, as F2 did for food. | A7 |
| A9 | XP on the page | Paid and projected per day, toasts, `lib/activity-score.ts`. | A8 |

- **A1 and A2 are kernel only.** From A2, `bsk oura sync` works end to end, so the numbers
  can be checked from the CLI before the page exists.
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
  question 7. *Replaced at the start of A6, below.*
- **The night is the longest `long_sleep`.** Naps add up separately.
- **Oura's own score bands** (70 and 85) on the sleep and readiness charts, since Chris
  hasn't set his own.

**Changed at the start of A6 (2026-10-22):** "Do A6 next. But I would like to put in a
cut-off begin date that only starts today, because today is when I started logging food, and
I want to start using both at the same time to avoid half life XP contributions missing."

- So Oura is read from 2026-10-22 and nothing earlier is synced (§2, §3.3). Health XP from
  activity and from food then cover the same days, with no stretch where only one of them
  could pay.
- It's the setting `Oura:StartDate`, defaulting to 2026-10-22 in code, so every host (the
  API, `bsk`, staging and production) honours it without anything to remember.

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

## 7. Oura API notes

Checked 2026-10-22. Facts from Oura's docs unless marked otherwise.

- **Docs and spec:** [API v2 docs](https://cloud.ouraring.com/v2/docs), which load OpenAPI
  spec 1.42 (`/v2/static/json/openapi-1.42.json`). The file name changes with each version,
  and old ones are removed. There's no official SDK.
- **OAuth** ([authentication](https://cloud.ouraring.com/docs/authentication)):
  - Authorize at `https://cloud.ouraring.com/oauth/authorize`. The new portal's
    `https://developer.ouraring.com/authorize` redirects there.
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
  `/daily_readiness` and `/sleep`.
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
- [ ] A7 — Phase 2 Q&A + rules
- [ ] A8 — Activity XP on the ledger
- [ ] A9 — Activity XP on the page

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

- **The start date** (§3.3, asked for at the start of A6):
  - `Oura:StartDate` is a `YYYY-MM-DD` setting, 2026-10-22 when it isn't set
    (`OuraOptions.DefaultStartDate`). A malformed one, or one before 2015-01-01, fails
    startup in words.
  - The sync's floor is the later of the start date and 2015-01-01:
    - The recent window starts there while the start date is under 14 days old.
    - The history loop stops there, and reaching it marks the history done.
    - A start date still to come reads nothing yet.
    - `--from` before it is refused, naming `Oura:StartDate`.
    - History read before a start date was set stays, and the backfill ends.
  - `GET /api/oura/status` and `GET /api/activity/summary`'s `oura` carry `startDate`
    (null when Oura isn't set up), and `bsk oura status` prints "Reads from".
  - The page says "LifeOS reads Oura from Thu, Oct 22, so the days before it are empty"
    while the year reaches before it. In the readouts, Tables and year weeks, those days read
    "before LifeOS read Oura".
- **`fly-setup.ps1`** imports `Oura__ClientId`, `Oura__ClientSecret` and
  `Integrations__TokenKey` from the env file, and `Oura__StartDate` when it's set.
  - It takes all three or none, and checks that the key is 32 bytes of base64.
  - Names match without regard to case, so `.env`'s `Oura__ClientID` is found.
  - Its summary says Oura is on or off, and from when, without printing a value.
  - When Oura is on, the steps left by hand add the redirect URI to register with Oura.
- **On staging:**
  - The three secrets were staged on `lifeos-mz55-staging` from `.env` with
    `fly secrets import --stage`, values on stdin. The whole script wasn't re-run, so the
    other secrets and the deploy token were left as they were.
  - Staging seals tokens with `.env`'s `Integrations__TokenKey`, so `bsk` run with `.env`
    loaded opens the same tokens.
  - Main was pushed (A0–A6), and CI ran the tests, applied 0036–0037 to staging and
    deployed.
  - Checked from outside without signing in:
    - `/health/ready` answers ready, which it can't do without a well-formed token key.
    - The three secrets show as deployed.
    - The served bundle has the start date's words.
    - `/api/oura/status` asks for sign-in.
    - Fly's logs show no errors.
  - Whether the page offers Connect Oura, rather than saying Oura isn't set up, is for
    Chris's first look.
- **Left for Chris:**
  1. Register `https://lifeos-mz55-staging.fly.dev/activity/connected` under the app's
     Redirect URIs at developer.ouraring.com.
  2. Connect Oura on staging's Activity page.
  3. On day one, force a real refresh with `bsk oura refresh`, with `.env` loaded. If Oura
     refuses it, the refresh URL moves to `moi.ouraring.com` with the `Oura__RefreshUrl`
     secret (A1).
  4. For a week, compare the page with the Oura app, imported workouts included if there
     are any.
- **Not built yet:** the `bsk oura forget` purge (§7), which this plan says is best done
  before real data reaches staging.
