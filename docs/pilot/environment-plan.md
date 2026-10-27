# Environment Plan — spaces, ratings and Environment's level

The design and build order for Environment's rules: the home's spaces, their ratings, the
Home Score, and Environment's level (HOME-1 to HOME-3 and REWARD-9 in
[ui-requirements.md](ui-requirements.md)), and photos of each space from a baseline (HOME-4,
§8). The decision behind it is **D18**, in the same
doc. It builds on Health's levels v2 in [xp-domains-plan.md](xp-domains-plan.md) §7, which it
reuses almost whole, and on the rewards ledger in [rewards-plan.md](rewards-plan.md). The
requirements say *what*; this doc says *how* and *in what order*, written so an agent with no
session context can pick up any phase.

**Scope agreed with Chris on 2026-10-27**, over three rounds of Q&A (§6). Until now
Environment collected XP and nothing else. With these rules, its level follows how well the
home is kept, and Environment XP sets how fast it can climb.

**Keep it small.** The thing under test is whether rating the home every few days, and
watching a Home Score, keeps it tidier, cleaner and nicer to be in. Everything in §7 has to
earn its place through use.

The formulas were settled in the Claude Doc **Environment XP Model — Formulas**
(<https://claude.ai/artifact/KEhekb6mstZT7uT6rqjWQS>) and checked with a Python model and
the **Environment XP Simulator** (<https://claude.ai/artifact/9w9LwByLbKyFk3u2TiGN9p>). This
doc is the build-side copy.

## 0. Read these first

- [xp-domains-plan.md](xp-domains-plan.md) §7: levels v2. Environment uses its shown level,
  fall, ranks, best level and rank, rank-up Gems, multiplier and exception days as they
  are. Only the consistency test and the curve's price differ.
- [food-log-plan.md](food-log-plan.md) §3.1: food entries as versioned `measurement`
  events. Ratings are stored the same way. The 1–5 half-point picker (§8) is reused for
  ratings.
- [ui-requirements.md](ui-requirements.md): HOME-1 to HOME-3, REWARD-9, D9 (archive), D17,
  D18.
- [kernel-build-plan.md](kernel-build-plan.md): how a phase lands (migration + `bsk` verb +
  view + tests via `./test.ps1`, strict build).
- Prior art to copy:
  - `DomainWalker`'s prefix sums: the consistency tests in O(1) per level.
  - `v_meal` (0034) and `v_exception_day` (0043): newest version per id wins, voided
    versions drop out.
  - `ExceptionDayService`: a write that folds the configured domains before and after and
    answers `progress`.

## 1. The rules (agreed 2026-10-27)

| Question | Decision |
|---|---|
| Is Environment still XP-based? | **Yes, with a second job for the home.** XP is the reward and sets the pace of the climb. The **Home Score** sets how high the level can hold. Rejected: XP only (measures effort, not the home), the home only (XP would move nothing, and ranks would climb in months), and the home paying daily XP (blurs the two). |
| The level | **The lower of the lifetime level** (from paid Environment XP) **and the condition level** (what the last 90 counted days' Home Scores support). Then Health v2's shown level: it rises at once, falls 1% a day, and keeps a best level and best rank. |
| Spaces | The home is split into spaces, each with a **weight** (1–10, how much it counts) and a **pace** (1–5, how fast it gets messy; 3 is normal). Chris's nine are in §2.1. A grouped space is rated as one. |
| Ratings | Each space is rated on **Tidiness, Cleanliness and Aesthetics**, 1–5 in half points, with the Food Log's picker and an example for each score. Points = (rating − 1) × 25. |
| Space score | **45% Tidiness, 35% Cleanliness, 20% Aesthetics.** A space counts once all three parts have a rating. |
| Ratings fade | Each part slips from the second day after it's rated: Tidiness half a point every 3 days, Cleanliness every 7, Aesthetics every 90. Pace multiplies the Tidiness and Cleanliness fades by pace / 3. A fade never goes below a rating of 2. Chris: "Ratings fade sounds like a really good new mechanic." |
| Home Score | The weighted average of the space scores, 0–100, at the end of each day. |
| Higher ranks need steadier upkeep | Level L needs the Home Score at or above a **bar** (30 at level 2, 85 at 100) on a **share** of the last 90 counted days (50% at level 2, 90% at 100), and enough days at that bar so far (min(90, L + 1)). |
| The lifetime curve | **Health's cubic curve at a quarter of the price:** 63,922 XP reaches level 100, about 7 years at 25 paid XP a day. Set from the clean-home rate, not the catch-up rate. |
| XP | 1 XP a minute of cleaning or tidying (the timed trigger), plus Task, Project, Goal and Habit prices, as today. **No daily cap.** No XP for the Home Score itself. |
| The multiplier's successful day | A **kept-up day:** the Home Score at or above the bar of the shown level going into the day. Health's tiers, window and freezing. |
| Exception days | The same marks as Health's, and **they also pause fades.** |
| Rank-up Gems, celebrations | As Health v2: 1 a rank, 5 for Grandmaster, the first time the best rank reaches it; celebrations only for a new best. |
| The routine | A Sunday walk-through (review day), quick checks of the office and kitchen, Aesthetics in the monthly review, and the three biggest drags as the next chores (§3.3). |

## 2. The formulas

### 2.1 Chris's spaces

| Space | Weight | Share | Pace |
|---|---|---|---|
| Office | 5 | 18.5% | 5 |
| Kitchen | 4 | 14.8% | 5 |
| Master Bath | 4 | 14.8% | 3 |
| Living Area | 3 | 11.1% | 4 |
| Stairway/Foyer/Halls | 3 | 11.1% | 4 |
| Other Baths | 3 | 11.1% | 2 |
| Bedroom | 2 | 7.4% | 1 |
| Exercise Room | 2 | 7.4% | 1 |
| Garage | 1 | 3.7% | 2 |

These are Chris's data, entered through the app in E5, not seeded by a migration.

### 2.2 Ratings, fades and the Home Score

For a part x of space s, rated r (1–5 in halves) on day d₀, read on day d:

```
p₀      = (r − 1) × 25                          points, 0–100
f       = max(0, (d − d₀) − 1)                   fading days, not counting exception days
rate_x  = 12.5 / N_x,  N = 3 (T), 7 (C), 90 (A)  points a day at pace 3
m       = pace / 3 for T and C, 1 for A
v       = max(min(p₀, 25), p₀ − rate_x · m · f)
S_s     = 0.45 T + 0.35 C + 0.20 A
H_d     = Σ w_s · S_s / Σ w_s                    over spaces counted on day d
```

- **Counted spaces on day d:** every part rated on or before d, and not archived on or
  before d (D9's `archive_change` carries the time).
- **Weights and paces apply to all of history.** Changing one re-reads past days, as any
  retune does (D12).
- **A later rating replaces a part's earlier one** from its day on. A rating can leave a part
  out: a quick check rates Tidiness and Cleanliness only.
- **Whole numbers,** so C# and TypeScript agree to the digit. Points are kept in
  ten-thousandths, rounding down at each step:
  - `p₀ = (2r − 2) × 125,000`
  - `fade = ⌊125,000 × f × k / (3 × N_x)⌋`, where k = pace for T and C, and 3 for A
  - `v = max(min(p₀, 250,000), p₀ − fade)`
  - `S = ⌊(45 T + 35 C + 20 A) / 100⌋`
  - `H = ⌊Σ w S / Σ w⌋`
- **Drag,** for "Fix next": `⌊w_s × part weight × (1,000,000 − v) / (Σ w × 100)⌋`, the
  Home Score a part costs. A kitchen tidiness of 50 costs 3.3 points.

### 2.3 The condition level

Counted days are settled days (4 a.m. the next morning, as Health's) that have a Home Score
and aren't exception days. Over the last `N = min(90, counted days)` of them, level L (2–100)
qualifies when both tests pass:

```
kept up:  #{d in window : H_d ≥ Q(L)}             ≥  ⌈P(L) · N⌉
age:      #{counted days so far : H_d ≥ Q(L)}     ≥  min(90, L + 1)

Q(L) = 30 + 55 (L − 2) / 98       the bar, exact (no rounding)
P(L) = 0.50 + 0.40 (L − 2) / 98   the share, as Health's
```

- **C** is the highest qualifying L, or 1. Q and P rise with L, so the walk stops at the first
  failure. C stays where it was on a day that isn't counted.
- **Whole numbers:** with H in ten-thousandths, the bar test is
  `98 × H ≥ (2,940 + 55 (L − 2)) × 10,000`, and `⌈P(L) · N⌉ = ⌈N (490 + 4 (L − 2)) / 980⌉`.
- `Q(51) = 57.5` is the only half among whole levels, which is why Q isn't rounded.

What each rank takes to hold its first level over a full window:

| Rank | Levels | Bar | About an average rating of | Days of 90 at the bar | Lifetime XP to reach |
|---|---|---|---|---|---|
| Novice | 1–9 | — | — | — | 0 |
| Apprentice | 10–19 | 34.5 | 2.4 | 48 (53%) | 369 |
| Journeyman | 20–29 | 40.1 | 2.6 | 52 (57%) | 1,517 |
| Adept | 30–39 | 45.7 | 2.8 | 56 (61%) | 3,648 |
| Expert | 40–49 | 51.3 | 3.1 | 59 (66%) | 6,974 |
| Veteran | 50–59 | 56.9 | 3.3 | 63 (70%) | 11,707 |
| Elite | 60–69 | 62.6 | 3.5 | 67 (74%) | 18,060 |
| Master | 70–79 | 68.2 | 3.7 | 70 (78%) | 26,245 |
| Champion | 80–89 | 73.8 | 4.0 | 74 (82%) | 36,476 |
| Legend | 90–99 | 79.4 | 4.2 | 78 (86%) | 48,964 |
| Grandmaster | 100 | 85.0 | 4.4 | 81 (90%) | 63,922 |

### 2.4 The lifetime curve, the shown level and the rest

- **Lifetime curve:** `CubicCurve(Linear: 15, Quadratic: 6, Cubic: 0.2125)`, every term of
  Health's divided by 4. As total XP:
  `XP(L) = 15n + 3n(n − 1) + ⌈17n(n − 1)(n − 2) / 480⌉`, n = L − 1. The existing
  `CubicCurve` takes these as they are.
- **Shown level, fall, rank, best:** xp-domains plan §7.2, unchanged. The fall pauses while
  the last 7 counted days pass level S's **kept-up** test (N = 7, no age test).
- **The multiplier's successful day:** `H_d ≥ Q(S)`, with S the shown level going into d.
  The tiers, the 30-day window with exception days skipped, and freezing at issue are
  Health's.

## 3. What it produces

### 3.1 Routines

From the Python model with Chris's nine spaces: 4 seeded runs of 8 years each, medians.
Paid XP a day is noted, and 15% of days earn none. The simulator's JavaScript port lands
within 3 levels of every line.

| Routine | Year 1 | Year 3 | Where it holds |
|---|---|---|---|
| Everything near-perfect, every space every 3 days, decor 4.5 (40 a day) | 54 | 85–86 | 100, Grandmaster at about 4.4 years |
| Daily office and kitchen checks, Sunday walk-through, decor 4 (35) | 51 | 80–82 | 91–93, Legend |
| Sunday + Wednesday check of the four busiest spaces (28) | 46–47 | 73–74 | 76–77, Master |
| Sunday + Wednesday check of office and kitchen (25) | 44 | 70–71 | 74, Master |
| Sunday walk-through only (20) | 40 | 64–65 | 67–68, Elite |
| Messy home, rated weekly (15) | 23–26 | 23–25 | 24–26, Journeyman |
| Good home, rated monthly (20) | 23–24 | 21–25 | 23–25, Journeyman |

Settled standards: Tidiness 4–5, Cleanliness 3.5–4.5, decor 3.5, unless noted.

### 3.2 The messy-home catch-up

Chris's home is messy now, so he'll earn 60–80 XP a day for a while. Median shown level,
Sunday + Wednesday routine settling at Tidiness 4.5, Cleanliness 4, decor 3.5:

| | 1 mo | 3 mo | 6 mo | 1 yr | 2 yr | 3 yr | 5 yr |
|---|---|---|---|---|---|---|---|
| Already clean, 25 a day | 13 | 23 | 32 | 44 | 60 | 70 | 74 |
| Messy, clean in 3 months, 80 a day then 25 | 8 | 28 | 45 | 53 | 66 | 72 | 74 |
| Messy, clean in 3 months, 25 a day throughout | 8 | 23 | 32 | 44 | 60 | 70 | 74 |
| Messy, clean in 6 months, 60 a day then 25 | 3 | 13 | 48 | 56 | 68 | 72 | 74 |

- While the home is messy it holds the level down, and the extra XP waits as headroom. At
  3 months, effort has earned level 39 and the home holds 28.
- Once the home is clean, the level jumps up to meet the headroom.
- The head start is about 6 months and gone by year 3. The routine, not the history, sets
  where the level holds.
- Messy days stay in the 90-day window, so the level trails the ratings for up to 3 months
  after the home is clean.

### 3.3 Breaks

From about level 73 (Sunday + Wednesday routine, 25 a day), with no ratings and no XP during
the break:

| Break | Level on return | Lowest | Back to where it was |
|---|---|---|---|
| 7 days | 72–73 | 71–72 | 50–90 days after return |
| 14 days | 69–70 | 67–69 | about 85 days |
| 30 days | 63 | 59 | about 85 days |
| 60 days | 46–47 | 42 | about 90 days |
| Any length, marked as exception days | No change | No change | No change |

## 4. Design

### 4.1 Space: a new subject type

A space is a durable thing Chris manages over time: he adds one, renames it, reweights it, and
archives it when he moves. So it's a subject, the **16th type, `Space`**.

- **Attributes:** `weight` (whole number 1–10, default 3) and `pace` (whole number 1–5,
  default 3). The title is its name. Written by `SpaceService`, so `bsk set` refuses both
  keys, as it does `cover`.
- **No status.** A space is there or archived (D9's `archive_change`), like an identity
  statement. An archived space leaves the Home Score from the day it was archived, and its
  history stays.
- **No relations** are needed. Areas (D1) are life areas, not rooms, so a space isn't one.

### 4.2 Ratings: versioned `measurement` events

A rating is something Chris observed about a space on a day: an event, like a food entry.

- **Event:** kind `measurement`, provenance `declared`, `occurred_at` = the day rated at noon
  in the home zone for a backdated rating, or now for today's. `recorded_at = now`.
- **Payload:**
  `{"space_rating": {"id": "<uuid>", "space": "<space subject id>", "day": "2026-10-27", "tidiness": 4.5, "cleanliness": 4, "aesthetics": 3.5, "note": "…"}}`.
  - Any of the three parts can be left out, but not all of them.
  - Each part is 1–5 in halves.
  - `note` is optional: one line, at most 140 characters, with `WorkoutNoteService`'s
    rules.
- **Edges:** a `concerns` edge to the space, so its detail lists its ratings.
- **Versions:** an edit writes a newer version with the same `id`. A delete writes
  `{"space_rating": {"id": "…", "voided": true}}`. Newest `recorded_at, id` wins, as meals
  do. No new event kind.
- **"Same as last time"** is written as an ordinary rating carrying each part's last rated
  value (not its faded value), dated today. The service works the values out, so the
  history shows what Chris confirmed.

### 4.3 Migration 0044 (`0044__spaces.sql`)

- The subject-type check gains `Space`, mirrored in `SubjectTypes` and the vocabulary drift
  test.
- `bsk.v_space`: one row per Space subject, with `id`, `title`, `weight`, `pace`,
  `archived_at` (from the newest `archive_change`, null when not archived) and `created_at`.
  Weight and pace fall back to 3 when missing or malformed (`jsonb_typeof` checks).
- `bsk.v_space_rating`: the newest version per rating id that isn't voided, with `id`,
  `space_id`, `day` (via `bsk.try_to_date`), `tidiness`, `cleanliness`, `aesthetics` (as
  numeric, null when left out), `note`, `rated_at` and `event_id`.
- A partial index:
  `event_space_rating_idx ON bsk.event ((payload->'space_rating'->>'id'), recorded_at) WHERE kind = 'measurement' AND jsonb_exists(payload, 'space_rating')`.
- `COMMENT ON VIEW` naming HOME-1, HOME-2 and D18, `GRANT SELECT … TO bsk_reader`, and both
  views added to `ReaderRoleTests`.

### 4.4 The Home Score in Domain

`HomeScore` (Domain, `Home.cs`) is a pure function, and it's the only source of Home Score
numbers:

```
HomeScore.Days(spaces, ratings, exceptionDays, from, to) → HomeDay[]
HomeDay(Day, Score?, Spaces: SpaceDay[])           Score null before any space counts
SpaceDay(SpaceId, Score, Parts: PartValue[3])
PartValue(Part, Rated, RatedOn, Value, Drag)
```

- `HomeRules` holds the tunables beside it: part weights 45/35/20, fade days 3/7/90, grace 1
  day, floor rating 2, pace divisor 3. Its drift test checks the weights sum to 100 and the
  floor sits between 1 and 5.
- Walking day by day keeps it O(days × spaces × 3). That's about 2,600 × 9 × 3 for 7 years,
  which is trivial.

### 4.5 Rules in code

`DomainRules` has one concrete `ConsistencyRule` today (xp-domains plan §7.4.1). E3 makes
the consistency test a rule of its own:

- **`IConsistencyRule`** answers what the walk needs:
  - `MaxLevel` and `WindowDays`
  - `AgeNeeded(level)`
  - whether a day reaches a level: `Reaches(level, day facts)`
  - `Qualifies(level, window counts)`
  - `Describe(level)`, in words
- **Health's `ConsistencyRule`** implements it unchanged: average base XP, active days and
  age.
- **`HomeConditionRule(WindowDays: 90, BarFrom: 30, BarTo: 85, ShareFrom: 0.50,
  ShareTo: 0.90)`** answers:
  - `Bar(level)` in ten-thousandths, exact
  - `KeptUpDaysNeeded(level, n)`, as Health's `ActiveDaysNeeded`
  - `AgeNeeded`
- **`DomainWalker`** keeps one prefix-sum array per distinct threshold the rule names.
  Health's are its distinct active minimums plus base XP. Environment's are its 99 bars,
  about 260,000 integers over 7 years. The tests stay O(1) per level.
- **`PeriodFacts` gains `HomeScore`** (long?, ten-thousandths). A new success rule,
  **`KeptUpAtShownLevel(HomeConditionRule)`**, passes when the score reaches the shown
  level's bar.
- **Counted days:**
  - Health counts every settled day since its first XP.
  - Environment counts settled days with a Home Score.
  - Both skip exception days.
  - The walk starts at the earlier of the first paid XP and the first Home Score, so XP
    earned before the first walk-through still builds the lifetime level.
- **The back-on-track test** calls the rule's `Qualifies` over the last `BackOnTrackDays`
  counted days, with no age test.
- **`LifeDomains.Environment`:**
  - `CubicCurve(15, 6, 0.2125m)`, `HomeConditionRule` as above, `FallRule(1, 0, 7)`,
    `KeptUpAtShownLevel`
  - Health's ranks, multiplier and Gems
  - no passive triggers
  - `Configured = [Health, Environment]`
- **The drift test adds:** the bar and share run upward, the bar stays inside 0–100, and
  Environment's ranks match Health's titles. Every rule it already checks now runs over both
  domains.

### 4.6 Daily credit for Environment

`ILedgerRepository.GetDomainDaysAsync` already returns each domain's XP days, exception days
and `SettledThrough`. For Environment, `DomainService` also reads the spaces and ratings
(`ISpaceRepository`, on the same connection) and computes the Home Score series. It adds the
score to each `DomainDay` as `HomeScore`, including days with a score and no XP. Every fold
caller goes through this, so they all see the same days: `DomainService`, `RewardService`,
`DomainPricing`, the simulator's `--replay` and the API reader.

Every rating write folds the configured domains before and after, pays any rank-up it
unlocks, and answers `progress`, as exception-day writes do. A rating can move a level both
ways: a backdated one changes past days.

### 4.7 API and CLI

| Endpoint | Does |
|---|---|
| `GET /api/environment` | `{ today, settledThrough, homeScore, spaces: [{ id, name, weight, pace, share, archivedAt, score, parts: { tidiness, cleanliness, aesthetics: { rated, ratedOn, now, drag } }, lastRatedOn }], fixNext: [{ spaceId, part, drag }] }`. `fixNext` holds the three biggest drags. |
| `GET /api/environment/days?days=N` | Default 90, 1–731. Per day: the Home Score, each space's score, and exception days. |
| `GET /api/environment/ratings?space=&days=N` | The ratings, newest first. |
| `POST /api/environment/spaces` | Body `{ name, weight?, pace? }`. Answers the space. |
| `POST /api/environment/spaces/{id}` | Body `{ name?, weight?, pace? }`. Archiving and restoring use the existing `/api/archive` and `/api/restore`; a restore is refused while another space uses the name, whichever route restores it. |
| `POST /api/environment/ratings` | Body `{ day?, ratings: [{ space, tidiness?, cleanliness?, aesthetics?, note?, same? }] }`. One walk-through saves many spaces. Answers `{ ratings, awards, progress }`. A future day, a part outside 1–5 or not in halves, or an empty rating is a 400 in words. |
| `POST /api/environment/ratings/{id}` | Edit. Answers as above. |
| `POST /api/environment/ratings/{id}/delete` | Delete. Answers as above. |

**`GET /api/domains`** (and `days`, `report`, `progress`): Environment's row becomes
configured. Its `consistency` block gains a **`kind`**:
- `"xp"` for Health, unchanged.
- `"home"` for Environment: the window's days and dates, the kept-up days, the average Home
  Score, and what the next level needs (bar, kept-up days needed against days held, age).

`current` carries today's Home Score against the bar of the level going into today (what
counts the day as kept up), and `shown.bar` is the shown level's bar (what keeps that level on
track: "68.2+ keeps level 70 on track"). Each day of `days` carries its Home Score and the bar
of its level going in. The web branches on `kind`.

**CLI:**
- `bsk space add "Office" --weight 5 --pace 5`, `bsk space list`,
  `bsk space set <space> [--name …] [--weight N] [--pace N]` and `bsk space archive <space>`.
  `<space>` is a name, unique among unarchived spaces, or an id.
- `bsk rate <space> <tidiness> <cleanliness> <aesthetics>` and
  `bsk rate <space> --tidiness 4 --cleanliness 4.5` (a partial rating).
  `bsk rate <space> --same` confirms the last rating.
  Each takes `[--day yesterday|<date>] [--note "…"]`.
  `bsk rate --edit <id> …` and `bsk rate --delete <id>`.
- `bsk home [--days N]`: the Home Score, each space with its parts now against as rated, and
  Fix next. `--days` adds the daily scores.
- `bsk domains environment`: the level, as Health's, with the condition lines in words.
- Every command takes `--json`, and the writes print `progress`.

### 4.8 Web

- **Environment page** (`/environment`, 🏡 in the nav, phone-first):
  - The Home Score, with today's bar for the shown level: "Home Score 71 · 68.2+ keeps level 70
    on track".
  - **Fix next:** the three biggest drags, e.g. "Office tidiness −6.3".
  - **Space cards:** name, score, three part bars (faded value, with the rated value as a
    ghost), and "rated 3 days ago".
  - **Walk-through:** a button opening a sheet (bottom on a phone, side on a desktop) with
    every space:
    - three of the Food Log's half-point pickers, with the score examples in HOME-2
    - **Same as last time** and an optional note
    - one **Save**, which writes them all in one POST
    - spaces sorted by how long since they were rated, then by drag
  - **Rate:** a card's button opens the same sheet for that space alone, with Tidiness and
    Cleanliness first.
  - **Spaces editor:** add, rename, weight, pace and archive, with each space's share shown.
  - **Home Score chart** (30 / 90 / 365): the daily score, with the shown level's bar as a
    dashed step line and exception days hatched, plus a table view.
- **Character page, Environment card:** the same as Health's (xp-domains plan §7.4.9), with
  the condition bar in place of the consistency bar:
  - "Your home supports level 67: a Home Score of 66.5+ on 69 of the last 90 days. Level 68
    needs 67.0+ on 70 days (you have 69)."
  - The headroom line when lifetime runs ahead: "Your effort has earned level 39. Your home
    holds you at 28."
  - Today: the Home Score against the bar.
  - The multiplier chip: "×1.2 · 24 of the last 30 days kept up".
  - The rank ladder shows each rank's bar, days of 90 and XP to reach.
- **Reports:** rank over time draws Health and Environment as two lines. The By domain table
  shows kept-up days and the average Home Score for Environment where Health shows active
  days and base XP. The spider still waits for a third configured domain.
- **Mock store:**
  - the nine spaces, seeded with a month of ratings that climb from messy
  - TypeScript ports of `HomeScore` and `HomeConditionRule` in `lib/domain-fold.ts` and a new
    `lib/home-score.ts`
  - unit tests that agree with C#'s worked examples to the digit

### 4.9 The simulator

`bsk domains simulate environment` runs routine patterns through the real `HomeScore` and
fold:
- walk-through days, check spaces and days, settled standards, a catch-up from a messy start
- paid XP a day before and after
- `--lapse` and `--exception` spans
- `--replay`, which folds the real ratings and projects from tomorrow

The regression bands come from §3, with the C# runs setting the exact edges:
- **Already clean:** holds 72–76.
- **Daily checks:** holds 89–94.
- **Top standard:** level 100 in 4–5 years without the multiplier, and 3–4 years with it. The
  §3 model left the multiplier off lifetime XP; the kernel pays it, so a home kept at the top
  standard reaches level 100 about a year sooner (C# runs: 4.4 years without, 3.4 with). Only
  this scenario is held back by lifetime XP, so the others are unchanged. E5's calibration can
  slow it with the price if 3.4 years is too quick.
- **Sunday only:** 65–70.
- **Messy weekly:** 22–27.
- **Monthly ratings:** 21–26.
- **3-month catch-up:** 50–56 at year 1, and within 2 of the clean start by year 5.
- **Marked break:** nothing moves during the span.

### 4.10 Switching over

- **No data migration.** Environment XP earned since 2026-10-20 sets the lifetime level the
  moment E3 deploys.
- The condition level is 1 until the first walk-through, so the shown level stays 1. A
  short note on the Environment card says so: "Rate your spaces to start your level".
- No Environment Gems have been paid, so none can be paid twice.
- Before pushing E3 and E4, keep `bsk domains environment --json` from staging to compare
  before and after.

## 5. Phases

| Phase | What lands | Migration | Pushable alone |
|---|---|---|---|
| **E0** | This plan, HOME-1 to HOME-3, REWARD-9, D18 | — | Yes |
| **E1** | Space type, ratings, views, `SpaceService`, `SpaceRatingService`, `bsk space`, `bsk rate`, the spaces and ratings endpoints. No level moves. | 0044 | Yes |
| **E2** | `HomeScore` and `HomeRules` in Domain, `GET /api/environment` and `/days`, `bsk home`. No level moves. | — | Yes |
| **E3** | `IConsistencyRule`, `HomeConditionRule`, `KeptUpAtShownLevel`, `LifeDomains.Environment`, the Home Score in the daily credit, rating writes answering `progress`, the API's `consistency.kind`, the simulator's Environment patterns and bands | — | No: with E4 |
| **E4** | The Environment page, walk-through sheet and spaces editor; the Character card; Reports; the mock store and TypeScript ports | — | With E3 |
| **E5** | Chris adds his nine spaces and does the first walk-through. Calibration: recheck the curve's price once the home is clean, and edit the rating examples. | — | — |
| **E6** | Photos in the kernel (§8): `v_space_photo`, shots and baselines, `SpacePhotoService`, `bsk space photo` / `baseline` / `shot`, the photo endpoints, the photo checks shared with the Food Log | 0045 | Yes |
| **E7** | Photos on the web (§8): a camera button per shot in the walk-through, Then and Now on each space card, the compare dialog, shots in the spaces editor, the day read from the photo | — | After E6 |

- **E3 changes the API's shape** for Environment's row, and the live web would break
  without E4, so they're committed separately and pushed together, as X10 and X11 were.
- Each phase updates the Build fields of HOME-1 to HOME-4 and REWARD-9 as it advances them.
- Checks before each commit: `./test.ps1` (.NET), and from `web/`: `npm test`,
  `npm run test:e2e` and `npm run a11y` after UI changes, plus a light-mode a11y spec for
  the new page. CI doesn't run Playwright.

## 6. How this was settled

**The ask (2026-10-27).** Chris: "I envision my Environment 'rank' or 'level' should be
largely influenced by how well my actual home environment is maintained." He wanted:
- each space rated on Tidiness, Cleanliness and Aesthetics, about 45/35/20
- spaces optionally weighted, the office higher
- higher ranks needing steady upkeep, "in a similar fashion to how Grandmaster health is
  difficult to maintain without constant work"

He wasn't sure Environment should stay XP-based. The main reason he had it that way was
rewarding himself for tasks, projects and time.

**Round 1: the model.** Three ways to combine XP and the home were laid out (§1, first row).
Chris took "XP sets the pace, the home sets the hold" and the fades: "I like these ideas.
Ratings fade sounds like a really good new mechanic."

**Round 2: spaces.** Chris gave the nine spaces, with weight and pace on 1–5 scales (§2.1).
Pace became a multiplier of pace / 3. Re-running the model with them barely moved the
results.

**Round 3: XP.** "I plan to reward myself with 1 XP per minute in cleaning/tidying progress,
Plus additional task/project rewards." About 25 a day is normal for a clean home, more while
catching up. The catch-up was modelled (§3.2): it banks headroom and doesn't change where the
level holds. That gave three rules:
- the curve is set from the clean-home rate
- the Character page shows the headroom
- there's no daily cap

**Chosen in planning** (not raised with Chris; each is easy to revisit):
- **Exception days also pause fades,** so a trip or a sick week neither helps nor hurts.
- **Weights and paces apply to all of history,** like any retune. An archived space leaves
  from the day it was archived.
- **A space counts once all three parts are rated.**
- **"Same as last time" works per space,** never "confirm the whole home", and records the
  values confirmed.
- **The bar isn't rounded,** because Q(51) lands on a half.
- **Space is its own subject type,** not an Area.
- **Ratings use the Food Log's picker and versioning.**
- **Rating photos** wait (§7). Chris asked for them the same day, with a baseline photo
  from the start, and settled them in one round (§8.1).

## 7. Later

- **Rating photos** moved to §8 (E6–E7).
- **Rating examples per space** (a bathroom's "4" isn't an office's), if the shared ones
  don't hold up.
- **The walk-through completing its repeating Task** when saved, so it pays without a second
  tap.
- **Reminders** for spaces fading below a bar.
- **Condition rules for Social and Finance.** `IConsistencyRule` lets another domain be held
  by something measured rather than daily XP. For Finance, that could be a savings rate or a
  budget kept.

## 8. Rating photos and the baseline (HOME-4, agreed 2026-10-27)

**The ask.** Chris, after E4: "I think rating photos are something I'll want to add. I think
having a 'baseline' photo from the start is especially important so I can see at a glance my
progress from the photos rather than just the ratings."

**Settled the same day.** Three questions were laid out, each with a recommendation, and
Chris took all three (§8.1). The rest (§8.2) was chosen in planning and is easy to revisit.

### 8.1 Agreed with Chris

1. **Shots, not one photo per space.** A room photographed from a different corner each time
   is hard to compare, and the Kitchen or Stairway/Foyer/Halls may not fit one frame.
   - A **shot** is a spot a space is photographed from, like "Office: Desk" or "Kitchen:
     Counters".
   - Every space starts with one shot, named after it, so most spaces need no setup. A space
     can add more.
   - Each shot has its own baseline and its own Then and Now.
   - Rejected: one photo per space (simpler, but a grouped space gets only its worst part).
2. **The first walk-through doesn't wait for photos.**
   - Chris does E5's first walk-through now and photographs each space with the phone's
     camera the same day, one photo from each spot he'll want to compare.
   - When E7 lands, he adds them. Each photo's day comes from its date taken, so they land on
     the walk-through's day as the baselines.
   - So E7 has to take past days, read the date taken, and let a shot be added before or
     after its photos are uploaded.
   - Rejected: holding the first walk-through until photos are built.
3. **Then and Now show on each space card:** the baseline beside the newest photo, each with
   that day's score. Tapping opens the compare dialog. A home gallery and a swipe slider
   wait (§8.4).

### 8.2 Chosen in planning

- **A photo belongs to a space (and shot) on a day, not to one rating.** The walk-through
  and the quick check offer a photo per shot, saved with the sheet's day. A photo can also
  be added with no rating (a before and after of a clean), and deleting a rating keeps its
  photos. Beside each photo the compare view shows the space's score that day.
- **The baseline is a shot's first photo,** until Chris picks another ("Make this the
  baseline", after rearranging or a renovation). Removing the baseline falls back to the
  earliest photo left.
- **Photos never change the Home Score or pay XP.** They're for seeing; the timer pays for
  the work.
- **The Food Log's photo pipeline (F6), reused:**
  - resized in the browser to 1,200 px, a JPEG at 0.8 (about 150 KB)
  - re-encoded, so the location never leaves the phone
  - stored in the private Supabase Storage bucket, served by `/api/artifacts/{id}/content`
  - Take photo and Choose photo buttons
  - the same refusals: JPEG, PNG or WebP only, at most 5 MB
- **The day comes from the photo** when the file carries its date taken (EXIF
  DateTimeOriginal, read before re-encoding). Otherwise it's the sheet's day. Chris can
  change it. Days are today or earlier.
- **Lining up the shot:** the camera button shows the shot's baseline as a thumbnail, so the
  next photo can match it. A camera overlay is later.
- **Removing a photo** hides it. Its bytes stay in the record, as food photos' do.
- **No reminders.** Each card says when its newest photo was taken ("photo 34 days ago").
- **Storage:** nine spaces photographed weekly is about 70 MB a year. The Food Log adds
  about 165 MB a year at three photos a day. Supabase's free 1 GB holds about four years of
  both.

### 8.3 Design

- **A photo is an `observation` event:** provenance `declared`, with the photo as its
  `artifact_id` and a `concerns` edge to the space. Payload:
  `{"space_photo": {"id": "<uuid>", "space": "<space id>", "shot": "<shot id>", "day": "2026-10-27", "photo": "<artifact id>"}}`.
  - A change of day or shot writes a newer version with the same `id`, carrying `photo` but
    not setting `artifact_id`, as the Food Log's versions do (one event owns the artifact).
  - Remove writes `{"space_photo": {"id": "…", "voided": true}}`.
  - Newest `recorded_at, id` wins.
- **Shots and baselines** are a space attribute written only by `SpaceService`:
  `shots: [{"id": "…", "name": "Desk", "baseline": "<photo id>"}]`.
  - With no `shots`, a space has one shot, `main`, named after the space.
  - `baseline` is left out while it's the first photo. A baseline whose photo was removed is
    ignored, so the earliest photo left takes over.
  - A shot can be renamed or removed. A removed shot's photos stay in the record and drop
    out of the page.
  - `bsk set` refuses `shots`, as it does `weight` and `pace`.
- **Migration 0045:**
  - `bsk.v_space_photo`: the newest version per photo id that isn't voided, with `id`,
    `space_id`, `shot_id`, `day`, `artifact_id`, `taken_at` and `event_id`
  - an index like the ratings' (`event_space_photo_idx`)
  - `bsk.v_space` recreated with `shots` (jsonb)
  - comments, grants and `ReaderRoleTests`
- **`SpacePhotoService`** (Application): add, change, remove and set the baseline. The
  checks move out of `FoodPhotoService` into a shared `PhotoChecks` (the sniff, the 5 MB
  limit, file names, the "not set up" refusal). Both services use it.
- **API:**
  - `POST /api/environment/spaces/{id}/photos`: multipart, with `photo`, `day` and `shot`
  - `POST /api/environment/photos/{id}` (day, shot) and `/photos/{id}/delete`
  - `POST /api/environment/photos/{id}/baseline`
  - `GET /api/environment/spaces/{id}/photos`: every photo, newest first, each with the
    space's score and parts on its day
  - `GET /api/environment`: each space row gains its shots, each shot with its baseline,
    its newest photo, a count and the newest photo's day
- **CLI:** `bsk space photo <space> --file <path> [--day] [--shot]`,
  `bsk space photo --remove <photo>`, `bsk space baseline <photo>`, and
  `bsk space shot add / rename / remove`.
- **Web:**
  - **The walk-through and quick check:** a camera button per shot, showing its baseline.
    The photos are held and sent after the ratings save, as a new food entry's are.
  - **Add photos** on a space card takes several files at once, for photos taken earlier
    (§8.1, answer 2). Each is dated from its date taken, or the day picked, and goes to the
    shot chosen.
  - **Space cards:** Then and Now side by side, each with its day and the space's score that
    day ("Then · Oct 27 · 48", "Now · Jan 12 · 81"). With one photo it shows alone, with "Add
    a photo to compare".
  - **The compare dialog** (tapping Then or Now): the two side by side (stacked on a
    phone), and a filmstrip of the shot's photos to pick either side. It also offers Make
    this the baseline, Change day and Remove photo.
  - **The spaces editor:** each space's shots (add, rename, remove).
  - **The mock store:** two seeded spaces get placeholder photos.
- **Tests:**
  - an upload round-trips byte for byte
  - the baseline defaults to the first photo, moves when picked, and falls back when
    removed
  - the Food Log's refusals, now from `PhotoChecks`, still hold for both
  - the date-taken reader on sample JPEGs (with and without EXIF)
  - an e2e golden path adds a photo in the walk-through and opens the compare dialog
  - a11y for the compare dialog, light and dark

### 8.4 Later

- **A camera overlay:** the baseline faded over the live camera to line up the shot.
- **A home gallery:** one screen with every space's Then and Now.
- **A swipe slider** in the compare dialog, dragging between Then and Now over one frame. It
  works only when the shots line up.
- **Suggesting ratings from a photo** (AI). This would need Chris's OK first, as the Oura
  data does.

## Status

- [x] E0 — Plan + requirements (HOME-1 to HOME-3, REWARD-9, D18)
- [x] E1 — Spaces and ratings in the kernel (migration 0044)
- [x] E2 — The Home Score (no migration)
- [x] E3 — Environment's rules (no migration; push with E4)
- [x] E4 — Environment on the web (no migration)
- [ ] E5 — Chris's spaces, first walk-through, calibration
- [x] E6–E7 planned (§8, HOME-4)
- [ ] E6 — Photos in the kernel (migration 0045)
- [ ] E7 — Photos on the web (no migration)
