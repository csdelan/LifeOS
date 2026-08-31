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

That being said, the ontology may need to evolve as well.  Do not try to FORCE a user
workflow requirement into an ontology that does not easily support it.  If needed,
evolve the ontology in a careful, future proof way.  

> THIS IS THE MAIN PURPOSE OF THE PILOT: 
> To Prove out and evolve the Ontology to real user workflows.


### Anatomy of a requirement

```
### <ID> — <title>
Status: <status>
Priority: <Pilot | Pilot phase 2 | Production | Someday Maybe>

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
| `CAP-` | Capture capability |
| `INBOX-` | Inbox / triage |
| `BROWSE-` | Browse (type → list → detail navigator) |
| `TODAY-` | Today (due / overdue / recurring) |
| `REVIEW-` | Review (diagnostics, the manual coach) |
| `JOURNAL-` | Journal & Timeline |
| `DASHBOARD-` | Dashboard view |
| `NAV-` / `GEN-` | Cross-cutting (navigation, shell, general) |

**Status vocabulary** — the handoff between the two roles:

`drafting` (Chris still shaping it — Claude holds off; Ontology fit stays _pending_) →
`proposed` (Chris hands it off; ready for an Ontology-fit pass) →
`mapped` (fits existing kernel, no change) →
`needs-kernel` (requires a schema/verb change; see backlog) → `building` → `done`.
`deferred` parks it deliberately.

**Handoff rule:** Claude does not write an Ontology fit until Chris moves a requirement
off `drafting`. Give the fullest workflow picture first — no jumping into solution space
before the requirement has settled.

**High Level Context Rule:** Whenever you process new changes in a requirement, you need to do a quick assessment of whether your entire ontology solution still works with no alterations. Since this is an iterative process, and the UI set and solution sets are interweaving, new or modified requirements added on CAN, and sometimes WILL require adjustments in previous decisions.

**Priority (horizon)** — which milestone a requirement belongs to, not its urgency:

| Value | Meaning |
|---|---|
| `Pilot` | In scope for the current throwaway pilot. |
| `Pilot phase 2` | A later pilot iteration — still throwaway, not the first cut. |
| `Production` | For the eventual real system, beyond the pilot. |
| `Someday Maybe` | Unscheduled; captured so it isn't lost (GTD someday/maybe). |

**Other Vocabulary**
An `item` is any trackable thing, including any subject or event.
An `identity statement` is aka `Value`.  I am using this concept as a statement in the typical form of "I am the type of person who Does XYZ in ABC situation." rather than a single word value like `honesty`.


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


### BROWSE-1 — I want an object graph view similar to Obsidian
Status: drafting
Priority: Production

**Workflow (Chris):**
- Object graph very similar to obsidian
- Filters to show orphans only, or omit orphans.
- Filter for "Area", or only certain types.
- Clicking on any node opens up the detail editor for that subject.

**Ontology fit (Claude):**
- pending



### GEN-1 — I want tags as a ubiquitous classification mechanism
Status: proposed
Priority: Pilot

**Workflow (Chris):**
- Everything created by hand should be taggable. From notes to first class objects like decisions or projects.
- Do not include tagging in the "capture" popup UI, but I should be able to add/remove tags during inbox triage.
- Tag scope is universal. As long as a tag is currently added to at least 1 item, it is part of the tag universe.
- Use tag autocomplete control, for easy add/delete of tags.
- Tags are separate from relations.  They are a flexible classification scheme, not a relation scheme.  So any user interface needs to separate out these 2 things clearly "Relate to" vs "Tag" so that the user understands they are separate.

**Ontology fit (Claude):**
- pending



### GEN-2 — Area of Focus should be a property of goals, tasks, projects (at a minimum)
Status: proposed
Priority: Pilot

**Workflow (Chris):**
- This will be GTD style Area of focus (or just "Areas").  So "Trading", "Family", "Dev Career" are normal values
- The UI should allow the user to maintain a master "Areas" list
- Support for multiple areas of focus for an item is a "nice to have", but not at all a requirement.
- I may decide later to add additional types to include "areas", but goals, tasks, and projects should be the minimum bar.

**Ontology fit (Claude):**
- pending

### GEN-3 — The UI should support habits as a first class concept, including occurrences of the habit.
Status: proposed
Priority: Pilot

**Workflow (Chris):**
- Each habit should have start date, end date, last date I confirmed the habit, streak, recurrence, cue, routine, reward
- The recurrence pattern of a habit can be daily, weekly, other interval, or set by trigger/cue
- A UI component/viewer should exist just for habits, because of the unique properties.
- I should be able to open a habit and view its streak/adherence history.  You can also do this in the review views (see REVIEW sections)
- Habits should always be related to some parent, such as a goal or identity statement (aka value).  This should be a validation rule when editing a habit.
- SIDE NOTE:  Depending on how habits are represented in the ontology, it could relate to commitment also.

**Ontology fit (Claude):**
- pending



### CAP-1 — I want to be able to quickly capture a note, idea, problem from anywhere in the app
Status: drafting
Priority: pilot

**Workflow (Chris):**
- Global hot key brings a pop up dialog to add a note, idea, problem
- Always visible button on the UI does the same thing (eg in a top banner/frame)
- auto focus cursor in the text box so I can just start typing immediately
- The most ubiquitous action that I want to make quick and easy.
- the dialog can have a simply 1 click selector (tabbable also) to choose what type it is. Default: note
- All captures default to the inbox. There must be an initial triage (but by design, it can be in the future)
- Many capture notes are just 1 liners, but the text box should be multi-line just in case, so I can see the entire message as I type.

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


### CAP-2 — I want Voice mode for captures. 
Status: proposed
Priority: Pilot Phase 2

**Workflow (Chris):**
- App captures mic audio for dictated note.
- Can be a different hot key, or autodetect voice on same hotkey as normal keyboard centric capture.
- Have an option to capture actual voice file, or to just transcribe the text.

**Ontology fit (Claude):**
- pending

### CAP-3 - I want AI assist mode for captures
Status: proposed
Priority: Production

**Workflow (Chris):**
- Voice activated wake up AI (if needed), then I just speak freely to give an AI instructions on what to capture.
- The AI will interpret my intent and capture the necessary content from its own interpretation.

**Ontology fit (Claude):**
- pending

### CAP-4 — I want to capture "documents" (represented as attachments)
Status: drafting
Priority: Pilot Phase 2

**Workflow (Chris):**
- Documents should be stored intact for later retrieval workflows
- Attachments should just be an add-on feature to the normal capture flow introduced in pilot phase. So it is in ADDITION to any text description.  For example, I can upload a bank statement PDF, along with a title describing the relevance of it (eg disputed Comcast charge reference statement)
- these captures that have attachments become a "document", which essentially serves as a reference item.


**Ontology fit (Claude):**
- pending


### CAP-5 — I want to capture URL reference
Status: drafting
Priority: Pilot Phase 2

**Workflow (Chris):**
- Documents should be stored intact for later retrieval workflows
- Attachments should just be an add-on feature to the normal capture flow introduced in pilot phase. So it is in ADDITION to any text description.  For example, I can upload a bank statement PDF, along with a title describing the relevance of it (eg disputed Comcast charge reference statement)
- these captures that have attachments become a "document", which essentially serves as a reference item.


**Ontology fit (Claude):**
- pending


### JOURNAL-1 — Support append only journals with rich content and inline attachments, media
Status: proposed
Priority: Pilot

**Workflow (Chris):**
- Support a rich Journal control that can be associated to any subject of any type (goals, problems, projects, habits, etc)
- Each journal is append only.  Only 1 journal is associated to any single object; I just keep appending to the same journal over time.
- In read only mode, the editor controls of the journal component should be hidden if possible.  Only if you click to append to the journal does it show the editor controls.
- It will be very common to support embedded images or video.  Should also support HTTP reference.
- Many subjects are likely to have a journal on them.  So it will be a very common task to journal about a subject (adding evolving thoughts about the subject). This could theoretically also be done by just creating a note and relating it to that subject, so thoughts can be added to a subject in multiple ways.

**Ontology fit (Claude):**
- pending


### JOURNAL-2 — TBD
Status: drafting
Priority: Pilot Phase 2

**Workflow (Chris):**
-  

**Ontology fit (Claude):**
- pending




### INBOX-1 — The Inbox is a source-agnostic triage queue, not a capture log
Status: needs-kernel
Priority: pilot

**Workflow (Chris):**
- The Inbox is **manual triage for anything that needs a processing decision** —
  GTD-style: review an item, then Do / Delegate / Defer / File / Drop. You can also think of the inbox as anything that requires my attention.  This will probably be the 2nd or 3rd most used view of the user interface, because the goal is to keep it to "Inbox Zero" (empty inbox) as much as possible.
> A corrollary to this "Inbox Zero" framing, is that the average number of items in the inbox can itself become a decent measure (though not a complete one) of how well I am keeping on top of concerns in my life.
- Membership is defined by *"this needs a human decision,"* independent of **what** the
  item is (note, idea, email, a nudge) or **where it came from** (I typed it, or — in the
  future — an AI agent read my email and dropped it here for me to triage).
- Membership can ALSO include a notification that results from missed commitments, or other diagnostics, rule breaks, etc. (again, anything that should require my attention is the goalpost)
- It may make sense to have different permitted actions on each item in the inbox based on its type.  The UI should only provide me appropriate actions based on the type of the item.  


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


### REVIEW-1 — I want to perform daily reviews
Status: drafting
Priority: Pilot phase 2

**Workflow (Chris):**
- App captures mic audio for dictated note.

**Ontology fit (Claude):**
- pending


### REVIEW-2 — I want to perform weekly reviews (every Sunday)
Status: drafting
Priority: Pilot phase 2

**Workflow (Chris):**
- App captures mic audio for dictated note.

**Ontology fit (Claude):**
- pending


### REVIEW-3 — I want to perform monthly reviews (EOM, give or take a day or 2)
Status: drafting
Priority: Production

**Workflow (Chris):**
- App captures mic audio for dictated note.

**Ontology fit (Claude):**
- pending


### REVIEW-4 — I want to perform yearly reviews (EOM, give or take a day or 2)
Status: drafting
Priority: Someday Maybe

**Workflow (Chris):**
- App captures mic audio for dictated note.

**Ontology fit (Claude):**
- pending


### REVIEW-5 — Habits should be recordable and reviewable during daily, or weekly reviews
Status: drafting
Priority: Pilot phase 2

**Workflow (Chris):**
- Habit streaks should be trackable.  I should be able to record whether I stuck with that habit or not.  The UI will show a small grid that renders a box for each time period I followed the habit.
- Support 3 states per habit record:  Habit followed, Habit not followed, and partial credit

**Ontology fit (Claude):**
- pending



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

