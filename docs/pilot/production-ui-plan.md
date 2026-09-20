# Production Web UI — Plan

The forward-looking plan for the **Production** LifeOS user interface: a polished,
web-centric application that replaces the throwaway WinForms Pilot once the ontology has
survived daily use. This doc owns the **UI technology, structure, and design** half of
Production; it does not re-open the ontology (see [ontology.md](../ontology.md)) or the
kernel backlog (see [kernel-build-plan.md](kernel-build-plan.md)).

**Status:** planning. Production has **not** started — the WinForms Pilot
([ui-refactor-plan.md](ui-refactor-plan.md)) remains the live build. This is the target we
scaffold toward.

**Decisions approved by Chris 2026-09-20:** React + TypeScript frontend; a .NET Web API in
front of `LifeOs.Application`; **no PostgREST**; Supabase Auth; single repo; the streamlined
tree-first navigation and high-polish direction in §5.

## 0. Read these first

- [ontology.md](../ontology.md) — the vocabulary (subject types, relations, provenance) and
  the three-layer, single-write-path model the whole UI rests on.
- [ui-requirements.md](ui-requirements.md) — the requirement list. `Horizon: Production`
  requirements (NAV-2 back/forward, BROWSE-1 graph, JOURNAL-2 rich media, CAP-3 AI capture,
  REVIEW-3 monthly, GEN-0's "web-centric, polished" mandate) are this doc's charter.
- [ui-refactor-plan.md](ui-refactor-plan.md) §3 — the kernel API surface (write verbs +
  reader views) the web UI consumes. `production-ui-types.ts` (beside this doc) is the
  TypeScript mirror of it, for scaffolding.

## 1. The one architectural pivot — API in front of Application

Everything else follows from this. **Production replaces the Pilot's "shell out to
`bsk.exe` per click" with an in-process ASP.NET Core Web API that hosts the existing
`LifeOs.Application` services. The browser never touches Postgres and never touches a CLI —
it only talks to that API.**

```
Pilot (today):
  WinForms ── reads ──▶ Postgres (bsk_reader, direct connection)
     └────── writes ──▶ bsk.exe (process/click) ─▶ Application services ─▶ Postgres (owner)

Production:
  React SPA ── HTTPS + Supabase JWT ──▶ LifeOs.Api ─▶ LifeOs.Application services ─▶ Postgres
                                            │           (the SAME services bsk calls)
                                            └── reads: the SubjectReader view-SQL, server-side
  bsk.exe   ────────────────────────────────────────▶ Application services (kept: scripts/CI/power use)
```

### What "API in front of Application" means, plainly

The real logic already lives as **services** in `LifeOs.Application` (PromotionService,
SubjectService, …) — transport-neutral and DI-registered via `AddLifeOsKernel`. The Web API
is a **thin translator**: each endpoint (1) validates the Supabase JWT, (2) deserializes
input, (3) calls the matching Application service, (4) serializes the result. **No business
logic in the API itself** — it is a doorway, not a room. The `bsk` CLI already works exactly
this way ([Cli.cs](../../src/LifeOs.Cli/Cli.cs): commands are "thin adapters that resolve a
service, call it, and format the result"). The Web API is the CLI's twin, speaking HTTP
instead of `argv`. Both doors open into the same room (Application), which stays the only
thing that writes.

### The single write path is preserved, not weakened

Invariant 9's real intent is *"one governed write path where every rule is enforced"* —
append-only, provenance, promote-never-mutates, status-only-by-event, atomic
create-and-link. Today that path is `bsk` → Application. In Production it is `bsk` **and**
the API, both routed through the identical Application services, **never raw SQL**. The DB
triggers and CHECK constraints still backstop every caller. `bsk` does not retire; it
becomes the power-user/CI door alongside the web door — the README's "many doors, one
authoritative store."

### The browser stops reading Postgres directly

The Pilot's `bsk_reader`-over-views reads are great on a desktop LAN but can't cross to a
browser. The reader SQL in [SubjectReader.cs](../../src/LifeOs.Pilot/Reader/SubjectReader.cs)
is **directly portable**: it moves into server-side read endpoints on the API (which holds
the reader connection). The browser gets JSON and a real security boundary.

### Why not PostgREST / Supabase's auto-API

PostgREST (Supabase's auto-generated data API) turns tables/views into REST endpoints. It is
the wrong tool here:

- **It cannot be the write path.** Writing through it means the client issues INSERT/UPDATE
  against tables, skipping `LifeOs.Application` — where split-promote, capture bifurcation,
  atomic create-and-link, status-only-by-event, and per-type validation live. Recreating
  those as Postgres functions + triggers + RLS would rewrite tested C# in PL/pgSQL and
  discard the single governed path.
- **Splitting reads to PostgREST + writes to .NET isn't worth it.** Some reads are shaped
  app-side (Dashboard composition, Tasks Overdue/Today/Upcoming grouping, long-term-goal
  horizon, Vision ordering); pushing that into the DB to satisfy PostgREST moves logic the
  wrong way, and two API surfaces means two auth/caching/versioning/observability stories.

**One .NET API for both reads and writes.** One JWT-validation point, one generated client,
one deployment, one test suite.

### Supabase's role in Production

Supabase is used as a **backend-services provider, not the API gateway**: managed **Postgres**
(already, per [supabase-hosting.md](supabase-hosting.md)), **Auth** (GoTrue), and later
**Storage** (the answer to the "managed binary artifact storage" backlog for
CAP-2/CAP-4/JOURNAL-2). We do **not** use its PostgREST data API or Edge Functions. Supabase
Auth needs no PostgREST: the browser signs in via Supabase, receives a JWT, and sends it to
the .NET API, which validates it against Supabase's public keys (JWKS).

## 2. Stack

| Layer | Choice | Why it fits this app |
|---|---|---|
| **API** | ASP.NET Core (Minimal API/controllers) over `LifeOs.Application` | Reuse all Domain/Application/Infrastructure; one language for the governed path; OpenAPI out of the box |
| **Frontend** | **React + TypeScript + Vite** | Strongest ecosystem for the hard widgets *and* best AI-codegen surface |
| **Server state** | **TanStack Query** | Purpose-built for the "many readers" model: caching, background refetch, invalidate-after-write, optimistic updates |
| **API client + types** | **OpenAPI-generated** (`openapi-typescript` / NSwag / Kiota) | Server is the single source of truth for types — no hand-mirrored vocabulary drift |
| **UI kit / styling** | **Tailwind + Radix primitives (shadcn/ui)** | GEN-0's polish/aesthetics/accessibility; WAI-ARIA by default; components you own (ideal for iteration) |
| **Routing** | **TanStack Router** (type-safe) or React Router | NAV-2 Back/Forward + deep-linkable detail fall out for free |
| **Data-dense lists** | **TanStack Table** (headless) | Context-sensitive columns, filter combinators (AND-across / OR-within, BROWSE-2) |
| **Graph (BROWSE-1)** | **React Flow** (Cytoscape/Sigma if it scales large) | The Obsidian-style alignment graph the web unlocks |
| **Rich journals (JOURNAL-2)** | **TipTap** (ProseMirror) | Append-only maps cleanly: each entry immutable, a correction is a new entry |
| **Forms (GEN-6)** | **React Hook Form + Zod** | Type-specific create forms; app-layer gates (e.g. Goal needs target_date to activate) live in Zod |
| **Command palette / keyboard** | **cmdk** + global hotkey | INBOX-3 keyboard triage, global New, quick capture |
| **Auth** | **Supabase Auth** (JWT validated by the .NET API) | Already on Supabase; small, swappable decision |
| **Hosting** | API container (Azure Container Apps / Fly.io); SPA on Cloudflare Pages or served by the API | Supabase stays the DB |

## 3. Frontend framework decision

**React + TypeScript**, chosen over Blazor. Blazor's one real advantage — sharing Domain
vocabulary types directly, killing the `PilotVocab` drift — is **recovered by generating the
TS client from the API's OpenAPI schema** (server built from the Domain = single source).
React wins on exactly the axes Production prioritizes: polish, accessibility, the graph and
rich-text ecosystems, and AI-codegen quality. Blazor remains a defensible fallback if never
leaving C# outweighs the UI ceiling.

## 4. Repo & structure

Keep the existing solution; add two projects and one folder in **the same repo**:

```
src/
  LifeOs.Domain / Application / Infrastructure   (unchanged — reused as-is)
  LifeOs.Cli                                     (unchanged — stays the CLI door)
  LifeOs.Api        ← NEW: ASP.NET Core; references Application + Infrastructure;
                      writes via Application services, reads via ported view-SQL
web/                ← NEW: the React app; web/src/api is OpenAPI-generated in production;
                      seeded for scaffolding by production-ui-types.ts
```

Same repo so one PR spans an API endpoint and the UI that consumes it, and the generated
client stays honest. The `bsk migrate` schema-authority + Supabase staging/prod model is
untouched — the API is just another schema consumer.

## 5. Design direction

Two priorities distinguish Production from the Pilot and shape every scaffolding choice.

### 5.1 Highly polished — design system before features

- Real design tokens up front (color, type scale, spacing rhythm, radius, shadow, motion);
  light/dark; themed to an actual identity, not default gray.
- A **component gallery / kitchen-sink** built first: Button, Card, ListRow, `<SubjectDetail>`,
  Tag, StatusPill, DateChip, EmptyState, Toast, TreeNode. Polish holds when primitives are
  nailed in isolation and reused everywhere.
- Polish includes designed empty states (DASHBOARD-1 distinguishes "genuinely clear" from
  "failed to load"), skeleton loaders, optimistic updates, smooth transitions. Accessibility
  comes largely free from Radix.
- **North stars:** **Linear** (polished, keyboard-first command center) and **Things**
  (calm, focused).

### 5.2 Streamlined navigation — the alignment graph as a tree, not tabs-per-type

The Pilot's NAV-1 uses ~11 top-level tabs and screen-hopping parent-first creation (GEN-7).
Production **replaces that** with a hierarchical outline rendered straight from the alignment
graph (`serves` / `results_in`), because the Value→Goal→Project→Task chain *is* a tree.

```
┌ Focus ─┬──────────────────────────────────────────────┐
│ Inbox  │  ▸ Health (Area)                              │   ┌ detail peek ──┐
│ Map ◀──┼──▾ Goal: Run a half-marathon        Active    │   │ Goal          │
│ Vision │     ▾ Project: 12-week base build   Active    │   │ target 2026…  │
│ People │        • Task: Long run Sat         Today     │   │ Tags | Rel |  │
│ Review │        • Task: Buy shoes         Not started  │   │ Journal | Hx  │
└────────┴──+ add child (inline)───────────────────────┘   └───────────────┘
```

- **Create-child-in-place** (Enter/Tab in the outline, outliner-style) replaces GEN-7's
  navigate-to-parent flow. The backend is ready: `bsk new --parent` creates + links
  atomically in one call.
- **Detail opens as an inline peek / side panel**, not a separate route — that is what
  "streamlined, no screen-hopping" means. `<SubjectDetail>` stays reusable and openable in
  place.
- **Fewer destinations:** e.g. Focus (command center) · Inbox · Map (the outline) · Vision ·
  Reviews · People · Areas. Old per-type tabs (Goals/Projects/Tasks/Habits) become saved
  filters/lenses over the same tree — never separate stores (NAV-1's own rule).
- **Multi-parent tolerant:** it is a DAG, not a strict tree (a Task can serve a Goal
  directly; a subject can have multiple parents), so a node may appear under more than one
  parent. This makes the tree (Map) and the eventual BROWSE-1 graph two views of one shared
  data model — structure it that way from the start.

### 5.3 Carry forward (ontology-driven UX, platform-neutral)

These are product truths, not WinForms artifacts, and translate 1:1 to components:

- Read-only-first detail + explicit Edit/Save/Cancel (BROWSE-2).
- **Tags ≠ Relationships**, kept visually separate everywhere (GEN-1).
- **Archive, never delete** (D9); an Archived filter to view/restore.
- Status moves only by event (D7 per-type vocabularies + terminal set).
- Dirty-navigation prompts; per-view remembered filters/sort/selection — on the web these
  become **URL state + localStorage** (better than the Pilot's `ViewStateStore`: shareable,
  bookmarkable deep links, and NAV-2 history for free).

### 5.4 What will not translate (by design)

| Pilot mechanism | Why it stops | Production replacement |
|---|---|---|
| Shell out to `bsk.exe` per click | Process-per-click, latency, no per-user CLI on a server | In-process Application services behind the API |
| Direct `bsk_reader` Postgres reads from the client | Browser can't/shouldn't hold a DB connection | Server-side read endpoints (same SQL) |
| `PilotVocab` hand-mirror | Drift-prone (has a drift-guard test) | Types generated from OpenAPI / the Domain |
| "Reload after every write" | Coarse | TanStack Query invalidation + optimistic updates |
| WinForms dialogs, FlowLayoutPanel tabs, WindowPlacement, TitleBarTint | Platform-specific | Modals, responsive nav, router, theming |

## 6. First-pass scope (for a cold Cursor agent)

A **design-forward look-and-feel prototype**, against **mock data typed from
`production-ui-types.ts`** — no backend yet, no .NET changes. Iterate on the feel before
wiring anything real (the nav is a genuine design departure). Priority order:

1. **Design system + token theme + component gallery** — the polish foundation (§5.1).
2. **Streamlined app shell** — few destinations, command palette (cmdk), global New (§5.2).
3. **The alignment outline (Map)** — expand/collapse tree from mock `RelationEdge` data,
   inline create-child, multi-parent tolerant (§5.2).
4. **Detail peek** — reusable `<SubjectDetail>` (Overview / Relationships / Tags / Journal /
   History) with Tags and Relationships visually separate (§5.3).
5. **Command-center Dashboard + Inbox** — the two hero screens that show the polish, keyboard
   triage on Inbox (INBOX-3).

Keep the eventual `LifeOs.Api` write endpoints out of the first pass and human-reviewed when
built (they are thin, but on the governed path).

## 7. Decisions log

**Settled (2026-09-20):**

- Frontend: **React + TypeScript (Vite)**.
- Backend: **.NET Web API in front of `LifeOs.Application`**; both reads and writes go through
  it; `bsk` CLI retained as a second adapter.
- **No PostgREST** for the app's data API.
- **Supabase** for Postgres + Auth + (later) Storage only.
- **Auth:** Supabase Auth (Google) in the browser; `LifeOs.Api` validates the JWT
  (JWKS, with optional legacy HS256) and restricts access to `ALLOWED_EMAILS`.
- **Single repo**, web app under `web/`.
- Navigation: **streamlined, tree-first** (alignment outline replaces per-type tabs);
  create-child-in-place; inline detail peek.
- Design: **high polish**, design-system-first; north stars Linear + Things.

**Open (decide before/at build time):**

- Hosting target (Azure Container Apps vs Fly.io; SPA host).
- Graph library once BROWSE-1 is built for real (React Flow vs Cytoscape/Sigma), driven by
  graph size.
- Read endpoint shaping: reuse `SubjectReader` view-SQL verbatim vs promote some reads into
  Application read services.

## 8. Reference — read models & verb surface

`production-ui-types.ts` (beside this doc) is the TypeScript mirror of
[ReadModels.cs](../../src/LifeOs.Pilot/Reader/ReadModels.cs) and the write-verb table in
[ui-refactor-plan.md](ui-refactor-plan.md) §3, plus the interim vocabulary from
[PilotVocab.cs](../../src/LifeOs.Pilot/Shell/PilotVocab.cs). It exists to shape mock data and
UI types during scaffolding; in Production it is **replaced** by the OpenAPI-generated client.
