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
| API and MCP | `src/LifeOs.Api` |
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

## History

### Rubric scores

Overall per area (mean of its six dimensions) and the repo score (mean of the areas).

| Date | Commit | Rater | Kernel | API, MCP | DB, tests | CLI, Pilot, ops | Web data | Web screens | Repo |
|---|---|---|---|---|---|---|---|---|---|
| 2026-11-13 | 308b96d | Claude Opus 5.5, third review's area reviewers | 6.8 | 6.3 | 6.4 | 6.0 | 5.6 | 5.6 | **6.1** |

Each dimension, as the mean of the six areas:

| Date | Structure | Single source | Safeguards | Scalability | Extendibility | Readability |
|---|---|---|---|---|---|---|
| 2026-11-13 | 6.3 | 5.5 | 6.7 | 5.4 | 5.5 | 7.3 |

Notes for 2026-11-13: each rater put their own uncertainty at ±0.5 (web data ±0.5 to ±1). The
split scores were database 6.5 and tests 6.2; CLI 6.0, Pilot 5.0 and ops 6.5; live web data 6.0
and the mock 4.5. The weakest dimensions are the three this repo weights most: scalability (lists
and views that read all history), single source (copies in the doors, the CLI and the web), and
extendibility (shared files every feature edits).

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
