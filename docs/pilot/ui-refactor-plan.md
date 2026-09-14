# Pilot WinForms UI — Refactor & Build Plan

A self-contained plan for building out the LifeOS Pilot WinForms UI on top of the
now-complete kernel. Written for an agent picking this up cold: read this top to
bottom, then read the two source-of-truth docs it points at before writing code.

> **This plan owns the UI/workflow half only.** The kernel (schema, `bsk` verbs,
> reader views) is built and tested — do not change it to make a screen easier. If the
> UI genuinely needs a capability the kernel lacks, stop and raise it; the ontology is
> evolved deliberately, not bent to a screen (that is the whole method — see the tenet).

## 0. Read these first

- **`docs/pilot/ui-requirements.md`** — the requirement list. Each requirement (IDs like
  `NAV-1`, `GEN-6`, `TASKS-1`) has a **Workflow (Chris)** half that is the authority on
  what a screen must let the user *do*. Build to that. The **Ontology fit (Claude)** half
  tells you which verbs/views back it.
- **`docs/pilot/kernel-build-plan.md`** — what the kernel now provides (phases 1–12, all
  done). The "Kernel build backlog" there maps features to migrations.
- Skim the ontology at `docs/ontology.md` for the vocabulary (subject types, relations,
  provenance) if a term is unclear.

## 1. What the Pilot is (and the one tenet)

A **throwaway WinForms app** whose only purpose is to live-test the ontology against real
daily use. Optimize for speed of iteration and for exercising the model, not polish.

**The tenet: the UI model is not the storage model.** The most intuitive workflow and the
most efficient storage frequently diverge, and the **app layer translates**. It is correct
for a screen to present X while the store holds Y and reconstructs X on read. Never make
the user think in storage terms; never bend the store to mirror a screen.

## 2. Architecture (do not violate)

- **Many readers, one writer.** The Pilot **reads** PostgreSQL directly through the
  read-only `bsk_reader` role over the flattened `bsk.v_*` views. It **writes** only by
  shelling out to the `bsk` CLI (invariant 9 — `bsk` is the sole write path). There is
  **no `ProjectReference`** from the Pilot to the kernel, and there must never be one.
- Reads live in `src/LifeOs.Pilot/Reader/SubjectReader.cs` (plain Dapper SQL over the
  views; `DefaultTypeMap.MatchNamesWithUnderscores = true`, so `snake_case` columns map to
  `PascalCase` properties). Read models are in `Reader/ReadModels.cs`.
- Writes go through `src/LifeOs.Pilot/Cli/BskCli.cs` — `Run("verb", "arg", …)` returns
  stdout or throws `BskException` (the CLI's plain-text error) on non-zero exit. Add
  `--json` and parse stdout when you need structured results back (e.g. a new subject's
  urn); today most calls rely on plain text + a reload.
- Status is event-driven: after a `status`/`archive`/adherence write, the derived
  projection updates on the next read for view-folded values, but the materialized
  `subject_current` needs `bsk rebuild`. The Pilot already runs `bsk rebuild` after a
  status change to hide that seam — keep doing that where a view reads `v_subject_current`.
  (Views that fold from source — `v_inbox`, `v_habit_*`, `is_archived`, appointment status
  via `subject_current_source` — are always fresh and need no rebuild.)
- **Dapper + `date` columns:** reading a bare SQL `date` into a scalar `DateTime`/`DateOnly`
  throws or mis-maps with the pinned Dapper. Select dates as text (`to_char(d,'YYYY-MM-DD')`)
  for scalar reads, or map onto a `DateOnly`/`DateTime` **property** on a POCO (that works).
- **Build/run:** `./run.ps1` builds and launches; `./test.ps1` runs the kernel test suite
  (the Pilot itself has no unit tests — verify by building clean under
  `TreatWarningsAsErrors` and running the app). The Pilot finds `bsk.exe` via `BSK_EXE` or
  the repo build output; the reader connection comes from `BSK_READER_CONNECTION_STRING`
  or the local `bsk_reader` default.

## 3. Kernel API surface (what you build on)

**Write verbs** (`bsk <verb> …`; all support `--json`):

| Verb | Use |
|---|---|
| `new <Type> "title" [--area u] [--parent p [--relation r]] [--attr k=v]…` | Create a subject; `--parent` creates + links atomically (GEN-7); Problem reuses by title; Problem/Idea auto-flag to inbox |
| `promote <source> <Type> "title"` | Split promote: `source` = event id (capture→subject) or subject ref (Idea→work via results_in); resolves the source's triage |
| `set <subject> k=v …` | Edit attributes (blank value removes the key); never status |
| `status <subject> <status>` | Move status by a state_change event (then `bsk rebuild`) |
| `archive <subject>` / `restore <subject>` | D9 archive flag (Areas refuse archive) |
| `tag <item> --add x --remove y` | Tags on a subject or event id (classification, not relations) |
| `link <from> <relation> <to>` | subject→subject alignment edge (serves/results_in/supersedes) |
| `relate <event-id> <subject> [--as concerns]` | event→subject edge (file a capture) |
| `flag` / `drop` / `file <item>` | Inbox triage marker transitions (INBOX-1) |
| `recur <subject> --freq …` / `--clear` | Set structured recurrence (daily/weekly/interval/monthly/trigger) |
| `adhere <habit> followed|partial|missed [--on date] [--note]` | Record a habit occurrence |
| `involve <subject> <person> --role <role> [--remove]` | People-association (attendee/owner/assignee/waiting_for/involves) |
| `materialize <series> --through <date>` | Create a recurring appointment's occurrence subjects |
| `capture "text"` / `journal` / `ideas` / `log` / `decide` | Source capture + activity/decision helpers |

**Reader views** (`bsk.v_*`, all `SELECT` for `bsk_reader`):

| View | What it gives the UI |
|---|---|
| `v_subject` | Every subject flattened + `archived`, `area`, `statement`, `attributes` |
| `v_subject_current` | Folded current status (materialized; needs `bsk rebuild`) |
| `v_subject_relation` / `v_subject_event` | Alignment edges / event→subject edges, with urns+types |
| `v_area` | Areas master list (name/description/notes) |
| `v_item_tag` / `v_tag_universe` | Tag assignments / the live tag set + counts (autocomplete) |
| `v_inbox` | Flagged-and-unresolved triage items (subjects + events), with content |
| `v_habit` | Habit fields (cue/routine/reward/start/end/allows_partial/recurrence) |
| `v_habit_occurrence` / `v_habit_streak` | Derived occurrences (with state) / current streaks |
| `v_person_association` | Who is involved in what, and in which role |
| `v_appointment` | Appointments (one-time, series, occurrences) with inherited fields + status |

Helper SQL functions you may call in read queries: `bsk.is_archived(uuid)`,
`bsk.is_terminal_status(text)`, `bsk.recurrence_occurrences(recur,anchor,from,to)`,
`bsk.try_to_date(text)`.

## 4. Design conventions every screen must follow

- **NAV-1 — persistent top-tab shell.** Destinations: Dashboard, Inbox, Tasks, Projects,
  Goals, Habits, Reviews, Vision, Browse, Areas, People/Agents. **Open on Dashboard every
  launch** (never restore last tab). Wrap tabs onto rows when narrow (no overflow menu).
  Each tab **remembers its own filters/sort/selection across restarts**. Dedicated tabs and
  Browse presets are two views of the **same** subjects — never separate stores.
- **GEN-6 — global New** from the shell: pick a type, then a **type-specific** form (only
  that type's fields; switching type before save keeps still-applicable values). Save = one
  `bsk new` at the end; Cancel writes nothing. Tags + Relationships assignable at creation
  and later.
- **GEN-7 — parent-first creation:** a parent's detail screen offers "New child" limited to
  valid child types (Value→Goal→Project→Task; Task also under Goal), inferring the relation
  and using `bsk new --parent` so it's atomic (no orphan on failure).
- **BROWSE-2 — read-only-first detail:** selecting an item shows it read-only; editing is an
  explicit opt-in with explicit **Save/Cancel** (no per-field autosave). Dirty-nav prompt
  (Save/Discard/Cancel) when leaving with unsaved edits. Detail sections where applicable:
  Overview, Relationships, Tags, Journal, History.
- **GEN-1 — Tags ≠ Relationships.** Keep them visually and conceptually separate everywhere:
  "Tag" (classification, `bsk tag`, autocomplete over `v_tag_universe`) vs "Relate to"
  (`bsk link`/`relate`). Adding either during Inbox triage does **not** resolve the item
  (INBOX-4).
- **D9 — archive, never delete.** Archived items are hidden from default views; provide an
  **Archived filter** to view/restore. Use `bsk archive`/`restore`. (Areas are permanent —
  no archive/delete action.)
- **Status vocab is per-type** (see D7 in the requirements / `StatusVocabulary`): offer each
  type its own status set; treat a null folded status as the type's default (e.g. Task = Not
  started, Appointment = Scheduled). Completing/cancelling is an explicit status change.

## 5. Current state (already built)

- `MainForm` — a `TabControl` shell with **Browse** and **Inbox** tabs (needs expanding to
  the full NAV-1 destination set + open-on-Dashboard + per-tab state).
- `BrowseView` — 3-pane type-tree → subject list → detail, with New / Change status / Link /
  Set dates (attributes) / Relate, all shelling out to `bsk`.
- `InboxView` — **already cut over to `v_inbox`** (INBOX-1): lists flagged items (notes +
  Ideas/Problems), with Promote / Relate (captures) / File / Drop.
- Dialogs: `InputDialog`, `LinkDialog`, `PromoteDialog`, `ValueDialog`, `AttributesDialog`.
- `SubjectReader` (reads), `BskCli` (writes), `WindowPlacement` (persisted window state).

## 6. Phased UI plan

Each phase should build clean under strict warnings and be verified by running the app. Land
each in a small commit. Sequence is by dependency and by how much each unblocks.

**U1 — Nav shell + Dashboard (NAV-1, DASHBOARD-1).**
Expand `MainForm` to the full tab strip, open-on-Dashboard, tab-wrapping, per-tab remembered
view state (persist like `WindowPlacement`), and the dirty-edit prompt on tab switch. Build a
Dashboard that **composes reads**: Active Goals/Projects, due/overdue Tasks (`v_subject` +
dates), Inbox summary (`v_inbox` count), today's Habits (`v_habit_occurrence` + one-click
adherence). No inline editors — each summary links into its focused screen. *Foundation for
everything else.*

**U2 — Object-creation framework (GEN-6 + GEN-7).**
A reusable global **New** flow (type picker → type-specific form) and the parent-first
"New child" flow. Type-specific create/edit forms for the core types, each per its GEN-8…16
spec: Goal, Project, Task, Value (Identity Statement), Problem, Decision, Idea, Person, Area,
Habit, Appointment. Consume `bsk new [--parent]`. *Unblocks creating every type used below.*

**U3 — Shared detail building blocks (GEN-1, GEN-2, BROWSE-2 sections).**
A Tag control (autocomplete over `v_tag_universe`, `bsk tag`), an Area selector
(`v_area` → `attributes.area`), and a Relationships editor (`bsk link`, `v_subject_relation`)
— kept visually separate. Wire the BROWSE-2 detail sections (Overview / Relationships / Tags /
Journal / History) as reusable panels reused by Browse and every dedicated screen.

**U4 — Task/Goal/Project working views (TASKS-1, GEN-8/9/10).**
The dedicated **Tasks** tab: Overdue / Today / Upcoming / Unscheduled grouping (derived from
`due`/`scheduled` vs today), always-visible title-only quick-entry (`bsk new Task`), inline
Complete / In-progress (`bsk status`), filters (Status/Due/Area/Goal-Project/Tags). Dedicated
**Goals** and **Projects** tabs (filtered reads + the U2 forms + GEN-7 child grouping).

**U5 — Habits (GEN-3, REVIEW-5 recording surface).**
A Habit viewer: the streak/adherence grid (`v_habit_occurrence` states + `v_habit_streak`),
one-click followed/partial/missed (`bsk adhere`), a recurrence editor (`bsk recur`), the
cue/routine/reward form, and the ≥1-parent (Goal/Value) validation on save. Surface the same
one-click recording on the Dashboard.

**U6 — Areas + People/Agents (GEN-2, GEN-15).**
Areas master list + Area detail (grouped related Identity Statements/Goals/Projects/Tasks/
Habits, with prepopulate-Area on create-from-Area). People/Agents directory with All/Humans/AI
filter (a `person_kind` attribute), archive, and the person's grouped involvements
(`v_person_association`). Wire the People selector used by involve/waiting-for/attendees.

**U7 — Appointments (CAL-1).**
A calendar-style Appointment form (date/start/end/all-day/location/meeting-link/attendees via
`involve`/recurrence via `recur`), materialize recurring occurrences (`bsk materialize`),
per-occurrence status, and today's Appointments on the Dashboard. No dedicated calendar grid
in the Pilot (Dashboard + Browse suffice).

**U8 — Vision + archive UX polish (GEN-16, D9).**
Vision as a composed read (non-archived Identity Statements = "Who I am"; long-term Goals,
`target_date` > ~2y = "Where I am going"), each linking to its detail; persistent manual
ordering. Add Archived filters + archive/restore actions across the list views.

## 7. Out of scope here — blocked on kernel Band E

These UI requirements depend on kernel work **not yet built** (deferred to Pilot phase 2 /
Production). Do **not** attempt them in this refactor; they need their kernel piece first:

- **Reviews** (REVIEW-0…5) — needs the Review subject type + mutable-body edit trail (D3) and
  review scheduling/missed (D8 composition).
- **Today: Primary focus & daily objectives** (TODAY-1/2, D6) — needs focus storage.
- **Voice capture / documents / rich journals** (CAP-2/4, JOURNAL-2) — need managed binary
  artifact storage and the `voice` write path.
- **Inbox-Zero commitment** (INBOX-2) — still `drafting`.

Leave their tabs (Reviews, and the Dashboard focus band) as clearly-labelled placeholders.

## 8. Verifying UI work

The Pilot has no automated tests. For each phase: build clean under
`TreatWarningsAsErrors`, run `./run.ps1`, and exercise the new screen against a seeded dev DB
(`./scripts/seed.ps1`). Confirm writes land by re-reading (the view refresh) and by checking
`bsk` output. Keep each screen's reads in `SubjectReader` and writes in `BskCli` — never
open a write path around the CLI.
