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
| A successful day (Health) | **At least 70 base XP** (before the multiplier). Flat, with no rise by rank. It's a provisional number, recalibrated after a few weeks of real Health XP (§2.4). |
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

A successful day is 70+ base XP at every rank.

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
  - Health uses `MinimumBaseXp(70)`.
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
    - **Steady** (95 / 15, essentials 70, 5% off): Grandmaster in every run, median 5–8
      years; the multiplier is worth more than a year.
    - **Human** (90 / 15, essentials 70, 10% off, 4% half): Champion always, Legend in at
      least 18 of 20, Grandmaster in 1–9 of 20 and never before 6.5 years, consistency 85–89%.
      Loosened from "never before about 7.5 years": the C# runs land 6.9 at the earliest.
    - **Improving** (+5 a year): Grandmaster in every run within 10 years.
    - **Lapse** (6 months in year 3): the standing doesn't move through it; at its end 0%
      and ×1.0; a month back, at least 70% and ×1.2.
    - **Below the minimum** (60): level 9, Novice, the rest banked, never multiplied.
    - **Perfect** (70): each rank the day its level arrives; Grandmaster in 7–8.5 years.
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

### 3.12 The Character page (web)

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
  recovery-day routine.
- **A weekly cadence**, for Social and Finance.
- **"Rank achieved" vs "currently qualified":** a permanent record of the highest rank ever
  reached, shown beside what today's rules qualify.
- **Tuning the rules in the app** instead of in code.
- **Each other domain's own rules**, one at a time, the way Health was done.
- **More domains:** a line in `Currencies`, plus a rules record when it's configured.
- **Life XP levels**, or an overall character level.
- **A backdated completion** ("completed yesterday"), so a late check-off counts on the right
  day.
- **Penalties** in domain XP: §3.4's credit read would need to count entries without an award
  key.
- **Area hints:** pre-fill an item's domain XP from its Area.

## Status

- [x] X0 — Requirements (REWARD-6, REWARD-7, REWARD-2 revision, D12) + this plan
- [x] X1 — Domain currencies + the credited day (migration 0030)
- [x] X2 — Established habits keep earning (migration 0031)
- [x] X3 — Rules + daily consistency (migration 0032)
- [x] X4 — Levels, ranks, rank-up Gems, simulator (no migration). Next: calibrate (§2.4).
- [x] X5 — The multiplier on earns (no migration)
- [x] X6 — Character page (no migration)
- [ ] X7 — Reports + charts
