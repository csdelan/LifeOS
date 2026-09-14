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
Horizon:    <Pilot | Pilot phase 2 | Production | Someday Maybe>
Definition: <drafting | active>
Build:      <unmapped | mapped | needs-kernel | building | built>

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
| `TASKS-` | Tasks working view |
| `CAL-` | Calendar / Appointments |
| `NAV-` | Navigation shell |
| `GEN-` | Cross-cutting conventions **and** per-subject-type object specs (Goals, Projects, Tasks, Identity Statements, Commitments, Decisions, Problems, People, Vision, …) |

> Organization note: `GEN-` has become a grab-bag of two different things — cross-cutting UI
> conventions (GEN-1 Tags, GEN-4/5/6/7) and per-type object specs (GEN-8..16). A future tidy
> could split the object specs into their own `OBJ-` prefix; deferred to avoid renumbering
> live references.

**Three status fields, each answering one question.** Nothing here is ever "final" — this
is a living spec, iterated as Pilot use reveals better behavior. The old single `Status`
tried to be two things at once; these split it so no field has to mean "finished".

**Definition** — *is the thinking settled enough to act?* (Chris's axis)
- `drafting` — Chris is still shaping it; Claude holds off, the Ontology fit stays _pending_.
- `active` — settled enough to map / build against **now**. *Not* frozen: an `active`
  requirement stays open to iteration forever. This is the resting state — there is no
  terminal "done".

**Build** — *where is the implementation?* (Claude's axis; may move **backward** when a
requirement changes)
- `unmapped` → `mapped` (fits existing kernel, no change) → `needs-kernel` (requires a
  schema / verb change; see backlog) → `building` → `built` (live in the Pilot — which is
  not "finished forever" either).

So "shipped but still evolving" is `Definition: active` + `Build: built`, and reworking it
drops Build back toward `mapped` while Definition stays `active`.

**Handoff rule:** Claude does not write an Ontology fit until Chris moves a requirement's
**Definition** off `drafting`. Give the fullest workflow picture first — no jumping into
solution space before it has settled.

**High Level Context Rule:** Whenever you process new changes in a requirement, you need to do a quick assessment of whether your entire ontology solution still works with no alterations. Since this is an iterative process, and the UI set and solution sets are interweaving, new or modified requirements added on CAN, and sometimes WILL require adjustments in previous decisions.

**Horizon** — which milestone a requirement belongs to, not its urgency:

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


### GEN-0 — LifeOS is a prioritization command center, not a second brain
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- LifeOS is primarily a command center for prioritizing my life. Its most important job is
  helping me decide what deserves my attention and effort now.
- It also helps me organize the parts of my life that make those priorities meaningful,
  including my vision, identity statements, Goals, Projects, Tasks, Commitments, and Habits.
- LifeOS is explicitly not intended to be a second brain or a general-purpose repository
  for everything I know. That will be a separate system.
- The Pilot should be optimized for rapid development in WinForms and for proving whether
  the workflows and ontology survive real daily use. Pilot phase 2 remains WinForms but
  expands into richer workflows. Production will probably be web-centric and place much
  greater emphasis on polish, aesthetics, accessibility, and fit for long-term use.
- The long-term direction is for AI to understand my life in real time and guide me toward
  the highest-return areas of focus. The Pilot must still be useful before that intelligence
  exists, so I need to be able to manage priorities myself.
- The most important product-level success test for the Pilot is whether I feel compelled
  to use it. Feature completeness does not validate the Pilot if I do not voluntarily make
  it part of my daily routine.

**Ontology fit (Claude):**
- No direct schema mapping — this is **product intent that shapes priorities**, worth
  recording as an ontology *boundary*: "prioritization command center, not a second brain"
  means the **alignment graph (`serves` / `results_in`) + diagnostics are the core**, and
  general knowledge storage is explicitly **out of scope**. That bounds what Tags, captures,
  and references are for — attention management, not a knowledge base.
- Kernel delta: none.
- Implications: when a later requirement pulls toward general-purpose note/knowledge
  storage, this is the line to check it against.


### NAV-1 — The Pilot uses persistent top-tab navigation and opens on Dashboard
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Use a persistent top tab strip as the primary navigation model for the WinForms Pilot.
- The initial top-level destinations are **Dashboard**, **Inbox**, **Browse**, **Vision**,
  **Reviews**, **People / Agents**, and **Areas**.
- Also provide dedicated navigation destinations for **Goals**, **Projects**, **Tasks**, and
  **Habits**. These may participate in the same top-tab navigation while the Pilot reveals
  whether grouping is needed to keep the strip manageable.
- Use this provisional common-work-first order: **Dashboard**, **Inbox**, **Tasks**,
  **Projects**, **Goals**, **Habits**, **Reviews**, **Vision**, **Browse**, **Areas**, and
  **People / Agents**. The order may be refined through Pilot use.
- When the available width is insufficient, wrap the tabs onto additional rows while
  preserving their left-to-right sequence. Do not replace them with horizontal scrolling or
  an overflow menu in the Pilot.
- Goals, Projects, Tasks, and Habits should also be available as preset Browse filters. The
  dedicated destinations and Browse presets are two ways to reach the same underlying
  objects, not separate copies or separate management workflows.
- Keep the top tabs visible while navigating and show one primary destination in the main
  content region at a time.
- Each dedicated tab should remember its own active filters, sorting, and selected item when
  I switch away and return. Persist that view state across application restarts.
- Open on Dashboard every time LifeOS starts. Do not restore the last-opened destination in
  place of Dashboard.
- Opening an item from Dashboard, a dedicated destination, Vision, or another summary should
  take me to its normal detail view and preserve a clear path back to the source context.
- Global and contextual object-creation forms should open as modal dialogs over the current
  main screen. Save or Cancel closes the dialog and returns me to the invoking screen.
- Ordinary browsing, read-only detail, and explicit editing should remain within the main
  application window following BROWSE-2 rather than opening a new window for every item.
- If I switch tabs while the current screen has unsaved edits, prompt me to **Save**,
  **Discard**, or **Stay**. Save should complete validation before navigation; a failed save
  or Stay should leave me on the current editor with my values preserved.
- Do not add Back or Forward navigation-history buttons to either WinForms phase. That
  behavior belongs to Production under NAV-2.
- The independent quick-capture and voice-capture windows are exceptions governed by CAP-1
  and CAP-2; they must retain their previously defined system-wide behavior.

**Ontology fit (Claude):**
- Maps to: **pure app shell — no ontology mapping.** Tab strip, tab order, open-on-Dashboard,
  per-tab remembered filters / sort / selection, modal create dialogs, and the unsaved-edit
  prompt are all app concerns. The dedicated Goals / Projects / Tasks / Habits tabs and their
  Browse presets are the **same subjects seen through different filtered reads** — one source,
  many views (the "many readers" principle), not separate stores.
- Kernel delta: none. Per-tab view state is app-local (like BROWSE-2's remembered filters).
- Implications: a dedicated tab and its Browse preset must never diverge — both are reads over
  the same subjects.
- Open decisions: none blocking (pure UI).


### NAV-2 — Production supports Back and Forward navigation history
Horizon:    Production
Definition: active
Build:      mapped

**Workflow (Chris):**
- Production should provide Back and Forward controls for moving through recently viewed
  screens and objects.
- Navigation history should restore the prior destination and enough view context to remain
  useful, including the selected item and applicable filters or scroll position.
- Moving Back or Forward must not create duplicate objects, repeat commands, or silently
  discard unsaved edits.
- This is a Production goal and is not required in the Pilot or Pilot phase 2 WinForms UI.

**Ontology fit (Claude):**
- Maps to: **pure app-side** navigation history (Back / Forward). No ontology involvement;
  restoring a prior destination + view context is app state.
- Kernel delta: none. Production horizon.
- Implications: "must not duplicate objects or repeat commands" is an app guarantee — history
  navigation only re-reads, never re-writes.
- Open decisions: none blocking.


### DASHBOARD-1 — The opening screen is a daily prioritization command center
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- On opening LifeOS, I want to understand what deserves my attention within roughly 30
  seconds. Starting in Pilot phase 2, this includes my current Primary focus and my top one
  to three objectives for the day.
- The opening screen should contain Active Goals and Projects, suggested next actions,
  due and overdue Tasks, an Inbox summary, and the Habits I should focus on today.
- In Pilot phase 2, my Primary focus and today's objectives should be visually dominant.
  Counts, navigation, history, and general reference information should not compete with
  them for attention.
- The Inbox should appear only as a compact summary and entry point. The full Inbox and its
  triage actions belong on the separate Inbox screen.
- Active Goals and Projects, suggested next actions, due and overdue Tasks, and today's
  Habits should also appear as compact summaries rather than full management screens.
- Today's Habit summary should show each applicable occurrence's current state and provide
  the one-click followed / not followed / partial-credit action defined by GEN-3 without
  requiring me to open the full Habit detail screen.
- Each summary should provide an obvious path to the corresponding focused screen or
  filtered list. The Dashboard should not duplicate every object editor.
- Each empty state should distinguish a genuinely clear category, such as having no
  overdue Tasks, from information that failed to load or is not configured yet.
- In Pilot phase 2, the Dashboard should provide an obvious path into the appropriate
  review or planning session where I can set or revise the manually controlled parts of my
  plan. Inline editing on the Dashboard is not required.
- The Active Goals and Projects summary should use a combination of items connected to my
  Primary focus, items I have manually pinned, recent activity, and system prioritization.
  The exact weighting is expected to improve through use.
- Future AI agents should be able to apply broader context and real intelligence to which
  Goals and Projects deserve attention, without changing the basic Dashboard workflow.
- Due and overdue Tasks or Commitments should appear in their own clearly labeled section,
  separate from next actions associated with my current focus. Both sections deserve strong
  visual emphasis; one must not hide the other.
- Dashboard is always the default opening view, following NAV-1. The app should not replace
  it with the last-opened screen on startup.

**Ontology fit (Claude):**
- Maps to: **almost entirely reads / projections** composed from things mapped elsewhere —
  Active Goals & Projects (status + focus), suggested next actions (the diagnostics / derived
  layer + TODAY-2), due / overdue Tasks (the TASKS-1 projection), Inbox summary (a `v_inbox`
  count), today's Habits (the Habit occurrence projection, GEN-3), and Primary focus +
  objectives (D6). One-click Habit recording writes an adherence event (GEN-3).
- Kernel delta: none structural of its own — rides D6 (focus / objectives), GEN-3 (habits),
  TASKS-1, and INBOX-1. The only new bit is a **`pinned` flag** for manually-pinned Goals /
  Projects — a small boolean attribute.
- Implications: the Dashboard stores essentially nothing; it composes reads. "Genuinely clear
  vs failed-to-load" empty states are app-side.
- Open decisions: `pinned` as a simple per-item boolean attribute (recommended).


### TODAY-1 — I want to set a Primary focus and one to three objectives for today
Horizon:    Pilot phase 2
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Each day can have one prominent **Primary focus** and between one and three **objectives**.
- These should be the first things I see when orienting myself in LifeOS.
- Primary focus represents where I intend to invest most of my mental energy. Examples
  include finishing a home-improvement project, finishing a BlueSkies development release,
  or improving my overall health.
- Primary focus and daily objectives should be flexible: each may select an existing Area
  of Focus or Goal, or may be entered as free text when no existing item is the right fit.
- Choosing an existing item should be quick and searchable, while the free-text path should
  not force me to create a permanent Goal or Area merely to plan today.
- Primary focus persists for an arbitrary length of time until I deliberately change it. It
  does not expire or reset merely because a new day, week, or month begins.
- I expect to set or change Primary focus and daily objectives mainly during daily, weekly,
  or monthly review and planning sessions, when I assess priorities and plan upcoming Goals,
  Projects, and Tasks.
- During Pilot phase 2 I need to manage them manually. Later recommendations may propose
  or pre-populate them, but I must remain able to override the plan.
- Daily objectives express the outcomes or areas of emphasis for the day. They should not
  automatically be treated as the same thing as every Task that happens to be due today.
- The UI should allow the Primary focus to be set, replaced, or cleared and objectives to
  be added, reordered, replaced, or removed.
- The normal UI should enforce a maximum of three active objectives. It should not silently
  discard an existing objective if a fourth is proposed.
- The UI should clearly distinguish priorities I selected from suggestions made by the
  system or a future AI.
- At the end of the day, each unfinished objective should be flagged into the Inbox for
  manual triage so it cannot be silently ignored. It should not automatically remain on the
  next day's plan.
- Objective scoring or outcomes such as achieved, partial, missed, or abandoned are extra
  credit rather than part of the initial workflow. Their definition is deferred until the
  daily-review workflow has been exercised.

**Ontology fit (Claude):**
- Maps to (D6): **Monthly focus + Weekly focus** = two persistent **Goal pointers**, each set
  in its review; record each change as an event so history falls out of the append-only log.
  **Daily objectives** = an **optional, ephemeral** per-day list (free text or a pointer to a
  Task / Goal) — *not* subjects, and not necessarily in the kernel at all (no history needed);
  the only durable write is that **unfinished objectives become Inbox items** (the triage
  marker) at day's end.
- Kernel delta (needs-kernel): a home for the **current Monthly / Weekly focus** (a Goal
  pointer with change history) — cleanest as `focus-set` events (current = latest) or a
  lightweight **Season** (the D6 alignment note). Daily objectives can stay app-local.
- Implications: the "weekly advances monthly" alignment is **read from the `serves` /
  `results_in` Goal graph** (D6), not stored. Max-3-objectives is an app rule.
- Open decisions: focus stored as events vs a Season vs a setting; **reconcile TODAY-1's
  free-text / set-on-the-Today-screen bullets** with the resolved "Goal-linked, review-set"
  model.


### TODAY-2 — Today's priorities combine manual planning with explainable suggestions
Horizon:    Pilot
Definition: drafting
Build:      unmapped

**Workflow (Chris):**
- The Pilot should combine priorities I manage myself with useful system suggestions.
- The long-term goal is for AI to understand my life in real time and recommend the
  highest-return things for me to focus on.
- Suggestions should draw attention to valuable action without taking control away from me.
- The UI should visually separate **My plan** from **Suggested next actions** while keeping
  both available from the same command-center screen.
- For the Pilot, suggested next actions should include anything due and the identified next
  action for an active Project or Goal. In Pilot phase 2, Primary focus should help determine
  which of those Project or Goal actions are most relevant.
- Due and overdue Tasks or Commitments should be presented separately from focus-aligned
  next actions. Both categories should be highlighted rather than allowing focus-aligned
  recommendations to obscure an urgent obligation, or vice versa.
- A suggestion should offer a direct next step appropriate to its type, such as opening the
  item, accepting it into today's plan, scheduling it, or dismissing it.
- Each suggestion should show a concise reason, such as overdue, serves the Primary Goal,
  commitment at risk, or neglected Project. More detailed explanation can be available on
  demand without overwhelming the main screen.
- My manual order and overrides win for the current day. The system may warn me about a
  conflict or risk, but it should not silently reorder my declared objectives.
- Open decision: whether accepting a suggested next action normally makes it one of today's
  objectives, leaves it as a separate recommended action, or depends on the suggestion.

**Ontology fit (Claude):**
- pending


### BROWSE-1 — I want an object graph view similar to Obsidian
Horizon:    Production
Definition: drafting
Build:      unmapped

**Workflow (Chris):**
- Object graph very similar to obsidian
- Filters to show orphans only, or omit orphans.
- Filter for "Area", or only certain types.
- Clicking on any node opens up the detail editor for that subject.

**Ontology fit (Claude):**
- pending


### BROWSE-2 — Browse uses filters, a context-sensitive item list, and read-only detail
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Use a three-pane Browse layout: a generalized filter pane, an item list, and a selected-
  item detail pane.
- Object type should be one filter within the filter pane rather than the pane being only a
  fixed list of types. The design should allow additional filters to be added as the Pilot
  reveals what is useful.
- The item-list columns should change according to what is being viewed rather than forcing
  every kind of item into one column set.
- For workflow objects such as Goals, Projects, Tasks, and other subjects, the list should
  initially support Title, Status, Due date, Area, and Tags where those fields apply.
- When viewing Events, show event-appropriate fields instead of empty or irrelevant workflow-
  object columns.
- The initial Pilot filters and sorting choices should include Status, Due date, Entered
  date, Area, and Title where applicable.
- Combine different filter categories with AND. For example, `Status = Active` and
  `Area = Trading` should return items satisfying both categories.
- When multiple values are selected inside one category, combine those values with OR. For
  example, selecting two Areas should return items in either selected Area while still
  respecting the other active filter categories.
- Apply filter changes immediately as I make them; do not require a separate Apply action.
- Remember the active Browse filters when I leave and return, and restore them after LifeOS
  restarts.
- Global object creation follows GEN-6. After creation, show and select the new object in
  Browse only when it matches the active filters; otherwise preserve the filtered results
  and confirm creation without changing the filters.
- Treat the initial columns, filters, and sorting choices as a starting point that can be
  refined iteratively during Pilot use.
- Selecting an item should show it in read-only mode first. Editing must be an explicit
  opt-in action rather than making every selection immediately editable.
- The detail pane should show an Edit action only when the selected item is editable.
- Edit mode should provide explicit Save and Cancel actions. Do not save automatically as
  individual fields change.
- If I try to select another item, leave Browse, or close the window while edits are unsaved,
  prompt me to **Save**, **Discard**, or **Cancel** the navigation.
- Saving should return the detail pane to read-only mode and refresh the item list with any
  changed display fields. Cancelling should discard the unsaved changes and return to the
  prior read-only values.
- When no item is selected, the detail pane should show a neutral empty state rather than a
  stale previously selected item's content.
- Organize the detail pane into clear sections or tabs for **Overview**, **Relationships**,
  **Tags**, **Journal**, and **History** where those sections apply to the selected item.
- Keep Tags and Relationships visually separate according to GEN-1. Journal and History
  should follow JOURNAL-1 and GEN-4 respectively rather than introducing Browse-specific
  versions of those interactions.

**Ontology fit (Claude):**
- Maps to: almost entirely **reads** over the existing `v_subject` / `v_event` /
  `v_subject_relation` views, plus the new Tag (GEN-1) and Area (GEN-2) dimensions. Filters
  (Status, Due, Entered, Area, Tags, Title) are query predicates; the tenet — **AND across
  categories, OR within a category** — is straightforward SQL.
- Context-sensitive columns = different projections for subjects vs events (a subject list
  shows Title / Status / Due / Area / Tags; an event list shows event-appropriate fields).
- Edit mode (read-only-first, explicit Save / Cancel) writes through `bsk`: attribute changes
  via `bsk set`, status via `state_change`. The dirty-nav prompt is pure app state.
- Kernel delta: none beyond Tags (GEN-1) and Area (GEN-2); the detail sections (Overview /
  Relationships / Tags / Journal / History) each read an existing edge / event source.
- Open decisions: none blocking — this rides on GEN-1 and GEN-2 landing.



### GEN-1 — I want tags as a ubiquitous classification mechanism
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Everything created by hand should be taggable. From notes to first class objects like decisions or projects.
- Do not include tagging in the "capture" popup UI, but I should be able to add/remove tags during inbox triage.
- Tag scope is universal. As long as a tag is currently added to at least 1 item, it is part of the tag universe.
- Use tag autocomplete control, for easy add/delete of tags.
- Tags are separate from relations.  They are a flexible classification scheme, not a relation scheme.  So any user interface needs to separate out these 2 things clearly "Relate to" vs "Tag" so that the user understands they are separate.

**Ontology fit (Claude):**
- Maps to: a **new tag primitive**, deliberately separate from `subject_relation`. The
  kernel has no general tags today — the closest is the `attributes.focus` string the
  Season/neglect logic already treats as a life-area tag (see GEN-2). Tags attach to *items*
  (subjects *and* events), so a raw capture can be tagged before it's ever a subject.
- Kernel delta (needs-kernel): a tag store (a normalized `item_tag` table is cheapest for
  the "tag universe" + autocomplete, vs a `tags` array per row), a `bsk tag <item> +x -y`
  verb kept distinct from `bsk relate` / `link`, and a reader for the live tag set.
- Implications & constraints: keeping Tags in their own store + own verb is what makes the
  UI's "Tag" vs "Relate to" separation real rather than cosmetic. "Universal" = one shared
  tag space across subjects and events.
- Open decisions: normalized table vs per-row array; whether the reserved `focus` attribute
  becomes just another tag or stays special for Season parking.



### GEN-2 — Area of Focus should be a property of goals, tasks, projects (at a minimum)
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Use the short user-facing term **Area**. An Area may represent either an ongoing domain of
  my life or an enduring responsibility; examples include Trading, Family, Health, and Dev
  Career.
- The UI should provide a master Areas list.
- The Area creation and edit form should include **Name**, **Description**, and **Notes**.
  Name is required; Description and Notes are optional.
- Color and icon fields are not required for the Pilot.
- Areas are permanent. Do not provide Archive or Delete actions for them.
- Do not add a separate prominence or emphasis field in the Pilot. An Area may naturally
  become less prominent when it is not my Primary focus and has less prominent active work;
  this does not change the Area itself.
- Pilot only needs zero-or-one Area per item. Multiple Areas on one item may be considered
  later but are not required.
- Goals, Projects, and Tasks must support an optional Area property at minimum.
- Identity Statements and Habits should also be visible from an Area detail screen when they
  are associated with that Area.
- The Area detail screen should show its related Identity Statements, Goals, Projects, Tasks,
  and Habits in clearly separated groups, with each item opening its normal detail view.
- Creating a Goal, Project, Task, Identity Statement, or Habit from an Area detail screen
  should prepopulate that Area when the selected object type supports it.
- Area editing should follow BROWSE-2: read-only detail first, explicit Edit, and explicit
  Save or Cancel.

**Ontology fit (Claude):**
- Maps to (D1): **Area = a new durable subject type.** The master "Areas" list is
  `bsk new Area "Trading"` etc.; an item names its Area via an attribute (`attributes.area`),
  a soft reference to the Area subject — kept as a *property*, not an alignment edge, so the
  `serves` / `results_in` graph stays about work, not classification.
- **Pre-existing overlap worth surfacing:** the kernel already has a `focus` axis — subjects
  carry an `attributes.focus` tag and Seasons park out-of-focus subjects by matching it.
  "Area of Focus" is conceptually that same axis — a strong candidate to **unify Area with
  `focus`** rather than add a parallel dimension.
- Kernel delta: add the `Area` subject type (a careful, future-proofed type extension); a
  master-list reader; `attributes.area` on items (settable via `bsk set`).
- Open decisions: unify with `focus` or keep separate; single vs multi-Area (`attributes.area`
  scalar vs array); attribute-reference (recommended) vs a graph edge if BROWSE-1 wants Area
  as a first-class graph node.

### GEN-3 — The UI should support habits as a first class concept, including occurrences of the habit.
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Each habit should have start date, end date, last date I confirmed the habit, streak, recurrence, cue, routine, reward
- The recurrence pattern of a habit can be daily, weekly, other interval, or set by trigger/cue
- A UI component/viewer should exist just for habits, because of the unique properties.
- I should be able to open a habit and view its streak/adherence history.  You can also do this in the review views (see REVIEW sections)
- Every expected Habit occurrence begins as unrecorded. It should not immediately be treated
  as **not followed** merely because no adherence has been entered yet.
- Keep occurrence deadlines simple: a daily Habit remains unrecorded until end-of-day, and
  a weekly Habit remains unrecorded until end-of-week. Configurable time-of-day windows are
  not required.
- Once the applicable daily or weekly period ends, an occurrence with no recorded adherence
  may be shown as **not followed**.
- I should be able to record an occurrence from the Dashboard/Today screen, Habit detail
  screen, daily review, or weekly review.
- Recording should be a one-click choice between **followed**, **not followed**, and
  **partial credit**, with an optional note available when I want to add context.
- The same occurrence and current state should be shown consistently from every entry point;
  recording it in one place must prevent another screen from presenting it as unrecorded.
- Partial credit should break the normal binary streak, but its distinct result must remain
  in history. I may later define a more granular streak or adherence score that gives some
  weight to both full and partial credit.
- I should be able to backfill or correct a past occurrence when I followed a Habit but
  forgot to record it, or when I previously selected the wrong result.
- Correcting an occurrence should automatically recalculate the streak and every displayed
  adherence summary affected by the correction. The chronological history should retain the
  original result and the later correction according to GEN-4.
- Habit adherence follows a **scout's honor** principle. For most Habits, I assess whether
  the cue occurred and how well I adhered; the system should not pretend it can determine
  that answer from missing data.
- Some objective Habits, such as reaching Inbox Zero at least once during a day, may be
  scored automatically when an algorithm can determine the result reliably. The UI should
  distinguish an automatically determined result from one I assessed manually.
- Habits should always be related to one or more parents, such as a Goal or identity
  statement (aka Value). This should be a validation rule when editing a Habit, and multiple
  parent relationships should be supported.
- SIDE NOTE:  Depending on how habits are represented in the ontology, it could relate to commitment also.

**Ontology fit (Claude):**
- Maps to (D2): a Habit **is a Commitment** with habit attributes (cue / routine / reward,
  start / end, `expected_cadence` for recurrence). Occurrences are **not stored** — they're a
  projection over cadence + adherence events: followed = an activity `evidences` it,
  not-followed = `violates`, **partial = fixed half-credit** (per D2), gated by an
  `allows_partial` attribute. Streak = derived. "Unrecorded until end of period, then
  not-followed" is a projection rule, not a stored fact. Parents (Goal / Value) = `serves`
  edges, with ≥1 enforced on save. Backfill / correct = append a corrective adherence event.
- Kernel delta: (1) the **partial-credit** third state (backlog); (2) an **occurrence +
  streak projection** over cadence & adherence events; (3) present Habit-Commitment misses
  as a gentle streak break — **exclude them from the confrontational breach report**;
  (4) habit fields are just attributes (free).
- Implications: "Habit" never appears in storage — it's a Commitment the UI dresses up. A
  missed day is an absence / `violates`, shown as a broken streak, not a breach.
- Open decisions: partial as a new edge relation vs an activity-event attribute; materialize
  "not-followed at window close" or leave it purely projected.


### GEN-4 — History views show chronological change with progressive detail
Horizon:    Pilot phase 2
Definition: active
Build:      mapped

**Workflow (Chris):**
- Any history view should present changes in chronological order so I can understand how
  the item, review, or activity evolved over time.
- The initial history view may use a straightforward chronological presentation without
  advanced filtering.
- Production should add flexible filters so I can narrow history by the kinds of records,
  changes, or activity relevant to the question I am investigating.
- A history row should expose additional detail according to the amount and complexity of
  its content: concise supporting information may appear in a tooltip, while substantial
  content should open in a drill-down page or detail view.
- The default presentation should remain readable and focused on meaningful information;
  access to more detail should not require showing every field in the main chronology.

**Ontology fit (Claude):**
- Maps to: the append-only **event log is already the chronology** — `v_event`,
  `state_change` history, concerning events, plus D3 review edit-events. A history view is a
  time-ordered query over these for the item in question.
- Kernel delta: none for the Pilot (reads exist). Production's flexible filters are richer
  queries, not schema.
- Implications: "progressive detail" (tooltip vs drill-down) is pure UI over existing event
  rows; nothing new to store.
- Open decisions: none blocking.


### GEN-5 — Self-assessed adherence uses scout's honor and remains correctable
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- For Habits and Commitments that cannot be measured objectively, I am the authority on
  whether I adhered. LifeOS should support honest self-assessment rather than claim certainty
  from the absence of automatically collected evidence.
- A missed or failed result must be correctable when I actually adhered but forgot to record
  it, recorded it late, or selected the wrong result.
- A correction should update the current status, streak, score, and other summaries that
  depend on that result while retaining a chronological history of the original result and
  correction.
- When adherence can be measured reliably, such as reaching Inbox Zero at least once in a
  day, LifeOS may score it automatically.
- The UI should make the source of an adherence result understandable: manually assessed or
  automatically determined. I should still be able to inspect and correct an automated
  result when its inputs were incomplete or wrong.
- The initial workflow should favor simple, transparent rules over elaborate scheduling or
  confidence logic.

**Ontology fit (Claude):**
- Maps to: the **provenance axis already carries this** — manual self-assessment =
  `declared`; auto-scored (e.g. Inbox Zero) = `derived`. A correction is a newer adherence
  event the projection prefers; append-only keeps the original *and* the correction in
  history.
- Kernel delta: none beyond GEN-3's partial-credit; a **latest-wins-per-occurrence**
  convention (or an explicit supersede) so a correction overrides the earlier result.
- Implications: "was this manual or automatic?" is answered by reading provenance — no new
  field. Pairs directly with GEN-3.
- Open decisions: correction via latest-wins-by-occurrence-key vs an explicit `supersedes`
  edge on adherence events.


### GEN-6 — Object creation is globally available and uses type-specific forms
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Provide a global **New** action from the main LifeOS application shell so I can create an
  object from any primary screen. Creation must not be limited to Browse.
- A keyboard shortcut for global **New** is not important for the Pilot.
- Activating **New** should first ask me to select the object type.
- Global **New** should create subjects of every supported subject type. Notes, Ideas, and
  other event-style entries should continue through the Capture workflow rather than being
  mixed into this object-creation flow.
- Problems are available through both paths: Capture provides minimal title-first intake,
  while global **New** opens the full Problem form defined by GEN-14.
- Because the supported type vocabulary is small, a simple visible type list is sufficient
  for the Pilot. Search, recent types, and favorites are not required.
- After I select a type, show a creation form designed for that type rather than one generic
  form containing every possible field.
- If I change the selected type before saving, update the form without an additional warning.
  Preserve values that remain applicable and discard values belonging only to the prior type.
- Require only the fields that are mandatory for the selected type. Other applicable fields
  should remain optional.
- Allow Tags and Relationships to be assigned during creation.
- Tags and Relationships must also remain editable after creation; choosing not to assign
  them initially must not limit the object later.
- Keep Tags and Relationships visually and conceptually separate according to GEN-1.
- Provide explicit Save and Cancel actions. Saving creates the object; cancelling returns me
  to the screen where I invoked **New** without creating anything.
- When creation is invoked from Browse, preserve the active filters. The new object should
  appear in the item list only when it matches those filters; do not clear or relax filters
  merely to reveal it.
- If the new object matches the active Browse filters, refresh the list and select it. If it
  does not match, leave the filtered list unchanged and provide clear confirmation that the
  object was created successfully.
- Parent-first child creation follows GEN-7 and should be the preferred path when I am
  creating work beneath an existing object.

**Ontology fit (Claude):**
- Maps to: `bsk new <Type> "<title>"` creates a subject of the chosen type; the kernel is
  **type-agnostic at write time**, so "pick a type, then a type-specific form" is entirely
  app-side. Initial attributes → `bsk set`; Tags → `bsk tag` (GEN-1); Relationships →
  `bsk link` / `bsk relate`. Notes / Ideas stay on the Capture path (D4), not here. A Problem
  opened via **New** creates the *same* `Problem` subject that capture would (GEN-14 / D4).
- Kernel delta: none of its own — rides `bsk new` + `bsk set` + Tags (GEN-1) + the new types
  (Area / Appointment, D1 / D10). Changing type before save is app-only (the kernel sees only
  the final create).
- Implications: "Save creates it / Cancel creates nothing" is a single `bsk new` at the end,
  not incremental writes. Tags & relationships are assignable at creation *and* later — same
  verbs either way.
- Open decisions: whether `bsk new` should accept attributes + tags + an initial parent link
  in **one atomic call** (cleaner for GEN-7's all-or-nothing guarantee) vs the app composing
  several `bsk` calls — see GEN-7.


### GEN-7 — Create a child from its parent and relate it automatically
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- A common creation workflow begins from an existing parent object: I create the parent,
  open it, and then create a child object from that context.
- The dominant Pilot hierarchy is **Value -> Goal -> Project -> Task**.
- A Task may also be created directly beneath a Goal when a Project would add unnecessary
  structure. These combinations cover the vast majority of initial parent-first creation.
- The parent detail view should provide an obvious **New child** or equivalent action from
  the area where related children are displayed.
- Starting creation from a parent should retain that parent context while I select the child
  type and complete the type-specific form from GEN-6.
- Limit the child-type chooser to types that are valid beneath the selected parent. Do not
  show every subject type in this contextual creation flow.
- Show the pending parent relationship clearly in the child form so I understand where the
  new object will be connected.
- Saving the child should create the child and automatically relate it to the parent. I
  should not have to reopen the new child and add the relationship manually.
- The initiating parent should be the child's only parent by default. I may add other parent
  relationships later through the normal Relationships UI.
- The UI should infer the appropriate parent/child relationship from the selected parent and
  child types when the relationship is unambiguous.
- If more than one relationship meaning is valid, ask me to choose the relationship as part
  of creation rather than silently selecting an arbitrary one.
- Cancelling child creation should return me to the unchanged parent detail view.
- Group related children by type on the parent detail screen so Goals, Projects, and Tasks
  remain easy to distinguish.
- After saving, keep focus on the parent, refresh its related-child section, and show the new
  child in the appropriate type group. Do not navigate automatically to the child.
- Other active Browse filters should remain authoritative and should not be cleared.
- If the child cannot be related successfully, do not present the workflow as successfully
  completed; preserve the entered child data so I can retry or cancel.

**Ontology fit (Claude):**
- Maps to: create the child (`bsk new <ChildType>`), then add a parent edge on the existing
  alignment graph. **Canonical parent→child relation map** for the Pilot hierarchy (the child
  is the edge's `from`, the parent the `to`):
  - Goal → Value: `serves`
  - Project → Goal: `results_in`
  - Task → Project: `serves`
  - Task → Goal (direct): `serves`
  These pairs are unambiguous, so the UI infers the relation silently; only a genuinely
  ambiguous pair prompts. The **leaf invariant** (nothing serves a Task) makes the child-type
  chooser under a Task empty for free.
- Kernel delta (needs-kernel): an **atomic create-and-link** (child + parent edge in one
  transaction) so "if the relate fails, don't report success and keep my child data" holds
  without leaving an orphan — important under the no-delete policy (D9). Plus recording the
  relation map above.
- Implications: the child defaults to a **single** parent; more are added later via normal
  Relationships. Grouping children by type on the parent screen is a read over incoming edges.
- Open decisions: atomic create+link as a `bsk` flag (e.g. `bsk new Task "…" --serves <p>`)
  vs app-composed with rollback; whether the relation map lives in the kernel or the app.


### GEN-8 — Goals use a type-specific form and must be developed before activation
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- The Goal creation and edit form should include **Title**, **Desired end state**,
  **Target date**, **Area**, **Status**, **Description**, and **Motivation**.
- Title is required. Other fields may remain incomplete while the Goal is still being
  developed, subject to the activation rules below.
- For now, every Goal should connect to at least one Value / identity statement. The Value
  relationship is a provisional rule that may be reconsidered after Pilot use.
- When a Goal is created as a child of a Value through GEN-7, prepopulate that Value
  relationship automatically.
- A target date is optional while a Goal is still inactive or being developed.
- A Goal cannot become **Active** without a target date. If I attempt to activate it without
  one, keep the form open, explain what is missing, and move focus to the Target date field.
- Do not add a Goal progress percentage, progress bar, or calculated completion score in the
  Pilot. The KISS principle is more important than an imprecise progress metric.
- The Goal detail view should show its related Values, child Projects, and any Tasks created
  directly beneath it, using the grouped parent/child behavior in GEN-7.
- Completing or abandoning a Goal should be an explicit Status change.
- When changing a Goal to a completed or abandoned status, allow optional notes but do not
  require a formal outcome or reflection before saving.
- Goal editing should follow BROWSE-2: read-only detail first, explicit Edit, and explicit
  Save or Cancel.

**Ontology fit (Claude):**
- Maps to: the existing **Goal** subject. Fields are attributes — `desired_end_state`,
  `target_date`, `motivation`, `description`, `area` (D1) — set via `bsk set`. Status moves by
  `state_change` (D7 vocab: developing → Active → Completed / Abandoned). The "≥1 Value" link
  is a `serves` edge Goal→Value (the ontology's canonical Goal-serves-Value); GEN-7
  prepopulates it when the Goal is created under a Value.
- Kernel delta: none of its own — rides D7 (status vocab + terminal set) and D1 (Area). "No
  progress %/bar" = nothing stored.
- Implications: the two gates — **target date required to activate** and **≥1 Value** — are
  **app-layer validations**, not kernel invariants (the kernel would still accept a Goal
  without them; the app is the gatekeeper). This matches the tenet; flag if you ever want them
  enforced in the kernel like the leaf rule. Completion / abandonment notes ride the
  `state_change` event.
- Open decisions: keep the activation gates app-side (recommended for the Pilot) vs
  kernel-enforced.


### GEN-9 — Projects use a type-specific form and may stand alone
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- The Project creation and edit form should include **Title**, **Description / scope**,
  **Start date**, **Target / due date**, **Area**, **Status**, and **Notes**.
- Do not include a separate Desired outcome field in the Pilot Project form.
- Title is required. Start date, Target / due date, Area, Description / scope, and Notes
  may remain empty unless a later workflow introduces a specific reason to require them.
- A Project may be created as a child of a Goal through GEN-7, but it may also exist as a
  standalone Project without a parent Goal.
- When a Project is created from a Goal, prepopulate that Goal relationship automatically.
  Do not require the user to reselect the initiating Goal.
- A Project does not need to have a next Task before it can become Active. The UI may call
  attention to an Active Project that has no next action, but it must not prevent activation
  or saving.
- A Target / due date is optional for both developing and Active Projects.
- The Project detail view should show any parent Goals and its child Tasks using the grouped
  parent/child behavior in GEN-7. A standalone Project should remain fully usable when the
  parent-Goal group is empty.
- Completing or abandoning a Project should be an explicit Status change.
- When changing a Project to a completed or abandoned status, allow optional notes but do
  not require a formal outcome or reflection before saving.
- Project editing should follow BROWSE-2: read-only detail first, explicit Edit, and explicit
  Save or Cancel.

**Ontology fit (Claude):**
- Maps to: the existing **Project** subject. Attributes — `description` / scope, `start_date`,
  `target_date`, `area` (D1), `notes` — via `bsk set`. Status by `state_change` (D7:
  developing → Active → Completed / Abandoned). Parent Goal = a `results_in` edge Project→Goal
  (GEN-7), **optional** — a standalone Project simply has no parent edge.
- Kernel delta: none of its own — rides D7 + D1.
- Implications: a standalone Project is an **intentional orphan** in the alignment graph — the
  BROWSE-1 "orphans" filter will surface it, which is correct, not an error. "Active without a
  next Task" is allowed (no blocking validation; the UI may nudge, per GEN-9).
- Open decisions: none blocking.


### GEN-10 — Tasks support title-only quick entry and optional planning detail
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- The Task creation and edit form should include **Title**, **Description / Notes**,
  **Status**, **Due date**, **Scheduled / Do date**, **Area**, **Priority**,
  **Estimated duration**, and **Relationships**, subject to the simplified Priority behavior
  below.
- Task creation must be extremely fast for the common case. Open with the Title field
  focused so I can type only a title and press Enter to create the Task immediately.
- Title is the only required field. Every other Task field may remain empty.
- The title-only path should not force me through the remaining fields, a second confirmation,
  or a separate Save action.
- After a title-only Task is created successfully, close the entry form and return me to the
  context from which I opened it. Do not keep the form open for another Task.
- Keep the full set of optional fields available in the same creation workflow so I can add
  planning detail before saving when needed.
- A Task may be created as a child of a Project or directly beneath a Goal through GEN-7,
  but it may also exist as a standalone Task without either parent.
- Global Task entry should create a standalone Task by default and must not guess or reuse a
  Goal or Project relationship from a prior creation.
- When Task entry is launched from a contextual Goal, Project, or other object screen, link
  the Task to that initiating object automatically when that relationship is valid. Show the
  pending relationship in the form, but do not require me to reselect the object.
- **Due date** and **Scheduled / Do date** are separate fields and must remain independently
  editable. Entering one must not silently populate or replace the other.
- Due date represents a genuine deadline. Scheduled / Do date represents when I intend to
  work on the Task; scheduling it does not create or imply a deadline.
- Do not ask me to choose a Priority during Pilot Task creation. Assign **Medium** by default
  so quick entry remains simple. The manual Priority choices may be reconsidered later.
- The initial Task statuses are **Not started**, **In progress**, **Waiting**,
  **Completed**, and **Cancelled**.
- New Tasks default to **Not started** unless their creation context explicitly supplies a
  different status.
- Completing or cancelling a Task should be an explicit Status change.
- When changing a Task to Completed or Cancelled, allow optional notes but do not require a
  note or an additional completion dialog.
- Open decision: whether selecting **Waiting** should offer a Person or AI agent and a
  follow-up date, and whether either field should be required.
- Task editing should follow BROWSE-2: read-only detail first, explicit Edit, and explicit
  Save or Cancel.

**Ontology fit (Claude):**
- Maps to: the existing **Task** subject (a **leaf** — nothing serves a Task). Attributes:
  `due` and `scheduled` (do-date) as **two independent keys**, plus `priority` (default
  Medium), `estimated_duration`, `area` (D1), `description`. Status by `state_change` (D7:
  Not started / In progress / Waiting / Completed / Cancelled). Title-only quick create = a
  bare `bsk new Task "<title>"`. Parent (Project or Goal) via GEN-7's `serves`, optional.
- Kernel delta: none structural — all fields are jsonb attributes (free); the only work is
  standardizing keys (`due` exists; add `scheduled`, `priority`, `estimated_duration`) plus
  D7 status.
- Implications: `due` (a real deadline) and `scheduled` (intent-to-work) stay separate and are
  never cross-populated. **Priority re-enters here** (default Medium) after being deferred
  earlier — harmless as a latent attribute, but note it against the earlier "let AI infer
  priority" stance. The leaf rule blocks children under a Task (GEN-7).
- Open decisions: **Waiting** → an optional `waiting_for` Person edge + a follow-up-date
  attribute (GEN-10's open question); final key name (`scheduled` vs `do_date`).


### TASKS-1 — The Tasks tab optimizes daily execution and quick entry
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Provide the dedicated **Tasks** destination defined by NAV-1 as a focused working view,
  not merely a generic list with a Task filter applied visibly.
- Group the default active view into **Overdue**, **Today**, **Upcoming**, and
  **Unscheduled** sections.
- Overdue contains incomplete Tasks whose Due date is before today.
- Today contains incomplete Tasks whose Due date or Scheduled / Do date is today, unless
  they already appear in Overdue.
- Upcoming contains incomplete Tasks with a future Due date or Scheduled / Do date.
- Unscheduled contains incomplete Tasks with neither date.
- Show each Task only once in the first applicable group, using the group order above as
  precedence. Completed and Cancelled Tasks are hidden from the default active view but
  remain available through Status filters.
- Provide an always-visible title-only quick-entry row at the top of the Tasks tab. Focused
  entry should allow me to type a Title and press Enter to create a standalone Task with the
  defaults from GEN-10.
- After successful inline creation, clear the entry row and leave it ready for another Task.
  This is intentionally different from the modal quick-entry form, which closes after save.
- Provide an obvious way to expand the inline entry into the full Task form before saving
  when I want to enter optional planning detail.
- If inline creation fails, preserve the typed Title in place and show an actionable error
  without creating a duplicate Task.
- Provide direct list actions to mark a Task **Completed** or **In progress** without opening
  its editor. Apply the change immediately and preserve it in Task history.
- Include filters for **Status**, **Due date**, **Scheduled / Do date**, **Area**,
  **Goal / Project**, and **Tags**. Apply changes immediately and remember this tab's filter
  state following NAV-1.
- Selecting a Task should show its read-only detail, with explicit Edit and Save / Cancel
  behavior following BROWSE-2.
- Multi-select and bulk actions are not required for Pilot. They may be reconsidered for a
  later phase after single-item workflows have been validated.

**Ontology fit (Claude):**
- Maps to: pure **reads** over Task subjects. The four groups are a derived projection over
  the two date attributes vs today — Overdue (incomplete, `due` < today), Today (`due` or
  `scheduled` = today), Upcoming (future `due` / `scheduled`), Unscheduled (neither) — with
  "show once, first group wins" as app precedence. Inline quick-entry = `bsk new Task`; list
  actions (Completed / In progress) = `state_change`.
- Kernel delta: none — rides GEN-10's attribute keys, D7 status, Tags (GEN-1), Area (D1), and
  the alignment edges for the Goal / Project filter.
- Implications: the groups are computed, not stored; Completed / Cancelled Tasks drop out of
  the default view but remain reachable via Status filters. This is the Task-scoped sibling of
  BROWSE-2.
- Open decisions: none blocking.


### GEN-11 — Identity Statements are timeless rather than status-driven work
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Use **Identity Statement** as the user-facing term throughout the UI. This is the concept
  represented as a Value in the ontology and in older requirements.
- An Identity Statement expresses a property of who I am, not an outcome I am trying to
  complete.
- An example is: **I'm the type of person who treats others how I would like to be treated
  myself.** The UI should present Identity Statements as first-person declarations rather
  than as Tasks, aspirations, or performance measures.
- The Identity Statement creation and edit form should include **Title**,
  **Statement / description**,
  **Why it matters**, **Area**, and **Notes**. Title is required; the other fields are
  optional.
- Do not give Identity Statements a Status field or workflow states. An identity is not Active,
  In progress, Completed, or otherwise status-driven; it simply is.
- Do not give Identity Statements target dates, due dates, scheduled dates, or other
  lifecycle dates. They are intended to be timeless and potentially eternal.
- Do not add adherence, compliance, or divergence state to an Identity Statement. Times when
  my behavior diverges from it should be reflected in Journals, reviews, or other activity
  records without changing what the Identity Statement is.
- Area is an optional property. An Identity Statement may describe who I am across my whole
  life without being forced into one Area.
- If an Identity Statement no longer represents me, I may archive it or delete it rather
  than changing a status.
- Archiving is reversible. Archived Identity Statements should be hidden from normal views
  but available through an explicit Archived filter, where I can restore them.
- Permanent deletion should require confirmation before the Identity Statement is removed.
- Sort Identity Statements alphabetically by Title in the Pilot. Manual ordering is not
  required in the general Identity Statement list; the composed Vision view has its own
  manual ordering under GEN-16.
- The Identity Statement detail view should prominently show all Goals connected to and
  supported by it, using the grouped parent/child behavior in GEN-7.
- Creating a Goal from an Identity Statement should use GEN-7 and automatically prepopulate
  the initiating relationship.
- Identity Statement editing should follow BROWSE-2: read-only detail first, explicit Edit,
  and explicit Save or Cancel.

**Ontology fit (Claude):**
- Maps to: the existing **Value** subject — "Identity Statement" is just the UI label. Value
  already carries a `statement` column (migration 0010), an exact home for the first-person
  declaration; "Why it matters", Notes, and `area` (D1) are attributes. **No status** — Value
  has no `state_change` workflow, which is precisely the status-less case D9 was built for: it
  uses the **universal archive flag** instead.
- Kernel delta: none of its own — Value + `statement` exist; the rest are attributes; archive
  is D9. Goals attach via the canonical `serves` edge (Goal→Value), prepopulated by GEN-7.
- Implications: Value is the one type with a first-class `statement` and no status. "Behavior
  diverging from an identity" is recorded via Journals / activity (GEN-11), never as a status
  on the Value.
- Open decisions: **reconcile with D9** — GEN-11 still mentions permanent *delete*, but D9 is
  archive-only (never delete); archive should be the mechanism. Also the `why_it_matters`
  attribute key.


### GEN-12 — Commitments support promises to myself and others
Horizon:    Pilot
Definition: drafting
Build:      unmapped

**Workflow (Chris):**
- This is an intentionally provisional first pass. Commitments are currently the object type
  I am least confident about in the LifeOS architecture, so the Pilot should keep this UI
  simple and easy to revise through use.
- A Commitment may represent a promise to myself or a promise involving another person or
  an AI agent.
- The Commitment creation and edit form should initially include **Title**,
  **Description / Notes**, **Person or AI agent**, **Due date**, **Recurrence**, **Area**,
  and **Status**.
- Title is required. The other fields are optional unless a particular recurring workflow
  later establishes a stronger rule.
- Use the same form for one-time and recurring Commitments. Selecting a recurrence should
  reveal only the additional recurrence controls that are needed; it should not switch to a
  different object-creation workflow.
- The provisional Commitment statuses are **Open**, **Fulfilled**, **Missed**, and
  **Cancelled**.
- New Commitments default to Open.
- Fulfilling, missing, or cancelling a Commitment should be an explicit Status change.
- When marking a Commitment Missed, allow me to record an optional reason. A missed
  Commitment must remain correctable when it was fulfilled but recorded late or incorrectly.
- Person or AI agent should use the same selector and should not treat virtual AI agents as
  free-text notes or second-class assignees.
- Commitment editing should follow BROWSE-2: read-only detail first, explicit Edit, and
  explicit Save or Cancel.
- Open decision: the durable distinction between a Commitment and a Task, and which user
  actions should create one versus the other, remains to be refined through Pilot use.

**Ontology fit (Claude):**
- pending


### GEN-13 — Decisions record conclusions already made and remain editable with history
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- A Decision represents a decision that has already been made. It is not primarily a
  workspace for comparing options or tracking a question that still needs an answer.
- The Decision creation and edit form should include **Title**, **Description**,
  **Decision date**, **Area**, **Status**, and **Relationships**.
- Title is required. The remaining fields should be easy to add without making the initial
  form cumbersome.
- Title should state the conclusion that was reached, such as **Use SQLite for local
  storage**, rather than restating the unresolved question.
- Description should provide the available context, reasoning, and consequences without
  requiring separate structured fields for each in the Pilot.
- Decision date is optional and should not default automatically to today or another date.
- The Decision statuses are **Open**, **Implementing**, **Cancelled**, and **Closed**.
- Open means the decision has been made but implementation has not started.
- Implementing means the actions that carry out the decision are underway.
- Closed means implementation is complete or the decision needs no further attention.
- Cancelled means the decision was reversed or I chose not to carry it out.
- All Decision status changes are manual. Creating, starting, completing, or cancelling
  related work must not silently move the Decision to Implementing, Closed, or Cancelled.
- Decisions remain editable after creation. Editing must preserve chronological history so
  I can see what changed and when, following GEN-4.
- The Decision detail screen should separate relationships into two clear groups:
  **Context** and **Resulting work**.
- Context contains the Problems, Ideas, Goals, Projects, or other objects that prompted or
  informed the Decision.
- Resulting work contains the Goals, Projects, Tasks, or other trackable objects created or
  affected to carry out the Decision.
- When Decision creation is launched from another object's detail screen, show and
  prepopulate that initiating object in Context without requiring me to select it again.
- From a Decision detail screen, provide a contextual action to create resulting work. Ask
  me for the valid object type, open its normal type-specific form, and show the pending
  Decision relationship before saving.
- Support both directions as normal workflows: I may create a Decision from a contextual
  object, or create resulting work from an existing Decision.
- Do not force Decisions into the Value -> Goal -> Project -> Task parent hierarchy. The two
  relationship groups express the thought and implementation flow without pretending that
  the Decision is a structural parent or child.
- Allow Relationships to be added both during creation and later editing so the thought
  workflow can evolve without requiring a rigid creation order.
- When a Decision is reversed, the normal workflow is to change the original Decision to
  Cancelled and create a linked replacement Decision containing the new conclusion.
- Provide a **Create replacement Decision** action that opens a new Decision form, preserves
  a visible link to the original Decision, and lets me enter the replacement conclusion.
  Do not overwrite the original conclusion or its history.
- Decision editing should follow BROWSE-2: read-only detail first, explicit Edit, and
  explicit Save or Cancel.

**Ontology fit (Claude):**
- Maps to: the existing **Decision** subject — and the ontology **already anticipated this**:
  Decision ships with the `supersedes` relation ("a Decision supersedes an earlier one"), which
  is exactly the reversal flow. `decision_date` and `area` (D1) are attributes; Status via
  `state_change` (D7: Open / Implementing / Cancelled / Closed; terminal = Cancelled / Closed),
  **all manual** — an app rule that related work never auto-moves the Decision.
- The two relationship groups are **one `results_in` chain split by direction**: **Context** =
  *incoming* `results_in` (the Problems / Ideas / Goals that led to the Decision), **Resulting
  work** = *outgoing* `results_in` (the Goals / Projects / Tasks the Decision produces). This
  slots Decision into the same chain as Problem → Idea → work (GEN-14 / CAP-6):
  Problem / Idea → **Decision** → work.
- **Reversal** = the purpose-built `supersedes`: a replacement Decision `supersedes` the
  original and the original moves to `Cancelled`; append-only + `supersedes` means the original
  conclusion and its history are never overwritten.
- Kernel delta: none of its own — Decision, `results_in`, and `supersedes` all exist; rides D7
  (status), D1 (Area), GEN-4 (history), and GEN-7's atomic create-and-link for
  create-resulting-work. Decision is deliberately **outside** the Value→Goal→Project→Task
  hierarchy, so it uses generic create + `results_in`, not GEN-7's parent map.
- Open decisions: Context-via-incoming-`results_in` fits Problems / Ideas (causal) cleanly but
  is a looser "informed by" for a Goal that merely informed without causing — fine for the
  Pilot, or allow a generic association later if it grates.


### GEN-14 — Problems track unresolved situations that need thought or action
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- A Problem is a durable object representing an unresolved situation or question that needs
  thought or action.
- The Problem creation and edit form should include **Title**, **Description**,
  **Date identified**, **Area**, **Status**, **Impact**, and **Relationships**.
- Title is required. The remaining fields are optional unless later Problem workflows
  establish a reason to require them.
- Date identified should default to the Problem's capture or creation date and remain
  editable afterward.
- Impact is a free-text field rather than a fixed Low / Medium / High rating.
- The Problem statuses are **Open**, **Working**, and **Resolved**.
- New Problems default to Open. Working means I am actively investigating or addressing the
  Problem. Resolved means it no longer needs further thought or action.
- A Problem may be created independently or contextually from another object.
- From a Problem detail screen, I should be able to create a related Decision, Goal,
  Project, or Task when that is the appropriate outcome or next step.
- Creating one of those objects from a Problem should automatically prepopulate the Problem
  relationship and show it in the new object's form.
- Creating related work or recording a Decision must not silently mark the Problem Resolved.
  Resolution remains an explicit Status change because the new object may represent only
  one part of the response.
- When changing a Problem to Resolved, allow optional resolution notes but do not require
  them or show an additional mandatory completion dialog.
- Problems remain editable after creation, with chronological change history following
  GEN-4.
- Problem editing should follow BROWSE-2: read-only detail first, explicit Edit, and
  explicit Save or Cancel.
- Problem must be available from global **New**, which opens the full type-specific form.
- A Problem created through the full global **New** form should also be flagged for Inbox
  triage. Deliberately entering its details does not bypass the Inbox decision workflow.
- Selecting Problem in quick capture immediately creates the same durable Problem object
  with Status Open, Date identified set from the capture date, and its other optional fields
  initially empty. It is also flagged for Inbox triage.
- Quick capture and global **New** are two entry paths into the same Problem type, not two
  different Problem concepts and not a later promotion from one object into another.

**Ontology fit (Claude):**
- Maps to: the existing **Problem** subject (the ontology's reuse-by-title anchor, §6). Fields
  are attributes — `date_identified` (default = capture / creation date), `impact` (free
  text), `area` (D1) — plus Description; Status by `state_change` (D7: Open / Working /
  Resolved). Both entry paths create the *same* Problem type: quick capture (D4 / CAP-1) makes
  it with Status Open + date-from-capture, and global **New** opens the full form — both
  flagged for Inbox triage.
- Kernel delta: none structural of its own — Problem exists; rides D4 (capture), D7 (status),
  D1 (Area), INBOX-1 (triage marker). The one soft spot is the Problem→work relation (below).
- Implications: creating a related Decision / Goal / Project / Task from a Problem must **not**
  auto-resolve it — resolution stays an explicit `state_change`; the new object relates back to
  the Problem.
- **Resolved (2026-09-14) — it's a `results_in` chain:** a Problem **`results_in`** Ideas that
  might solve it, and an Idea **`results_in`** (a.k.a. "promotes to") a new Project / Task /
  Goal. A Problem may also `results_in` a Project / Task / Goal **directly**, skipping Ideas,
  when it's simple enough. So "addresses" = the existing `results_in` (from = the source that
  led to it, to = the produced work) — no new edge type. Resolution of the Problem itself stays
  an explicit `state_change`.
- Open decision: does capture **reuse** an existing same-title Problem (the reuse-by-title
  anchor, §6) or always create new?


### GEN-15 — People and AI agents share one filterable directory
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Humans and virtual AI agents should appear together in one **People / Agents** directory
  rather than requiring separate navigation or management screens.
- Provide a clear filter for **All**, **Humans**, and **AI agents**. Filtering should update
  the list immediately and retain the selected filter when I leave and return.
- The creation and edit form should include **Name**, **Human / AI type**,
  **Role or relationship**, **Description**, **Contact / reference information**,
  **Notes**, **Tags**, and **Relationships**.
- Name and Human / AI type are required. The other fields are optional.
- Use type-appropriate labels or hints where helpful, but keep the common form and directory
  structure consistent between humans and AI agents.
- People and AI agents should be archived when they are no longer active or relevant. Pilot
  does not need permanent deletion as the normal removal workflow.
- Archived records should be hidden from the normal directory and selection controls but
  available through an Archived filter, where they can be reviewed or restored.
- A Person or Agent detail screen should group related Commitments, Tasks, Appointments,
  delegated work, and other related objects so I can understand the context at a glance.
- Selectors used by Commitments, Appointments, delegation, and Relationships should search
  the same combined directory and visually distinguish humans from AI agents.
- In Pilot, AI agents are tracked as records only. LifeOS should not attempt to launch,
  message, control, or monitor an agent from this UI.
- AI agents will query LifeOS through their own integration path; the Pilot UI should not
  imply that LifeOS initiates those interactions.
- People / Agent editing should follow BROWSE-2: read-only detail first, explicit Edit, and
  explicit Save or Cancel.

**Ontology fit (Claude):**
- Maps to: the existing **Person** subject; an **AI agent is a Person with a type attribute**
  (`person_kind` = human | ai), not a new type. Role, contact / reference, description, and
  notes are attributes; Tags (GEN-1) and Relationships as usual. Archive via D9 (no delete).
  The directory + All / Humans / AI filter are reads; the detail screen's grouped Commitments /
  Tasks / Appointments / delegated work are reads over incoming relations.
- Kernel delta: none of its own — Person exists; the human / AI split is one attribute;
  archive is D9.
- Implications: one directory backs every Person selector — Commitment owner (GEN-12), Inbox
  Delegate (INBOX-3), Appointment attendees (CAL-1), and AI-authored captures (CAP-3) all point
  here. Contact details are plain attributes (fine for a single-user local Pilot). AI agents
  are records only in the Pilot; the integration path is out of scope.
- Open decisions: the `person_kind` attribute key; whether agents grow distinct fields later.


### GEN-16 — Vision is composed from Identity Statements and long-term Goals
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Vision is not a separate entity, editable document, or object type in LifeOS.
- Do not offer Vision in global **New** and do not build a Vision creation or edit form.
- When the UI presents my Vision, compose it from my Identity Statements and long-term
  Goals. Identity Statements describe who I am; long-term Goals describe the outcomes I am
  working toward.
- Treat a Goal as long-term when its Target date is more than two years beyond the current
  date. Use this as a rolling threshold rather than a hard-coded calendar year.
- Provide Vision as a top-level navigation destination in the Pilot.
- A Vision presentation should link each included Identity Statement and Goal to its normal
  detail view rather than copying it into separately editable Vision content.
- Changes to Vision happen by editing the underlying Identity Statements and Goals. There is
  no separate Vision status, lifecycle, save action, or change history.
- History remains attached to the underlying Identity Statements and Goals so the composed
  Vision always reflects their current state while preserving their individual histories.
- Reviews may surface this composed Vision as higher-horizon context, especially during
  monthly and yearly reflection and planning, without creating a new Vision record.
- The Pilot may use a simple grouped presentation: **Who I am** for Identity Statements and
  **Where I am going** for long-term Goals. Additional visual polish belongs to Production.
- Hide archived Identity Statements and completed, cancelled, or otherwise inactive Goals
  from the default Vision presentation. Provide an explicit way to include them when I want
  historical context without mixing them into the normal view.
- Allow me to arrange the Identity Statements and long-term Goals in my own persistent order
  within their respective Vision groups. Do not replace my order with alphabetical or
  target-date sorting.
- Journal entries should not appear inline in Vision. They remain available through the
  linked Identity Statement and Goal detail screens.

**Ontology fit (Claude):**
- Maps to: **not a subject** — a composed, derived **read**. "Who I am" = non-archived Values
  (GEN-11); "Where I am going" = long-term Goals, i.e. Goals whose `target_date` is more than
  ~2 years out (a rolling query predicate, not a stored flag) and still active. Each shown item
  links to its normal detail view; there is no Vision entity, status, or history.
- Kernel delta: none structural — a query over Values + Goals. The only stored bit is the
  **manual Vision ordering** you set within each group — a small per-item attribute
  (`vision_order`) or a tiny ordered-list setting.
- Implications: Vision always reflects the current Values / Goals (edit those to change it);
  archived Values and inactive Goals drop out by default via D9 + status. Journals never appear
  inline (they stay on the item's detail).
- Open decisions: where the manual ordering lives (per-item `vision_order` attribute vs one
  ordered-list setting) — a "view-ordering" question that may recur.


### CAL-1 — Appointments support manual calendar-style scheduling
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Pilot Appointments are entered and maintained manually. Synchronization with an external
  calendar is deferred to a later phase.
- Use a familiar calendar-style creation and edit form with **Title**, **Date**,
  **Start time**, **End time**, **All day**, **Location**, **Meeting link**,
  **People / attendees**, **Recurrence**, **Area**, **Status**, **Notes**, and
  **Relationships**.
- Title, date, and the applicable start/end values are required. The other fields are
  optional.
- Selecting All day should replace or disable the time controls rather than requiring
  meaningless start and end times.
- Start and end values should use the local time zone by default and validate that the end
  does not precede the start.
- One-time and recurring Appointments should use the same form. Recurrence controls should
  appear only when recurrence is enabled.
- The Appointment statuses are **Scheduled**, **Completed**, **Cancelled**, and **Missed**.
- New Appointments default to Scheduled.
- Do not change an Appointment automatically to Missed merely because its end time has
  passed. I will set Completed, Cancelled, or Missed myself.
- Status changes remain editable with chronological history following GEN-4.
- Each occurrence of a recurring Appointment should have its own editable Status so one
  missed or cancelled occurrence does not rewrite the status of the entire series.
- Today's Appointments should appear prominently on the Dashboard in chronological order,
  with their time, title, and current status visible at a glance.
- Activating an Appointment from the Dashboard should open its normal detail view without
  requiring me to find it again in Browse.
- Appointment creation should be available through global **New** and may prepopulate Area
  or Relationships when invoked from a valid contextual object screen.
- Dashboard and Browse are sufficient for Pilot Appointment access. Do not build a dedicated
  day, week, month, or agenda Calendar screen in the Pilot.
- Appointment-focused views should contain Appointments rather than mixing Scheduled Tasks
  or dated Commitments into a calendar-style presentation.
- Do not require reminders in Pilot or Pilot phase 2. Reminder behavior is deferred to a
  later phase.
- Appointment editing should follow BROWSE-2: read-only detail first, explicit Edit, and
  explicit Save or Cancel.

**Ontology fit (Claude):**
- Maps to (D10): a new **Appointment** subject type. Time fields (`date`, `start`, `end`,
  `all_day`, `location`, `meeting_link`), `area` (D1), and notes are attributes. Status is
  per-**occurrence** (D7: Scheduled / Completed / Cancelled / Missed). Recurrence via D8.
- Kernel delta (needs-kernel): the **Appointment type** (D10) + **materialized recurring
  occurrences** — each occurrence its own status-bearing record (the deliberate D10 divergence
  from Habit's *projected* occurrences) + recurrence (D8). **Attendees** link to `Person`
  subjects, but "attendee" isn't a `serves` / `results_in` / `supersedes` meaning — see the
  People-association item.
- Implications: no dedicated calendar screen in the Pilot (Dashboard + Browse only); an
  Appointment view shows only Appointments, never dated Tasks / Commitments. Status is never
  auto-advanced to Missed — the user sets it.
- Open decisions: **how People links meaning "attendee / involves" are modeled** — a generic
  association relation vs a Person-ref attribute (recurs across Appointments, Commitments,
  delegation, Task `waiting_for`). Lean: a Person-ref attribute for the Pilot.



### CAP-1 — I want to be able to quickly capture a note, idea, problem from anywhere in the app
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- A system-wide global hotkey brings up the capture dialog even when LifeOS is not the
  focused application.
- Use Alt+N as the provisional Pilot hotkey. The final shortcut has not been decided yet.
- Keep system-wide capture available while the main LifeOS window is minimized to the
  system tray.
- Always visible button on the UI does the same thing (eg in a top banner/frame)
- auto focus cursor in the text box so I can just start typing immediately
- The most ubiquitous action that I want to make quick and easy.
- the dialog can have a simply 1 click selector (tabbable also) to choose what type it is. Default: note
- Note, Idea, and Problem should all use the same simple text box during quick capture. Do
  not introduce type-specific fields into this popup.
- A captured Problem immediately creates the durable Problem defined by GEN-14 rather than
  a temporary capture that must later be promoted. Derive its Title from the captured text,
  preserve the full text as its Description, default Status to Open, and default Date
  identified to the capture date.
- The additional Problem fields remain available through its normal detail editor after
  capture; they must not slow down the quick-capture dialog.
- All captures default to the inbox. There must be an initial triage (but by design, it can be in the future)
- Many capture notes are just 1 liners, but the text box should be multi-line just in case, so I can see the entire message as I type.
- Pressing Enter should submit the capture. I should also be able to Tab to the Submit button
  and activate it with Enter.
- Ctrl+Enter should insert a line break within the multiline text box rather than submitting.
- Tabbing to the Note / Idea / Problem selector is sufficient. Direct per-type keyboard
  shortcuts are not required.
- Pressing Escape should immediately discard the current draft and close that capture
  dialog without an additional warning.
- If the global hotkey is pressed while another capture dialog is open, create another
  independent capture dialog rather than focusing or replacing the existing one.
- Place concurrent capture dialogs using window tiling so they do not open in the same
  location and I can see each draft at the same time.
- Submitting or cancelling one capture dialog must not affect the content or selected type
  in any other open capture dialog.
- On successful capture, close the popup immediately. Do not show an additional confirmation
  dialog; closing the popup is sufficient feedback.
- Do not require a separate title. Derive the display title automatically from the entered
  text while preserving the complete original content.
- Do not submit blank or whitespace-only content.
- If saving fails, keep the popup open, preserve everything I entered and the selected type,
  and show the error within the dialog so I can retry without retyping.

**Ontology fit (Claude):**
- Maps to (D4 — capture bifurcates by type):
  - **Note** → a `note` event + artifact (the plain, reference-ish capture), flagged for the
    Inbox via the triage marker (INBOX-1).
  - **Problem** → **immediately a `Problem` subject** (GEN-14): Title derived from the text,
    full text as Description, Status `Open`, Date identified = capture date; flagged for triage.
  - **Idea** → **immediately an `Idea` subject** (CAP-6): full text, derived Title, Status
    `New`; flagged for triage.
  (Document / URL flavors are CAP-4 / CAP-5 — events + reference.) All land in the Inbox.
- Kernel delta (needs-kernel): the **bifurcated capture path** (D4) — create an *event* (note)
  or a *subject* (idea / problem) from the same dialog — plus **flag-on-create** so a freshly
  created capture subject enters the triage queue (the INBOX-1 marker on a subject, not only an
  event). `bsk capture` (note) and `bsk new Problem/Idea` already exist; the new bits are
  choosing between them by type and attaching the triage marker.
- Implications: **"promote" now splits** — a `note` (event) promotes *into* a subject, while an
  `Idea` (already a subject) "promotes" *to another type* by creating it, adding a `results_in`
  edge from the Idea, and setting the Idea `Promoted` (CAP-6). Concurrency, hotkeys, and
  title-derivation stay app-side.
- Open decisions: does capturing a Problem **reuse an existing same-title Problem** (the
  ontology's reuse-by-title anchor, §6) or always create new? Does a plain `note` keep any
  flavor tag at all now that idea / problem are their own subjects? *(Supersedes the earlier
  A / B / C flavored-note mapping, now retired.)*


### CAP-2 — I want Voice mode for captures. 
Horizon:    Pilot phase 2
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Alt+V is the system-wide hotkey for voice capture. It should remain available while the
  main LifeOS window is minimized to the system tray.
- Invoking voice capture should begin microphone recording immediately without requiring a
  separate Record action.
- Show an unmistakable recording indicator so I can tell that the microphone is active.
- Show a live volume/audio-level bar while recording so I can tell at a glance whether the
  microphone is detecting my speech.
- Transcribe speech live in real time and display the growing transcript in the capture
  window.
- While recording, pressing Enter or selecting a visible Stop button should stop the
  microphone and move the capture into review mode.
- Stopping recording should not submit automatically. I should be able to review and edit
  the transcript before explicitly submitting the capture.
- Review mode should provide audio playback so I can compare the recording with the live
  transcript before making corrections.
- Keep the recording available temporarily for review and playback even when **Keep audio**
  is not selected. After successful submission, discard it unless retention is required by
  **Keep audio** or transcription failure.
- Pressing Escape should immediately cancel the voice capture and discard its transcript
  and temporary audio without an additional warning.
- If Alt+V is pressed while a voice-capture window is already open, do not start another
  recording. Focus the existing voice-capture window instead.
- Voice capture should provide the same tabbable Note / Idea / Problem selector as keyboard
  capture, with Note selected by default.
- The default behavior is **transcript only**. Do not retain the audio recording after a
  successful transcription unless I select a **Keep audio** checkbox for that capture.
- When **Keep audio** is selected, save the original recording with the transcript so it can
  be retrieved later.
- If transcription fails, preserve the audio and place it in the Inbox for later processing
  even when **Keep audio** was not selected. The transcript-only default must not cause the
  only usable copy of a failed capture to be discarded.
- Clearly identify a failed-transcription Inbox item as needing transcription or manual
  processing.
- A successful voice capture should enter the same Inbox triage workflow as a keyboard
  capture.

**Ontology fit (Claude):**
- Maps to: the **`voice` event kind** — which already exists in the ontology but has **no
  `bsk` write path today** (one of the 4 unreachable kinds). Audio = a binary artifact; the
  transcript = the event's text content. Note / Idea / Problem flavor per D4. Enters the
  same inbox via the triage marker (INBOX-1).
- Kernel delta: a `bsk` write verb for `voice` events (closes an unreachable kind);
  **managed binary artifact storage** for the audio (shared with CAP-4 / JOURNAL-2 — see
  backlog). The transcription itself is app-side, not kernel.
- Implications: "Keep audio" = retain the binary artifact; transcript-only = discard it
  after success; failed transcription = keep audio + flag to inbox as needing processing.
- Open decisions: where audio bytes live (DB large object vs a managed file dir) — one
  decision shared across all binary attachments.

### CAP-3 - I want AI assist mode for captures
Horizon:    Production
Definition: active
Build:      mapped

**Workflow (Chris):**
- Voice activated wake up AI (if needed), then I just speak freely to give an AI instructions on what to capture.
- The AI will interpret my intent and capture the necessary content from its own interpretation.

**Ontology fit (Claude):**
- Maps to: an **AI-authored capture** — provenance `derived` / `observed`, written through
  `bsk` by an agent (the single-writer invariant already treats agents as callers). The AI
  may emit a note / idea / problem or a structured subject; provenance keeps it
  distinguishable from `declared` human capture.
- Kernel delta: none to the ontology (provenance + agent-as-caller exist); needs the AI
  integration and, for attribution, the agent modeled as a (virtual) `Person` the event can
  reference. Production horizon.
- Implications: ties to INBOX-3 delegation-to-AI (virtual-agent People) and CAP-2's voice
  front-end.
- Open decisions: how "who captured this" is attributed (a virtual `Person` vs a provenance
  source string).

### CAP-4 — I want to capture "documents" (represented as attachments)
Horizon:    Pilot phase 2
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Documents should be stored intact for later retrieval workflows
- Attachments should just be an add-on feature to the normal capture flow introduced in pilot phase. So it is in ADDITION to any text description.  For example, I can upload a bank statement PDF, along with a title describing the relevance of it (eg disputed Comcast charge reference statement)
- these captures that have attachments become a "document", which essentially serves as a reference item.
- Use a standard file picker to select the attachment. Drag-and-drop and clipboard paste are
  not required for the Pilot phases.
- Support one attachment per capture for simplicity. Capturing another file creates another
  reference item rather than adding it to the existing capture.
- Copy the selected file into LifeOS-managed storage so the reference remains usable if the
  original file is moved, renamed, or deleted.
- Embedded file preview is not required for Pilot or Pilot phase 2.
- Double-clicking the attachment should open the managed copy in the operating system's
  associated application.
- Production or Someday/Maybe may add in-app previews for common formats such as images and
  PDFs, but the Pilot workflow must not depend on them.


**Ontology fit (Claude):**
- Maps to (D4): a capture-with-attachment is a **`document` / reference flavor** of the one
  capture model — the text description is the event content / title; the file is a **binary
  artifact** copied into managed storage. "Reference item" = a filed capture, not a new
  subject type.
- Kernel delta: **managed binary artifact storage** (shared with CAP-2 audio / JOURNAL-2
  media — one backlog item); the reference flavor (D4); copy-into-managed-store semantics.
- Implications: today's artifact table holds *text* content; binary / large-file handling is
  genuinely new infra. One attachment per capture keeps it simple (another file = another
  reference item).
- Open decisions: blob-in-DB vs managed-file-dir + stored path; retention / dedup.


### CAP-5 — I want to capture URL reference
Horizon:    Pilot phase 2
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Capture and retain the live URL as a reference item.
- Storing a snapshot or archived copy of the referenced page is not required for Pilot
  phase 2.
- The URL capture may include descriptive text so I can record why the link matters rather
  than relying only on the address.
- Double-clicking or activating the URL should open it in the operating system's default
  browser.
- URL references should enter the Inbox for normal tagging, relating, editing, and explicit
  triage resolution.


**Ontology fit (Claude):**
- Maps to (D4): a **`url` / reference flavor** — the live URL (+ optional description) stored
  as plain-text artifact content. The lightest reference type: no snapshot (deferred), no
  binary storage, so **no new infra** unlike CAP-4.
- Kernel delta: just the D4 reference flavor; opening in the default browser is app-side.
- Implications: shares the one capture / reference model; enters the inbox like any capture.
- Open decisions: a distinct `url` flavor vs a plain note carrying a `url` attribute.


### CAP-6 — Ideas remain lightweight until promoted or rejected
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- An Idea is a lightweight captured thought, not a durable work object with a large
  type-specific form or lifecycle.
- Quick capture should create an Idea with the complete original text, a display Title
  derived from that text, its capture timestamp, and Status New.
- New Ideas enter the Inbox and remain available until I explicitly decide what to do with
  them. Age alone must not close, delete, or promote an Idea.
- The Idea statuses are **New**, **Promoted**, and **Rejected**.
- New means I have not yet converted the Idea into a durable, actionable, or otherwise
  trackable object.
- Promoted means I converted it into a Goal, Project, Task, Problem, Decision, or another
  supported trackable object.
- Rejected means I decided against the Idea. Rejection should preserve the Idea and its
  history while hiding it from normal active views; it is the Idea's archival outcome, not
  permanent deletion.
- Provide explicit Promote and Reject actions during Inbox triage and from the Idea's detail
  view.
- Promote should ask me for the destination object type, open that type's normal creation
  form with applicable Idea content prepopulated, and allow me to review or edit it before
  saving.
- A successful promotion should automatically relate the resulting object to the source
  Idea, set the Idea to Promoted, and resolve its Inbox triage state.
- If creation or relationship saving fails, keep the Idea New and in the Inbox, preserve my
  entered values, and do not report the promotion as successful.
- Reject should set the Idea to Rejected and resolve its Inbox triage state without deleting
  the captured content.
- Editing, tagging, or relating a New Idea without using Promote or Reject must not remove it
  from the Inbox, following INBOX-4.
- Global **New** does not need a separate full Idea form; Capture is the Pilot entry path for
  Ideas.
- Idea content and status remain editable with chronological change history following GEN-4.

**Ontology fit (Claude):**
- Maps to: the existing **Idea** subject, created on capture (CAP-1 / D4) with Status `New`.
  Statuses New / Promoted / Rejected (D7). **Promote** = create the target subject
  (Goal / Project / Task / Problem / Decision), add a **`results_in`** edge from the Idea to it,
  and set the Idea `Promoted` — the subject→subject "promote" from D4 (Problem→Idea→work is one
  `results_in` chain; see GEN-14). **Reject** = set the terminal `Rejected` status, which — like
  Completed / Cancelled — drops the Idea from default active views while preserving it.
- Kernel delta: none of its own — Idea exists; rides D4 (capture + split promote), D7 (status),
  D9 (Rejected = archived), INBOX-1 (triage marker).
- Implications: an Idea stays deliberately lightweight — no big form, a 3-status lifecycle;
  Capture is its only entry path (no global-New Idea form). Editing / tagging / relating a New
  Idea does not resolve it (INBOX-4); only Promote or Reject does.
- **Resolved (2026-09-14): Rejected is a (terminal) status** (D7), not the archive flag — like
  Completed / Cancelled it hides the Idea from default active views; the universal archive flag
  (D9) stays a separate, orthogonal mechanism. No blocking open decisions.


### JOURNAL-1 — Support append-only plain-text journals on subjects
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- A Journal can be associated with a subject of any type, including Goals, Problems,
  Projects, and Habits.
- Each subject has one Journal that accumulates entries over time.
- Display the Journal as a chronological series of individually timestamped entries.
- Journal entries are append-only. Once an entry is appended, it becomes permanently
  read-only; a correction or clarification must be added as a new entry.
- Plain text is sufficient for the Pilot. Rich formatting and inline media belong to the
  Production requirement JOURNAL-2.
- On the subject detail screen, show the existing Journal history first. Keep editor controls
  hidden in the normal read view.
- Provide an obvious **Append** action that reveals a plain-text editor at the bottom of the
  history, near where the new entry will appear.
- After appending, return to the clean read view and show the new timestamped entry in the
  chronology.
- Journaling about a subject will be a common action because it records evolving thoughts
  about that subject over time.
- Creating a separate Note and relating it to the subject remains a different valid workflow;
  the UI should not imply that every thought concerning a subject must be appended to its
  Journal.

**Ontology fit (Claude):**
- Maps to: **`journal` events, each `concerns` the subject** — and append-only immutability
  is an *exact* fit for "entries are permanently read-only; a correction is a new entry."
  "One Journal per subject" is a **projection** (all journal-kind events concerning that
  subject, chronological), not a stored container.
- Kernel delta: minimal — today it's `bsk journal` then `bsk relate <event> <subject>`; a
  convenience verb that journals-and-relates atomically + a `v_subject_journal` reader would
  smooth it. No ontology change.
- Implications: the cleanest fit in the batch — the append-only spine was built for exactly
  this. "Journal vs a related Note" is a flavor distinction (D4); both are events that
  `concern` the subject.
- Open decisions: a dedicated journal-append verb vs composing the existing two.


### JOURNAL-2 — Production journals support rich formatting and inline media
Horizon:    Production
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Production Journal entries should support headings, lists, bold, italic, and hyperlinks.
- Production Journal entries should support inline images and video.
- A single Journal entry may contain multiple inline images or other media items.
- Rich content should appear in the intended position within the entry rather than being
  collected only as a separate attachment list.
- The read view should render rich content cleanly while continuing to hide editing controls
  until I choose Append.
- The append-only rule from JOURNAL-1 still applies to rich entries: after an entry is saved,
  corrections are made through a new entry rather than editing history.

**Ontology fit (Claude):**
- Maps to: same as JOURNAL-1 (append-only `journal` events) with **rich artifact content**
  (markdown / HTML + inline media artifacts). Append-only is unchanged.
- Kernel delta: rich content format + **inline media storage** (shares the managed binary
  artifact infra with CAP-2 / CAP-4). Production horizon.
- Implications: media items are artifacts referenced inline; no new relation or subject.
- Open decisions: content format (markdown vs HTML); media storage (the shared infra call).




### INBOX-1 — The Inbox is a source-agnostic triage queue, not a capture log
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- The Inbox is **manual triage for anything that needs a processing decision** —
  GTD-style: review an item, then Do / Delegate / Defer / File / Drop. You can also think of the inbox as anything that requires my attention.  This will probably be the 2nd or 3rd most used view of the user interface, because the goal is to keep it to "Inbox Zero" (empty inbox) as much as possible.
> A corrollary to this "Inbox Zero" framing, is that the average number of items in the inbox can itself become a decent measure (though not a complete one) of how well I am keeping on top of concerns in my life.
- Membership is defined by *"this needs a human decision,"* independent of **what** the
  item is (note, idea, email, a nudge) or **where it came from** (I typed it, or — in the
  future — an AI agent read my email and dropped it here for me to triage).
- Membership can ALSO include a notification that results from missed commitments, or other diagnostics, rule breaks, etc. (again, anything that should require my attention is the goalpost)
- An unfinished daily objective should be flagged into the Inbox at the end of the day. It
  remains there until I explicitly decide what to do with it through the normal triage
  workflow; it must not roll into the next day's plan silently.
- Permitted actions depend on the item's type. The UI should show only appropriate actions,
  using the triage behavior specified by INBOX-3.


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


### INBOX-2 — Reaching Inbox Zero is a recurring commitment to myself
Horizon:    Pilot phase 2
Definition: drafting
Build:      unmapped

**Workflow (Chris):**
- Clearing the Inbox should be represented as a recurring Commitment I make to myself,
  rather than as a mandatory step embedded in the daily-review workflow.
- The Commitment is satisfied by reaching Inbox Zero during its applicable period.
- Once the required tracking logic exists, LifeOS should recognize that Inbox Zero was
  reached and satisfy the Commitment automatically; I should not also have to check off a
  separate completion control.
- The automatically determined result should remain inspectable and correctable according
  to the scout's-honor behavior in GEN-5.
- If I do not reach Inbox Zero by the Commitment's deadline, it should use the same missed-
  Commitment tracking and reminder behavior as other Commitments.
- The Dashboard Inbox summary may show both the current Inbox count and the state of this
  Commitment without displaying the full triage queue.
- Open decision: the recurrence and deadline for the Inbox Zero Commitment.

**Ontology fit (Claude):**
- pending


### INBOX-3 — The Inbox provides prioritized, type-specific triage with a selected-item preview
Horizon:    Pilot
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Present the Inbox as a list with a selected-item preview. I should be able to scan the
  queue while seeing enough content and context to make a processing decision without
  opening every item in a separate window.
- Sort items by interpreted priority when LifeOS can determine a meaningful priority.
  Items whose priority cannot be determined must remain visible in a consistent fallback
  order rather than being hidden or treated as unimportant.
- When an item has no due date, other meaningful date, or determinable priority, sort it
  newest first.
- Do not add manual Inbox ordering, pinning, or priority overrides. The goal is Inbox Zero,
  so managing the queue's sort order should not become a workflow of its own.
- When priority is interpreted rather than manually declared, the preview should provide a
  concise explanation of why the item appears where it does when that explanation is
  available.
- Show the selected item's full content in the preview rather than an abbreviated snippet.
- Every editable item should provide an Edit action that opens the appropriate type-specific
  editor so I can modify its fields. Closing the editor should return me to the same Inbox
  context and refresh the preview with the saved changes.
- Editing an item, including changing its type or fields, must not resolve it or remove it
  from the Inbox. I must still choose an explicit resolution action such as Do, Defer,
  Delegate, File, or Drop.
- Show only the actions appropriate to the selected item's type. The screen should not
  present every theoretical Inbox action when an action would be invalid or meaningless for
  that item.
- **Do** should offer **Mark in progress** or **Mark completed** when those states make
  sense for the selected type. Other item types may expose a different type-appropriate Do
  action rather than being forced into Task-style states.
- **Defer** should support a specific date, a relative number of days, a relative number of
  minutes or hours, **Someday/Maybe**, or open-ended **Postponed** with no date.
- Relative defer choices should be fast to enter, while a specific date remains available
  when precision matters.
- **Delegate** is expected to be used less frequently because I am a solopreneur and will
  mostly delegate to AI agents. It should not dominate the primary triage controls.
- Delegation should allow me to select or update the responsible Person and add or update
  notes. The Person selector must support virtual AI agents as well as human people once AI
  delegation is available.
- After any action, remove the item from the active Inbox when that action resolves the need
  for triage, select the next prioritized item, and provide clear feedback about what
  happened.
- The Pilot should support keyboard-efficient processing. Up and Down arrow keys should move
  the selected item through the Inbox list, and Enter should activate the currently focused
  action or control.
- Keyboard behavior must not bypass required confirmation, including the confirmation for
  Drop.
- Open decision: the phase in which delegation to virtual AI agents first becomes available.

**Ontology fit (Claude):**
- Maps to: composes INBOX-1's **triage marker** with type-specific actions. The GTD exits
  reuse existing verbs — Do = promote→Task / `state_change`; Defer = `attributes.due` (+ the
  Defer variants below); Delegate = an owner link to a `Person` (incl. virtual AI agents as
  People); File = `bsk relate`; Drop = the triage-marker Drop outcome.
- Priority ordering = the diagnostics / derived layer (due-ness, neglect, breach-risk), with
  a newest-first fallback when no priority is computable — a read-only ranking, no stored
  order (matches "no manual pinning").
- Kernel delta: Defer needs richer date semantics — a specific date, relative (+N days /
  hours), **Someday/Maybe**, and open-ended **Postponed (no date)** — likely a small
  `defer_state` attribute alongside `due`. Virtual-agent People = `Person` subjects flagged
  as agents.
- Implications: editing an item (even changing its type) must **not** clear the triage
  marker — only an explicit Do / Defer / Delegate / File / Drop resolves it.
- Open decisions: Defer storage (one `defer_state` enum + date vs several attributes); when
  virtual-agent delegation first ships.


### INBOX-4 — Tagging and relating organize an item but do not resolve Inbox triage
Horizon:    Pilot
Definition: active
Build:      mapped

**Workflow (Chris):**
- Adding and removing Tags will be one of the most common actions while processing an Inbox
  item. The selected-item view should keep the Tag control readily available.
- Relating the item to another LifeOS object will also be a common action and should be
  available from the selected-item view without opening an unrelated screen.
- Tags and relationships must remain visually and conceptually separate, consistent with
  GEN-1: Tags classify an item, while relationships connect it to another object.
- Adding or removing Tags, or adding a relationship, does not by itself remove the item from
  the Inbox. These actions organize the item but do not answer what I intend to do with it.
- Removing an item from the Inbox requires an explicit GTD-style resolution decision such
  as Do, Defer, Delegate, File, or Drop.
- **File** means retain the item as reference material and resolve it out of the Inbox.
- Filing does not require the item to have a Tag or relationship. I may file an item as a
  standalone reference when no additional classification or connection is useful.
- **Drop** must ask for confirmation before resolving and removing the item from the Inbox.
  The Pilot should not rely on an immediate Drop action followed only by Undo.
- I should be able to organize the item first and then make the resolution decision without
  losing the selected item or the context I just added.

**Ontology fit (Claude):**
- Maps to: **Tag (GEN-1) and Relate (`bsk relate` / `link`) are organization, not
  resolution** — neither clears the triage marker. Only a GTD resolution (Do / Defer /
  Delegate / **File** / Drop) transitions the marker. File = retain as reference + resolve
  (a marker→resolved transition with a "reference" disposition, no promote required).
- Kernel delta: none beyond INBOX-1's marker + GEN-1 tags — this requirement is mostly a
  **constraint** on when the marker flips (organize actions must leave it set).
- Implications: cleanly separates classification (tags / relations) from triage resolution —
  the marker is the single source of "still needs a decision." Drop stays confirm-gated.
- Open decisions: whether "File" records a distinct reference disposition on the resolved
  marker, or is just Drop-without-discard.


### REVIEW-0 — Review sessions are recurring commitments with a consistent sequence
Horizon:    Pilot phase 2
Definition: active
Build:      needs-kernel

**Workflow (Chris):**
- Daily, weekly, and monthly review sessions are commitments I make to myself, not optional
  informational reminders in a separate notification system.
- Their schedules, reminders, due state, and missed state should use the normal Commitment
  tracking experience. A due or missed review should therefore surface wherever other due
  or missed Commitments surface.
- Review-Commitment adherence should remain backfillable and correctable according to
  GEN-5, with the original timing and correction retained in history.
- Daily, weekly, and monthly reviews should be distinct guided experiences suited to their
  different time horizons. They should not merely be the same form with a different title.
- The review types should still share useful reflection prompts where appropriate, including
  **What worked well?** and **What do I want to do differently?**
- If multiple review cadences fall on the same day, they should be completed in this order:
  daily first, then weekly, then monthly. The UI should show this order clearly and guide me
  to the next review without combining them into one session.
- Within every review session, reflection on the previous period comes before planning the
  next period. Once I reach planning, I may move backward to revise a reflection response,
  but the default forward flow should preserve reflection -> planning.
- Completing reflection should lead directly into the appropriate planning step rather than
  returning me to an unrelated screen between the two parts.
- Planning should combine two sources: lessons from the period just reviewed and direction
  from longer-horizon Goals and the current Primary focus.
- Goals are generally multi-week or annual outcomes rather than special daily or weekly
  objects. Focus determines which Goals should be prominent for the current period, while
  Projects, Tasks, Commitments, and Decisions are the primary ways I make progress toward
  those Goals.
- Goal target dates still matter at shorter horizons. A Goal whose target date lands within
  the upcoming week should be surfaced during weekly review and planning.
- The planning UI should keep the relevant prior-period learnings and higher-horizon Goals
  available while I make decisions, so I do not have to remember or repeatedly navigate
  between them.
- This is an iterative planning aid rather than an automatic cascade. I decide what to carry
  forward, change, add, or leave unchanged.
- Review sections and questions should guide me, not act as hard validation rules. I decide
  when the review has served its purpose and explicitly mark the session complete.

**Ontology fit (Claude):**
- Maps to (D3 + D8): a Review is a **subject with a mutable body + an append-only edit trail**
  (D3) — a **new subject type** — whose *scheduling, due, and missed* behavior **composes the
  shared recurrence + missed mechanism (D8)** rather than being a Commitment (the god-type
  resolution). The reflection→planning sequence, the daily→weekly→monthly ordering, and
  "guide, don't hard-validate" are app-side flow. Planning reads Goals + Primary focus (D6).
- Kernel delta (needs-kernel): the **Review subject type** + its **mutable-body-plus-edit-trail**
  storage (D3), and review scheduling / missed via D8.
- Implications: reviews surface as due / missed wherever other recurring obligations do (via
  D8), yet their content is an editable document (D3) — the two halves are deliberately
  different mechanisms. Adherence is backfillable / correctable (GEN-5).
- Open decisions: none beyond D3 / D8 details.


### REVIEW-1 — I want to perform daily reviews
Horizon:    Pilot phase 2
Definition: active
Build:      mapped

**Workflow (Chris):**
- A daily review should be created automatically at the start of every day and remain
  available throughout that day.
- Each daily review should contain one editable working document, not an append-only series
  of timestamped entries. I should be able to use that document for stream-of-consciousness
  journaling throughout the day.
- This editable daily-review document is intentionally different from the append-only
  subject journals described by JOURNAL-1.
- The UI should make the current day's document easy to reopen and continue editing without
  starting a separate review or journal entry.
- The review becomes due at the end of the day. It does not need a specific clock time;
  end-of-day is the meaningful deadline.
- The end-of-day experience should begin with a comprehensive recap of everything recorded
  about my activity during the day. This should be based on the available database records,
  not only on items I manually remembered to add to the review.
- The initial recap can present the recorded activity directly. In later phases, AI agents
  may summarize it, identify patterns, or add useful interpretation, but the underlying
  recorded activity should remain accessible.
- Activity and change history should follow the chronological behavior in GEN-4. Advanced
  history filters are deferred to Production.
- The initial daily review should then provide a guided journaling and reflection workflow
  rather than an automatically scored performance report.
- The initial predefined reflection questions are **What worked well?** and **What do I
  want to do differently?** Each question should have a clear response area.
- The questions should make it easy to think through the day without requiring me to
  design the review from scratch each time.
- The session should also include a planning area within the review itself where I can
  reassess my Primary focus and daily objectives and write down the next items that need
  attention. Projects and Tasks are the main work items, but Habit occurrences and
  Appointments are also relevant to planning.
- The planning area is not an embedded replacement for the normal Project, Task, Habit, or
  Appointment editors. For now, I will separately create or modify those items as needed.
- The planning workflow should be flexible rather than forcing every day through the same
  set of edits.
- The next day's plan should be informed both by what I learned during the current day and
  by the current Focus, prominent Goals, and the nearer-term Projects, Tasks, Commitments,
  and Decisions that advance them.
- Once the corresponding weekly review exists, the daily review should provide a link to
  that weekly-review document.
- Clearing the Inbox is not a required step inside the daily-review flow; Inbox Zero is a
  separate recurring Commitment described by INBOX-2.
- Primary focus does not need to change during a review; keeping the current focus should
  be an explicit, low-friction choice.
- The review should feel like one coherent session, with visible progress through the
  predefined questions and a clear completion action.
- I should be able to move backward and revise an answer before completing the review.
- I should be able to explicitly complete the review whenever I feel it is complete. The
  UI should not require every question to be answered or every planning field to contain a
  value before enabling completion.
- A completed daily-review document should remain editable afterward. Post-completion edits
  should be retained in history and shown chronologically according to GEN-4 rather than
  silently replacing the earlier completed version.
- Automatic scoring, automatic summaries, and formal daily-objective outcome categories
  are extra credit and are deferred for later definition.
- A daily review cannot meaningfully be deferred because the details will be forgotten. If
  it is not completed by its end-of-day deadline, it should become a missed Commitment.
- After a review is missed, I should be able to record an optional reason for missing it.
- Open decision: whether responses are plain text only or also support dictated input, and
  how the comprehensive activity recap is grouped in the initial WinForms UI.

**Ontology fit (Claude):**
- Maps to: a **daily Review subject** (D3), **lazily created** on first access each day (no
  scheduler in the Pilot). One editable body = the mutable-body-plus-edit-trail (D3); the
  end-of-day **recap** is a read over the day's events (GEN-4). Reflection questions + the
  planning area are fields within the body. Due at end-of-day; missed via D8; the link to the
  week's Review is a relation between Review subjects.
- Kernel delta: none of its own — rides D3 (Review type + edit trail), D8 (missed / backfill),
  GEN-4 (recap reads), D6 (focus / objectives in planning). Lazy creation is app-side.
- Implications: the daily Review's editable body is explicitly *unlike* append-only Journals
  (JOURNAL-1) — that contrast is the whole reason for D3. Post-completion edits are just more
  edit-events (still editable, history retained). "Can't defer a daily review" is an app rule;
  an uncompleted one becomes missed (D8).
- Open decisions: plain text vs dictated responses; recap grouping (both app-side).


### REVIEW-2 — I want to perform weekly reviews (every Sunday)
Horizon:    Pilot phase 2
Definition: active
Build:      mapped

**Workflow (Chris):**
- The weekly review should be created on Sunday and should normally be completed on Sunday.
  It does not need to exist as an editable document throughout the preceding week.
- Until the weekly review is created, I record real-time thoughts in the daily reviews. The
  weekly review should not create a second running document earlier in the week.
- The weekly review should use one editable document rather than a collection of separate
  documents. Guided reflection and planning can appear as sections within that document.
- It should combine reflection on the prior week with planning for the upcoming week.
- Begin with a comprehensive weekly summary. Initially include links to every daily review
  from the week, completed and overdue Tasks, Project progress, Habit adherence,
  Appointments, missed Commitments, Inbox history, and any other recorded weekly activity.
  It is preferable to begin comprehensively and trim the summary later through real use.
- Links to daily reviews should open the corresponding review without losing the weekly
  review session. The linked daily reviews should also link back to this weekly review once
  it has been created.
- Include the common reflection questions from REVIEW-0 plus questions specific to weekly
  progress, patterns, and upcoming priorities.
- Weekly planning should begin by asking whether I want to keep or change my Primary focus
  based on what I learned during the prior week.
- Focus determines which of my existing longer-term Goals are most prominent for the week;
  there is no separate weekly-cadence Goal concept.
- Any Goal whose target date falls within the upcoming week should be called out even if it
  was not otherwise selected as prominent through Focus.
- After the focus decision, I should identify the important Projects for the next week,
  then schedule or plan other Tasks and Appointments. Commitments and Decisions may also be
  relevant because they help advance Goals alongside Projects and Tasks.
- The next week's plan should be informed both by learnings from the prior week and by the
  applicable longer-horizon Goals.
- Primary focus may persist unchanged across weeks; keeping it should be an explicit,
  low-friction choice rather than forcing a change.
- If Sunday ends without completion, the weekly review becomes a missed Commitment with no
  grace period. I should still be able to backfill and complete it late; doing so must not
  erase the fact that the original Commitment was missed.
- A completed weekly-review document should remain editable afterward. Post-completion
  edits should be retained and presented through the chronological history behavior in
  GEN-4.
- A partially written Sunday review should be preserved if I close LifeOS before explicitly
  completing it, so I can reopen the same document and continue later.
- Open decision: the remaining weekly-specific reflection questions.

**Ontology fit (Claude):**
- Maps to: a **weekly Review subject** (D3), created on Sunday; same mutable-body model. The
  **weekly summary** is a read / rollup over the week (daily Reviews, completed / overdue Tasks,
  Project progress, Habit adherence, Appointments, missed Commitments, Inbox history) — all
  reads over already-mapped data. Daily↔weekly links are relations between Review subjects.
  Weekly planning reads Goals + Primary focus (D6), surfacing Goals whose target date lands in
  the week.
- Kernel delta: none of its own — rides D3, D8, D6, GEN-4, and the per-type data it summarizes.
- Implications: no second running document mid-week (only daily Reviews accumulate); the weekly
  Review is created and normally completed Sunday. Missed with no grace period, but backfillable
  (D8 / GEN-5).
- Open decisions: the weekly-specific reflection questions (content, app-side).


### REVIEW-3 — I want to perform monthly reviews with an end-of-month completion window
Horizon:    Production
Definition: active
Build:      mapped

**Workflow (Chris):**
- The monthly review should be created on the last calendar day of the month.
- It may be completed on the last day of the reviewed month or during the first three
  calendar days of the following month. If it is still incomplete after that window, it
  becomes a missed Commitment.
- It should combine reflection on the prior month with planning for the upcoming month.
- The monthly review should use one editable document, consistent with the daily and weekly
  review model. Guided reflection and planning can appear as sections within it.
- Begin with a summary that links to every completed weekly review covering the month.
- Because a month may end partway through a week, also identify and link to any daily reviews
  within the month that are not covered by one of those completed weekly reviews. The UI
  should avoid omitting those days or presenting their activity as though it was already
  summarized by a weekly review.
- The monthly summary may include the other recorded activity used by shorter reviews, but
  should use the completed weekly reviews as its primary roll-up where coverage exists.
- Include the common reflection questions from REVIEW-0 plus questions specific to monthly
  progress, patterns, and upcoming priorities.
- Monthly planning should begin by reassessing whether to keep or change my Primary focus.
- It should then review progress toward long-term Goals, identify the Goals and Projects
  that should be prominent during the upcoming month, review important target dates, and
  account for realistic capacity.
- Tasks, Commitments, Decisions, Appointments, and Habits may also inform the plan where they
  materially affect progress or capacity.
- This is an initial comprehensive scope that should be refined through actual use rather
  than treated as a permanently fixed checklist.
- Primary focus may persist unchanged across months; the review should not force a change.
- A partially written monthly review should be preserved if I close LifeOS before explicitly
  completing it, so I can reopen the same document and continue later.
- A completed monthly-review document should remain editable afterward. Post-completion
  edits should be retained and presented through the chronological history behavior in
  GEN-4.
- If the review becomes missed after its three-day completion window, I should still be able
  to backfill it later. Backfilling must not erase the fact that the original Commitment was
  missed or that the review was completed late.
- Open decision: the remaining monthly-specific reflection questions.

**Ontology fit (Claude):**
- Maps to: a **monthly Review subject** (D3), created on the last day of the month, completable
  through the first three days of the next (the grace window = an app / D8 rule); same
  mutable-body model. The summary primarily rolls up the month's **completed weekly Reviews**,
  plus any daily Reviews not covered by one — a coverage read over Review subjects and their
  date ranges. Monthly planning reads long-term Goals + Primary focus (D6).
- Kernel delta: none of its own — rides D3, D8, D6, GEN-4.
- Implications: the "which daily reviews aren't covered by a weekly review" gap-check is a
  date-range read, not new storage. Production horizon.
- Open decisions: the monthly-specific reflection questions (content, app-side).


### REVIEW-4 — I want to perform yearly reviews (EOM, give or take a day or 2)
Horizon:    Someday Maybe
Definition: drafting
Build:      unmapped

**Workflow (Chris):**
- App captures mic audio for dictated note.

**Ontology fit (Claude):**
- pending


### REVIEW-5 — Habits should be recordable and reviewable during daily, or weekly reviews
Horizon:    Pilot phase 2
Definition: active
Build:      mapped

**Workflow (Chris):**
- Habit streaks should be trackable.  I should be able to record whether I stuck with that habit or not.  The UI will show a small grid that renders a box for each time period I followed the habit.
- Support 3 states per habit record:  Habit followed, Habit not followed, and partial credit
- During daily and weekly reviews, each applicable Habit occurrence should offer the same
  one-click recording control used by Dashboard/Today and Habit detail.
- A daily occurrence remains visibly unrecorded until end-of-day, and a weekly occurrence
  remains unrecorded until end-of-week; the review must not present it as a failure
  prematurely.
- Each state in the history grid should be visually distinguishable, including unrecorded,
  followed, not followed, and partial credit.
- Selecting followed, not followed, or partial credit should update the review and Habit
  history immediately, with an optional note available for additional context.
- Partial credit breaks the normal binary streak but remains visible as its own historical
  result so a more granular adherence score can be added later.
- I should be able to correct or backfill an occurrence directly from a review. Doing so
  should recalculate the visible streak and adherence summary while preserving the change
  in chronological history.
- Reviews should distinguish manually self-assessed occurrences from automatically scored
  objective occurrences without making the manual path feel secondary or less trustworthy.

**Ontology fit (Claude):**
- Maps to: the **Habit occurrence recording** from GEN-3, surfaced inside daily / weekly
  Reviews — the same one-click followed / not-followed / partial control writing the same
  adherence events, and the history grid is the Habit streak projection (GEN-3). Correct /
  backfill from a review = a corrective adherence event (GEN-5).
- Kernel delta: none of its own — rides GEN-3 (habit occurrences + partial credit + streak
  projection) and GEN-5 (correction). Purely a second surface onto the same data.
- Implications: recording a habit in a review and on the Dashboard are the **same** occurrence —
  consistency comes from both reading / writing the one adherence stream (GEN-3).
- Open decisions: none blocking.



---

## Cross-cutting ontology decisions

Foundational modeling calls that many requirements inherit. Decided directions are locked;
open sub-points are flagged. Owned by Claude; set with Chris. (Locked 2026-09-01.)

**D1 — Area of Focus = a durable subject.** "Areas" (Trading, Family, Dev Career) are
first-class subjects with a master list, that Goals/Tasks/Projects point to; they appear as
a node/filter in the BROWSE-1 graph. Open: the item→Area link — a dedicated attribute vs a
`serves`/membership relation (a relation slightly bends the "Areas are a property, Tags are
separate" framing).

**D2 — Habit = a UI abstraction over Commitment (storage).** Habit is the user-facing
concept; the underlying data model is a Commitment. Mechanics verified-compatible: followed
= an event `evidences` it, not-followed = `violates` it; streak / adherence-over-a-window =
the breach query + a date filter (the kernel already names this as the intended extension);
recurrence rides `expected_cadence` + the neglect clock; cue/routine/reward are attributes;
manual-vs-auto is provenance. **Two strain points:** (a) **partial credit** is a third
adherence state the binary `evidences`/`violates` vocabulary can't express — a real kernel
gap; (b) Commitment's framing is **confrontational** (breach = "a line you drew and
crossed"), while a Habit wants a gentle streak/scout's-honor tone — an app-layer
presentation concern. **Resolved (2026-09-01):** partial credit = **fixed half-credit** — a
three-state record (done / half / not-done), gated per-habit by an `allows_partial` flag
(binary habits like *Brush Teeth* stay two-state; malleable ones like *tidy the room* allow
half). Missed occurrences read as a **gentle streak break**, never surfaced in the
confrontational breach report. Evolve the ontology only if later strain exceeds these.

**Revised (2026-09-13) — Habit gets its own type.** Given the god-type concern (see D8),
Habit moves from *is a Commitment* to **its own subject type that composes the shared
adherence / recurrence mechanism (D8)** rather than being stored as a Commitment. The
partial-credit + gentle-framing resolution above is unchanged, but now holds *by
construction*: Habit's distinct workflow (streak, partial credit, cue / routine / reward)
and its exclusion from the breach report fall out of it being a separate type, not
special-casing inside Commitment. Supersedes the earlier lean toward Habit = Commitment.

**D3 — Review = a subject with a mutable body + an append-only edit trail.** Reviews are
editable working documents (unlike append-only JOURNAL-1). Store the body as a subject
attribute; append each save as an edit event so "history retained" holds; the app presents
one document. Reconciles editable-doc UX with the append-only spine.

**D4 — Capture bifurcates: reference-captures are events, thinking-captures are subjects.**
*(Revised 2026-09-13, superseding the earlier "one flavor model".)* Newer requirements split
capture by kind: **note / document / URL** land as **events + artifacts** (reference-ish
captures — CAP-4 / CAP-5), while **idea / problem** create their **durable subject
immediately** on capture (CAP-6 Idea with Status New; GEN-14 / CAP-1 Problem), flagged for
Inbox triage. Consequences: (a) **"promote" splits** — event→subject (a `note` → an Idea)
vs subject→subject (an Idea → a Goal, per CAP-6, which relates the source Idea and sets it
Promoted); (b) the **triage marker (INBOX-1) attaches to subjects too**, not only events
(INBOX-1's "item = subject or event" already allows this). CAP-1's older Ontology-fit
(leaning "idea = flavored note") is now **stale** and will be re-mapped. Open: whether
`note` keeps any flavor tag once idea / problem are their own subjects.

**D5 — The recurring-commitment engine is derived from the UIs, not decided top-down.**
Reviews (REVIEW-0..3), Inbox Zero (INBOX-2), and Habits (GEN-3) are all "recurring
self-commitments with per-period satisfaction + missed detection + backfillable
correction." Let each UI settle, then extract the shared storage primitive — don't design
it first. *(See D8: the extracted primitive is a shared **mechanism** composed per-type,
not a Commitment base type.)*

**D6 — Primary focus & daily objectives model. RESOLVED (2026-09-14) — hybrid.**
- **Primary focus = two levels, both Goal-linked (resolved 2026-09-14):** a **Monthly focus**
  (set in the monthly review) and a **Weekly focus** (set in the weekly review). Each is a
  durable pointer to a **Goal** — not daily, not free text. Store each as a thin persistent
  setting; record changes as events so history falls out of the append-only log for free.
- **Soft alignment:** the Weekly focus should *usually* advance the Monthly focus, but this
  is **not enforced**. Because both point to Goals, alignment is **read from the existing
  alignment graph** (`serves` / `results_in`) — the UI can show the two foci are aligned when
  the weekly Goal advances the monthly Goal, and gently flag it when they aren't. No new
  relation type; it rides the graph we already have. Open detail: what counts as "advances" —
  a direct edge vs any path.
- **Daily objectives** = an **optional**, lightweight, *ephemeral* per-day list; each entry
  is either free text or a pointer to an existing item (Task / Goal). Not first-class
  subjects; no rich daily history required. Unfinished objectives at day's end become
  **Inbox items** (the triage marker, INBOX-1) — the only durable record they leave.
- Rationale (Chris, 2026-09-14): no need to browse daily-objective history; objectives must
  stay optional and low-friction; Primary focus is a review-cadence, Goal-linked thing.
- **Possible alignment (check during mapping):** a weekly/monthly Goal-focus resembles the
  kernel's existing `focus` / Season axis (a time-boxed focus that parks out-of-focus work);
  Primary focus may *be* a lightweight Season rather than a new mechanism.
- Sub-point resolved (2026-09-14): **two foci** — separate Monthly and Weekly, with the soft
  alignment described above. D6 is fully settled.
- **Reconcile:** TODAY-1 still allows Primary focus as free text / set on the Today screen —
  Chris to align those bullets with "Goal-linked, review-set" when convenient.

With this resolved, DASHBOARD-1 / TODAY-1 / TODAY-2 / REVIEW-* can now be mapped.

**D7 — Per-type status vocabularies + terminal classification.** Each subject type now
carries its own status set, moved by `state_change` events: Goal (developing → Active →
Completed / Abandoned), Project (same shape), Task (Not started / In progress / Waiting /
Completed / Cancelled), Commitment (Open / Fulfilled / Missed / Cancelled), Decision (Open /
Implementing / Cancelled / Closed), Problem (Open / Working / Resolved), Appointment
(Scheduled / Completed / Cancelled / Missed), Idea (New / Promoted / Rejected). **Identity
Statement has no status** (it uses archive, D9). The diagnostics depend on which statuses
are **terminal** — neglect skips terminal subjects via `is_terminal_status` (migration
0008); breach is Commitment-only. Deliverable: a documented per-type status map + terminal
set, and an extended `is_terminal_status`. Open: enforce the vocabularies as kernel enums
vs validate them app-side.

**D8 — Recurrence + adherence is a shared *mechanism*, not a base type (resolves the
Commitment god-type).** Chris confirmed Commitment is too abstract to be a first-order
concept — too many different-workflow things ride it (which is why productivity apps don't
surface "commitments"). So we do **not** make Commitment the universal recurring base. What
is shared is *mechanism*, not identity: (1) a **recurrence representation** richer than
today's `expected_cadence` — it must express calendar-anchored patterns (*every Sunday*,
*last calendar day of month*) as well as intervals; (2) **adherence / fulfillment recorded
as events** that `evidences` / `violates` the subject; (3) **missed-detection + backfillable
correction** (the old D5 "engine"). Habit, recurring Appointments, Reviews (for
scheduling / missed-tracking), and Commitment-the-promise each **compose** this mechanism as
their own type. **Commitment (GEN-12) shrinks to its natural meaning — a promise to self or
others** — and is never surfaced as a universal abstraction. Diagnostic *framing* stays
per-type: breach only for promises, a gentle streak for Habit, a missed-review nudge for
Reviews.

**D9 — Universal archive / active flag; nothing is ever deleted.** Chris: never delete —
only toggle inactive / archived. So archive is a **cross-cutting flag on every item**,
orthogonal to workflow status, that **hides it from all default views**, is **reversible**,
and **never removes data** (append-only: archive / restore is a recorded state change).
Because it is orthogonal to status, **status-less types (Identity Statement) can still
archive**. Applies uniformly to subjects, and to events where meaningful. Replaces the
per-type archive handling scattered across GEN-11 / GEN-15 / CAP-6. Exceptions: Areas are
declared permanent (GEN-2) — they just become less prominent, not archived. Open: one
boolean vs a small lifecycle state. (Resolved: a "Rejected" Idea is a terminal *status*, not
this flag — terminal-status hiding and archive hiding are separate mechanisms that both drop an
item from default views.)

**D10 — Appointment = a new durable subject type** (net-new, alongside Area — the pilot
pushes the ontology from 11 → 13 types). Time attributes (date / start / end / all-day /
location / meeting link), attendees = `Person` relations, recurrence via D8. **Divergence to
note:** CAL-1 wants **materialized per-occurrence status** (each occurrence individually
Scheduled / Completed / Cancelled / Missed), unlike Habit's **projected** occurrences (D2).
Two different recurrence-instance models under one roof — deliberate, recorded here so it
isn't an accident.

### Method for the Habit ↔ Commitment discrepancy

"Habit" (UI concept) and "Commitment" (storage type) are deliberately decoupled — Chris
never reasons in storage terms. Chris specifies the Habit UI fully; Claude owns whether/how
it lands on Commitment. Data mechanics are verified-compatible (D2); the open strain is
partial-credit + framing. If richer UI outgrows Commitment, evolve carefully (Habit as a
Commitment specialization, or its own type sharing the recurrence+adherence machinery) —
the pilot's purpose is to evolve the ontology to fit real workflows, not force-fit them.

**Resolved (2026-09-13):** took the *own-type* path — Habit becomes its own subject type
(revised D2) and Commitment stays narrow (D8). The decoupling principle still stands for
every future type: a UI concept is not a storage type, and shared behavior is composed as a
mechanism, never inherited from a god-type.

---

## Kernel build backlog

The roll-up of every `needs-kernel` delta above — the authoritative feeder for ontology
work. One line per item; details live in the requirement.

- **Triage marker primitive** (INBOX-1) — a flag that asserts inbox membership + a `Drop`
  outcome + a `v_inbox` projection (flagged AND not resolved). Source-agnostic; attaches to
  subjects and events (D4).
- **Tag primitive** (GEN-1) — a tag store separate from relations + a `bsk tag` verb + a
  live "tag universe" reader; reconcile with the existing reserved `focus` attribute.
- **New subject types** — Area (GEN-2 / D1), Appointment (CAL-1 / D10), Habit (revised D2),
  Review (D3 / REVIEW-0): the pilot grows the model **11 → ~15 types**; plus master-list /
  readers and `attributes.area` on items.
- **Habit subject type** (GEN-3 / revised D2) — its own type composing the shared
  adherence / recurrence mechanism, **no longer stored as a Commitment**.
- **Per-type status vocabularies + terminal classification** (D7) — a documented status map
  + an extended `is_terminal_status`.
- **Recurrence representation** (D8) — richer than `expected_cadence`; calendar-anchored
  patterns (every Sunday, last day of month) + intervals; shared by Habit / Appointment /
  Commitment / Review.
- **Shared adherence + missed / backfill mechanism** (D8, was the "recurring engine") —
  `evidences` / `violates` events + missed-detection + correction, composed per-type with
  per-type diagnostic framing.
- **Universal archive / active flag** (D9) — cross-cutting hide-from-default-views flag;
  reversible; never deletes; orthogonal to workflow status.
- **Capture bifurcation** (D4) — note / document / url as events; idea / problem as
  subjects-on-capture; split the `promote` paths (event→subject vs subject→subject).
- **Atomic create-and-link + parent→child relation map** (GEN-7) — a one-transaction create +
  parent edge so parent-first creation is all-or-nothing (no orphan on failure); canonical
  map: Goal→Value `serves`, Project→Goal `results_in`, Task→Project/Goal `serves`.
- **Managed binary artifact storage** (CAP-2 / CAP-4 / JOURNAL-2) — blob / file storage for
  audio, attachments, and inline media; today's artifact table holds text only.
- **`voice` write path** (CAP-2) — a `bsk` verb that writes `voice` events (closes one of
  the 4 unreachable event kinds).
- **Partial-credit adherence** (D2 / GEN-3) — a third state (**fixed half-credit**) beyond
  `evidences` / `violates`, gated per-habit by `allows_partial`.
- **Habit occurrence + streak projection** (GEN-3) — a projection over recurrence +
  adherence events; misses shown as a gentle streak break, excluded from the breach report.
- **Review subject type + mutable-body edit trail** (D3 / REVIEW-0) — a body attribute with
  each save appended as an edit event; scheduling / missed via the D8 mechanism.
- **D6 focus storage** (TODAY-1) — current Monthly + Weekly Primary-focus Goal pointers with
  change history (focus-set events or a lightweight Season); daily objectives stay app-local,
  materializing into Inbox items only when unfinished.
- **Materialized recurring occurrences** (CAL-1 / D10) — each Appointment occurrence is its own
  status-bearing record (unlike Habit's *projected* occurrences).
- **People-association link** — a non-alignment way to attach a `Person` (involves / attendee /
  owner / assignee / waiting-for), recurring across CAL-1, GEN-12, INBOX-3, GEN-10; lean: a
  Person-ref attribute for the Pilot.

*(Prior mapping gaps already closed: the `concerns` write path — shipped as `bsk relate`.)*
