# Code quality scorecard

A fixed way to score the code from 1 to 10, so scores from different reviews can be compared over
time. It has two halves:

- **Rubric scores:** each area is scored on six dimensions by a reviewer reading the code. These
  are judgement, made comparable only by keeping the rubric, the areas and the method the same.
- **Measured numbers:** `scripts/quality-metrics.ps1` counts sizes, big files and test ratios at any
  commit. These are exact and can be worked out for old commits too, so they show the trend
  before the first scored review.

Neither half is the truth on its own. A score that moves less than a point between reviews is
noise; a measured number that moves with it is a signal.

## The scale

Each dimension gets a score from 1 to 10 (half points allowed).

| Score | Means |
|---|---|
| 1–2 | **Spaghetti.** No real boundaries; logic wherever it landed; copies everywhere; little or no testing; any change can break something unrelated. |
| 3–4 | **Tangled.** It works, but responsibilities blur; several god files; rules copied and drifting; tests thin or brittle. |
| 5–6 | **Typical professional.** Layers mostly clear; some oversized files and known duplication; tests cover the main paths; a new feature touches many places. |
| 7–8 | **Strong.** Boundaries clear and mostly enforced; most rules in one place and copies checked; broad tests, invariants included; debt small, known and tracked; a new feature is mostly local. |
| 9 | **Exemplary.** Invariants enforced by checks that fail; a new feature is local and guided; almost no drift; reads and writes stay bounded as data grows. |
| 10 | **Near perfect.** Kept in reserve: nothing a strong reviewer would change. |

Calibration: a well-run commercial codebase sits around 6. Score the code as it is now, not
plans or intent. Weigh how severe and how widespread problems are, not how many a review listed.

**Target: 7 to 8 (Strong) in every area.** Chris's call (2026-11-13): beyond that isn't worth the
effort for a system with one user. So a review's proposals aim at whatever keeps an area below 7,
and work that would only take an area from 8 towards 9 isn't proposed.

## The dimensions

1. **Structure:** boundaries, layering, single responsibility, file and function size.
2. **Single source of truth:** each rule, list and contract is defined once; any copy has a check.
3. **Safeguards:** tests, and invariants enforced by code, lint or tests that fail when broken
   rather than by convention.
4. **Scalability:** cost as data and history grow: bounded reads, index use, algorithmic cost.
5. **Extendibility:** cost of the next feature: how many places it touches, clear extension points.
6. **Readability:** naming, comments that say why, consistent idioms, the repo's conventions kept.

An area's **overall** is the mean of its six scores, to one decimal. The **repo** score is the mean
of the six areas' overalls. Both are worked out, never chosen.

## The areas

The same six areas every time, so each can be compared with its own past:

| Area | Covers |
|---|---|
| Kernel | `src/LifeOs.Domain`, `src/LifeOs.Application`, `src/LifeOs.Infrastructure` |
| API and MCP | `src/LifeOs.Api`, `src/LifeOs.Contracts` (its shapes, in `src/LifeOs.Api/Read` until CR89) |
| Database and .NET tests | `db/`, `tests/` |
| CLI, Pilot and ops | `src/LifeOs.Cli`, `src/LifeOs.Pilot`, `scripts/`, `.github/`, Docker and Fly config |
| Web data layer | `web/src/lib`, `web/src/api` |
| Web screens | `web/src/pages`, `web/src/components`, `web/e2e`, web config |

## Running it

1. Run `./scripts/quality-metrics.ps1` at the commit being scored, and add its row to the table
   below.
2. One reviewer per area reviews the code for findings first, then scores it against this file,
   with evidence for each score and what would raise it by one point. The first scores were made
   this way, so later ones should be too: a reviewer fresh from an hour of fault-finding scores
   lower than a cold reader would. Use the same model as last time where possible, and note it:
   a different model is a different ruler.
3. Add the scores below, and the review's findings to `docs/pilot/code-review-plan.md` as before.

From 2026-11-17 a scheduled task in Chris's Claude app (`lifeos-nightly-quality`, 2 AM) does this
every night, with reviewers who aren't shown any earlier score. Each night's review, with each
area's six scores and the night's notes, goes in `docs/agents/quality-reviews/<date>.md`; the
night adds one row to each table below, and up to ten of its fixes are built as CRs
(`docs/pilot/code-review-plan.md`, Nightly reviews).

## History

### Rubric scores

Overall per area (mean of its six dimensions) and the repo score (mean of the areas).

| Date | Commit | Rater | Kernel | API, MCP | DB, tests | CLI, Pilot, ops | Web data | Web screens | Repo |
|---|---|---|---|---|---|---|---|---|---|
| 2026-11-13 | 308b96d | Claude Opus 5.5, third review's area reviewers | 6.8 | 6.3 | 6.4 | 6.0 | 5.6 | 5.6 | **6.1** |
| 2026-11-13 | 6152b61 | Claude Opus 5.5, fourth review's area reviewers | 6.7 | 6.9 | 6.3 | 6.4 | 6.2 | 6.0 | **6.4** |
| 2026-11-14 | 1a079d3 | Claude Opus 5.5, fifth review's area reviewers | 6.5 | 6.8 | 6.4 | 6.4 | 6.3 | 6.3 | **6.5** |
| 2026-11-17 | 58a9e53 | Claude Opus 5.5, nightly review | 7.0 | 6.3 | 7.0 | 6.2 | 6.7 | 6.5 | **6.6** |
| 2026-11-18 | 2867e33 | Claude Opus 5.5, nightly review | 6.4 | 6.4 | 7.0 | 6.3 | 6.6 | 6.7 | **6.6** |
| 2026-11-19 | d35b8e2 | Claude Opus 5.5, nightly review | 6.5 | 6.1 | 7.2 | 6.1 | 6.5 | 6.3 | **6.4** |

Each dimension, as the mean of the six areas:

| Date | Commit | Structure | Single source | Safeguards | Scalability | Extendibility | Readability |
|---|---|---|---|---|---|---|---|
| 2026-11-13 | 308b96d | 6.3 | 5.5 | 6.7 | 5.4 | 5.5 | 7.3 |
| 2026-11-13 | 6152b61 | 6.5 | 5.8 | 6.8 | 5.8 | 6.1 | 7.4 |
| 2026-11-14 | 1a079d3 | 6.5 | 5.9 | 6.8 | 6.0 | 6.2 | 7.4 |
| 2026-11-17 | 58a9e53 | 6.3 | 6.3 | 7.1 | 6.6 | 6.3 | 7.1 |
| 2026-11-18 | 2867e33 | 6.3 | 6.3 | 7.0 | 6.4 | 6.3 | 7.1 |
| 2026-11-19 | d35b8e2 | 6.2 | 6.3 | 6.8 | 6.2 | 6.2 | 7.1 |

Each area's six scores at 6152b61 (Structure, Single source, Safeguards, Scalability,
Extendibility, Readability):

| Area | Scores | Overall |
|---|---|---|
| Kernel | 7, 6.5, 7, 6, 6, 7.5 | 6.7 |
| API and MCP | 7, 6.5, 7.5, 6, 6.5, 8 | 6.9 |
| Database and .NET tests | 6.5, 5.5, 7.5, 5.5, 5.5, 7.5 | 6.3 |
| CLI, Pilot and ops | 6.5, 5.5, 6.5, 6, 6.5, 7.5 | 6.4 |
| Web data layer | 6, 5.5, 7, 5.5, 6, 7 | 6.2 |
| Web screens | 6, 5.5, 5.5, 6, 6, 7 | 6.0 |

Each area's six scores at 1a079d3, in the same order:

| Area | Scores | Overall |
|---|---|---|
| Kernel | 7, 6, 6.5, 6, 6, 7.5 | 6.5 |
| API and MCP | 7, 6.5, 7, 6, 6.5, 8 | 6.8 |
| Database and .NET tests | 6.5, 5.5, 7.5, 5.5, 6, 7.5 | 6.4 |
| CLI, Pilot and ops | 6.5, 5.5, 6.5, 6, 6.5, 7.5 | 6.4 |
| Web data layer | 6, 6, 7, 6, 6, 7 | 6.3 |
| Web screens | 6, 6, 6.5, 6.5, 6, 7 | 6.3 |

Notes for 2026-11-13: each rater put their own uncertainty at ±0.5 (web data ±0.5 to ±1). The
split scores were database 6.5 and tests 6.2; CLI 6.0, Pilot 5.0 and ops 6.5; live web data 6.0
and the mock 4.5. The weakest dimensions are the three this repo weights most: scalability (lists
and views that read all history), single source (copies in the doors, the CLI and the web), and
extendibility (shared files every feature edits).

Notes for 6152b61 (after CR58 to CR90): the same method, model and prompts as the first scores, with
the context brought up to CR90; each reviewer was asked not to start from the earlier scores, though
the plan they read names the 6.1. Each again put their uncertainty at ±0.5. The repo rose 0.3, under
the one point that counts as more than noise on its own, but the measured numbers moved the same way
(big-file share 26.3% to 22.2%, both test ratios up). The areas the P2 work touched rose: API 6.3 to
6.9, web data 5.6 to 6.2, web screens 5.6 to 6.0, CLI, Pilot and ops 6.0 to 6.4; the kernel (6.8 to
6.7) and the database and tests (6.4 to 6.3) held within noise. By dimension, extendibility rose most
(5.5 to 6.1), then scalability (5.4 to 5.8). The splits: database 6.4 and tests 6.3; CLI 6.8, Pilot
5.3 and ops 6.8; live web data 6.8 and the mock 5.0. No area is at 7 yet; single source of truth and
scalability (5.8 each) are still the weakest. Twelve of the fourth review's proposals finish a CR
built that day, each because its check covered the one case it fixed rather than the class
(`docs/pilot/code-review-plan.md`, Fourth review).

Notes for 1a079d3 (after CR91 to CR107): the same method and model, with prompts written afresh
in the same shape (findings first, then scores against this file), and the context brought up to
CR107. Unlike the earlier rounds, each reviewer was told its area's fourth-review scores, asked not
to start from them and to say why wherever it differed by a point or more; that anchors more than
before, so a small move here says less. Each put their uncertainty at ±0.5 (the kernel ±0.4). The
repo rose 0.1, which is noise, and the measured numbers moved the same way (big-file share 22.2% to
19.2%, the web's test ratio 0.35 to 0.37, .NET's held at 0.68). Web screens rose most (6.0 to 6.3),
its safeguards 5.5 to 6.5 from the checks CR94 to CR107 added; web data rose 6.2 to 6.3; the kernel
(6.7 to 6.5) and the API (6.9 to 6.8) fell within noise, from findings in code that was already
there at 6152b61 (R5, R8, R19), and the CLI, Pilot and ops (6.4) and the database and tests (6.3 to
6.4) held. The splits: database 6.5 and tests 6.3; CLI 6.8, Pilot 5.2 and ops 6.5; live web data 6.7
and the mock 5.1. No area is at 7 yet; single source of truth (5.9) and scalability (6.0) are still
the weakest. As in the fourth review, most new proposals finish a CR whose check covered its one case
rather than the class (`docs/pilot/code-review-plan.md`, Fifth review).

### Measured numbers

See `scripts/quality-metrics.ps1` for what each column counts. The rows before 2026-11-13 were
worked out afterwards from git.

| Date | Commit | Note | Prod lines | Test : prod (.NET) | Test : prod (web) | Files > 500 lines | Lines in files > 500 | Mock lines | Largest file |
|---|---|---|---|---|---|---|---|---|---|
| 2026-09-20 | 59ac4f9 | | 31,118 | 0.78 | 0.06 | 7 | 13.3% | 2,986 | subject-detail.tsx (683) |
| 2026-10-29 | 4a74220 | | 93,698 | 0.64 | 0.31 | 34 | 27.9% | 9,333 | production-ui-types.ts (2121) |
| 2026-11-12 | a39c712 | Before the first review | 106,117 | 0.60 | 0.32 | 40 | 28.7% | 10,641 | production-ui-types.ts (2442) |
| 2026-11-13 | ce29ce3 | After CR1–CR26 | 107,181 | 0.62 | 0.32 | 38 | 26.4% | 10,547 | production-ui-types.ts (2443) |
| 2026-11-13 | 308b96d | After CR27–CR57; first scores | 107,901 | 0.63 | 0.33 | 38 | 25.9% | 10,472 | production-ui-types.ts (2460) |
| 2026-11-13 | da72df9 | After CR58–CR67 (third review's P0, P1) | 108,537 | 0.64 | 0.33 | 39 | 26.3% | 10,474 | production-ui-types.ts (2460) |
| 2026-11-13 | 6152b61 | After CR68–CR90 (its P2); second scores | 104,556 | 0.68 | 0.35 | 34 | 22.2% | 12,722 | production-ui-types.ts (2097) |
| 2026-11-14 | 1a079d3 | After CR91–CR107; third scores | 105,777 | 0.68 | 0.37 | 30 | 19.2% | 12,916 | production-ui-types.ts (2106) |
| 2026-11-17 | 58a9e53 | Nightly review | 108,142 | 0.75 | 0.37 | 31 | 19.6% | 13,223 | production-ui-types.ts (2107) |
| 2026-11-18 | 2867e33 | Nightly review | 109,226 | 0.75 | 0.38 | 31 | 19.5% | 13,359 | production-ui-types.ts (2118) |
| 2026-11-19 | d35b8e2 | Nightly review | 110,619 | 0.75 | 0.38 | 32 | 20.0% | 13,419 | production-ui-types.ts (2133) |

From 6152b61 the API's count includes `src/LifeOs.Contracts`, which CR89 made from `src/LifeOs.Api/Read`.
Part of that row's fall in prod lines, and the mock's rise, is CR84 moving about 2,200 lines that only
the mock and tests used (the domain fold, the metric trend, the habit streak, the exercise rules, the
agents and consent stand-ins, the review recap) into `lib/mock`; the rest is code deleted.
