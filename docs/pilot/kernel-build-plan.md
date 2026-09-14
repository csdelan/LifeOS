# Kernel Build Plan — sequencing the Pilot ontology backlog

The dependency-sequenced implementation plan for the "Kernel build backlog" in
[ui-requirements.md](ui-requirements.md). The backlog is the *what*; this doc is the
*order* and the *shape of each increment*. The cross-cutting decisions D1–D10 (in the
requirements doc) are locked inputs; this plan does not re-open them.

**Approved by Chris 2026-09-14.** D9 archive resolved to **Option B** (archive/restore as
recorded events + a derived flag, not a mutable boolean). Sequence and the Band-A defaults
below approved as written.

## How each phase lands

Every phase is one small, reviewable commit carrying, where applicable:

- a **numbered migration** in `db/` (schema/predicate/view; idempotent; strict-build clean),
- a **`bsk` verb** (the only write path — invariant 9) + its Application service / Npgsql repo,
- a **reader view / projection** for `bsk_reader` (many readers, one writer),
- **tests** against the Postgres fixture (`./test.ps1`).

Append-only is absolute: nothing is mutated or deleted; status moves by `state_change`;
archive is a flag (D9); projections are rebuildable via `bsk rebuild`.

## Why this order

Two orthogonal "hide from default views" rules — **terminal status (D7)** and **archive
(D9)** — are read by both the reader views and the diagnostics. They are established
*first* so every new reader/type built afterward is born terminal- and archive-aware,
instead of being retrofitted. New subject types are near-free (one `type` CHECK + a
`Vocabulary.cs` line); the real work is the primitives around them (tags, triage marker,
recurrence, adherence/occurrence projections, materialized occurrences).

## Phases

### Band A — Foundational cross-cutting primitives (Pilot)

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| 1 | **D7 status vocab + terminal set** | per-type status map (Domain mirror) + extend `is_terminal_status` (`fulfilled`,`missed`,`promoted`,`rejected`) | — |
| 2 | **D9 universal archive flag** | archive/restore **as events** + derived `is_archived`; readers + diagnostics exclude archived; `bsk archive`/`bsk restore` | 1 |
| 3 | **Tag primitive (GEN-1)** | `item_tag` store (subjects *and* events), `bsk tag +x -y`, tag-universe reader | — |
| 4 | **Area type + `attributes.area` (GEN-2/D1)** | add `Area` type; master-list + area-on-item readers; area kept separate from `focus` | 2 |
| 5 | **Triage marker + Drop + `v_inbox` (INBOX-1)** | assertable inbox flag (subjects/events), Drop outcome, `v_inbox` = flagged ∧ unresolved | 2 |

### Band B — Core creation workflows (Pilot)

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| 6 | **Atomic create-and-link (GEN-7)** | one-transaction `bsk new <Type> --parent <p>` inferring the relation map (Goal→Value `serves`, Project→Goal `results_in`, Task→Project/Goal `serves`); rollback → no orphan | existing types |
| 7 | **Capture bifurcation + split promote (D4/CAP-1/CAP-6)** | note/url = events; idea/problem = subjects-on-capture + triage flag; promote splits (event→subject vs Idea→work `results_in`, Idea→Promoted) | 5 |

### Band C — Recurrence + the Habit vertical (Pilot)

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| 8 | **Recurrence representation (D8, shared)** | richer-than-`expected_cadence`: calendar-anchored (*every Sunday*, *last day of month*) + intervals; window-expansion helper; additive (neglect keeps working) | — |
| 9 | **Habit type + adherence + partial credit (D2/GEN-3)** | `Habit` type, `allows_partial`, adherence-as-events (followed/not-followed/partial + note + backfill) | 1, 8 |
| 10 | **Habit occurrence + streak projection (GEN-3/GEN-5)** | derived occurrences (unrecorded→not-followed at window close), streak, latest-wins correction; misses excluded from breach report | 9 |

### Band D — Appointments (Pilot)

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| 11 | **People-association link** | Person-ref attribute convention (attendee/owner/assignee/waiting-for) + "objects involving Person X" reader | — |
| 12 | **Appointment type + materialized occurrences (CAL-1/D10)** | `Appointment` type, time attrs, **materialized per-occurrence records each with own status** (the deliberate D10 divergence from Habit's *projected* occurrences) | 8, 11 |

### Band E — Deferred (Pilot phase 2 / Production) — not in the first cut

- Review type + D3 mutable-body edit trail + review scheduling/missed via the D8 mechanism (REVIEW-0/1/2)
- D6 focus storage (Monthly/Weekly Goal pointers as change-events; daily objectives app-local)
- Managed binary artifact storage (CAP-2/4, JOURNAL-2) → then `voice` write path (CAP-2)
- INBOX-2 (still `drafting`, unmapped)

## Band-A settled defaults (approved)

- **D7** — status vocab validated **app-side** (Pilot UI + optional `bsk` guard), not DB enums;
  the kernel owns the terminal predicate + the documented map.
- **D9** — **Option B**: archive/restore recorded as events, folded into a derived
  `is_archived`; reversible with history, matching the append-only spine.
- **GEN-1 tags** — a normalized `item_tag` table (enables the tag universe + autocomplete),
  not a per-row array.
- **D1/Area** — item→Area as an `attributes.area` reference, kept **separate** from the
  existing `focus`/Season axis (overlap noted, not fused, in the Pilot).
- **INBOX-1** — hybrid: explicit flag on entry + explicit Drop; promote/relate stay
  untouched and count as resolution.

## Status

- [x] Phase 1 — D7 status vocab + terminal set
- [x] Phase 2 — D9 universal archive flag
- [x] Phase 3 — Tag primitive
- [x] Phase 4 — Area type
- [ ] Phase 5 — Triage marker + `v_inbox`
- [ ] Phase 6 — Atomic create-and-link
- [ ] Phase 7 — Capture bifurcation + split promote
- [ ] Phase 8 — Recurrence representation
- [ ] Phase 9 — Habit type + adherence + partial credit
- [ ] Phase 10 — Habit occurrence + streak projection
- [ ] Phase 11 — People-association link
- [ ] Phase 12 — Appointment type + materialized occurrences
