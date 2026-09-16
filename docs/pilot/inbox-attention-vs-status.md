# Inbox Attention vs. Status — separating the two axes

A design decision for the Pilot ontology. It supersedes the triage-state shape from
migration [0015](../../db/migrations/0015__triage_marker.sql) (INBOX-1) where that shape
conflicts with what is written here; it does **not** re-open the assert-not-infer inbox
seam, which is kept and reinforced.

**Approved by Chris 2026-09-16.** Derived-inbox model chosen (Option: *derived*, not
*fully explicit*). Recommendations below agreed as written; implementation is a follow-up
increment, not yet built.

## The problem

The Inbox triage marker carries one field with values `flagged | dropped | filed |
promoted` (newest-per-item wins; `v_inbox` = items whose newest marker is `flagged`).
Only `flagged` is about *attention*. `dropped` ("I looked, it's nothing") and, more
weakly, `filed` ("keep as reference") are **lifecycle outcomes** — they describe the state
of the *object*, not whether it needs my attention. That is status information buried
inside the inbox marker.

Two consequences fell out of this in use:

- **`File` and `Drop` are indistinguishable to the user.** Both do the identical
  mechanical thing — fall out of `WHERE state = 'flagged'`. Nothing anywhere reads the
  `filed` vs `dropped` distinction back, so it is an inert choice.
- **Dropping a Problem should be a status change.** "This problem is nothing" is a
  statement about the Problem's lifecycle (it is Cancelled / Dismissed), and belongs in
  the Status field — not smuggled into a triage state.

## The principle

There are **two orthogonal axes**, and the inbox should only ever encode the first:

| Axis | Question | Where it lives |
| --- | --- | --- |
| **Attention** | Does this need me to look at it? | Inbox membership (the triage `flagged` marker) |
| **Status** | What state is the object in? | The subject's Status field (`state_change` events) |

> **Inbox membership means "needs attention," full stop. The resolution of an item
> belongs in its Status field.** — the rule this note enforces.

## What sets `flagged` today (unchanged)

Attention is *asserted*, not inferred, and set in three places:

1. **Every `bsk capture`** — a raw note event enters the inbox on capture.
2. **`bsk new` of a Problem or Idea** — flagged on creation. Other subject types (Goal,
   Project, Task, …) are deliberate work, not captures, and are **not** flagged.
3. **`bsk flag <ref>`** — flags *any* item (subject or event) into the inbox at any time.

Because the marker is append-only and newest-wins, (3) already supports **manual
re-flagging**: flagging an already-resolved subject appends a fresh `flagged` marker and it
reappears in the inbox. This capability exists in the kernel now; the Pilot UI has simply
not surfaced a button for it. Keeping attention as its own asserted property (rather than
inferring it) is what preserves this flexibility — and the future email-reading-agent seam.

## The decision: a derived inbox

`v_inbox` already hides archived subjects (`NOT bsk.is_archived(s.id)`). We add the
symmetric terminal-status rule, reusing the existing `bsk.is_terminal_status` predicate
(migration 0011):

```
v_inbox = newest triage marker is 'flagged'
          AND NOT is_archived(subject)
          AND NOT is_terminal_status(subject)     -- new predicate
```

The moment a triage decision moves a subject's Status to a terminal value, it leaves the
inbox **for free** — no parallel `dropped` marker required. Status becomes the single
source of truth for "is this resolved," exactly as the principle demands.

*Chosen over the "fully explicit" alternative* (an item stays in the inbox until the
attention flag is explicitly cleared, independent of status). Derived is less bookkeeping
and prevents the two axes from drifting out of agreement. The one case the explicit model
handled — "clarified, but no status change" — is retained below as the single Dismiss
action.

## Actions, redesigned

The triage decisions split cleanly across the two axes:

| Action | What it really is | Axis |
| --- | --- | --- |
| **Drop** a subject | a Status change → a terminal status (e.g. Cancelled / Dismissed) | Status |
| **Promote** | spawn / activate tracked work; set the source's status | Status (+ new subject) |
| **Dismiss** (was "File") | "I looked, nothing changes — get it out of my inbox" | Attention only |

This dissolves the original File-vs-Drop confusion: **Drop is a Status verb, Dismiss is the
Attention verb.** They stop being confusable peers because they live on different axes.
`dropped` and `filed` are retired as triage states; the only attention-only marker states
are `flagged` and a single "cleared/dismissed."

## Two wrinkles (why the old shape existed)

1. **Events have no Status.** A captured note is an immutable event, not a status-bearing
   subject, so there is no lifecycle to reflect an outcome into. For a raw note the only
   honest triage outcomes are *promote into a subject* or *dismiss*. This is likely why the
   triage marker was originally built as one uniform mechanism across both subjects and
   events. Under this design the marker still exists for events — but as attention only, no
   longer carrying pseudo-status for subjects.

2. **Problem has no "it's nothing" terminal status.** Its vocabulary is
   `Open / Working / Resolved`, and "Resolved" means *solved*, not *dismissed as noise*.
   For "Drop = it's nothing" to land honestly in Status, Problem needs a terminal status
   such as **Cancelled** or **Dismissed** added to its vocabulary
   ([StatusVocabulary.cs](../../src/LifeOs.Domain/StatusVocabulary.cs)).

## Follow-up work

Done (migration [0021](../../db/migrations/0021__inbox_terminal_status.sql)):

- [x] Add `NOT is_terminal_status(...)` to the `v_inbox` projection — a terminal Status now
  clears the inbox, via a live join to `subject_current_source` (no rebuild lag).
- [x] Add terminal **Cancelled** to the Problem vocabulary (`StatusVocabulary.cs` /
  `PilotVocab.cs`); `'cancelled'` was already in `is_terminal_status`, so no predicate change.

Done (triage reroute):

- [x] Reroute **Drop on a subject** to a `state_change` to a terminal status. `bsk drop` on
  a subject now sets that type's dismiss status (`StatusVocabulary.DismissStatusFor`) — the
  inbox clears via the terminal-status rule, no `dropped` marker written. **Drop on an
  event** stays attention-only (the marker), since events have no status.
- [x] Per-type "dismissed as noise" terminal chosen for every status-bearing type:
  Goal/Project → `Abandoned`, Idea → `Rejected`, the rest → `Cancelled`.

Done (File → Dismiss):

- [x] Retired `filed` / `dropped` as triage states — collapsed into a single attention-only
  `dismissed` (`TriageStates`). Historical `filed`/`dropped` markers still fold correctly
  (`v_inbox` keeps only `flagged`), so no data migration is needed.
- [x] Renamed the Pilot's **File** action and the `bsk file` verb to **Dismiss** /
  `bsk dismiss` (attention-only clear, no status change). **Drop** is now a Status verb:
  subject-only in the UI (disabled for events); an event is cleared with Dismiss. `bsk drop`
  on an event degrades gracefully to a dismiss.

Not yet built:

- [ ] Surface **re-flag** in the Pilot UI (a button over the existing `bsk flag` verb) so a
  resolved subject can be pulled back into the inbox on demand.

## Related note: "Promote" on a Problem

Separately observed and worth tracking, though not decided here: promoting a *Problem*
directly into a Project/Goal/Task skips the intuitive step **Problem → generates Ideas
(candidate solutions) → an Idea is promoted into tracked work**. A Problem is a condition,
not a unit of work. Likely direction: make **"Add Idea"** the primary action on a Problem
and keep direct Promote as a secondary shortcut for trivial problems. To be settled by
usage during the pilot.
