# XP Domains Plan — life domains, levels, ranks and consistency

The design and build order for life-domain XP (REWARD-6 and REWARD-7 in
[ui-requirements.md](ui-requirements.md)) and the decision behind it (**D12**, in the same
doc). It also changes REWARD-2, so established habits keep earning. It builds directly on the
rewards ledger in [rewards-plan.md](rewards-plan.md). The requirements say *what*; this doc
says *how* and *in what order*, written so an agent with no session context can pick up any
phase.

**Scope agreed with Chris on 2026-10-20**, after two review rounds with the agent he
brainstormed the approach with (§5). **Health XP is the pilot domain**: it gets real rules.
Social, Environment and Finance collect XP only until each gets rules of its own.

**Keep the pilot small.** The mechanic under test is: earn XP for useful behavior, level up,
and unlock ranks through demonstrated consistency. That's enough to learn whether
gamification changes Chris's days. Everything in §6 has to earn its place through use.

**Added on 2026-10-25 (§7), levels v2:**
- Health's level follows recent consistency, so level and rank can fall slowly, while a best
  level and best rank are kept.
- Days marked as exceptions are skipped.
- The lifetime curve is cheaper for the early levels.

Phases X8–X11. Until X10 lands the app runs §1–§3's rules. After that, §7 replaces what they
say about ranks, the level cap, banked XP and the successful day.

**Added on 2026-10-27:** Environment gets rules of its own. It uses §7's shown level, fall,
ranks and Gems, but its level is held by a Home Score from rated spaces rather than by daily
XP. See [environment-plan.md](environment-plan.md) (REWARD-9, HOME-1 to HOME-3, D18).

## 0. Read these first

- [rewards-plan.md](rewards-plan.md): the ledger, award keys, reconcile, bounties. Everything
  here is a currency on that ledger or a fold over it.
- [ui-requirements.md](ui-requirements.md): REWARD-2, REWARD-6, REWARD-7, D11, D12.
- [kernel-build-plan.md](kernel-build-plan.md): how a phase lands (migration + `bsk` verb +
  view + tests via `./test.ps1`, strict build).
- Prior art to copy:
  - `v_habit_establishment` (0027): success over a window, plus an age check, gating a
    promotion. A rank gate is the same shape.
  - `NpgsqlLedgerRepository.GetDailyTotalsAsync` (R7): date math done in Postgres with a zone
    parameter. The solution runs with invariant globalization, so .NET can't convert IANA
    zones on Windows. Do every local-date conversion here the same way.
  - `BountyDefaults` and its drift test: tunable numbers kept in Domain code, checked against
    the vocabulary.

## 1. The rules (agreed 2026-10-20)

> **Levels v2 (§7)** replaces the rows on levels, ranks, rank regression, lifetime vs current,
> "XP ahead of consistency", the successful day and the spider charts once X10 lands.

| Question | Decision |
|---|---|
| Which domains? | **Health XP, Social XP, Environment XP** (clean, tidy, home maintenance, decluttering), **Finance XP** (income and expense, budgeting, net worth, savings, financial discipline). More later. |
| What are they? | Tracked currencies, like XP and Resolve: earned, never spent. |
| Defaults? | **0 in every domain, on every trigger.** Chris prices domain XP per item. |
| Plain XP? | Stays as general **Life XP** (relabelled; key stays `xp`). No levels, not on the spider chart, for now. |
| Domains without rules | Social, Environment and Finance **collect XP only**: a total, with no level, rank or consistency. Each gets its own rules later, and because progression is computed from history (D12), its whole history counts the day it does. |
| Lifetime XP | Accumulates permanently. |
| Levels | 1–100 per domain, computed from the domain's total XP, with each domain's own formula. Everyone starts at level 1 with 0 XP. |
| Health's formula | `XPnext(L) = 300 + 25(L−1) + 70·max(0, L−45)` is the XP to go from level L to L+1. 254,925 XP reaches 100 (§2.1). |
| A successful day (Health) | **At least 60 base XP** (before the multiplier). Flat, with no rise by rank. It was 70 until activity XP's calibration (2026-10-22, §2.4), and is still recalibrated after a few weeks of real Health XP. |
| Consistency | The share of successful days in a window. The "successful day" test is a **replaceable rule per domain**; 70 XP is only Health's first rule (§3.2). |
| Recovery days | In the pilot, pricing handles them. The 70 counts *all* Health XP, so sleep, nutrition, mobility or a recovery routine can carry a rest day. An explicit recovery-day rule is designed for later. |
| Ranks | One every 10 levels, 11 in all, Novice to Grandmaster (§2.2). Entering a rank needs the level, consistency at or above its bar over its window, **and** a domain at least as old as the window, so Grandmaster takes a year of effort no matter what. Rank difficulty rises through the bars and windows only. |
| Rank milestones | Designed now, built later: a rank can also require achievements, such as completing a Goal like "Run a 10K". Health's pilot ranks require none. |
| XP ahead of consistency? | **Level stops at the top of the current rank** (9 for Novice). Extra XP is banked. When the bar is met, the level jumps straight up, through several ranks if they're all met. |
| Rank regression | **Never from inactivity.** Ranks are recalculated from history, so a rule change or a correction to old entries can change one (§3.5). |
| Lifetime vs current | Level and rank are lifetime advancement: a terrible month never lowers them. Rolling consistency and the multiplier are current performance, and they do drop. The UI shows both. |
| Multiplier | From the 30 complete days before the day the XP is credited to, capped at 1.3× (§2.3). It multiplies that domain's XP. **It's frozen when the XP is issued** and never recalculated (§3.6). |
| Which day does XP count on? | **The day it happened**: a habit day's own date, or the local date of a completion. |
| Time zone | One home zone, **America/Los_Angeles**, so the server and every device agree on what happened on which day. |
| Rank-up reward | **Gems: 1 per rank, 5 for Grandmaster.** One-time and never taken back, so never paid twice for the same rank. |
| Established habits | **Keep earning through logged maintenance**: a collapsed Maintaining group with one-tap check-offs (§3.9). An unlogged day earns nothing, and nothing marks it as a failure. |
| Cadence | Daily for Health. The rules carry a cadence so Social or Finance can later be judged by the week; only daily is built now. |
| Spider chart, for a period | **Consistency %** per configured domain over 7 days, 30 days, 3 months, 6 months or 1 year, showing how much history supports each value. |
| Spider chart, since the start | **Level** per configured domain, 1–100. |
| Fewer than three configured domains | Horizontal bars instead of a spider chart. |
| Changing a formula | Levels recalculate instantly from the XP. The XP is the record; the level is a reading of it. |
| Simulation | A 10-year simulator over realistic earning patterns, part of the core regression suite and available as `bsk domains simulate` (§3.10). |
| Feedback | Level-ups and rank-ups get the full confetti and fanfare. |

## 2. Health, the pilot domain

### 2.1 Level curve

> **Levels v2 (§7.2)** replaces this curve with a cheaper cubic one once X10 lands.

`XPnext(L) = 300 + 25(L−1) + 70·max(0, L−45)`, the cost of going from level L to L+1.
Level L needs the sum of `XPnext(1..L−1)`.

| Level | Total XP | Reached after (100/day, no multiplier) | Days per level at that point |
|---|---|---|---|
| 2 | 300 | 3 days | 3 |
| 10 | 3,600 | 5 weeks | 5 |
| 20 | 9,975 | 3 months | 8 |
| 30 | 18,850 | 6 months | 10 |
| 45 | 36,850 | 1 year | 14 |
| 46 | 38,250 | | 15 (the ramp starts) |
| 50 | 44,800 | 15 months | 19 |
| 70 | 100,350 | 2.75 years | 38 |
| 90 | 193,900 | 5.3 years | 57 |
| 100 | 254,925 | 7.0 years | (level 99→100 costs 6,530) |

**Expected timelines** (`DomainSimulator` as built in X4: 20 seeded runs each, with the rank
gates, a flat 70 minimum and the multiplier; `bsk domains simulate` reproduces each line). A
full day does the 70 XP of essentials plus extras that vary around the average; a half day
does half the essentials plus its extras, so it usually misses the 70.

| Pattern | Result |
|---|---|
| Steady: full days about 95 base XP (spread 15), 5% of days off, so about 90 a day overall | Grandmaster in every run at 5.9 years; 7.7 without the multiplier |
| Steady, with 6 months off in year 3 | Grandmaster in every run at 6.5 years. Level and rank hold through the break. |
| Human: full days about 90 (spread 15), 10% of days off, 4% half days | Legend in every run (median 5.7 years); Grandmaster in 7 runs of 20, the earliest at 6.9 years. About 87% of days succeed, just under Legend's 88% bar and Grandmaster's 90%, so a few days a year decide it. Grandmaster's bar allows only about 36 missed days a year. |
| Improving: Human, with the extras growing 5 XP a day each year | Grandmaster in every run, median 6.2 years |
| Exactly 70 every day | Grandmaster in 7.7 years, each rank the day its level arrives |
| Always below 70 (a flat 60) | Capped at level 9 however much XP it banks |

The level formula stays as it is: these land inside Chris's 5–10 year range.

The X0 figures came from a Python sketch. X4's simulator agrees on every line but Human,
which the sketch put at "always Legend; Grandmaster in about 1 run in 4, after 8+ years".
Human sits right on the top two bars, so it's the first line to check at calibration (§2.4).

### 2.2 Ranks

> **Levels v2 (§7.2)** replaces these gates. Each rank is held, and can be lost, through the
> consistency level, with no window per rank.

| Rank | Levels | Title | To enter: consistency over window | Rank-up Gems |
|---|---|---|---|---|
| 0 | 1–9 | Novice | — | — |
| 1 | 10–19 | Apprentice | 50% over 14 days | 1 |
| 2 | 20–29 | Journeyman | 60% over 30 days | 1 |
| 3 | 30–39 | Adept | 65% over 60 days | 1 |
| 4 | 40–49 | Expert | 70% over 90 days | 1 |
| 5 | 50–59 | Veteran | 75% over 120 days | 1 |
| 6 | 60–69 | Elite | 78% over 180 days | 1 |
| 7 | 70–79 | Master | 80% over 240 days | 1 |
| 8 | 80–89 | Champion | 85% over 300 days | 1 |
| 9 | 90–99 | Legend | 88% over 365 days | 1 |
| 10 | 100 | Grandmaster | 90% over 365 days | 5 |

A successful day is 60+ base XP at every rank (70 until 2026-10-22, §2.4).

### 2.3 Multiplier

| Successful days, of the 30 complete days before the credited day | Multiplier |
|---|---|
| under 50% | 1.0× |
| 50–69% | 1.1× |
| 70–84% | 1.2× |
| 85% or more | 1.3× |

These are Chris's starting tiers, scaled to his 30% cap, to tune once he's lived with them.

### 2.4 Calibration

70 is a placeholder: nothing is priced yet, so nobody knows what Chris's real days are worth.
The plan:

1. Ship X1.
2. Chris prices his Health items so that a day with his essentials done reaches the minimum,
   and a rest day can reach it through recovery items.
3. He lives with it for 2–4 weeks.
4. Once X4 lands, `bsk domains simulate --replay` runs his real days so far through the
   rules, and projects them forward.
5. He sets the minimum, the tiers and the bars from that.

**Lowered to 60 (2026-10-22).** Activity XP's calibration (activity plan §3.8) simulated a
healthy routine of food, steps, sleep and four workouts a week. At 70 it reached 70%
successful days and level 59 after 10 years, with the ranks stopped at Veteran. At 60 it
reached 89%, level 100, and Grandmaster in 7.5 years. Chris chose 60: "Lower the Health
successful-day bar to 60 base XP."

- The curve, tiers and rank bars are unchanged.
- Steps 3–5 still apply once there are a few weeks of real days.
- Ranks and successful days are re-read from history, so past days are judged at 60 too.
  Multipliers already issued stay as they were paid.

**Low levels made harder (2026-10-27).** Two days after levels v2 (§7) went live, Chris found
the low levels too easy: "I can drink all day long, not exercise at all, have few steps, and
my level still increases." Level 2 needed an average of 20 base XP a day, and a day counted
as active from 10 action XP, which ordinary logged food alone could pay. Of the two options
offered, he took the gentler one: "Let's try the gentler option first and see how it goes."

- **An active day takes at least 20 action XP**, not 10: about 7,500 steps (20), or 5,000
  steps (12) plus ordinary logged food. This holds through level 44, then rises to 32 at 100
  as before.
- **Level 2 needs an average of 30 base XP a day**, not 20. Level 100 still needs 80, so the
  average now runs 30 → 80 (§7.2) and every level below 100 needs a little more.
- **Nothing else moved:** the lifetime curve, the active shares, the window, the fall and the
  multiplier.
- **It's retroactive.** The level is re-read from history every time, so past days are judged
  by the new bars too, and the best level and best rank are re-read with them. Gems already
  paid for reaching a rank stay.
- **The middle levels get a little harder too.** Every average between levels 2 and 99 rises
  by up to 10. The Steady routine still reaches Grandmaster in every run at about the same
  time, but its dips in the last two years go a few levels deeper (§7.4.11).
- The firmer option, kept in case this isn't enough: an active day from 25 action XP and
  level 2 from 40 base XP.

## 3. Design

### 3.1 Domains are tracked currencies

- Four new entries in `Currencies.All`, all `Spendable: false`, after Resolve:

  | Key | Label | Symbol (web) |
  |---|---|---|
  | `health_xp` | Health XP | ❤️ |
  | `social_xp` | Social XP | 🤝 |
  | `environment_xp` | Environment XP | 🏡 |
  | `finance_xp` | Finance XP | 📈 |

- `xp` keeps its key and is relabelled **Life XP** (Domain and the web's `CURRENCIES`). Its
  hint becomes "General experience from everything".
- No migration and no new defaults. Currencies are vocabulary data (D11). `BountyDefaults`
  gains nothing, so every domain pays 0 until priced. Redeem already refuses tracked
  currencies.
- The web's `CURRENCIES` mirror gains the four, in the same order.
- The bounty editor now has ten boxes per trigger, so group them: **Spend** (Coins,
  Freedom, Gems, Karma), **Track** (Life XP, Resolve), **Life domains** (Health, Social,
  Environment, Finance). Chips and toasts already render any known currency with its symbol.

### 3.2 Domain rules live in code

`LifeDomains` in Domain holds a `DomainRules` record for each **configured** domain; only
Health has one in the pilot. A domain currency without rules collects XP and nothing else.

- `Currency`: the currency key.
- `Cadence`: `Daily`. It's the length of the period the success rule judges. `Weekly` is
  named in the design but not built.
- `Curve`: an `ILevelCurve` with `CostToNext(level)`, `TotalFor(level)` and
  `LevelFor(totalXp)`. Health uses `RampCurve(Base: 300, PerLevel: 25, RampFrom: 45,
  RampPerLevel: 70)`. Another domain can use another shape.
- `Success`: an **`ISuccessRule`** with `bool Succeeded(PeriodFacts facts)`.
  - `PeriodFacts` holds the period (start, end) and its base and paid XP. It can grow later
    (habits followed, commitments kept, a recovery-day flag) without touching the fold.
  - Health uses `MinimumBaseXp(60)` (70 until 2026-10-22, §2.4).
  - **Designed, not built:** `AllOf` / `AnyOf` combinators; "these habits were followed";
    "these commitments were kept"; "an approved recovery-day routine". For example, Health
    later becoming "70 XP and my core nutrition and activity habits, or my recovery routine".
    Social's "two proactive outreaches a week" and Finance's "the scheduled review is done"
    are the same kind of rule on a weekly cadence. Swapping the rule changes no other code.
- `Ranks`: an ordered list of `RankRule(StartLevel, Title, MinConsistency, WindowDays,
  Milestones)`. The first is Novice at level 1 with no gate. `Milestones` is an optional list
  of achievements and is **empty for every pilot rank**. The likely shape is Goals that must
  be Completed. Until a phase builds the evaluator, a rank that lists milestones can't be
  entered, and the drift test refuses any.
- `Multiplier(WindowDays: 30, Tiers: [(0.85, 1.3), (0.70, 1.2), (0.50, 1.1)])`; anything
  lower is 1.0.
- `RankUpGems(PerRank: 1, Top: 5)`.

The drift test checks that:

- every rules record names a tracked domain currency, at most once
- ranks start at level 1, rise strictly and end at 100
- bars and windows never fall from one rank to the next
- no pilot rank lists milestones

Retuning means editing these numbers and deploying. Every reader computes from the XP on each
read, so a change shows immediately. Editing the rules in the app is later (§6).

- **As built (X3):**
  - `LifeDomains.Health` holds everything above except `Curve`, which arrives with levels in
    X4. `ISuccessRule` also has `Describe()` ("at least 70 base XP"), so readers can show the
    rule in words.
  - The drift test also checks that rank titles are distinct, Novice has no gate, every tier
    pays more than 1.0, and a higher share never earns a smaller multiplier.
- **As built (X4):**
  - `Curve` joined. `ILevelCurve` also has `MaxLevel`. `RampCurve(Base, PerLevel, RampFrom,
    RampPerLevel, MaxLevel = 100)` computes `TotalFor` in closed form and `LevelFor` by binary
    search. The drift test checks that a curve runs to the last rank's level and no level is
    cheaper than the one before.
  - `DomainRules` also answers `TopLevelOf(rank)` (the level below the next rank's start; 100
    for the last), `GemsFor(rank)` (`Top` for the last rank, `PerRank` otherwise) and
    `RankAwardKey(rank)`.

### 3.3 The day an XP counts on

Chris's rule is that XP counts on **the day it happened**. Each earn records that day:

- Every `earn` payload gains **`credited_on`**, a local date:
  - a habit day (`followed` / `partial`): its occurrence date
  - a streak milestone: the date of the run's last (nth) day
  - `completed`, `timed`, `established`: the date the trigger happened, in the home zone
- **Migration 0030** recreates `v_reward_trigger` / `v_reward_due` with an **`occurred_on`**
  column:
  - day rows: the occurrence date
  - streak rows: `max(occurrence_date)` of the run's n days
  - every other row: null
- `NpgsqlLedgerRepository` is built with the home zone, so the `ILedgerRepository` signatures
  don't change. `GetDueAsync` / `GetTriggersAsync` return
  `coalesce(occurred_on, (triggered_at AT TIME ZONE @tz)::date)` as `DueAward.CreditedOn`.
  `RewardService.Entry` stamps it on each earn. A reversal carries none: it gives back an
  earn, which has the day.
- **Home zone:**
  - Set by `Rewards:HomeTimeZone` (environment `Rewards__HomeTimeZone`), default
    `America/Los_Angeles`. `bsk` reads the same variable.
  - It travels next to the coin rate.
  - An unknown zone must fail loudly, not misdate days. The solution runs with invariant
    globalization, so .NET can't resolve IANA names; Postgres does the date math. Two checks:
    - Building the kernel checks the name's shape, so a Windows id such as
      `Pacific Standard Time` stops the API at startup and `bsk` before it runs.
    - A well-formed name Postgres doesn't know (`Mars/Olympus`) fails the first reconcile or
      trigger read, with a message naming `Rewards:HomeTimeZone`.
  - **A check-off without a date** is today in the home zone too (fixed 2026-10-20).
    `bsk adhere` and `POST /api/adhere` used the UTC date, so after about 5 pm Pacific a
    check-off landed on tomorrow, outside the day's credit. `AdherenceService` now asks
    `IHomeCalendar`, which Postgres answers. Today's check-offs, including the Maintaining
    group, also send their row's date.
  - **The habit views' today** is the home zone's too (migration 0033, fixed 2026-10-20).
    They read `current_date`, the database's UTC date, so after about 5 pm Pacific:
    - today's unlogged habits read as missed, and the Habits page's streak fell to 0
    - an evening graduation landed on tomorrow
    - the establishment window ran a day ahead

    `bsk.home_today()` now replaces `current_date` in those views. The views read the
    database setting `bsk.home_zone` rather than a per-connection one, because connections
    go through Supabase's pooler. Unset, it means `America/Los_Angeles`.
  - **Changing the home zone** therefore takes two settings: `Rewards:HomeTimeZone` for the
    app, and `ALTER DATABASE <db> SET bsk.home_zone = '<zone>'` for the habit views. The
    tests pin the views to UTC.
- **Earns from before X1** have no `credited_on`. They read as their `occurred_at` in the home
  zone. None of them carry domain XP, so nothing is affected.
- **History is different on purpose.** R7's History keeps filing entries by when they were
  written, in the viewer's zone, so a past day there never changes. Domain views file by
  credited day. A habit day logged late shows on the logging day in History and on the day
  it happened on the Character page.
- **Tasks** can't be marked "completed earlier" (a status change is always now), so a task
  counts on the day it's checked off. A backdated completion is a later option (§6).

### 3.4 Daily credit

What counts toward a domain on a day is **what the ledger pays now, on the day it happened**:

- For every award key with a positive net in a domain currency, its latest `earn` counts on
  that earn's credited day.
- A reversal takes the award off the day it was credited. Completing again files it on the
  new completion's day.
- So domain days follow the record as it stands, unlike History.

Each day carries two figures per domain:

- `base`: what it would have paid without the multiplier. This is the payload's `base` when
  present (§3.6), else its `amounts`.
- `paid`: what the ledger pays.

Success rules read `base`; levels read `paid`.

**Migration 0032** adds `bsk.v_award_credit`: one row per currently paid award key × tracked
currency, with award_key, currency, base, paid, credited_on (null for old earns) and
occurred_at. `ILedgerRepository.GetDomainDaysAsync(currencies, tz)` totals it per currency
per day, filling a null `credited_on` from `occurred_at` in the zone.

- Only award keys count: redeems can't touch tracked currencies, and `open` carries no
  amounts.
- **As built (X3):** the view lists every currency, and readers pick the domain ones. It
  also carries the latest earn's `event_id`. The repository is built with the home zone
  (§3.3), so `GetDomainDaysAsync(currencies)` takes no zone. It returns today's date in that
  zone along with the days, so every reader agrees on what today is.
- Rank-up awards pay Gems, not domain XP, so they never feed back into a domain.
- Penalties, when they come, will need a rule here (§6).

### 3.5 The progression fold

> **Levels v2 (§7.4.4)** replaces this fold's level cap, banked XP and rank walk.

`DomainProgress.Fold(rules, days, today)` is a pure C# function over one domain's daily
series. Every progression number in the app comes from it.

- **Start** is the first day with paid XP. Before it there's nothing to show.
- **Periods** come from the cadence; for `Daily`, a period is a day.
- **Walk** each period from the start to today, carrying the rank held:
  1. `succeeded(p) = rules.Success.Succeeded(facts(p))`. The current period counts only once
     it has succeeded. Until then, windows that include it end at the period before, so
     today can only help.
  2. `total(p)` is the cumulative paid XP through p, and
     `rawLevel(p) = Curve.LevelFor(total(p))`.
  3. While the next rank's start level ≤ `rawLevel(p)`, the domain is at least that rank's
     window old, the share of succeeded periods in the window ending at p is at or above its
     bar, and its milestones are met (none in the pilot): promote, and record
     `qualifiedOn` = p. Several ranks can be reached on one day.
- **Outputs:**
  - **Lifetime:** total XP; raw level; rank (index, title, `qualifiedOn`); **level =
    min(rawLevel, the rank's top level)** (9 for Novice, 19 for Apprentice … 100); XP into
    the level and to the next; **banked XP** (XP beyond the cap's next level, while capped).
  - **Next rank:** its level, bar and window, the successful periods so far, days old.
  - **Current:** today's base XP against the success rule; consistency over the last 30
    complete days; the **multiplier now**, from those same 30 days, where days before the
    start count as unsuccessful so the multiplier builds over the first weeks.
  - The per-period series: dates, base, paid, succeeded.
- **The multiplier for any day** is a function the fold exposes: the tier of the successful
  share of the 30 complete days before that day (§3.6 uses it).
- **What can lower a rank, and what can't.** Inactivity can't: XP never falls by itself, and
  once the walk qualifies a rank it holds for every later period. A rank can only change if
  the past changes: a retuned rule, or a correction to old entries, because the walk replays
  the record under today's rules. This is the pilot's model, **recomputed qualification**. A
  permanent "highest rank achieved" record is later (§6), which is why the output says
  `qualifiedOn`, not "achieved".
- **Cost:** seven years is about 2,600 days, so folding the configured domains on each read
  is trivial. Readers, reconcile and the simulator all call it; nothing is cached or stored.
- **As built (X3), the daily part:**
  - `DomainProgress.Fold` gives `Start`, `TotalXp`, `Periods`, today's base and success,
    `Consistency` (the 30 complete days before today), `MultiplierNow` and `NextTier` (with
    the successful days it still needs).
  - `MultiplierFor(day)` reads the window before that day, so a day never lifts its own
    multiplier.
  - `WindowEndingOn(end, days)` is the window rank gates will read: today joins it only once
    it has succeeded.
  - Credit dated after today is left out.
  - Levels, the rank walk and the lifetime figures join in X4.
- **As built (X4), the rest:**
  - The walk records a `DomainStanding` (day, total XP, raw level, level, rank) for each day
    from the start to today. `StandingOn(day)` reads one; a day after the last holds its
    standing.
  - A rank's gate: the raw level at its start level, no milestones, a window that fits wholly
    on or after the start (the age check), and at least `ceil(bar × window)` successful days
    in it. "Days old" counts today only once it has succeeded, like the windows.
  - Lifetime outputs: `Level`, `RawLevel`, `Rank`, `RankRule`, `RankQualifiedOn`,
    `RanksReached` (each with its `QualifiedOn`), `TopLevel`, `Capped`, `XpIntoLevel`,
    `XpToNext` and `BankedXp`. While capped, the level's bar shows full (`XpIntoLevel` is its
    cost, `XpToNext` 0) and banked XP is everything beyond what the next level takes. At level
    100 nothing is banked.
  - `Next`: the next rank's rule, its window now, days old, the parts of the gate met
    (`LevelReached`, `OldEnough`, `BarMet`) and the successful days still needed.
  - Windows read from a running count of successful days, so a fold is linear in days.

### 3.6 The multiplier on earns

- In `ReconcileOnceAsync`, after pricing a due award, each domain currency in the price is
  paid at **the multiplier of the day the award is credited to**:
  `paid = round(base × multiplierFor(creditedOn))`, rounding halves up (25 × 1.1 = 28).
  - The multiplier is computed from the record as it stands at payment, one fold per domain
    per reconcile.
  - For XP logged on the day, that's simply today's multiplier.
  - Logging late gives no advantage: Monday's walk logged on Friday is priced from the 30
    days before Monday.
- **Frozen when issued.** When any multiplier is above 1.0, the earn's payload adds `base`
  (the amounts before the multiplier) and `multipliers` (`{"health_xp": 1.2}`). Nothing ever
  re-prices an issued earn.
  - Late logging therefore doesn't guarantee the same outcome as logging on time. Say Monday's
    run paid 40 at 1.0×. On Friday, backfilling earlier days lifts Monday's window to 1.2×. A
    yoga session then logged for Monday pays at 1.2×, while the run keeps its 40.
  - Recalculating issued awards was considered and rejected for the pilot (§5).
- Reversals are unchanged: exactly the key's net, multiplier included. Completing again is
  priced at the multiplier of its new credited day.
- It applies to configured domains only. Unconfigured domains, Life XP, Resolve and the
  spendables never multiply.
- `Award` carries `base` and `multipliers`, so the toast can say "❤️ 28 Health XP (25 × 1.1)".
- Earns record their amounts when issued, so XP earned before X5 is never multiplied after the
  fact.
- **As built (X5):**
  - No migration: 0032's `v_award_credit` already reads a stamped `base`.
  - `ReconcileOnceAsync` folds the configured domains once, only when a due earn's price
    carries their XP, and prices each such earn with `DomainPricing.Price(price, creditedOn,
    folds)`. The same fold is the "before" for `progress`. A lost race re-reads and re-prices.
  - `base` and `multipliers` list only the currencies that were multiplied, so their keys
    match. A domain at ×1.0 stamps nothing, and only positive amounts multiply.
  - `MultiplierRule.Apply(amount, multiplier)` is the one rounding rule; the simulator uses it
    too.
  - `Award`, the API's `AwardRow` and `bsk … --json` carry `base` and `multipliers`. The CLI
    prints "+78 Health XP (60 × 1.3)", and the web toast's description reads "Stretch — day
    followed (❤️ 60 × 1.3)".
  - The mock store doesn't multiply yet; it gets the fold in X6. (X6: it does now.)
  - Backfilling several days in a row prices each one from the days before it, so logging
    them oldest first pays more than newest first. That's the frozen-at-issuance trade-off
    above, not a bug.

### 3.7 Rank-up Gems

> **Levels v2 (§7.4.6):** Gems are paid the first time the best rank reaches a rank, with the
> same keys.

- Award key `rank:<domain>:<start level>` (for example `rank:health_xp:10`), trigger `rank`,
  priced from the domain's `RankUpGems`, not an item bounty. It's due when the fold says the
  rank is qualified on a day on or after the wallet's opening.
- `RewardService.ReconcileDomainsAsync()` **earns only; it never reverses a rank award.**
  - The key is paid at most once, so losing and regaining a rank never pays twice.
  - It runs under the wallet lock with the expect-unpaid check, so two writers can't both pay
    it.
  - There's no subject, so no `concerns` edge. The payload names `domain`.
  - `derived_from` is the domain's latest earn credited on or before the `qualifiedOn` day.
  - The ledger repository needs a paid-awards read for subject-less keys (by trigger). The
    sweep's `ReconcileAllAsync` must skip `rank` keys when it looks for awards to reverse.
- **When it runs:**
  - after every reconcile that wrote an entry with domain XP
  - in the `bsk rewards reconcile` sweep
  - from `POST /api/domains/settle`, which the Character page calls when it opens, so a rank
    reached by time alone (an old miss leaving the window, the domain turning old enough) pays
    and celebrates when Chris looks
- **As built (X4):**
  - The wallet's opening day is read in the home zone with the daily credit
    (`DomainCredit.OpenedOn`). A rank that qualified before it never pays.
  - `ILedgerRepository` gained `GetPaidKeysAsync(trigger)` (keys with a nonzero net) and
    `GetLatestCreditEventAsync(currency, day)` (for `derived_from`).
  - The earn has no subject and a `note` naming the rank ("Health Apprentice"), so the ledger
    and the toast can say what it was for. It's credited on the qualifying day, provenance
    Derived. A lost race re-reads and tries again.
  - The sweep can't reverse one: it looks for reversals item by item, and a rank award has
    no item. `ReconcileAllAsync` pays rank-ups at its end.
  - It runs after a reconcile that wrote XP in a **configured** domain (unconfigured domains
    have no ranks).
  - `Award.Subject` is nullable and `Award` carries `Note`; the web's `Award` type and toast
    follow ("Health Apprentice — rank-up", Gems, so the full celebration).

### 3.8 Level-up and rank-up feedback

- Every endpoint that answers `awards` (`/api/status`, `/api/adhere`, `/api/timer`, and the new
  `/api/domains/settle`) also answers **`progress`**: each domain whose level or rank changed
  in that write, as `{ domain, levelFrom, levelTo, rankFrom, rankTo }`. It comes from folding
  the touched domains before and after the reconcile.
- Web (`lib/earn-feedback.ts`):
  - A level-up gets the full celebration and a toast: "❤️ Health level 12!"
  - A rank-up gets a bigger one with the title: "You're now a Health Journeyman."
  - A level lost to a reversal gets a quiet toast, like "Gave back".
- **As built (X4), the API half:**
  - `progress` is on all four endpoints, and `bsk status`, `adhere` and `timer` print it:
    "Health: level 9 → 12, now Apprentice." Each row also carries `rankTitle`.
  - When a rank-up is paid in the same call, the row starts from the rank below the lowest
    one paid, so a rank reached by time alone still announces on settle.
  - A reversal that takes a level away shows too ("level 2 → 1").
  - The web celebration waits for X6.
- **As built (X6), the web half:**
  - `announceEarned({ awards, progress })` in `lib/earn-feedback.ts` replaces the awards-only
    toast on every earning write (status, adhere, timer complete, settle).
  - A level-up toasts "❤️ Health level 12!" with the full celebration. A rank-up toasts
    "You're now a Health Journeyman." (pointing at the Character page) with a grand one:
    twice the confetti and a longer fanfare. A level lost to a reversal toasts "Health back
    to level 9" quietly.
  - One write celebrates once, at its biggest: grand, then full, then a chime for any earn.
  - The words come from `progressMessage` in `lib/domains.ts`.

### 3.9 Established habits keep earning

REWARD-2 used to say an established habit "graduates and leaves daily tracking". That would
make Health XP collapse just as a behavior becomes routine, which is the behavior worth
rewarding for years. Instead:

- **What stops at establishment:** the building workflow. That means scheduled occurrences
  (so no day is ever marked missed), streak milestones, and the establishment target.
- **What continues: logged days.** A day recorded for an Established habit pays its
  `followed` / `partial` price under the same `day:` award key, so the same bounty. Chris can
  lower it.
- **An unlogged day earns nothing**, and nothing marks it as a miss or a broken streak.
- **Abandoned** habits are unchanged: days after abandoning don't pay.
- **Kernel:**
  - `bsk_derived.habit_occurrence_source` already keeps recorded days after graduation. Only
    the day rows' filter in `v_reward_trigger` (`occurrence_date <= graduated_on`) stops them
    paying.
  - **Migration 0031** lets day rows through for habits whose current status is Established.
    Streak rows keep the filter.
  - `AdherenceService` already records days on terminal habits.
- **Web:**
  - Today gets a collapsed **Maintaining** group under the habits: each Established habit with
    a one-tap Followed (and Partial, where the habit allows it). The group's header says how
    many are done today, with no red, no "missed" and no streak counters. `todayHabitRows`
    stops dropping Established habits and puts them in this group.
  - The Habits page's Established section gets the same check-offs.
  - The mock store pays maintained days.
- **As built (X2):**
  - Migration 0031 joins each day row to the habit's current status, so a day passes when
    it's on or before graduation, or when the habit is Established now.
  - A check-off has no "Not followed" button. Tapping the lit button again undoes the day:
    it records `missed`, which pays nothing and gives back what the day paid, and the row
    reads "Not logged today", never missed.
  - The day a habit graduates is still a tracked day (0027's rule), so a day logged on the
    abandoning date still pays.
  - Established → Abandoned keeps what the maintained days paid. `graduated_on` moves to the
    abandoning date, so the stretch between establishing and abandoning is scheduled again
    (unlogged days read as missed). In theory its logged days could then complete a streak
    milestone. That transition is rare, so it's left alone.

### 3.10 The simulator

`DomainSimulator` (Application, pure) runs `DomainProgress.Fold` and the §3.6 pricing over
synthetic daily series. It's both a regression suite and Chris's calibration tool.

- **Pattern parameters:**
  - average base XP on an active day, and its variability
  - share of days off (0 XP)
  - share of half days
  - growth in daily XP per year
  - lapses (for example, 6 months off starting in year 3)
  - years, and the number of seeded runs
- **Output:** for each rank, the share of runs that reach it and the 10th / 50th / 90th
  percentile time to get there; the final level spread; and average consistency.
- **Regression tests** (in `./test.ps1`): named scenarios with expected bands, so a retune
  that breaks progression fails the build:
  - **Steady** (~90 a day, 5% off): reaches Grandmaster in every run, median 5–8 years.
  - **Human** (10% off, some half days): always reaches Legend; Grandmaster in under half the
    runs, never before about 7.5 years.
  - **Improving** (Human, +5 a day per year): reaches Grandmaster within 10 years.
  - **Lapse and return:** level and rank are unchanged after 6 months off; 30-day consistency
    falls to 0% and the multiplier to 1.0×, then both recover.
  - **Below the minimum** (a flat 60): capped at level 9, with all XP banked.
  - **Perfect** (exactly 70 every day): reaches each rank as soon as its level arrives;
    consistency never holds it back.

  These test the spread of realistic outcomes, not just whether perfect adherence wins.
- **`bsk domains simulate`:**
  - `--avg 90 --sd 25 --off 8% --half 5% --growth 5 --lapse 3y:6m --years 10 --runs 20`
  - `--replay` starts from Chris's real history and projects the pattern forward from today.
- Milestones join the simulator when their evaluator is built.
- **As built (X4):**
  - `DomainSimulator.Run(rules, pattern, options, from[, history])`, seeded per run. Full
    days are normal draws, never below the essentials. A half day is half the essentials
    plus its extras (half the XP when there are no essentials). A lapse is all zero days.
  - Each day is priced at `MultiplierFor` from a fold of base XP, rounding halves up; a
    second fold over the paid XP gives the levels and ranks.
  - Output per rank: runs reaching it and p10 / p50 / p90 years and dates. Plus the final
    level spread, final ranks, average consistency and each run's fold.
  - The regression bands, as built:
    - **Steady** (the bar + 25 / 15, essentials at the bar, 5% off): Grandmaster in every
      run, median 5–8 years; the multiplier is worth more than a year.
    - **Human** (the bar + 20 / 15, essentials at the bar, 10% off, 4% half): Champion
      always, Legend in at least 18 of 20, Grandmaster in 1–9 of 20 and never before 6.5
      years, consistency 85–89%.
      Loosened from "never before about 7.5 years": the C# runs land 6.9 at the earliest.
    - **Improving** (+5 a year): Grandmaster in every run within 10 years.
    - **Lapse** (6 months in year 3): the standing doesn't move through it; at its end 0%
      and ×1.0; a month back, at least 70% and ×1.2.
    - **Below the minimum** (the bar − 10): level 9, Novice, the rest banked, never multiplied.
    - **Perfect** (exactly the bar): each rank the day its level arrives; Grandmaster in
      8.5–9.5 years at 60 (7–8.5 at 70).
    - Since the bar moved to 60 (§2.4) the scenarios are set against it, so they keep their
      meaning when it moves. Steady and Human were 95 and 90 with essentials of 70.
  - `bsk domains simulate`: `--avg --sd --essentials --off --half --growth --lapse --years
    --runs --seed --no-multiplier --replay`. `--off` and `--half` are percentages; `--lapse`
    is `<start>:<length>` in d, w, m or y. Without `--replay` it needs no database.
  - `--replay` folds the real days so far and projects from tomorrow. Without `--avg` and
    `--sd`, full days are drawn from the real active days, and the off share comes from the
    real complete days.

### 3.11 Reads

| Read | Shape |
|---|---|
| `GET /api/domains` | Per domain: key, label, total XP, and `configured`. Configured domains add the lifetime figures (level, raw level, rank, qualifiedOn, XP into the level and to the next, banked XP), the next rank (title, level, bar, window, consistency now, successful days and days needed, days old against the window) and the current figures (today's base XP against the rule, 30-day consistency, multiplier now, the next tier and what it needs). |
| `GET /api/domains/{key}/days?from&to` | Per day: base, paid, succeeded. For the daily chart. |
| `GET /api/domains/report?days=7\|30\|90\|180\|365` | Per configured domain: days counted (since its start, capped at the period) and the period length, successful days, consistency %, XP paid and base, average per day, level at the period's start and now, ranks reached in the period. Unconfigured domains: XP earned only. |
| `POST /api/domains/settle` | Runs `ReconcileDomainsAsync`; answers `{ awards, progress }`. |
| `bsk domains [<domain>]` | `GET /api/domains` in text. `--days N` prints the daily series, `--report 30` the period report, `simulate …` the simulator (§3.10). |

Dates are local days in the home zone, not the viewer's zone.

- **As built (X3):**
  - `DomainService` (Application) serves the reads: `GetAsync` for every domain and
    `GetDaysAsync(domain, days)` for one domain's series. A domain can be named by key,
    label or first word ("health").
  - `bsk domains` shows each domain's XP. For Health it adds today against the rule, 30-day
    consistency, the multiplier now, and the next tier with the days it needs.
  - `bsk domains health --days N` lists the series, 14 days by default. `--days` without a
    domain lists every domain.
  - `GET /api/domains` and the lifetime figures come with X4.
- **As built (X4):**
  - `GET /api/domains` answers `{ today, domains }`. Each row has key, label, name, total XP
    and `configured`. Configured rows add `lifetime`, `nextRank`, `current` (with the rule in
    words and its target) and `ranks`: the ladder, each rank with its requirement and
    `qualifiedOn` once reached.
  - `POST /api/domains/settle` answers `{ awards, progress }`.
  - `bsk domains` adds the lifetime lines: the level (or the top of the rank, with XP
    banked), the rank and since when, and the next rank's gate part by part. `--json` carries
    the same figures.
  - The `days` endpoint and `--report` remain for X6 and X7.
- **As built (X6):**
  - `GET /api/domains/{key}/days?days=N` (default 30, 1–731) answers `{ key, label, name,
    configured, today, target, days }`, oldest first. Each day has base, paid and
    `succeeded` (null for an unconfigured domain). It takes a day count rather than
    `from`/`to`; the chart only ever asks for the last N days. Anything that isn't a
    domain, or a count out of range, is a 400.
- **As built (X7):**
  - `GET /api/domains/report?days=N` (default 30, 1–731) answers `{ today, days, domains }`.
    Every domain has key, label, name, `configured`, `from`/`to` and the XP credited to those
    days (`base`, `paid`).
  - A configured domain adds `daysCounted`, `successfulDays`, `consistency` and
    `averageBase`. It also gives `levelFrom`/`levelTo` and `rankFrom`/`rankTo`, the rank
    title now, and `ranksReached` (title and day). These are null for a domain without rules.
  - `DomainProgress.Report(days)` (Domain) computes a configured domain's period;
    `DomainService.GetReportAsync` reads every domain.
  - The period is the window a rank gate of that length reads today: the last N days, ending
    today once today has succeeded and yesterday until then. An unfinished today never counts
    against it. Its XP counts once it does.
  - `daysCounted` is the days of the period on or after the domain's start. Consistency and
    the average are over those days, so a young domain reads "8 of its 9 days", not "8 of
    30".
  - Levels compare the end of the day before the period with now. `ranksReached` lists the
    ranks entered since the period began.
  - A domain without rules reports the XP credited to the N days ending today.
  - `bsk domains [<domain>] --report N` prints the same, with a line per figure, and
    `--json`. `--days` with `--report` is refused.

### 3.12 The Character page (web)

> **Levels v2 (§7.4.9)** redesigns this page once X11 lands.

- **`/character`**, called "Character", in the nav and the command palette.
- **One card per configured domain**, showing lifetime and current side by side:
  - **Lifetime:** symbol and name, "Level 12", a rank badge ("Apprentice"), and a progress
    bar to the next level. While capped: "Level 19 (top of Apprentice) · 1,250 XP banked ·
    Journeyman needs a successful day on 60% of days over 30 days: 14 of 18 so far".
  - **Current:** a ring for today (base XP against the 70), 30-day consistency, and a
    multiplier chip: "×1.2 · 78% of the last 30 days".
  - After a long break, the lifetime half stays where it was, and the current half honestly
    shows the drop.
- **Unconfigured domains** get a small card each: the XP total and "Levels start once this
  domain has its own rules".
- **Health's card opens a daily chart:** bars of base XP per day with the 70 as a line, the
  last 30 days by default.
- **A rank ladder:** all 11 ranks, the current one highlighted, each with its requirement.
- Mock-first, as before. The mock store computes progress with a small TypeScript port of the
  fold, tested on the same examples as the C# tests.
- **As built (X6):**
  - `lib/domain-fold.ts` ports Health's rules, the level curve and `DomainProgress` line for
    line. Its 45 Vitest cases mirror the C# level, progress and rounding tests.
  - The mock store now does what the API does:
    - It credits each earn to a day, as `v_award_credit` does.
    - It prices configured-domain XP at that day's multiplier and stamps `base` and
      `multipliers`.
    - It pays rank-up Gems once.
    - Status, adhere, timer and settle answer `progress`.
    - It serves `domains()`, `domainDays()` and `settleDomains()`.
  - The seed gives Morning mobility 45 days of Health XP: level 9 Novice, 40 XP short of
    level 10, 24 of the last 30 days successful, ×1.2. Following it today pays 108 (90 ×
    1.2) and reaches Apprentice, so the rank-up shows straight away.
  - Opening the page calls `settleDomains()` once; a rank it pays toasts and celebrates like
    any other.
  - The card:
    - Lifetime half: "Level 9", a rank badge, a progress bar, "460 of 500 XP · 40 to
      level 10" (or, while capped, "Level 19 (top of Apprentice) · N XP banked"), then the
      next rank's gate from `nextRankText` and "3,560 Health XP in all · since … ·
      Apprentice since …".
    - Now half: today's ring (✓ and 90/70 once the day succeeds), "Today: 0 of 70 base XP ·
      70 to go", "24 of the last 30 days successful", the chip "×1.2 · 80% of the last 30
      days" and what the next tier needs.
    - After 30 days without a successful day, a note says the multiplier is back to ×1.0
      while the level and rank stay.
  - The daily chart (`components/character/domain-daily-chart.tsx`):
    - Ranges: 30 days, 90 days or 1 year. Successful days are the full tone, days short of
      70 a lighter step of it. The 70 is a dashed line, labelled on the axis.
    - Hover, or the arrow keys on the focused plot, reads a day ("Oct 19: 90 base XP (108
      paid) · a successful day").
    - The table view lists the days newest first, in a keyboard-scrollable box.
    - The range and whether the chart is open are remembered per viewer.
  - The rank ladder lists all 11 ranks with their levels, requirement and Gems. The rank held
    is marked "(you)" and `aria-current`; each rank reached shows the day.
  - Tests:
    - Vitest: the fold port, the mock store's domain half, the words and the toasts, and the
      page itself (the card, the ladder, the other domains, the chart and its table, and a
      settle that pays on open).
    - An e2e golden path: Level 9 Novice → follow Morning mobility → "You're now a Health
      Apprentice." → Level 10 Apprentice.
    - axe on the page with the chart open, as a chart and as a table, in both themes.

### 3.13 Reports and spider charts (web)

> **Levels v2 (§7.4.10):** rank over time replaces the period consistency chart once X11 lands.

- Period chips: 7 days, 30 days, 3 months, 6 months, 1 year.
- **Only configured domains are plotted.** With fewer than three configured, the page shows
  horizontal bars (consistency, and level) instead of a spider chart. Today that means bars
  until two more domains have rules.
- **Period spider:** one axis per configured domain, valued at consistency % (0–100). It can
  overlay a second period (say, 7 days over 1 year) to show the trend.
- **Since-the-start spider:** level per configured domain on a 0–100 axis, with rings at each
  rank.
- **How much history backs each value:**
  - Each axis label says how many days the value covers ("Health · 88% · 12 of 30 days").
  - A domain with less history than the period is drawn hollow / dashed. Two good days never
    look like a year at 90%.
- A per-domain table under the chart (days counted, successful days, consistency, XP,
  average per day, levels gained) doubles as the chart's Table view. Screen readers get the
  values in words. Colours go through the dataviz validator in both themes.
- Life XP, Resolve and unconfigured domains aren't plotted. Unconfigured domains' XP shows in
  the table.
- **As built (X7):**
  - The Character page has two tabs: Domains (the X6 cards) and Reports
    (`/character?view=reports`), in `components/character/domain-reports.tsx`.
  - Period chips: 7 days, 30 days, 3 months, 6 months, 1 year (prose says "12 months"). A
    "Compare with" menu adds a second period. Both choices are remembered per viewer.
  - **Consistency:**
    - One bar per configured domain with fewer than three configured, which means Health
      alone today.
    - Each bar's label gives the value and its history: "77% · 44 of 90 days", or "80% · all
      30 days".
    - A domain younger than the period is a hollow, dashed bar.
    - A comparison period is a tick across the bar, named under it: "30 days: 80% · all 30
      days".
  - **Since the start:** each configured domain's level as a bar from 1 to 100, ticked where
    each rank begins. It reads `GET /api/domains`.
  - **From three configured domains,** both become spider charts:
    - Consistency has rings at 25/50/75/100%, the period filled, the comparison dashed and a
      hollow vertex where history is short. Each axis label gives the value and its history.
    - Level has a ring at each rank.
    - The spiders are built and unit-tested now; the app shows them once two more domains
      have rules.
  - **By domain:** a table of every domain's days counted, successful days, consistency, XP
    (with base when it differs), base XP a day, level from → to and ranks reached.
    Unconfigured domains show "XP only" and their XP. It's the charts' table view, in a
    keyboard-scrollable box.
  - Words: each bar's label and value are visible text. Each spider's accessible name lists
    every value in words.
  - Colours:
    - The period is `--chart-earned`; the comparison is plain ink marked by shape (a tick or
      a dashed line), not a second hue.
    - The dataviz validator passes the green alone in both themes (#006c4b on #fefcf8,
      #37a17f on #111c18).
    - Against the ink, the green clears CVD and normal-vision separation (ΔE ≥ 23.8) and 3:1
      contrast. The ink only fails the hue-band checks, which are meant for coloured series.
  - While a new period loads, the last one stays on screen dimmed, labelled with the period
    it covers.
  - Mock: `toDomainReportRow` / `DomainProgress.report` port the C# report, and
    `mockDomainReport` serves it.
  - Tests:
    - .NET: the report's window, counts, XP, standing and ranks, today once it succeeds, a
      young domain, before the start, and the endpoint. Also a `bsk domains --report` smoke
      test on a throwaway database.
    - Vitest: the TS port on the same examples, the mock read, the words, the Reports view
      (bars, hollow, comparison, a rank reached in the period, the table) and both spiders.
    - e2e: Reports → 3 months → compare 30 days.
    - axe: Reports compared, in both themes.

## 4. Phases

Each phase is one commit: a migration (where applicable), the `bsk` verb, views, tests
(`./test.ps1`), and a clean strict build. Web phases go mock-first, then live, with
`npm run gen:api` after API changes, plus `npm run test:e2e` and `npm run a11y` (both themes).
Each phase also updates the Build fields of the requirements it advances.

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| X0 | Requirements + plan | REWARD-6 rewritten, REWARD-7, REWARD-2 revised, D12, backlog line, this doc. | — |
| X1 | Domain currencies + the credited day | The four currencies and the Life XP label (Domain and web); bounty editor groups; migration 0030 (`occurred_on`); the home zone setting; `credited_on` on every earn. Tests: domain XP prices and pays on every trigger, and pays nothing unpriced; a late-logged habit day carries its own date; a 10 pm Pacific completion carries the Pacific date; Redeem refuses domain XP. **From here Chris can price Health items and start earning (§2.4).** | — |
| X2 | Established habits keep earning | Migration 0031 (day rows for Established habits); Today's Maintaining group and the Habits page's check-offs; mock store. Tests: a logged day on an Established habit pays its day price; an unlogged one creates no missed day; no streak milestone after establishment; Abandoned days still don't pay; moving back to Building behaves as before. REWARD-2 Build returns to `built`. | X1 |
| X3 | Rules + daily consistency | `LifeDomains` with Health's rules (`ISuccessRule`, `MinimumBaseXp(70)`, `Cadence.Daily`, `RankRule.Milestones` empty), plus the drift test; migration 0032 `v_award_credit`; `GetDomainDaysAsync`; the fold's periods, success, consistency and multiplier-for-a-day parts; `bsk domains --days`. Tests: success reads base, not paid; a reversal takes a day's credit away and re-completing moves it; days before the start; today counts once it succeeds; tier edges at 49/50/69/70/84/85%; unconfigured domains report XP only. | X1 |
| X4 | Levels, ranks, rank-up Gems, simulator | `RampCurve` (table tests: totals 300 / 3,600 / 36,850 / 38,250 / 254,925 at levels 2 / 10 / 45 / 46 / 100); the level cap and banked XP; the rank walk (chained ranks, the age check, no regression from inactivity, a retune can re-read it); `rank:` awards (earn-only, never twice) with `ReconcileDomainsAsync`, the sweep and `POST /api/domains/settle`; `progress` on the earning responses; `GET /api/domains`; `bsk domains`; `DomainSimulator`, its regression scenarios and `bsk domains simulate [--replay]`. **Then calibrate (§2.4).** | X3 |
| X5 | The multiplier on earns | Pricing at the credited day's multiplier, `base` / `multipliers` stamped, `Award` carries them. Tests: rounding; same-day logging uses today's multiplier; a late entry uses its own day's; the "two multipliers on one day" case in §3.6; a reversal returns what was paid; completing again is priced at its new day; success reads base; unconfigured domains and other currencies never multiply. | X4 |
| X6 | Character page | §3.12, plus the level-up and rank-up feedback (§3.8). Vitest, one e2e golden path, a11y in both themes. | X4, X5 |
| X7 | Reports + charts | `GET /api/domains/report`, `bsk domains --report`, the bars / spiders with history depth and tables (§3.13). | X4 |

- **X1 should ship first.** Health XP starts adding up, on the right days, while the rest is
  built; levels appear later, computed from it. XP earned before X5 is never multiplied.
- X2 and X3 are independent of each other (habit views vs ledger reads). Run them in either
  order, or in parallel by two sessions that touch different files.
- X6 and X7 can run in parallel once X5 is in: they touch different pages.
- Migrations continue from 0030. Merging to `main` promotes them to staging through CI as
  usual.

**Levels v2 (added 2026-10-25, §7):**

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| X8 | Requirements + plan | REWARD-6 and REWARD-7 updated, REWARD-8 (exception days), D17, the backlog line; §7 and its pointers in §1–§6; the formulas doc marked settled. | X7 |
| X9 | v2 inputs: exception days, action XP, settled days | Migration 0043 (`v_exception_day`, §7.4.3); `ExceptionDayService`; the three `/api/domains/exceptions` endpoints; `bsk domains exception` and `bsk domains exceptions`; `DomainCredit` gains `ExceptionDays` and `SettledThrough`, and `DomainDay` gains `Passive`, in both the ledger repository and `RewardReader` (§7.4.2). The fold ignores the new inputs, so no number Chris sees changes. Tests: mark, change the comment, unmark and mark again; a blank comment and a future day are refused; no day means today in the home zone; the list's range and order; a voided day drops out of the view; the reader role sees the view; the passive split by trigger; `SettledThrough` either side of 4 a.m.; the endpoints' 400s. REWARD-8 Build → `building`. | X8 |
| X10 | Levels v2 in the kernel | §7.4.1 and §7.4.4–§7.4.8: `CubicCurve`, `ConsistencyRule`, `FallRule`, `ActiveAtShownLevel`, the new `RankRule` and the drift test; the v2 fold; pricing, Gems on a first best rank, `progress` with best fields, the reads and `bsk domains`; the simulator and its scenarios (§7.4.11). The Domain and `LifeDomains` tests are rewritten for v2. Tests: curve totals (60 / 1,476 / 46,825 / 255,686 at levels 2 / 10 / 50 / 100); the rank table; a level never passes while one below it fails; the age test counts active days; S rises to T at once; the fall timeline from 100 with no XP (Legend day 10, Champion 21, Master 33, Elite 46, Novice 249); S never falls below T; 7 days back on track pauses the fall; exception days are skipped and pause it; a marked lapse leaves level and rank unchanged; an unsettled day never lowers S; best never falls; Gems on the first best rank only, and never again for a rank X4 paid; multiplier success at the shown level, exception days skipped; unconfigured domains report XP only. **Push with X11.** | X9 |
| X11 | Levels v2 on the web | `npm run gen:api`; the types; the `domain-fold.ts` port; the mock and a new seed (a follow lifts a new best level, 10 / Apprentice, and one exception day); `domains.ts` words; `announceEarned`; the Character page and the exception dialog, chart and ladder (§7.4.9); Reports' rank over time (§7.4.10). Vitest on the port and the views, the e2e golden path and the Reports path, and axe in both themes. REWARD-6, REWARD-7 and REWARD-8 Build → `built`. **Then calibrate (§2.4).** | X10 |

- **X9 can be pushed alone**; X10 and X11 are pushed together, because X10 changes the API's
  shape (§7.5).
- **0043 is the only v2 migration.**

## 5. How this design was settled

**Review rounds (2026-10-20).** Chris's brainstorming agent reviewed the first draft, and its
findings were checked with a 10-year simulation of the rules. The changes from the first
draft:

- **The daily minimum rose by rank** (100 + 5 per rank). Simulated, every realistic pattern
  stalled at level 19, and the reviewer's softer 70 + 2 per rank still stalled realistic
  patterns between levels 59 and 79. A rising minimum fights the high consistency bars, and
  daily XP is easy to inflate by pricing anyway. It became a flat 70; difficulty rises through
  the bars and windows, and later milestones.
- **Established habits stopped earning.** They now keep earning through logged maintenance
  (§3.9).
- **"Ranks can't be lost" was ambiguous.** It's now explicit: no regression from inactivity;
  recomputed after rule changes or corrections; rank Gems permanent and paid once.
- **The late-entry multiplier was unspecified.** It's now the credited day's multiplier,
  frozen when issued, with the side effect documented and tested.
- **Social, Environment and Finance had copies of Health's rules.** That would have shown
  meaningless percentages, so they now collect XP only until configured.
- **Added for later, designed now:** rank milestones, a replaceable success rule, a cadence
  setting, and the simulator in the regression suite.

**Chosen in planning**, because one option was clearly better:

- **Ranks are computed, not recorded** (D12). The walk replays the record under today's rules,
  giving "a formula change shows instantly" and "inactivity never costs a rank" together,
  with no promotion event to keep in sync.
- **Recalculating issued multipliers was rejected** for the pilot. It would mean reversing
  and re-pricing past earns whenever a backfill moves a window. Frozen-at-issuance is
  auditable and follows from the immutable ledger.
- **Domain rules live in code**, like the bounty defaults, behind interfaces for the curve and
  the success rule.
- **The multiplier uses complete days only.** Rank gates and reports count today once it has
  succeeded.
- **Multiplied amounts round halves up.**
- **The multiplier counts days before a domain's start as unsuccessful**, so it can't sit at
  1.3× after day one. Reports count only days since the start, and show how many.
- **Domains aren't tied to Areas.** Chris prices them per item.

## 6. Later (outside X1–X7)

Each of these has to earn its place through use of the pilot.

- **Rank milestones:** the evaluator (Goals completed), and simulating them.
- **Richer success rules:** `AllOf` / `AnyOf`, habits followed, commitments kept, an approved
  recovery-day routine. *v2's exception days cover recovery days (§7.4.3).*
- **A weekly cadence**, for Social and Finance.
- **"Rank achieved" vs "currently qualified":** a permanent record of the highest rank ever
  reached, shown beside what today's rules qualify. *v2 builds this as the best rank (§7).*
- **Tuning the rules in the app** instead of in code.
- **Each other domain's own rules**, one at a time, the way Health was done.
- **More domains:** a line in `Currencies`, plus a rules record when it's configured.
- **Life XP levels**, or an overall character level.
- **A backdated completion** ("completed yesterday"), so a late check-off counts on the right
  day.
- **Penalties** in domain XP: §3.4's credit read would need to count entries without an award
  key.
- **Area hints:** pre-fill an item's domain XP from its Area.

## 7. Levels v2: consistency sets the level, ranks can fall (agreed 2026-10-25)

Under X1–X7 a rank is entered through a gate and never lost, so it says what Chris once did,
not how he's doing. Rank is the figure he wants to track over time and compare across
domains, so v2 ties level and rank to recent consistency:

- The level is the lower of what lifetime XP has paid for and what the last 90 days support.
- It falls slowly while he isn't keeping up, and a best level and best rank keep the record.
- Days he marks as exceptions (sickness, injury, travel) neither help nor hurt.

The formulas were settled in the Claude Doc **Health XP Model — Formulas**
(<https://claude.ai/artifact/Wo23SEZ7arEgeCLix3dzBn>). They were checked with a Python model
and the consistency simulator (<https://claude.ai/artifact/QRoGVdpFzZnMBW5yUqBx1S>). This
section is the build-side copy. Once X10 lands, §7 replaces what §1, §2.2, §3.5, §3.7 and
§3.12 say about ranks, the level cap, banked XP and the successful day. Everything else in
§1–§3 still holds.

### 7.1 The rules

| Question | X1–X7 (live) | v2 |
|---|---|---|
| A day's Health XP | Food, activity, habits and tasks. | Unchanged. Sleep is now labelled **passive**, and everything else is **action XP**: `B = H − sleep`. |
| Lifetime level | From total paid XP on §2.1's curve. Never falls. | From total paid XP on **the new curve** (§7.2). Never falls. It's the ceiling the level can't pass. |
| Counted days | Every calendar day since the start. | **Settled** days since the first Health XP, minus exception days. A day settles at 4 a.m. the next morning, when steps and exercise pay (Oura's day runs 4 a.m. to 4 a.m.). |
| Consistency level | No such thing: rank gates read consistency. | **C**: the highest level the last `N = min(90, counted days)` counted days support. It has to pass an age test, an average test and an active-day test (§7.2). |
| Level | `min(raw level, top of the rank held)`, with the rest banked. | **Shown level S** follows the target `T = min(lifetime level, C)`. It **rises to T at once**. It **falls 1% of itself a day**, with no hold, never below T. The fall **pauses** while the last 7 counted days pass level S's own average and active-day tests, and on exception days. S is shown rounded up. |
| Level cap, banked XP | Yes. | **Gone.** |
| Rank | Entered through a gate (level, bar over a window, age). Never falls from inactivity. | **`min(10, ⌊S / 10⌋)`**, so it can fall. The titles are unchanged (§2.2). The gates are gone. |
| Best level, best rank | None. A permanent record was a §6 "later". | The highest S and rank ever reached, worked out from history like everything else (D12). Inactivity never lowers them. A retune or a correction can, as before. |
| Multiplier tiers | §2.3. | Unchanged. |
| The multiplier's successful day | 60+ base XP. | **Active at the shown level:** that day's action XP ≥ `M(S)`, where S is the level going into the day. |
| The multiplier's 30 days | The 30 complete calendar days before the credited day. | The 30 complete days before it **with exception days skipped**, so the window reaches back one more day for each. Days before the start still count as unsuccessful. Still frozen when issued. |
| Rank-up Gems | 1 per rank and 5 for Grandmaster, paid once. | The same, paid **the first time the best rank reaches it**. Same award keys, so a rank already paid under X4 is never paid again, and none is ever reversed. |
| Celebrations | Every level-up and rank-up. | **Only a new best level (full) or a new best rank (grand).** Getting back to an earlier level is a quiet note. A drop caused by an undo is a quiet toast. A drop over time shows on the Character page, not as a toast (§7.4.7). |
| Exception days | None. | A day Chris marks with a **required comment**. There's no limit, and he can unmark it later. It covers every configured domain, which is Health only for now. It's skipped by every window and pauses a fall. XP earned on it still counts toward lifetime XP. |
| Reports across domains | Consistency % per period, level since the start. | **Each domain's rank over time** (§7.4.10). |

### 7.2 Health's formulas

For a counted day d, `H_d` is the day's base Health XP and `B_d` its action XP. The window is
the last N counted days. Level L (2–100) qualifies when all three of these pass:

```
age:          #{counted days so far with B_d ≥ M(L)}  ≥  min(90, L + 1)
average:      Σ_window H_d                             ≥  A(L) · N
active days:  #{d in window : B_d ≥ M(L)}              ≥  ⌈P(L) · N⌉

A(L) = 30 + round(50 · (L − 2) / 98)      average base XP a day: 30 at level 2, 80 at level 100
M(L) = max(20, round(0.4 · A(L)))         action XP for an active day: 20 to 32
P(L) = 0.50 + 0.40 · (L − 2) / 98         share of days active: 50% to 90%
```

- **C** is the highest qualifying L, or 1. Every level reads the same window, so when a level
  passes, every level below it passes too, and the walk can stop at the first failure.
- **Whole-number math:**
  - Neither rounding ever lands exactly on a half, so the rounding rule doesn't matter. A half
    would need `50(L − 2)` to equal 49 times an odd number, or `4A` to be odd.
  - Until 2026-10-27, A ran 20 → 80 and M's floor was 10 (§2.4).
  - `⌈P(L) · N⌉ = ⌈N · (490 + 4(L − 2)) / 980⌉`.
  - The average test is `Σ H ≥ A · N` in integers.
- **Age counts active days**, not days of any kind. Counting any day let consistency climb
  after a short binge stopped.

The shown level, kept as a whole number of **ten-thousandths of a level** so C# and
TypeScript agree to the digit:

```
T_t = min(lifetime level_t, C_t)
Ŝ_t = T_t · 10000                          if T_t ≥ S_(t−1)                (rising)
Ŝ_t = Ŝ_(t−1)                              on an exception day, or back on track
Ŝ_t = max(T_t · 10000, ⌊Ŝ_(t−1) · 99 / 100⌋)  otherwise                     (falling)
S_t = ⌈Ŝ_t / 10000⌉          rank_t = min(10, ⌊S_t / 10⌋)
back on track: the last 7 counted days pass level S_(t−1)'s average and active-day tests (N = 7)
```

The lifetime curve, as the total XP that reaches level L:

```
XP(L) = 60n + 12n(n − 1) + ⌈17n(n − 1)(n − 2) / 120⌉,   n = L − 1
```

This is `⌈60n + 24·C(n,2) + 0.85·C(n,3)⌉` in whole numbers, and it matches the float form at
every level. Each level costs at least as much as the one before.

| Level | Total XP (new) | Total XP (§2.1) | At about 100 paid XP a day | Cost of the next level |
|---|---|---|---|---|
| 2 | 60 | 300 | day 1 | 84 |
| 3 | 144 | 625 | day 2 | 109 |
| 10 | 1,476 | 3,600 | about 2 weeks | 306 |
| 20 | 6,068 | 9,975 | about 2 months | 661 |
| 30 | 14,590 | 18,850 | about 5 months | 1,101 |
| 45 | 36,602 | 36,850 | about 1 year | 1,920 |
| 50 | 46,825 | 44,800 | about 15 months | 2,235 |
| 70 | 104,979 | 100,350 | about 2.9 years | 3,710 |
| 90 | 195,854 | 193,900 | about 5.4 years | 5,524 |
| 100 | 255,686 | 254,925 | about 7 years | — |

What each rank takes to hold its first level over a full 90-day window:

| Rank | Levels | Average base XP a day | Action XP for an active day | Active days of 90 | Total XP to reach |
|---|---|---|---|---|---|
| Novice | 1–9 | — | — | — | 0 |
| Apprentice | 10–19 | 34 | 20 | 48 (53%) | 1,476 |
| Journeyman | 20–29 | 39 | 20 | 52 (57%) | 6,068 |
| Adept | 30–39 | 44 | 20 | 56 (61%) | 14,590 |
| Expert | 40–49 | 49 | 20 | 59 (66%) | 27,893 |
| Veteran | 50–59 | 54 | 22 | 63 (70%) | 46,825 |
| Elite | 60–69 | 60 | 24 | 67 (74%) | 72,237 |
| Master | 70–79 | 65 | 26 | 70 (78%) | 104,979 |
| Champion | 80–89 | 70 | 28 | 74 (82%) | 145,902 |
| Legend | 90–99 | 75 | 30 | 78 (86%) | 195,854 |
| Grandmaster | 100 | 80 | 32 | 81 (90%) | 255,686 |

The tunable numbers are 30 and 80 (the average), 0.4 and 20 (the active day), 50% and 90%
(the active share), 90 days (the window), 1% a day with no hold and 7 days back on track (the
fall), and §2.3's multiplier tiers.

### 7.3 What it produces

From the Python model (6 seeded runs of 10 years each, using the routines of activity plan
§3.8), under the first numbers (A from 20, M from 10). These are the targets X10's simulator
bands are checked against; §7.4.11 has the C# runs under the 2026-10-27 numbers:

| Routine (average base XP a day) | Year 1 | Year 5 | Level 100 | Lowest level, years 9–10 | Rank drops a year |
|---|---|---|---|---|---|
| High activity (100) | 49–50, Expert | 96–97, Legend | 5.3–5.4 years | 100 | 0 |
| Calibration routine (79) | 44–45, Expert | 88, Champion | 6.8–6.9 years | 90–94 | 3–6 |
| Food at the Food Log's good day (75) | 43–44, Expert | 86, Champion | 2 runs of 6 (7.2 and 9.6 years) | 84–89 | 2–5 |
| Routine with 10% of days off (71) | 41–43, Expert | 82–84, Champion | 1 run of 6 (9.6 years) | 69–79 | 3–5 |
| Lower adherence (56) | 38, Adept | 53–64, Veteran to Elite | Never | 50–56 | 2–6 |

- **The calibration routine's rank drops** are nearly all short dips from Grandmaster to
  Legend, usually regained in about 9 days. Grandmaster is the only one-level rank, so falling
  one level loses it. Chris accepted them rather than add a hold.
- **Breaks from Grandmaster** (90 base XP a day before and after; lowest level on the right):

  | Break | Level on return | Lowest | Back to 100 |
  |---|---|---|---|
  | 7 days | 100 | 100 | No change |
  | 14 days | 95 | 90 | 80 days after return |
  | 30 days | 81 | 78 | 80 days after return |
  | 60 days | 60 | 57 | 80 days after return |
  | 120 days | 33 | 32 | 80 days after return |
  | Any length, marked as exception days | 100 | 100 | No change |

- **Level 100, then nothing logged:** raw consistency holds 9 days (90-day window × 10%
  missable), so the fall starts on day 10. It reaches Legend on day 10, Champion on day 21,
  Master on day 33, Elite on day 46, level 60 at two months, 45 at three, and Novice on day
  249.

### 7.4 Design

#### 7.4.1 Rules in code

`DomainRules` changes shape (Domain, `LifeDomains.cs`). Its X3–X4 parts that v2 drops go
with it:

- `Curve`: Health gets a new `ILevelCurve`, **`CubicCurve(Linear: 60, Quadratic: 24,
  Cubic: 0.85)`**. `TotalFor` is the closed form in §7.2, in whole numbers. `CostToNext` is
  the difference of totals, and `LevelFor` is a binary search, as `RampCurve` does.
  `RampCurve` stays, unused, as another shape a domain can pick.
- `Consistency`: new, **`ConsistencyRule(WindowDays: 90, AverageFrom: 30, AverageTo: 80,
  ActiveShare: 0.4, ActiveFloor: 20, ShareFrom: 0.50, ShareTo: 0.90)`** (from 20 and 10 until
  2026-10-27, §2.4).
  - It answers `Average(level)`, `ActiveMinimum(level)` and `ActiveDaysNeeded(level, n)`,
    and the age test's `min(WindowDays, level + 1)`.
  - It uses the whole-number forms in §7.2.
- `Fall`: new, **`FallRule(PercentPerDay: 1, HoldDays: 0, BackOnTrackDays: 7)`**. It works
  in ten-thousandths of a level (§7.2). `HoldDays` stays at 0, but it's there because the
  simulator tried other values.
- `Success`: Health's becomes **`ActiveAtShownLevel`**: a day succeeds when its action XP
  reaches `ActiveMinimum` of the shown level going into that day. `MinimumBaseXp` stays as
  another rule.
  - `PeriodFacts` gains `ActionXp` and `ShownLevel`, so the rule stays a pure function of
    the facts.
  - The walk supplies `ShownLevel`, because it comes from the days before.
- `Ranks`: **`RankRule(StartLevel, Title)`**, one every 10 levels, the same eleven titles.
  `MinConsistency`, `WindowDays` and `Milestones` go, and `TopLevelOf` goes with the cap.
- `PassiveTriggers`: new, the award triggers whose XP is passive. Health's is `["sleep"]`
  (`ActivityAwards.SleepTrigger`). Action XP is base minus passive.
- `Multiplier` and `RankUpGems`: unchanged. `GemsFor` and `RankAwardKey` keep their keys,
  `rank:health_xp:<start level>`.
- The drift test drops its gate checks and adds:
  - the consistency rule's ranges run upward
  - `ActiveMinimum` never falls as the level rises
  - ranks start every 10 levels from 1 to 100
  - the curve never gets cheaper per level
  - the passive triggers are known award triggers

#### 7.4.2 Daily credit: action XP and exception days

- **`DomainDay(Day, Base, Paid)` gains `Passive`**, the base XP of passive awards credited
  that day. Action XP is `Base − Passive`.
  - `v_award_credit` already carries `award_key`, and activity awards are keyed
    `<trigger>:<yyyy-MM-dd>` (`sleep:2026-10-24`). No item trigger is named `sleep`.
  - So `GetDomainDaysAsync` can total passive base per day in its SQL with no migration:
    `sum(base) FILTER (WHERE split_part(award_key, ':', 1) = ANY(@passive))`.
  - It returns that in `CreditDay`, and `DomainService.DaysOf` passes it into `DomainDay`.
    The passive triggers come from the rules, so only Domain knows that sleep is passive.
- **Exception days** come back with the credit, as `DomainCredit.ExceptionDays` (date and
  comment), so the fold takes them as an input and every reader agrees. Their storage is
  §7.4.3.
- **The settle cutoff:** `DomainCredit` also returns **`SettledThrough`**, the last settled
  day. That's yesterday from 4 a.m. in the home zone, and the day before until then. Postgres
  computes it, like today's date (§3.3):
  `((now() AT TIME ZONE @tz) - interval '4 hours')::date - 1`.
  The 4 hours is a constant in Domain beside the rules (`LifeDomains.SettleHour`), matching
  Oura's day.

#### 7.4.3 Exception days: storage, writes and reads

An exception day is a fact Chris declares about a day, so it's an event, like a workout note
or a food day's close. Nothing about it is managed over time, so it isn't a subject.

- **Event:** kind `note`, provenance `declared`, `occurred_at = recorded_at = now`. The day
  lives in the payload: `{"exception_day": {"day": "2026-10-25", "comment": "Flu, in bed"}}`.
  - **Changing the comment** writes a newer version.
  - **Unmarking** writes `{"exception_day": {"day": "2026-10-25", "voided": true}}`, as
    deleting a meal does.
  - The newest version per day wins, by `recorded_at DESC, id DESC`.
  - No new event kind, so the kind check is untouched.
- **Migration 0043** (`0043__exception_days.sql`):
  - `bsk.v_exception_day`: one row per marked day that isn't voided, with `day`, `comment`,
    `marked_at` and `event_id`. Days are parsed with `bsk.try_to_date`, and the payload's
    shape is checked with `jsonb_typeof`.
  - A partial index, `event_exception_day_idx ON bsk.event ((payload->'exception_day'->>'day'),
    recorded_at) WHERE kind = 'note' AND jsonb_exists(payload, 'exception_day')`.
  - `COMMENT ON VIEW` naming REWARD-8 and D17, `GRANT SELECT … TO bsk_reader`, and the view
    added to `ReaderRoleTests`.
- **Domain:** `ExceptionDay(Day, Comment, MarkedAt)`. (`day`, not `on`, throughout: `on` is a
  reserved word in SQL.)
- **`ExceptionDayService`** (Application/Rewards), with `sourceId` and `IHomeCalendar` like
  the other services:
  - `MarkAsync(DateOnly? day, string comment)`:
    - No day means today in the home zone.
    - A day after today is refused, in words.
    - The comment is required, and normalized by `WorkoutNoteService`'s rules: one line, at
      most 140 characters, no control characters.
    - Marking a day already marked with the same comment writes nothing. A different
      comment writes a new version.
  - `UnmarkAsync(DateOnly day)`: a day that isn't marked writes nothing.
  - `ListAsync(days)`: the marked days among the last N, newest first.
  - Every write folds the configured domains before it. After it, `RewardService` pays any
    rank-up the change unlocks and answers `progress`, as `POST /api/domains/settle` does
    (`RewardService.ChangeDomainInputsAsync`).
- **Reads for the fold:**
  - `ILedgerRepository.GetDomainDaysAsync` returns the marked days in `DomainCredit`, read
    from `v_exception_day` on the same connection, with `SettledThrough` and the passive
    split (§7.4.2). `GetExceptionDaysAsync(days)` answers the list.
  - `RewardReader` reuses the same repository on the reader connection, so it changes with
    it.
  - Every fold caller then sees the same days: `DomainService`, `RewardService`,
    `DomainPricing`, the simulator's `--replay`, and the API reader.
- **API,** all in the `/api/domains` group, allowlisted. Writes are POSTs, as elsewhere:

  | Endpoint | Does |
  |---|---|
  | `GET /api/domains/exceptions?days=N` | Default 90, 1–731. Answers `{ today, from, exceptions: [{ day, comment, markedAt }] }`. |
  | `POST /api/domains/exceptions` | Body `{ day?, comment }`. Answers `{ exception, awards, progress }`. A future day, a blank comment or a malformed date is a 400 in words. |
  | `POST /api/domains/exceptions/{day}/unmark` | Answers `{ exception: null, awards, progress }`. |

- **CLI:**
  - `bsk domains exception [<date>] "<comment>"` (the date can also be `today` or
    `yesterday`) and `bsk domains exception --unmark <date>`.
  - `bsk domains exceptions [--days N]`.
  - Each takes `--json`, and the writes print `progress` like `bsk adhere`.

#### 7.4.4 The fold, v2

`DomainProgress.Fold(rules, days, exceptions, today, settledThrough)` stays a pure function,
and it's still the only source of progression numbers (D12, D17). It walks each calendar day
from the start (the first day with paid XP) to today:

1. **Lifetime XP:** add the day's paid XP to the total, and read the lifetime level off the
   curve. Paid XP counts the moment it's paid, on any day.
2. **What kind of day it is:**
   - An **exception day** isn't counted.
   - A day **after `settledThrough`** (today, and yesterday before 4 a.m.) isn't counted yet.
   - Every other day is a **counted day**. It joins the running sums: base XP, and, for each
     distinct `ActiveMinimum`, how many counted days reached it. Those make the age, average
     and active-day tests O(1) per level.
3. **Consistency level:** on a counted day, test levels upward from 2 over the last
   `N = min(90, counted days)` counted days, stopping at the first failure (§7.2). On any
   other day C stays where it was.
4. **Target and shown level,** in ten-thousandths (§7.2), where `T = min(lifetime level, C)`:
   - If T ≥ S, S rises to T, on any day.
   - On a counted day with T < S, S falls 1%, never below T, unless the last 7 counted days
     are back on track for level S.
   - On an exception day or an unsettled day, S doesn't fall.
5. **Multiplier success:** a day is **active** when its action XP reaches `ActiveMinimum(S)`,
   using S as it stood going into the day. That's the multiplier's successful day.
6. **Best:** best level and best rank are the running maxima of S and rank. The first day the
   best rank reaches each rank is recorded, which is when its Gems are due (§7.4.6).

Each day's `DomainStanding` gets:

- day, total XP, lifetime level, consistency level, target, `ShownExact` (ten-thousandths),
  level (S) and rank
- best level and best rank
- whether the day was counted, an exception or active
- whether the shown level was falling or paused

**`MultiplierFor(day)`** reads the 30 complete days before `day`, skipping exception days:
the share that were active, with days before the start counting as not active.

**Outputs** replace the X4 ones:

- **Lifetime:** `TotalXp`, `LifetimeLevel`, `XpIntoLevel`, `XpToNext`.
- **Consistency:** `ConsistencyLevel`, the window (N, from, to), its average base XP, and
  `NextConsistency`. That's what level C + 1 needs: the average against the window's, active
  days of `ActiveMinimum(C + 1)` against the window's, and the age test against the active
  days so far. It's null at 100.
- **Shown:** `Level`, `ShownExact`, `Target` and `Rank`.
  - `LimitedBy` says whether lifetime or consistency sets the target (null when they're
    equal).
  - Also `Falling`, `Paused`, `FallingSince`, and `LastChange`, the last day S moved and
    from what.
- **Best:** `BestLevel`, `BestRank`, `RanksReached` (rank and the day it was first reached),
  and the day the best level was reached.
- **Current:**
  - today's base and action XP, `ActiveMinimum(S)`, and whether today is already active
  - the last 30 counted days' active share, `MultiplierNow` and `NextTier`
  - `SettledThrough`
- **`Report(days)`:**
  - The last N calendar days ending at `SettledThrough`, so an unfinished day never counts.
  - Days counted, exception days, active days, average base XP, XP paid and base.
  - Level and rank at the start against now, best rank, and the ranks first reached in the
    period.
  - The daily `Standings`, for the rank-over-time chart.

**Gone:** `Capped`, `BankedXp`, `TopLevel`, `RawLevel`, `NextRank`, `DaysOld`, the rank walk
and `Qualifies`. `ConsistencyWindow` stays for the multiplier's window.

**Cost:** about 2,600 days × at most 100 levels, with O(1) tests, so still trivial per read.

#### 7.4.5 The multiplier on earns

`DomainPricing.Price(price, creditedOn, folds)` doesn't change. It calls `MultiplierFor`,
which now reads active days (§7.4.4). The frozen-when-issued rule (§3.6) and rounding are
unchanged. Earns paid before X10 keep what they paid.

#### 7.4.6 Rank-up Gems

`RewardService.PayRankUpsAsync` keeps its shape. It now loops over `RanksReached` as the
first days the **best** rank reached each rank, on or after the wallet's opening.

- Same award keys, so a rank paid under X4's gates is never paid again, and it stays paid
  even if the v2 best rank is lower.
- `RankEntry` keeps dating the earn on that day and citing the domain's latest credit event
  on or before it.

#### 7.4.7 Progress and feedback

- **`DomainChange`** (and `DomainChangeRow`) gains `BestLevelFrom`, `BestLevelTo`,
  `BestRankFrom` and `BestRankTo` beside the level and rank. It's still built by folding
  before and after the write.
  - `RewardService.DomainChanges` drops its capped-level logic.
  - When a write pays a rank-up, the row's best rank starts from the rank below the lowest
    one paid, as now, so a rank reached by time alone still announces on settle.
- Every response that answers `progress` keeps answering it: status, adhere, timer, food
  writes and food settle, activity sync, domains settle. So do the exception-day writes, since
  marking or unmarking a day can move a level.
- **Web (`announceEarned`, `progressMessage`):**
  - A new best rank gets the grand celebration ("You're now a Health Expert.").
  - A new best level gets the full one ("❤️ Health level 23!").
  - Getting back to a level below the best is a quiet note: "Health back to level 22".
  - A level lost in a write is a quiet toast whose words fit the cause. The old reason text
    ("A reversal took back the XP that level needed") is wrong for an exception day being
    unmarked.
- **Changes over time** (a fall, or a rise as days settle) happen at 4 a.m., when nothing is
  writing:
  - The Character page shows them. Its card reads "Down from 96 on Oct 24 · falling 1% a day
    toward 90", from `LastChange` and the standings.
  - A new best reached by time alone is celebrated once, the next time the page opens. The
    page keeps the last best it celebrated per domain in `localStorage` and compares. If
    nothing is stored (the first visit after v2), it stores the current best without
    celebrating.
  - A rank reached by time still pays and celebrates through settle, as now. The page then
    updates what it stored, so it doesn't celebrate twice.

#### 7.4.8 Reads

| Read | v2 shape |
|---|---|
| `GET /api/domains` | Configured rows replace `lifetime` / `nextRank` / `current` / `ranks` with: **`lifetime`** (total XP, lifetime level, XP into it and to the next); **`consistency`** (level, the window's days, dates and average, and what the next level needs: average, active minimum, active days and age, each with where it stands now); **`shown`** (level, exact level to two places, target, rank and title, `limitedBy`, `falling`, `paused`, `fallingSince`, `lastChange`); **`best`** (level, rank, title, and the day each was reached); **`current`** (today's base and action XP, the active minimum, today active, the rule in words, the 30-day active share, multiplier and next tier, `settledThrough`); **`ranks`** (the ladder: title, start level, the average, active minimum and active days of 90 it takes to hold, total XP to reach, Gems, and the day the best rank first reached it). |
| `GET /api/domains/{key}/days?days=N` | Per day: base, **action**, paid, **exception** (its comment, or null), **counted**, **active** (replacing `succeeded`), and the lifetime, consistency and shown levels and rank. The top level adds the active minimum per day, which replaces the flat `target` line. |
| `GET /api/domains/report?days=N` | Per configured domain: `from`/`to` (ending at `settledThrough`), days counted, **exception days**, **active days** (replacing successful days), average base XP, XP, level and rank from → to, **best rank**, the ranks first reached in the period, and **`ranks`**: the rank (and level) for each day of the period. Unconfigured domains: XP only, as now. |
| Exception days | See §7.4.3. |
| `bsk domains [<domain>]` | The same in text: "Level 47, Expert (best 52, Veteran)", then the target and what limits it, then falling or paused. Then the lifetime level and its XP to go, the consistency level and what the next one needs, today against the active minimum, the multiplier, and recent exception days. `--days` adds the action, exception and active columns. `--report` gets the period's figures and the rank at the start and end. `--json` carries all of it. |

#### 7.4.9 The Character page (web)

The header line "Levels and ranks are for life" goes. It becomes: "Your level follows your
last 90 days. It rises at once, falls slowly, and your best is kept."

Health's card:

- **Level and rank:** "Level 47", the rank badge, and "Best: level 52, Veteran" with its day.
- **One state line:**
  - "Lifetime XP sets your level: 1,240 XP to level 48."
  - "Your last 90 days support level 47."
  - "Falling 1% a day toward 41 (about half a level a day). Seven days back on track pauses
    it: 43+ base XP a day on average, with 5 active days."
  - "Paused: your last 7 days are back on track."
- **Two bars:**
  - **Lifetime level**, with XP progress to the next level.
  - **Consistency level**, with what the next one needs: "Level 48 needs a 90-day average
    of 49 base XP (you're at 47) and 62 active days of 90 (you have 60)." Each part shows
    whether it's met.
- **Now:**
  - Today's ring is action XP against the active minimum: "Today: 12 of 17 action XP to
    count as active".
  - The multiplier chip reads "×1.2 · 24 of the last 30 days active".
  - The note after a long break now says the level is falling, not that it stays.
- **Exception days:**
  - A "Mark a day as an exception" button opens a dialog with the date (today, yesterday, or
    pick an earlier one) and a required comment.
  - The last 90 days' exception days are listed with their comments, each with "Unmark".
  - Marking or unmarking answers `progress`, so the toasts above apply.
- **The daily chart** (`domain-daily-chart.tsx`):
  - Bars are action XP, the full tone on active days and a lighter step otherwise. Sleep is
    stacked on top in a lighter tone, labelled "sleep (passive)".
  - The active minimum is a dashed step line, since it moves with the level.
  - Exception days get a hatched band, with the comment in the hover text and the table.
  - The table view adds action, sleep, active and exception columns.
- **The rank ladder:** each rank's levels, what it takes to hold (average, active minimum,
  active days of 90), total XP to reach, Gems, and the day the best rank first reached it.
  The current rank is marked "(you)" with `aria-current`, and the best rank is marked
  "best" when it's higher.

#### 7.4.10 Reports (web)

- **Rank over time** replaces the period consistency chart.
  - It's a step line per configured domain across the chosen period (7 days to a year),
    with the rank titles on the axis.
  - The best rank is a dotted line, and exception days are hatched bands.
  - "Compare with" goes, since the line shows the trend itself.
  - With Health alone that's one line. From three configured domains, the lines share the
    chart, which is the balance view Chris asked for.
- **Since the start:** each configured domain's shown level as a bar from 1 to 100, ticked
  at each rank, with the best level as a second tick.
- **Spiders,** from three configured domains: rank per domain, today filled and the period's
  start dashed. They're built and unit-tested but stay unreachable until two more domains
  have rules, as now.
- **The By domain table:** days counted, exception days, active days and share, base XP a
  day, XP, level from → to, rank from → to, best rank, and new bests in the period.
- The same words, colours and dataviz validation as X7 (§3.13).

#### 7.4.11 The simulator

`DomainSimulator.Run` folds and prices with the v2 fold.

- **Patterns gain:**
  - passive XP a day, `--sleep` (default 15, drawn from the activity plan's sleep spread)
  - exception spans, `--exception <start>:<length>`, beside `--lapse`
- **Output:**
  - per rank, the runs whose best rank reaches it, with p10 / p50 / p90 years
  - the final shown level and rank, and the best level
  - **rank drops a year** (years 6–10), the **lowest level in the last 2 years**, and the
    average active share
- **Regression scenarios,** each set from C# runs and checked against §7.3:
  - **Steady** (the calibration routine's average): Grandmaster in every run in 6–8 years;
    the lowest level in years 9–10 stays above 85 (in the 90s before 2026-10-27).
  - **Human** (10% of days off): Champion by year 5 in every run; level 100 in about a
    third of runs.
  - **High activity** (100 a day): level 100 in about 5.3 years, with no rank drops after.
  - **Lapse** (6 months off in year 3): the level falls, then is back within about a year
    of returning.
  - **Marked lapse** (the same 6 months as exception days): level and rank don't move.
  - **Below the bar** (a flat 38 base XP): settles at the level that average supports,
    level 18 (A(18) = 38, A(19) = 39), and stays there. Until 2026-10-27 it was a flat 30
    and level 19.
  - **Level 100, then nothing:** Legend on day 10, Champion on day 21, Novice on day 249
    (§7.3).
- `--replay` folds the real days, exception days and passive split included, and projects
  from tomorrow. `--no-multiplier` stays.
- **What the C# runs gave** (20 runs of 10 years, seed 1), and the bands the tests hold:
  - Steady: Grandmaster in every run at 6.8–7.3 years; the lowest level in the last 2 years
    91–94; 3 to 7 rank drops a year (short Legend dips).
  - Human: Champion at about 4.3 years in every run; Grandmaster in 7 of 20.
  - High activity: level 100 at 5.3–5.4 years, and no drops after.
  - Lapse: back to the level it had within a year of returning; Grandmaster at 7.3–7.7 years.
  - Marked lapse: nothing moves during the span; the same final results as the lapse.
  - Below the bar: level 18 (Apprentice) in every run.
  - **After the 2026-10-27 calibration (§2.4):** Steady: Grandmaster in every run at
    6.7–7.2 years; the lowest level in the last 2 years 87–95; 3 to 6 rank drops a year.
    Human: Champion at about 4.3 years in every run; Grandmaster in 7 of 20, as before.

#### 7.4.12 Switching over

- **No data migration.** The deploy that carries X10 re-reads all of Health's history under
  v2, and Chris's level is recomputed on the spot.
  - His history began on 2026-10-20. The cheaper curve lifts his lifetime level.
  - The age test holds the consistency level near his count of active days for now.
  - The shown level is the lower of the two.
- **Gems:** ranks already paid stay paid, and none is paid twice. A best rank lower than one
  already paid doesn't take anything back.
- **Before pushing,** run `bsk domains health --json` on staging and keep the output, so the
  before and after can be compared.

### 7.5 Build notes

The phases are X8–X11 in §4.

- **X8 is docs only:** the requirements and this section.
- **X9 can be pushed alone.** It adds a migration, endpoints and `bsk` verbs, but no number
  Chris sees changes, because the fold ignores the new inputs until X10.
- **X10 and X11 are pushed together.** X10 changes the shape of `GET /api/domains`, `days`,
  `report` and `progress`, so the live web would break between them. Commit X10, then X11,
  then push once. Before that push, keep `bsk domains health --json` from staging
  (§7.4.12).
- **X10 is the biggest phase.** While building it can be split into the fold (Domain and its
  tests) and the wiring (pricing, Gems, reads, CLI, simulator), as long as both land before
  the push.
- **0043 is the only migration.** Merging to `main` promotes it to staging through CI, as
  usual.
- Each phase updates the Build fields of REWARD-6, REWARD-7 and REWARD-8 as it advances them.
- **Then calibrate** (§2.4): replay the real days with `bsk domains simulate --replay`, and
  revisit the average needed for level 100 once there are a few weeks of them.

### 7.6 How v2 was settled

**The proposal (2026-10-25).** Chris's planning agent proposed dual-gated levels: the level
would be the lower of the lifetime level and a consistency level. It came with per-level
formulas (each level its own window of L + 1 days, an active share from 75% to 90%), a cubic
lifetime curve, and a fall capped at 2 levels a day. A review, run against Python models of
the rules, changed:

- **One shared 90-day window**, not a window per level. Per-level windows judged low levels
  on only a few days (level 10 on 11), so they swung with every bad day. A level could also
  pass while one below it failed.
- **An active share from 50% to 90%**, not 75% to 90%. In the narrow band each 1% of active
  days was worth 6–7 levels, so one bad patch failed every level at once.
- **Exception days with a comment**, generalizing separate sick and vacation days.
- **Rank follows the shown level, and the fall is slowed**, rather than rank keeping its own
  slower clock. Rank is the figure Chris compares across domains.
- **Celebrations only for a new best**, so a dip and a recovery stay quiet.
- **The age test counts active days.** Counting days of any kind let consistency climb after
  a short binge stopped.

**The fall.** Fixed falls of 2, 1 and 0.25 levels a day were tried in the simulator. Chris
wanted a sedentary stretch to cost more at high levels than low ones, and chose **1% of the
level a day, with no hold**: 1 level a day at level 100, 0.1 a day at level 10. He accepted
the short Grandmaster dips it brings (§7.3) rather than add a 3-day hold, which would have cut
them by about 40%.

**Decisions (2026-10-25).** Chris chose the new lifetime curve and took every other proposed
default:

| Decision | Chosen | Not chosen |
|---|---|---|
| Lifetime curve | The new curve (§7.2) | Keep §2.1's curve |
| Limit on exception days | None; a comment is required | A cap, such as 30 a quarter, or a confirmation for runs over 14 days |
| Which domains an exception covers | Every configured domain | Chosen per domain when marking |
| When a day settles | 4 a.m. the next morning | Also wait for the Oura sync |
| Average needed for level 100 | 80 base XP a day, recalibrated in use | Set from real days now (the routine sits at about 79) |
| The multiplier's successful day | Active at the shown level | Keep 60 base XP (nearly the same results) |
| Reports across domains | Each domain's rank over time | Today's consistency % and level |

**Chosen in planning** (not raised with Chris; each is easy to revisit):

- **Exception days are today or earlier.** Marking a future day, such as a planned trip, is
  in §7.7.
- **The shown level is kept in ten-thousandths**, as a whole number, so the C# fold and its
  TypeScript port can't drift apart on rounding.
- **The multiplier's window stays on complete calendar days**, as live, with exception days
  skipped. It doesn't wait for 4 a.m., so the multiplier doesn't shift overnight.
- **Lifetime XP counts as soon as it's paid**, so a level-up from earning shows straight
  away. Consistency moves only as days settle, and a fall only happens on a settled day.
- **A drop over time isn't toasted.** It happens at 4 a.m., when nobody is writing, so the
  Character page shows it. A new best reached by time alone celebrates the next time Chris
  opens the page (§7.4.7).
- **X10 and X11 are pushed together**, because X10 changes the API's shape.

### 7.7 Later (outside X8–X11)

- **Exception days ahead of time**, for a planned trip.
- **Exception days per domain**, or a cap on them, if the defaults don't hold up in use.
- **Rank milestones** (§6) need rethinking: they gated entering a rank, and a rank can now
  fall.
- **Calibrating the average needed for level 100** from Chris's real days. It's part of
  calibration (§2.4), using `bsk domains simulate --replay`.

## Status

- [x] X0 — Requirements (REWARD-6, REWARD-7, REWARD-2 revision, D12) + this plan
- [x] X1 — Domain currencies + the credited day (migration 0030)
- [x] X2 — Established habits keep earning (migration 0031)
- [x] X3 — Rules + daily consistency (migration 0032)
- [x] X4 — Levels, ranks, rank-up Gems, simulator (no migration). Next: calibrate (§2.4).
- [x] X5 — The multiplier on earns (no migration)
- [x] X6 — Character page (no migration)
- [x] X7 — Reports + charts (no migration)
- [x] X8 — Levels v2: requirements + plan (§7)
- [x] X9 — v2 inputs: exception days, action XP, settled days (migration 0043)
- [x] X10 — Levels v2 in the kernel (no migration; push with X11)
- [x] X11 — Levels v2 on the web (no migration). Next: calibrate (§2.4).
- [x] Calibration, 2026-10-27: an active day from 20 action XP and level 2 from a 30 base XP
  average (§2.4; no migration). Next: Chris lives with it.
