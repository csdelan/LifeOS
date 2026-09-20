# LifeOS Production UI (first pass)

A design-forward React prototype of the LifeOS command center. It runs entirely against
**mock data** typed from [`docs/pilot/production-ui-types.ts`](../docs/pilot/production-ui-types.ts).
There is no backend, no auth, and no `.NET` involvement in this folder.

```
npm install
npm run dev
```

Opens on **Focus** at `http://localhost:5173`. Toggle light/dark from the sidebar or `⌘K`.

## What this pass includes

- Design system (Hearth tokens) + `/gallery` kitchen sink
- Streamlined shell: Focus · Inbox · Map · Vision · Reviews · People · Areas
- Command palette (`⌘K` / `Ctrl+K`) and global **New** (`⌘N`)
- Map: alignment outline from `RelationEdge` data, expand/collapse, inline create-child,
  multi-parent (a node can appear under more than one parent), detail peek
- `<SubjectDetail>` peek with Overview / Relationships / Tags / Journal / History
  (tags and relationships kept visually separate)
- Focus dashboard and Inbox keyboard triage (↑/↓, Enter; Promote / Relate / Dismiss / Drop)

## Layout

```
web/src/
  lib/production-ui-types.ts   copied seed types (the read/write contract)
  lib/mock/                    in-memory store, generators, write client
  lib/queries.ts               TanStack Query hooks over the mock fetchers
  lib/mock/api.ts              ★ swap seam for LifeOs.Api
  components/ui/               shadcn/ui (Radix) primitives
  components/primitives/       LifeOS design-system widgets
  components/detail/           reusable SubjectDetail peek
  pages/                       route screens
```

## Swapping mock data for the real API

1. Generate the TypeScript client from `LifeOs.Api` OpenAPI (replaces
   `lib/production-ui-types.ts`).
2. Rewrite **`src/lib/mock/api.ts`** (`lifeOsReads`) to call those endpoints.
   Hooks in `lib/queries.ts` stay the same.
3. Rewrite **`src/lib/mock/write-client.ts`** so `createMockWriteClient` becomes an HTTP
   `LifeOsWriteClient` (same method names and return shapes). Toasts and optimistic
   invalidation in the hooks remain.

Do not call Postgres or `bsk.exe` from the browser. Production reads and writes go through
the .NET API only.

## Non-goals (left as seams)

BROWSE-1 graph view, rich-text journals, appointments calendar, real auth, real persistence.
