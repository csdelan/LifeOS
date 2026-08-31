# Pilot UI & Workflow Requirements

The tracked source of truth for what the pilot's UI should let a user *do*, and how
each of those needs maps onto the Life Kernel ontology. Every requirement has a stable
ID so it can be referenced from chat, commits, and the kernel backlog without being
re-litigated.

## How we work

Two roles, one document:

- **Workflow / UI (Chris)** — describes what a human user wants to do: the goal, the
  steps, the UI expectations. Owns the **Workflow** half of each requirement.
- **Ontology / schema (Claude)** — maps each requirement onto subjects, events,
  relations, and attributes; names the schema/verb cost and the user-facing
  consequences. Owns the **Ontology fit** half.

### Guiding tenet: the UI model is not the storage model

The most intuitive user workflow and the most efficient database representation will
**frequently diverge**, and that is expected — not a smell to be engineered away. The
**application layer translates** between them. So an "Ontology fit" entry is allowed to
say *"the UI presents X, the app layer stores Y and reconstructs X on read."* The user
should never be made to think in storage terms to get their workflow, and the store
should never be bent out of shape to mirror a screen. When the two pull apart, the
translation lives in the application layer, and the requirement records both sides.

### Anatomy of a requirement

```
### <ID> — <title>
Status: <status>   Priority: <pilot | later>

**Workflow (Chris):**
- Goal / job-to-be-done, steps, UI expectations.

**Ontology fit (Claude):**
- Maps to: subjects / events / relations / attributes.
- Kernel delta: verbs, migrations, projections — or "none".
- Implications & constraints: what the model forces or forbids the user.
- Open decisions: unresolved forks.
```

**ID scheme** — area prefix + number, tracking the locked five-screen map so related
requirements cluster and the ID says where it lives:

| Prefix | Area |
|---|---|
| `CAP-` | Capture bar (the persistent top strip) |
| `INBOX-` | Inbox / triage |
| `BROWSE-` | Browse (type → list → detail navigator) |
| `TODAY-` | Today (due / overdue / recurring) |
| `REVIEW-` | Review (diagnostics, the manual coach) |
| `JOURNAL-` | Journal & Timeline |
| `NAV-` / `GEN-` | Cross-cutting (navigation, shell, general) |

**Status vocabulary** — the handoff between the two roles:

`proposed` (Chris stated it) → `mapped` (fits existing kernel, no change) →
`needs-kernel` (requires a schema/verb change; see backlog) → `building` → `done`.
`deferred` parks it deliberately.

## Visual references

Wireframes and spatial layout live in the published artifacts, not in this markdown —
these are the canonical picture; this doc is the traceable requirement list beside them.

- **Pilot Screen Map** — the 23 capabilities across 5 screens + capture bar, wireframed,
  each traced to its reads/writes:
  https://claude.ai/code/artifact/d4302007-1eda-4a3d-b6e6-a7d704d40306
- **Pilot Capability List** — the locked short-term scope (A–F productivity loop +
  "see & trust" band), 23 capabilities tagged Ready/Build/Deferred:
  https://claude.ai/code/artifact/5423121a-9951-4a1a-89b9-2b6b809fbec9
- **Life Kernel Use-Case Map** (mechanics layer) — low-level use cases → ontology
  mechanics → `bsk` verb → coverage:
  https://claude.ai/code/artifact/9b8f4c97-369b-4b8d-9e94-11341edf1705
- **What BlueSkies Does For You** (human/brochure layer) — jargon-free promises tracing
  down to the mechanics IDs:
  https://claude.ai/code/artifact/a650b1e8-3000-45e3-92ed-2a6245f52fdd

---

## Requirements

### CAP-1 — Capture a Note or an Idea as equally-cheap, first-class captures
Status: proposed   Priority: pilot

**Workflow (Chris):**
- In capture mode, jotting a *note* and jotting an *idea* are the same lightweight act
  with a different flavor — one text box, pick the flavor, done. Both land in the inbox
  for triage.
- The flavor should read as first-class to the user (I capture "ideas" as readily as
  "notes"); it's fine if the two are represented differently under the covers.

**Ontology fit (Claude):**
- Maps to: a `note` flavor lands cleanly as a `note` event (`bsk capture`). An *idea*
  has **no lightweight kernel path today** — `bsk ideas` is a structured, problem-anchored
  brainstorm that requires a problem statement, *creates a Problem subject as a side
  effect*, and reads N ideas from stdin. That is a deliberate sit-down ritual, the wrong
  tool for a stray thought.
- Options for what a quick "Idea" capture stores:
  - **A. Idea = note** — no kernel change; "idea" isn't distinct at capture time, you
    promote it to an `Idea` subject later.
  - **B. Idea = flavored note** *(recommended)* — a `note` carrying an `idea` flavor
    (e.g. `bsk capture --as idea`); Inbox shows the flavor and Promote pre-selects the
    `Idea` type. `bsk ideas` stays for real brainstorms.
  - **C. Idea = solo idea_session** — relax `bsk ideas` to allow a problemless,
    single-idea session; larger change, stretches the "session is problem-anchored"
    semantics.
- Kernel delta (option B): an optional flavor/tag argument on `bsk capture`, threaded
  through to the artifact so the reader can surface it.
- Implications & constraints: at capture time "Idea" is a *flavor*, not necessarily a
  distinct event kind; the heavyweight structured brainstorm (`bsk ideas`) remains a
  separate act reachable elsewhere. This is a clean example of the guiding tenet — the UI
  offers two sibling captures; the store may file them the same way plus a marker.
- Open decisions: **which of A / B / C.** (Leaning B.)

### INBOX-1 — The Inbox is a source-agnostic triage queue, not a capture log
Status: needs-kernel   Priority: pilot

**Workflow (Chris):**
- The Inbox is **manual triage for anything that needs an initial processing decision** —
  GTD-style: review an item, then Do / Delegate / Defer / File / Drop.
- Membership is defined by *"this needs a human decision,"* independent of **what** the
  item is (note, idea, email, a nudge) or **where it came from** (I typed it, or — in the
  future — an AI agent read my email and dropped it here for me to triage).

**Ontology fit (Claude):**
- Maps to: an **explicit triage marker** on an item (asserted membership), *not* the
  current negative inference. Today's pilot query defines the inbox as
  `kind IN ('note','journal') AND not-yet-promoted AND not-yet-related` — every clause is
  wrong under this definition: it filters by kind (should be kind-agnostic), it infers
  membership (a source can't *assert* it), and its only exits are promote/relate.
- Resolution: an item leaves when it's **promoted** (organize → subject), **related**
  (file → `concerns` an existing subject), or **dropped**. Project `v_inbox` =
  *flagged AND not resolved*.
- Most GTD outcomes already have kernel homes; only Drop is a missing primitive:

  | GTD decision | Kernel home |
  |---|---|
  | Do | promote → Task (due today), or log an activity |
  | Defer | promote → Task/Project + due/do-on date (`bsk set`) |
  | Delegate | promote → Commitment/Task + a Person relation ("waiting-for") |
  | File / reference | `bsk relate` (concerns an existing subject) |
  | **Drop** | ⬅ missing — needs a marker |

- Kernel delta: a first-class **triage-flag primitive** (entry), a **Drop** outcome
  (exit), and a `v_inbox` projection. This deliberately reopens the earlier "option (a):
  no dismiss state" simplification — a real triage queue needs "I looked, it's nothing"
  as a recordable outcome, which pure inference cannot express.
- Implications & constraints: append-only means **Drop is a marker, not a delete**;
  membership becomes an *assertable property*, which is exactly the seam a future
  email-reading agent plugs into (it flags; it doesn't need to be a `bsk capture`).
- Open decisions: is resolution **recorded explicitly** (a triage-resolved event with an
  outcome) or **inferred** from promote/relate/drop? (Hybrid: explicit flag on entry +
  explicit Drop; promote/relate stay untouched and count as resolution.)

---

## Kernel build backlog

The roll-up of every `needs-kernel` delta above — the authoritative feeder for ontology
work. One line per item; details live in the requirement.

- **Triage marker primitive** (INBOX-1) — a flag that asserts inbox membership + a `Drop`
  outcome + a `v_inbox` projection (flagged AND not resolved). Makes membership explicit
  and source-agnostic.
- **Capture flavor** (CAP-1, if option B) — an optional flavor/tag on `bsk capture`
  threaded through to the artifact, so a quick "Idea" is a first-class capture without the
  `bsk ideas` brainstorm ritual.

*(Prior mapping gaps already closed: the `concerns` write path — shipped as `bsk relate`.)*
