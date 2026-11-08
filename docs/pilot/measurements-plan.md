# Measurements Plan — weight first, then any metric, lab results and charts with Oura

The design and build order for health measurements Chris records himself: weight first,
then body measurements, blood work (CMP, A1c, lipids…), VO2 max and the like, charted later
beside his Oura data to see trends and relationships (MEASURE-1 to MEASURE-6 in
[ui-requirements.md](ui-requirements.md)). The decision behind it is **D20**, in the same
doc. The requirements say *what*; this doc says *how* and *in what order*, written so an
agent with no session context can pick up any phase.

**Scope agreed with Chris on 2026-11-07** (§6). Oura doesn't track weight, and Chris wants
one place for everything he measures about his body, Oura's numbers included.

**Keep it small.** The thing under test is whether seeing weight move toward a goal, with a
date it's heading for, keeps him weighing in and steering. Everything in §7 has to earn its
place through use.

## 0. Read these first

- [food-log-plan.md](food-log-plan.md) §3.1: food entries as versioned `measurement`
  events. Readings are stored the same way.
- [environment-plan.md](environment-plan.md) §4.1–§4.3: `Space` as a status-less subject
  type with attributes one service writes, and its ratings as versioned events with a
  `concerns` edge. A Metric and its readings copy that shape.
- [activity-plan.md](activity-plan.md) §3.2: the Oura views (`v_activity_day`,
  `v_sleep_day`, `v_readiness_day`) that Compare (M5) reads beside the readings.
- [ui-requirements.md](ui-requirements.md): MEASURE-1 to MEASURE-6, D9 (archive), D13, D14,
  D18, D20.
- [kernel-build-plan.md](kernel-build-plan.md): how a phase lands (migration + `bsk` verb +
  view + tests via `./test.ps1`, strict build).

## 1. The rules (agreed 2026-11-07)

| Question | Decision |
|---|---|
| Weight only, or general? | **General.** A *metric* is anything measured (Weight, Waist, HbA1c, LDL, VO2 max), and weight is the first one. Everything in this plan works for any metric; only the starter list knows what weight is. |
| Who defines metrics? | **Chris, in the app**, starting from a starter list (§2.1) so names and units are consistent. A list fixed in code would need a change every time he starts tracking something new. |
| Units | **Pounds and inches.** Each metric has one unit, and a reading keeps the unit it was entered in. No conversions. |
| When a reading happened | **Its exact time,** not just the day: weight at 6 a.m. and 9 p.m. differ by pounds. A reading can be for any time up to now, so past weigh-ins can be added by hand. |
| The day's number | **The first reading of the day** (home zone), usually the morning one before food. |
| Past weight history | **No import.** Chris will add past weigh-ins by hand if he wants them. |
| Scale | An **Arboleaf** smart scale. It has no public API; its app sends readings to Apple Health, Google Health Connect and Fitbit (§7). Weigh-ins are typed in for now. |
| Goal weight | **185 lb**, drawn as a target line, with **the date the trend is heading for it**, once there are readings on 2 or more days (§2.3). |
| XP | **None.** Measurements are information only: no Health XP, no currency, no level moves. A habit ("Weigh in each morning") is the way to be nudged. |
| The chart | Each reading as a dot, plus a **smoothed trend line** (§2.2), the target line, and ranges from 30 days to all time. |
| Several metrics at once | **Stacked charts on one shared timeline,** with one hover line across them all. Not two lines on one chart with two y-axes. |
| Blood pressure | **Two metrics entered together** (systolic and diastolic). |
| Lab results | A **lab visit** is a group of readings that share a date and a lab, with the report PDF optional (M4). |
| VO2 max | **Both** Oura's estimate and a measured lab test, as two separate series. |
| Agents | Measurements and lab results come under the **same Health switch** that covers Oura in `/mcp`. |
| The page | **Measurements**, in the nav beside Food Log and Activity. |

## 2. The formulas

### 2.1 Starter list

| Key | Name | Unit | Decimals | Better |
|---|---|---|---|---|
| `weight` | Weight | lb | 1 | lower |

M1 ships Weight only. M3 adds body fat (%), waist (in), resting blood pressure (mmHg, two
metrics) and VO2 max (mL/kg/min). M4 adds the lab panels (the CMP's 14 tests and eGFR, the
lipid panel, A1c), each with its usual unit and a common adult range (§4.9).

- **Better** says which way is good: `lower` or `higher`. It decides which way the goal date
  counts (§2.3) and, from M3, which side of a range is flagged. A metric may have none.
- **A metric made from the list** keeps its key in `attributes.catalog`, so the app knows
  which one is weight even after a rename.

### 2.2 Day values and the trend line

For a metric's live readings:

```
day value v_d      = the reading with the earliest taken_at on home-zone day d
trend on first day = v_first
trend on day d     = t_prev + α_g · (v_d − t_prev),   α_g = 1 − (1 − α)^g
                     g = days since the previous day with a value,  α = 0.2
```

- The trend moves only on days with a value. A gap of g days lets the next value pull it
  further (α_g grows with g), since the old trend has gone stale.
- **α = 0.2** smooths a day-to-day swing of 1–3 lb into a line that still turns within a
  week or two. It's a constant in `MetricTrend`, to recalibrate in use (M2).
- The trend runs over all of a metric's history, so a 30-day chart starts from the trend as
  it was, not from that day's raw value.
- Values are worked out in `double` and given back rounded to the metric's decimals.

### 2.3 The goal date

With a target T and a direction (Better), from the day values:

```
basis   = the day values from the newest day back 27 days (28 days in all);
          if fewer than 2, the newest 2 day values
slope s = least-squares slope of value against day number over the basis (units a day)
now     = the trend on the newest day
```

| Status | When | Shown |
|---|---|---|
| `no_target` | No target set | Nothing |
| `too_few` | Fewer than 2 days with a value | "Log another day to see when you'll reach 185." |
| `reached` | `now` at or past T in the Better direction (≤ T for lower) | "At or under 185." |
| `heading` | s moves toward T, and T is ≤ 730 days away | "185 by Mar 14 at 1.2 lb a week." Date = newest day + ⌈(now − T) / −s⌉ for lower |
| `far` | s moves toward T, but more than 730 days away | "More than 2 years away at this rate." |
| `not_heading` | s is 0 or moves away from T | "Not heading toward 185 (up 0.4 lb a week)." |

- **The rate a week** is 7·s, rounded to one more decimal than the metric's.
- **The basis** is reported (`days`, `from`, `to`), so the page can say "from 2 weigh-ins"
  while it's an early estimate.
- **Direction:** Better, or when a metric has none, lower if `now` is above T and higher if
  below. (With no Better, a metric that's past its target reads as heading the other way.
  Every body metric on the starter list has one; most lab tests don't, since either side of
  their range is a concern.)
- These are pure functions in `LifeOs.Domain.MetricTrend`. The web's mock store gets a
  TypeScript port in M2, with shared cases so the two agree.

## 3. What it produces

**A morning:** Chris steps on the scale, opens Measurements on his phone, taps *Log
weight*, types 192.4 and saves. The Weight card shows the dot, the trend at 193.1, the
dashed 185 line, and "185 by Mar 14 at 1.2 lb a week, from 23 weigh-ins".

**A missed week:** he backfills last Tuesday's weigh-in with its date and time. It slots
into the chart in order, and the trend and goal date are worked out again on the next read.

**A lab visit (M4):** after his physical, he logs one visit with the date, the lab and the
PDF, and types the CMP and lipid values once. Each value lands on its own metric's chart
with its normal range, and the out-of-range ones are flagged.

## 4. Design

### 4.1 Metric: a new subject type

A metric is a durable thing Chris manages over time: he adds one, renames it, sets its
target, and archives it when he stops tracking it. So it's a subject, the **17th type,
`Metric`**.

- **Attributes**, all written by `MetricService`, so `bsk set` refuses them:
  - `unit` (text, 1–16 characters, one line). Required.
  - `decimals` (whole number 0–3): how many decimal places a reading takes and shows.
  - `target` (a number with at most `decimals` places, optional).
  - `better` (`lower` | `higher`, optional).
  - `catalog` (the starter-list key, optional; set once, when the metric is made from it).
- **Title** is its name: one line, 1–60 characters, unique among the metrics not archived.
- **No status.** A metric is there or archived (D9's `archive_change`), like a space. An
  archived metric takes no new readings, and its history stays. Restoring it is refused
  while another metric uses its name.
- **The unit can't change once a metric has readings,** since that would mislabel them.
  Decimals and the target can change at any time.
- **No relations** in M1. Tying a Goal to a metric ("185 lb by June 1") is §7.

### 4.2 Readings: versioned `measurement` events

A reading is something Chris measured about himself: an event, like a food entry.

- **Event:** kind `measurement`, provenance `declared`, `occurred_at` = when it was taken
  (now by default), `recorded_at = now`.
- **Payload:**
  `{"metric_reading": {"id": "<uuid>", "metric": "<metric subject id>", "value": 192.4, "unit": "lb", "note": "…"}}`.
  - `value` is a number with at most the metric's decimals, between −1,000,000 and
    1,000,000.
  - `unit` is the metric's unit when it was written.
  - `note` is optional: one line, at most 140 characters.
- **Edges:** a `concerns` edge to the metric, so its detail lists its readings.
- **Versions:** an edit writes a newer version with the same `id` (value, time or note); a
  delete writes `{"metric_reading": {"id": "…", "voided": true}}`. Newest `recorded_at, id`
  wins, as meals do. No new event kind.
- **Times:** up to a minute ahead of now is accepted (clock skew); later is refused. Any
  past time is fine. A date with no time is noon in the home zone, as a backdated rating is.
- **An archived metric** takes no new readings, but its readings can still be edited or
  deleted.

### 4.3 Migration 0050 (`0050__metrics.sql`)

- The subject-type check gains `Metric`, mirrored in `SubjectTypes`.
- `bsk.v_metric`: one row per Metric subject: `id`, `urn`, `name`, `unit` (text, `''`
  when missing), `decimals` (0–3, 1 when missing or malformed), `target` (numeric or null),
  `better` (`lower`, `higher` or null), `catalog`, `archived_at`, `archived_on`,
  `created_at`.
- `bsk.v_metric_reading`: the newest version per reading id that isn't voided, of a subject
  that is a Metric: `id`, `metric_id`, `value`, `unit`, `note`, `taken_at`, `taken_on`
  (home-zone date), `taken_local`, `first_logged_at`, `logged_at`, `versions`, `event_id`.
- A partial index: `event_metric_reading_idx ON bsk.event ((payload->'metric_reading'->>'id'), recorded_at) WHERE kind = 'measurement' AND jsonb_exists(payload, 'metric_reading')`.
- `COMMENT ON VIEW` naming MEASURE-1 and D20, `GRANT SELECT … TO bsk_reader`, and both
  views added to `ReaderRoleTests`. The backup role reads them through 0046's default
  privileges.
- **Numbering:** the SuperJournal branch also wants 0050. Whichever lands on main second
  renumbers.

### 4.4 Rules in Domain

`Metrics.cs`: `Metric`, `MetricReading`, `MetricLimits` (the checks, in words, shared by the
services, the CLI and the API), `MetricBetter`, `MetricCatalog` (§2.1) and `MetricTrend`
(§2.2–§2.3: `DayValues`, `Trend`, `Project`).

### 4.5 Services

- `MetricService`: list, add (from the starter list when the name matches one, e.g. `Weight`
  → lb, 1 decimal, lower), set (name, unit, decimals, target or no target, better), archive,
  restore and resolve (by id, urn, short id or name).
- `MetricReadingService`: log, edit, delete, list and resolve a reading (by id or its first 4+
  hex digits). No ledger, no fold: readings never move a level.
- `MetricReadService` (the reads the CLI and the API share): one metric with its readings,
  day values, trend and goal for a range of days, worked out from all its history.

### 4.6 API and CLI

| Endpoint | Does |
|---|---|
| `GET /api/measurements/metrics` | Every metric, archived ones included, by name: `[{ id, urn, name, unit, decimals, target, better, catalog, archivedAt, readings, lastReading }]`. |
| `POST /api/measurements/metrics` | Body `{ name, unit?, decimals?, target?, better? }`. A starter-list name fills the rest. Answers the metric. |
| `POST /api/measurements/metrics/{id}` | Body `{ name?, unit?, decimals?, target?, clearTarget?, better? }`. Archiving and restoring use `/api/archive` and `/api/restore`. |
| `GET /api/measurements/metrics/{id}?days=N` | One metric (by id or name): `{ today, from, metric, readings (newest first), days: [{ day, value, trend, readingId }], goal: { status, target, now, ratePerWeek, reachedBy, basis: { days, from, to } } }`. `days` 1–3,660, all history when left out. |
| `POST /api/measurements/readings` | Body `{ metric, value, takenAt?, note? }`. `takenAt` is an ISO instant; now when left out. Answers `{ reading }`. |
| `POST /api/measurements/readings/{id}` | Body `{ value?, takenAt?, note? }`; a blank note removes it. Answers `{ reading }`. |
| `POST /api/measurements/readings/{id}/delete` | Answers `{ reading: null }`. |

A refusal (a future time, too many decimals, an archived or unknown metric, a unit change
with readings) is a 400 in words, with nothing written.

**CLI:**
- `bsk metric add Weight [--unit lb] [--decimals 1] [--target 185] [--better lower]`,
  `bsk metric list [--archived]`,
  `bsk metric set <metric> [--name …] [--unit …] [--decimals N] [--target N | --no-target] [--better lower|higher]`,
  `bsk metric archive <metric>` and `bsk metric restore <metric>`.
- `bsk metric show <metric> [--days N]`: the readings by day, the trend and the goal date.
- `bsk measure <metric> <value> [--at 7:10 | --at "2026-11-01 07:10" | --at 2026-11-01] [--note …]`,
  `bsk measure --edit <id> [--value N] [--at …] [--note …]`,
  `bsk measure --delete <id>` and `bsk measure --list [<metric>] [--days N]`.
- Every command takes `--json`.

### 4.7 Web (M2)

- **Measurements** page (`/measurements`), in the nav after Activity and in the command
  palette. Its first card is **Weight**:
  - *Start tracking weight* while there's no Weight metric, which makes it from the starter
    list and asks for a goal.
  - *Log weight*: a sheet with the value (a number pad on a phone), the date and time (now
    by default, any past time), and a note. Saving closes it and the chart updates.
  - The chart: dots for the readings, the trend line, the dashed target line labelled 185,
    hover or arrow keys reading a day in words, and 30 days, 90 days, 1 year and All.
  - The goal line under the chart (§2.3).
  - A table view of the readings, newest first, each with Change and Delete.
- **A new chart component** (`TrendChart`) in the house style of `DailyMetricChart`:
  hand-built SVG, a time axis spaced by date (not by bar), gaps left as gaps. It's built so
  M5 can stack several on one timeline.
- The mock store, a TypeScript port of `MetricTrend` with shared cases, unit tests, a golden
  path, and a11y in both themes and on a phone.

**As built (M2, 2026-11-07).** Filled in from how LifeOS already works:

- **Dots are each day's number**, the first weigh-in of the day, so the dots and the trend
  agree. The table lists every weigh-in; a later one that day says *Not the day's first* and
  has no trend of its own.
- **The trend line breaks across a gap of more than a week** (`TrendChart`'s 7 days). The
  dots either side still show, and the trend carries on from where it was (§2.2).
- **The axis** runs from the range's start, or from the first weigh-in when the whole history
  fits in the range, so a new metric doesn't sit at the right edge of an empty 90 days.
- **The range** defaults to 90 days and is remembered in the browser.
- **The goal line** reads "185.0 lb by Jan 21, 2027 at 0.9 lb a week, from 23 weigh-ins." With
  fewer than 7 days in the basis it says "an early estimate from 3 weigh-ins"; with one day,
  "Log another day to see when you'll reach 185.0 lb."; the other statuses as §2.3.
- **The time is sent only when it's changed.** Left alone, a new weigh-in is the kernel's
  now, and a changed weigh-in keeps its exact time.
- **An archived Weight** shows as not tracked; *Start tracking weight* makes a new one, as a
  name is unique only among metrics not archived.
- **Toasts**: "Logged 192.4 lb", "Saved 192.4 lb", "Deleted the reading".
- **Shared fixes found on the way**: a destructive button's dark-theme tint was 4.3:1 against
  its text (the Food Log's Delete, the Inbox's Drop); it's now 5:1. The chart tables'
  headings are padded like their cells.

### 4.8 Any metric (M3)

**Chris's answers (2026-11-07),** all four as recommended:

| Question | Answer |
|---|---|
| How the page holds many metrics (M4 adds ~20 lab values) | **A compact list, Weight open.** Weight keeps its full card at the top; every other metric is a row (latest value, when, a High/Low flag, a small trend line) that opens its full card in place. |
| Which normal ranges come filled in | **Blood pressure only:** systolic 90–119, diastolic 60–79 (the American Heart Association's "normal"). Body fat, waist and VO2 max depend on age and sex, so he sets those. |
| How blood pressure is charted | **One chart, two lines:** systolic and diastolic on one axis, each with its dots, trend and normal band; a day reads "122/78". |
| Logging on a phone | **Log weight stays** as the bottom bar; every other metric is logged from its own row or card. |

**Kernel (migration 0051):**

- **A normal range** is two optional attributes, `low` and `high`: numbers with at most the
  metric's decimal places, `low` ≤ `high`, written only by `MetricService` (so `bsk set`
  refuses them). Either can be left out ("up to 40 in" has only `high`).
- **A reading is flagged** `low` below `low` and `high` above `high`; the bounds themselves are
  normal, as on a lab report. The flag is worked out on read from the metric's range as it is
  now, so changing the range re-flags old readings. (Whether a lab reading carries its own
  printed range is M4's call.)
- **The starter list** gains Body fat (%, 1 place, lower), Waist (in, 1, lower), Systolic BP
  (mmHg, 0, lower, 90–119), Diastolic BP (mmHg, 0, lower, 60–79) and VO2 max (lab) (mL/kg/min,
  1, higher). Making one by name fills in its range.
- **Logging several at once:** `MetricReadingService.LogManyAsync` writes readings of several
  metrics with one time and note in one transaction, all checked before anything is written.
  Blood pressure uses it; M4's lab visit can build on it.
- `bsk.v_metric` gains `low` and `high`; nothing else in the schema changes.

**API and CLI:**

- `MetricRow` gains `low` and `high`; `POST /api/measurements/metrics[/{id}]` takes `low`,
  `high`, `clearLow` and `clearHigh`; every reading row gains `flag` (`low`, `high` or null).
- `POST /api/measurements/readings/batch`: `{ readings: [{ metric, value }], takenAt?, note? }`,
  answering `{ readings }`; all or nothing.
- `bsk metric add|set … [--low N] [--high N] [--no-low] [--no-high]`; `list` and `show` print the
  range, and `show` and `bsk measure --list` flag readings outside it.
- `bsk measure "Systolic BP" 122 --and "Diastolic BP=78"` logs them together.

**Web:**

- **Weight** stays at the top as M2 built it (or *Start tracking weight*), with **Log weight**
  in the phone's bottom bar.
- **Your other metrics** follow as rows, in starter-list order (Body fat, Waist, Blood
  pressure, VO2 max (lab)) and then his own by name. A row shows the newest value with High or
  Low, when, and the trend over the last 90 days; it opens the metric's card in place, and has
  its own Log button.
- **Blood pressure is one row and one card** made of the two metrics (paired by their
  starter-list keys): "124/79", one chart with both series, one Log sheet with both values,
  and a table with a row for each pair.
- **Add a metric** offers the starter metrics not yet tracked, or **your own**: name, unit,
  decimal places, goal, which way is better and normal range.
- **Each card's Edit** changes the name, unit (only while it has no readings), decimal places,
  goal, which way is better and normal range, or archives it. It replaces M2's goal dialog.
- **Archived metrics** are listed at the foot of the page with Restore.
- **The chart** shades the normal range; a flagged dot is drawn hollow, so it shows without
  colour, and the table says High or Low.

**As built (M3, 2026-11-07).** Filled in from how LifeOS already works:

- **Migration 0051** recreates `bsk.v_metric` with `low` and `high` on the end; nothing else
  in the schema changes. Ranges are kept to the metric's decimal places, and the low end must
  be at or below the high end ("The normal range's low end (300) is above its high end (30).").
  Changing a metric's decimals checks its range again, as it checks the goal.
- **A flag is worked out on read** from the range as it is now, in C# (`Metric.Flag`) and in
  the mock store, so a new range re-flags old readings at once.
- **Logging together** (`LogManyAsync`, `POST …/readings/batch`) takes up to 50 readings, one
  value for each metric, and refuses a metric given twice. `LogAsync` is the one-reading case
  of it. `bsk measure "Systolic BP" 122 --and "Diastolic BP=78"` splits each `--and` at its
  last `=`.
- **Blood pressure pairs by instant:** a pair is the two readings with the same time, which
  one write gives them. Changing a pair's time changes both. A half without its pair (one was
  deleted, or logged alone from the CLI) shows as "121/— mmHg", and its sheet can fill the
  missing half in at the pair's time.
- **A pair's flag** names each kind once: "High", "Low", or "High, low". Its readout and table
  read "121/78 mmHg (high)".
- **Rows**: the newest value and its flag, when, and a small trend line over the last 90 days
  (left out on a phone, where the name needs the room). The row button reads as one sentence
  to a screen reader: "Blood pressure: 121/78 mmHg, high, Thu, Nov 5, 7:30 AM". Which rows are
  open is remembered in the browser, as each card's range is.
- **The card** is Weight's, made general: Weight keeps its own testids and its big trend
  number; an opened row shows "Trend 117/76 mmHg", the goal line for each metric that has
  one (blood pressure's prefixed "Systolic:" or "Diastolic:"), and "Normal: systolic 90–119,
  diastolic 60–79 mmHg". A second series is drawn with square dots, so systolic and diastolic
  read apart without colour.
- **The axis doesn't stretch to show a range**: the band is clipped to the axis, which still
  fits the values, the trend and the goal.
- **Edit** has Name, Unit (locked once there are readings, saying why), Decimal places, Goal,
  Better (lower or higher), and the range's Low end and High end; a blank field clears it.
  Blood pressure's Edit picks Systolic or Diastolic, and its Archive archives both.
- **Add a metric** lists the starter metrics not tracked yet as one-click choices (blood
  pressure adds whichever half is missing), then *Your own*. The new metric's row opens.
- **Toasts**: "Logged 122/78 mmHg", "Saved 117/78 mmHg"; a single metric's are M2's.

### 4.9 Lab results (M4)

**Chris's answers (2026-11-08),** all as recommended but the panels. His first labs are drawn on
2026-11-09, with results soon after: the first real use.

| Question | Answer |
|---|---|
| A result's normal range | **The report's.** Each result keeps the range printed beside it, and its High or Low follows that range for good, as the report does. The form fills each range in from the last report (common adult ranges the first time), so only the ones that differ are changed. The chart shades the newest report's range. |
| Where results go | **A Lab results section** below Your metrics: the visits newest first (date, lab, how many results were outside the range, the report), each opening to its results laid out like the report; then each test under its panel's heading, opening its chart across visits. Lab tests stay out of Your metrics. |
| Which panels the starter list has | **The CMP, the lipid panel and A1c only.** Any other test on a report is added from the visit form the first time it comes up. |

**Kernel (migration 0053):**

- **A lab visit** is a versioned `measurement` event, declared, with `occurred_at` when the
  sample was collected:
  `{"lab_visit": {"id": "<uuid>", "lab": "Quest Diagnostics", "note": "…", "report": "<artifact id>"}}`.
  It isn't a subject: it's something that happened, corrected now and then but never managed
  over time. An edit writes a newer version with the same id; a delete writes
  `{id, voided: true}`; the newest `recorded_at, id` wins, as readings do.
  - `lab` and `note` are optional, one line each, up to 60 and 140 characters.
  - **The report** (a PDF or a photo of the paper, up to 10 MB, known by its first bytes) is an
    artifact. The version that adds it is the event that stores the bytes, as a space photo's
    first version is; later versions carry `report`. A version with neither has no report.
- **A result** is a reading of a test's metric with three more keys: `visit` (the visit's id) and
  `low` and `high`, the range printed beside it (either or both). Its time is the visit's:
  changing the visit's time moves its results, and a result's own time can't be changed apart
  from it.
- **Flags:** a reading with a range of its own is flagged against that range, for good; any other
  against its metric's range as it is now (M3).
- **A test's range follows its newest report:** after any write to a visit, each test it touched
  takes the range of its newest result that has one. So the chart shades the newest report's
  range, and the next visit's form starts from it.
- **Lab tests** are metrics with a `panel` attribute: `cmp`, `lipids`, `a1c`, or `other` for one
  added from the visit form. Only `MetricService` writes it. Lab tests are listed under Lab
  results, not Your metrics, and Add a metric doesn't offer them.
- **The starter list** gains the panels' tests, in the order reports print them. The ranges are
  MedlinePlus's (checked 2026-11-08), with the bounds normal; HDL's is left out because its low
  end depends on sex (40 for men, 50 for women), so the first report fills it in.

  | Panel | Test | Unit | Decimals | Better | Range |
  |---|---|---|---|---|---|
  | CMP | Glucose | mg/dL | 0 | — | 70–100 |
  | | BUN | mg/dL | 0 | — | 6–20 |
  | | Creatinine | mg/dL | 2 | — | 0.60–1.30 |
  | | eGFR | mL/min/1.73m2 | 0 | higher | at least 60 |
  | | Sodium | mmol/L | 0 | — | 135–145 |
  | | Potassium | mmol/L | 1 | — | 3.7–5.2 |
  | | Chloride | mmol/L | 0 | — | 96–106 |
  | | CO2 | mmol/L | 0 | — | 23–29 |
  | | Calcium | mg/dL | 1 | — | 8.5–10.2 |
  | | Total protein | g/dL | 1 | — | 6.0–8.3 |
  | | Albumin | g/dL | 1 | — | 3.4–5.4 |
  | | Total bilirubin | mg/dL | 1 | — | 0.1–1.2 |
  | | Alkaline phosphatase | U/L | 0 | — | 20–130 |
  | | AST | U/L | 0 | — | 8–33 |
  | | ALT | U/L | 0 | — | 4–36 |
  | Lipid panel | Total cholesterol | mg/dL | 0 | lower | up to 199 |
  | | HDL | mg/dL | 0 | higher | — |
  | | Triglycerides | mg/dL | 0 | lower | up to 149 |
  | | LDL | mg/dL | 0 | lower | up to 99 |
  | | Non-HDL cholesterol | mg/dL | 0 | lower | up to 129 |
  | A1c | A1c | % | 1 | lower | up to 5.6 |

  "Below 200" is "up to 199" because the bounds themselves are normal. Triglycerides' 150 is
  the usual adult cutoff, which MedlinePlus doesn't print.
- **Views:** `bsk.v_lab_visit` (the newest live version of each visit, with its report's file
  name, type and size); `bsk.v_metric_reading` gains `visit_id`, `low` and `high` at its end;
  `bsk.v_metric` gains `panel`. An index keeps the visit view off a full scan, as 0050's does.
- **`LabVisitService`** saves a visit and all its results as one write: a test not tracked yet
  is made first (from the starter list by its name, or with the unit given, under `other`);
  in an edit, the results left out are deleted and the rest re-versioned only when they
  change; every refusal is checked before anything is written. It also adds or removes the
  report, deletes a visit with its results, and lists them.
- **`/mcp`:** visits and results come under the Health switch. With health off,
  `recent_changes` leaves out readings and visits (closing a gap M1 left for readings), and
  each is listed once, as its newest version.

**API and CLI:**

| Endpoint | Does |
|---|---|
| `GET /api/measurements/visits` | `{ today, visits }`, newest first; each `{ id, lab, note, collectedAt, collectedOn, collectedLocal, report: { id, filename, contentType, byteSize } \| null, results, outside, loggedAt, versions }`, its results ordered as the report prints them. |
| `GET /api/measurements/visits/{id}` | `{ visit }`. |
| `POST /api/measurements/visits` | `{ collectedAt?, lab?, note?, results: [{ metric, value, low?, high?, note?, unit? }] }`, answering `{ visit }`. `unit` adds a test that isn't tracked or on the starter list. |
| `POST /api/measurements/visits/{id}` | The same body, with `reading` on each result kept; the visit's results become the ones given. `collectedAt` left out keeps the time, and a blank `lab` or `note` clears it. |
| `POST /api/measurements/visits/{id}/delete` | Deletes the visit and its results; `{ visit: null }`. |
| `POST /api/measurements/visits/{id}/report` | Multipart `report`: adds or replaces the report. `…/report/delete` removes it. |

- Reading rows gain `visitId`, `low` and `high`; metric rows gain `panel`.
- `bsk lab add [--at "2026-11-09 08:15"] [--lab …] [--note …] "Glucose=104@70..100" "LDL=131@..99" "TSH=2.1@0.45..4.5" --unit "TSH=mIU/L"`;
  a range is `low..high`, `..high` or `low..`.
- `bsk lab list`, `bsk lab show <visit>`, `bsk lab set <visit> [--at …] [--lab …] [--note …] [results…] [--drop <test>]`,
  `bsk lab report <visit> <file>` (or `--remove`) and `bsk lab delete <visit>`. A visit is
  named by its id or its first 4+ hex digits. Every command takes `--json`.

**Web:**

- **Lab results** follows Your metrics, with *Log a lab visit*. With no visits it says what a
  visit holds.
  - **Visits**, newest first: "Nov 9, 2026 · Quest Diagnostics", "21 results, 3 outside the
    range", and the report. A visit opens in place to its results by panel (Test, Result, Range,
    Flag), with *Edit visit* and the report to open.
  - **Tests by panel** (CMP, Lipid panel, A1c, Other tests): each a row as in Your metrics, but
    with no Log button, opening its card. The card's table gives each result's range; its Change
    opens the visit.
- **The visit sheet**, for a new visit and for an edit: when it was collected (the date; the
  time is optional), the lab (suggesting ones used before) and a note; the panels as toggles,
  each listing its tests with a Result and the range's two ends, filled in; tests tracked under
  Other tests listed too; *Add a test* (name and unit) for anything else. Blank results are
  skipped. The report (PDF or photo) is optional and uploads after the visit saves. An edit
  also has *Delete visit*.
- **Toasts:** "Saved 21 results from Nov 9", "Deleted the lab visit".

**As built (M4, 2026-11-08).** Filled in while building it:

- **A visit can have no results yet**, so it can be logged when the blood is drawn and filled
  in when the results come back. It reads "No results yet", and its toast is "Saved the lab
  visit from Nov 9".
- **When it was collected:** the time is optional. With none, it's noon on that day, or now
  for today before noon, as a reading with no time is.
- **A test added in the sheet** takes as many decimal places as its values are written with
  (up to 3), and goes under Other tests. Once made, it's listed in the sheet like any tracked
  test.
- **An archived test** isn't offered in the sheet unless the visit already has a result for
  it; logging to one is refused ("'TSH' is archived: bring it back before adding results to
  it.").
- **Which panels the sheet opens with:** in an edit, the ones the visit has results on; for a
  new visit, the ones the newest visit had (the starter three the first time). In an edit, a
  result left blank, or on a panel turned off, is deleted, and the sheet says so.
- **The report uploads after the visit saves.** If the upload fails, the visit stays saved and
  a second toast says "The visit is saved, but its report isn't: …".
- **A lab test's card** leads with "Latest 104 mg/dL (high)" instead of a trend, says
  "Newest report's range: 65–99 mg/dL", and opens on all time because labs come months apart.
  Its table shows each result's range where other cards show the trend. A result has *Change …
  in its lab visit* and no Delete of its own: it's deleted from its visit.
- **The results table on a phone:** the unit is set small after its number, and eGFR's long
  unit wraps under it, so the table fits a 375 px screen without scrolling sideways. A range
  never breaks at its dash.
- **The mock** seeds one visit 120 days back from Quest Diagnostics: 21 results with the
  report's own ranges, Glucose, LDL and non-HDL cholesterol high, and a report to open.

### 4.10 Compare (M5)

- **A series** is anything with a value per day: each metric's day values, and from Oura:
  steps, active calories, sleep score, readiness, HRV (`average_hrv`), lowest heart rate,
  temperature deviation; from the Food Log: calories and the diet score.
- `GET /api/series?keys=metric:<id>,oura.sleep_score,…&from=&to=` answers each series's
  points, with its unit and label.
- The Compare view picks 2–4 series and stacks them with one shared time axis and a hover
  line across all of them, with weekly averages for noisy series.

## 5. Phases

| Phase | What lands | Migration | Pushable alone |
|---|---|---|---|
| **M0** | This plan, MEASURE-1 to MEASURE-6, D20 | — | Yes |
| **M1** | The Metric type, readings, views, `MetricTrend`, `MetricService`, `MetricReadingService`, `bsk metric`, `bsk measure`, the `/api/measurements` endpoints, Metric items under `/mcp`'s Health switch. Nothing on the web yet. | 0050 | Yes |
| **M2** | The Measurements page with the Weight card, `TrendChart`, the TypeScript port and the mock store. Chris sets his 185 goal and starts logging. Calibration: α and the goal-date basis after a few weeks of use. | — | After M1 |
| **M3** | Any metric on the web: add one from the starter list or from scratch (name, unit, decimals, goal, which way is better), a card for each, normal ranges and flags. Body fat, waist, blood pressure (two metrics entered together), VO2 max (lab). | 0051 | Yes |
| **M4** | Lab results: a lab visit (date, lab, all its values at once, the PDF), reference ranges per reading, high and low flags, the CMP, lipid and A1c panels in the starter list. Design detail is settled before it starts. | 0053 | Yes |
| **M5** | Compare: the series read, the stacked chart, the series picker; Oura's VO2 max estimate pulled as a series. | — | Yes |
| **M6** | Agents: `/mcp`'s `health` tool gains the readings and the goal, and the series read for a range. | — | Yes |

- Each phase updates the Build fields of MEASURE-1 to MEASURE-6 as it advances them.
- Checks before each commit: `./test.ps1` (.NET), and from `web/`: `npm test`,
  `npm run test:e2e` and `npm run a11y` after UI changes. CI doesn't run Playwright.

## 6. How this was settled

- **2026-11-07, Chris's ask:** "I would like to add the ability to record custom health
  metrics in LifeOs. Oura ring does not track weight, and this will be the first use case for
  health metrics. However, I will track a wide variety of metrics over time within LifeOs,
  such as blood work (CMP, A1c, etc), body measurements, VO2Max and the like. Eventual goal is
  to be able to chart all or some of this together with other Oura stats to see trends and
  relationships."
- **Claude's analysis** (in chat) proposed a general measurements feature built on the Food
  Log's and Environment's patterns, the calls in §1, and nine questions.
- **Chris's answers, 2026-11-07:**
  - "I have an Arboleaf smart scale. I won't worry about importing past weight data, as long
    as I can add new weight data by hand for previous dates (if I decide to)."
  - "No health XP for measurements. They are info only."
  - "Goal weight: 185, yes target line and add a trending date based on extrapolation of my
    weigh-ins trend….. once I have more than 1 reading"
  - "Agree with other defaults": pounds and inches, the first weigh-in of the day, the page
    called Measurements, past labs and their PDFs kept with each visit (M4), VO2 max from both
    Oura and a lab, and agents under the same Health switch as Oura.
- **Filled in here:** the trend's smoothing (α = 0.2, exponential, gap-aware), the goal date
  from a 28-day least-squares rate measured from the trend, `better` as the goal's direction,
  noon for a date with no time, the unit locked once there are readings, and the past-weight
  import dropped from the phases.

## 7. Later

Each of these has to earn its place through use.

- **The scale feeding LifeOS:** the Arboleaf app sends readings to Apple Health, Google
  Health Connect and Fitbit. Of those, only Fitbit has a web API a server can read, so the
  likely path is Fitbit's body weight log as an `observed` source, as Oura's records are
  (D14). **[unverified]** Checked 2026-11-07 against retailer and app-store listings only;
  check Fitbit's API before planning it.
- **A Goal tied to a metric:** "185 lb by June 1" as a Goal that reads its progress from
  Weight.
- **A weigh-in checking off a habit** ("Weigh in each morning").
- **Claude reading a lab PDF** and filling in the values for Chris to confirm (needs `/mcp`
  writes, agent-access plan Phase 2).
- **Correlations with a delay:** "readiness the day after a late meal", weight against
  weekly calories.
- **Patient-portal import** (FHIR) for past lab results.
- **Unit conversions** (kg, cm, mmol/L), if a source ever sends them.

## Status

- [x] M0 — Plan + requirements (MEASURE-1 to MEASURE-6, D20)
- [x] M1 — Metrics and readings in the kernel (migration 0050)
- [x] M2 — Weight on the web (no migration; calibration of α and the basis after a few weeks)
- [x] M3 — Any metric on the web (migration 0051)
- [x] M4 — Lab results (migration 0053)
- [ ] M5 — Compare with Oura and the Food Log
- [ ] M6 — Agents
