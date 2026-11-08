# Reviews Plan — daily and weekly reviews on the live site, the LifeOS half

The design and build order for the daily and weekly reviews (REVIEW-0, REVIEW-1, REVIEW-2 and
REVIEW-5 in [ui-requirements.md](ui-requirements.md), decisions D3 and D8 in the same doc). The
requirements say *what*; this doc says *how* and *in what order*, written so an agent with no
session context can pick up any phase.

**Why now.** Until this plan, the Reviews page worked only in mock mode: on the live site it
showed nothing, and saving said reviews were mock-only. Sunday is review day, and Chris had no
working review.

**The split with SuperJournal.** On 2026-10-28 Chris decided that review *answers* will live in
SuperJournal, one entry per question with every version kept (csdelan/LifeOS#29, epic #27).
SuperJournal isn't deployed yet: csdelan/SuperJournal#2 waits on
csdelan/BlueSkies.Platform#184, and LifeOS's client for it (#28) sits unmerged on branch
`claude/superjournal-connect-9e24c0`. Everything else about a review stays in LifeOS under #29's
own design: the Review subject, whether it's due, done or missed, completing it, the recap and the
habit check-offs. This plan builds that half now, and puts the answers behind a port (§4) so the
SuperJournal implementation can replace the interim one without touching the page.

## 0. Read these first

- [ui-requirements.md](ui-requirements.md): REVIEW-0 (the shared rules), REVIEW-1 (daily),
  REVIEW-2 (weekly, with Chris's 2026-10-23 week boundaries), REVIEW-5 (habit check-offs), D3
  (a review is an editable document with an edit trail), D8 (recurrence, due and missed as a
  shared mechanism).
- csdelan/LifeOS#29 (the SuperJournal design for answers) and #27 (the epic).
- [measurements-plan.md](measurements-plan.md) §4.1: a subject type whose attributes one service
  writes. A Review copies that shape.
- `AGENTS.md`: Pacific days, Monday-to-Sunday weeks, the reviews' Sunday naming.

## 1. The rules

| Question | Decision |
|---|---|
| Which reviews | **Daily and weekly.** Monthly (REVIEW-3) and yearly (REVIEW-4) come later. |
| What a week is | **Monday to Sunday.** A weekly review is **named by its Sunday**, looks back on the week ending that day and plans the week after (Chris, 2026-10-23): "Weekly review, Sun Oct 25" looks back on Mon Oct 19 – Sun Oct 25 and plans Mon Oct 26 – Sun Nov 1. |
| When one exists | **Lazily, the first time it's opened** (REVIEW-1's ontology fit: no scheduler). The page opens today's daily review, and on a Sunday that week's review, as soon as you look at it. |
| How many | **One per kind and period.** Opening it again, or from two tabs at once, returns the same review. |
| Ahead of time | **Never.** Neither kind opens before its day, so a week has only daily reviews until its Sunday ("The weekly review should not create a second running document earlier in the week", REVIEW-2). |
| Due | **At the end of its own day** (D8); a weekly review is due on its Sunday. No grace period. |
| Missed | **An Open review whose day is over reads as Missed.** Nothing writes that on a schedule: it's worked out on every read from the day and when the review was first completed. Missed can also be set by hand ("not doing this one"), which is also what Drop sets. |
| Late | **A past review can be opened and completed late** (a backfill). It reads "Completed late" and stays missed: completing it doesn't erase the miss (REVIEW-2). |
| Completing | A **status change to Completed**, as `bsk status` writes it, so it's in the review's history. Completing a completed review writes nothing. A completed review stays editable. |
| Keep focus | A **"keep my Primary focus" switch** on each review, on unless turned off. |
| The recap | **A read over `bsk`**: status changes, completed and overdue Tasks, repeating Tasks, habits, appointments and the Inbox for the days it looks back on. |
| Habit check-offs | **The same buttons as Focus** (followed, partial, not followed) for each habit day the review looks back on, writing the same adherence events (REVIEW-5). |
| Answers | **Behind a port** (§4). Where they live until SuperJournal is up is Chris's call, asked 2026-11-08. |

## 2. The model

### 2.1 Review: the 18th subject type (migration 0052)

- `type = 'Review'`, title "Daily review, Sun Nov 8" or "Weekly review, Sun Nov 8".
- `attributes.kind` is `daily` or `weekly`; `attributes.period` is the day (`yyyy-MM-dd`), or the
  Sunday naming the week. Only `ReviewService` writes them; `bsk set` refuses both.
- `attributes.keep_focus`: true unless false.
- A unique index on `(attributes->>'kind', attributes->>'period') WHERE type = 'Review'` keeps one
  review per period, so a race to open it lands on one subject (`ReviewService` catches the
  duplicate and reads the winner).
- Statuses: **Open, Completed, Missed** (`StatusVocabulary`, mirrored in the Pilot). Drop sets
  Missed.
- `bsk.v_review` reads each review's kind, period, folded status, `keep_focus`, `completed_at`
  (when the current Completed was set), and `first_completed_at` / `first_completed_on` (the first
  Completed ever, and its home-zone day). A review whose kind or period doesn't parse is left out.

### 2.2 The rules in Domain (`Reviews.cs`)

- `ReviewPeriods`: `WeekSunday`, `PeriodFor`, `LooksBack`, `Plans`, `Title`, `WhyNotOpen`.
- `Review.StatusAsOf(today)`: Completed; Missed when set by hand, or Open past its day and never
  completed; otherwise Open. A review completed on time and reopened reads Open, not Missed.
- `Review.MissedAsOf(today)`: first completed after its day, or never completed and past its day
  (or set Missed). `Review.CompletedLate`: Completed now, first completed after its day.

### 2.3 Service and CLI

- `ReviewService` (Application/Reviews): `OpenAsync(kind, day?)` is open-or-create for the period
  holding the day (today by default), refusing a future one in words; `CompleteAsync(id)` and
  `CompleteAsync(kind, day?)` (opens first, for a backfill); `SetKeepFocusAsync`.
- `bsk review open daily|weekly [--date D]`, `bsk review complete daily|weekly [--date D]`,
  `bsk review list [--kind K] [--from D] [--to D]`. `D` is `yyyy-MM-dd`, `today` or `yesterday`.
- `/mcp`'s guide lists the type, so agents see reviews through `list_items` and `get_item`.

## 3. API and web (R2, R3)

### 3.1 API (R2)

`/api/reviews`, the twin of `bsk review`. Reads use `ReviewReader` on the reader connection;
writes call `ReviewService`. Refusals are 400s in words; an unknown review is a 404.

| Endpoint | What it does |
|---|---|
| `GET /api/reviews?kind=&from=&to=` | The reviews whose period is in the range (the four weeks to today by default, at most 400 days), daily first on a day. Each row has its `date` (the day, or the Sunday), `weekStart`–`weekEnd` (its Monday-to-Sunday week), `planStart`–`planEnd`, `dueOn`, `status` as of today (Open, Completed, Missed), `missed`, `completedLate`, `completedAt` and `keepFocus`. |
| `POST /api/reviews/open {kind, date?}` | Open-or-create the review holding the day (today by default). A weekly review is the week's, named by its Sunday. Ahead of its day: 400. |
| `POST /api/reviews/{id}/complete` | Completes it; again writes nothing. |
| `POST /api/reviews/{id} {keepFocus?}` | Saves what's sent. R4 adds the answers. |
| `GET /api/reviews/recap?from=&to=&asOf=` | What was recorded over the days (at most 62): status changes (reviews' own left out), Tasks completed, Tasks open and due before `asOf` (the earlier of today and `to`), habit days, appointments, journal entries and what came into the Inbox. A repeating Task's series is left out; the web adds the repeating Tasks' own section, as it did before. |
| `GET /api/habits/occurrences?from=&to=` | Gains a range, for a weekly review's check-offs (REVIEW-5). |

### 3.2 The live Reviews page (R3)

- `web/src/lib/reviews.ts` is the web's twin of the rules in Domain: `reviewPeriod` (the days a
  review looks back on and plans), `whyNotOpen`, `reviewStateAsOf` (the mock works status out
  with it; the live site reads it from the API) and `reviewHabitDays`.
- **Daily** shows today by default; **Weekly** shows the week ending this Sunday. Previous and
  Next step a day or a week. An old link with a weekday for a weekly review lands on its Sunday.
- **Opening:** today's review, and on a Sunday the week's, opens as soon as it's looked at. A past
  one that was never written reads Missed, with *Write it late*. A future one says when it opens.
- **Recap:** the server's sections plus the repeating Tasks' section, for any period that has
  started, with or without a review. Habit days leave the recap for their own check-offs.
- **Habit check-offs (REVIEW-5):** each habit day the review looks back on, up to today, with the
  Focus buttons; a week picks one day at a time. A habit counts while Building or Established,
  from its start (or the day it was made) to its end.
- **The week:** a weekly review lists its seven daily reviews and where each stands; a daily one
  links to its week's review.
- **Answers:** on the live site the questions show but can't be written until §4 is settled, and
  the page says so. Keep focus saves.
- **Mock mode** follows the same rules: reading never makes a review, opening does, and its
  status is worked out on read.

## 4. Answers

Waiting on Chris (asked 2026-11-08): where answers go until SuperJournal is live.

## 5. Phases

| Phase | What lands | Migration | Pushable alone |
|---|---|---|---|
| **R0** | This plan | — | Yes |
| **R1** | The Review type, `bsk.v_review`, the rules in Domain, `ReviewService`, `bsk review`, the `/mcp` guide row | 0052 | Yes |
| **R2** | `/api/reviews`: list, open, complete, keep focus; the recap read; habit days for a range; `schema.d.ts` regenerated | — | After R1 |
| **R3** | The live Reviews page: Monday-to-Sunday weeks named by their Sunday, open on view, Missed and late, the recap and the habit check-offs | — | After R2 |
| **R4** | Answers behind a port (§4) | Maybe | After R2 |
| **R5** | Answers in SuperJournal (#29), once SuperJournal#2 and #28 are in | No | After #28 |

- **Migration numbers:** this plan takes 0052. Branch `claude/superjournal-connect-9e24c0` has
  `0049__journal_pointer.sql`, which must be renumbered above the highest number on `main` when
  it merges (0053 or higher, more if R4 adds one).
- Checks before each commit: `./test.ps1` (.NET), and from `web/`: `npm run lint`,
  `npm run typecheck`, `npm test`.

## 6. How this was settled

- **2026-10-23, Chris:** weeks run Monday to Sunday; the weekly review is named by its Sunday,
  looks back on the week ending that day and plans the next (REVIEW-2). The placeholder page
  stayed on `startOfWeekSunday` until REVIEW-2 was built; R3 moved it to Monday-to-Sunday weeks.
- **2026-10-28, Chris (#27, #29):** answers live in SuperJournal, one entry per question, every
  version kept; the review subject, due, done and missed, the recap and the check-offs stay in
  LifeOS.
- **2026-11-08:** Chris picked "Reviews, the LifeOS half" as the next most useful piece of work,
  since SuperJournal is blocked on hosting.
- **Filled in here:** lazy creation on first open; one per kind and period; nothing opens ahead
  of its day; Missed read, not written; a late completion stays missed; Missed as Drop's status;
  keep focus on by default.

## 7. Later

- **A reason for a missed review** (REVIEW-1: "record an optional reason for missing it").
- **The weekly-specific questions** (REVIEW-2's open decision) and REVIEW-0's guided
  reflection-then-planning flow with progress through the questions.
- **Goals whose target date lands in the coming week** called out in weekly planning (REVIEW-2).
- **Monthly reviews** (REVIEW-3) with their three-day grace window.
- **Reviews in the Pilot**, if Chris wants them there.
- **A missed-review nudge** wherever other due or missed things surface (D8, REVIEW-0).
- **REVIEW-5's rest:** an optional note on a habit day from the review, and telling
  self-assessed habit days from automatically scored ones.

## Status

- [x] R0 — This plan
- [x] R1 — Reviews in the kernel (migration 0052)
- [x] R2 — The reviews API
- [x] R3 — The live Reviews page
- [ ] R4 — Answers behind a port
- [ ] R5 — Answers in SuperJournal (blocked on csdelan/SuperJournal#2 and #28)
