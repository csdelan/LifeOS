# LifeOS Production UI

A design-forward React app of the LifeOS command center. By default it runs against
**mock data** typed from `src/lib/production-ui-types.ts`. Set `VITE_API_BASE_URL` to
talk to `LifeOs.Api` (the thin HTTP door into `LifeOs.Application`). The mock stays
available whenever that variable is unset.

```
npm install
npm run dev
```

Opens on **Focus** at `http://localhost:5173`. Toggle light/dark from the sidebar or `⌘K`.

## Live mode (real kernel data)

The API writes **only** through existing Application services (the same path `bsk`
uses). It reads **only** via `SELECT` over `bsk.v_*` as the `bsk_reader` role.

1. Start local Postgres and apply schema (from the repo root):

   ```
   docker compose up -d
   ./run.ps1 migrate
   ./scripts/seed.ps1          # optional, but Focus/Map/Inbox look better with data
   ```

   Staging (Supabase) uses the same env vars the Pilot already honors:
   `BSK_CONNECTION_STRING` (owner / writes) and `BSK_READER_CONNECTION_STRING`
   (`bsk_reader` / reads). Unset, they fall back to the local Docker defaults.

2. Run the API:

   ```
   dotnet run --project src/LifeOs.Api
   ```

   OpenAPI: [http://localhost:5280/openapi/v1.json](http://localhost:5280/openapi/v1.json)
   Swagger UI: [http://localhost:5280/swagger](http://localhost:5280/swagger)

3. Point the web app at it:

   ```
   cd web
   $env:VITE_API_BASE_URL = "http://localhost:5280"   # PowerShell
   npm run dev
   ```

   Or copy `.env.example` to `.env.local` with `VITE_API_BASE_URL=http://localhost:5280`.

4. (Optional) regenerate the typed client after API contract changes:

   ```
   npm run gen:api
   ```

   Types land in `src/api/schema.d.ts`. Vocabulary helpers (`STATUS_BY_TYPE`,
   `pickApplicableAttrs`, …) stay in `src/lib/production-ui-types.ts`.

Auth is a marked `TODO(auth)` seam on the API — local/dev requests are unauthenticated
this pass.

## What this pass includes

- Design system (Hearth tokens) + `/gallery` kitchen sink
- Streamlined shell: Focus · Inbox · Map · Vision · Reviews · People · Areas
- Command palette (`⌘K` / `Ctrl+K`) and global **New** (`⌘N`)
- Map: alignment outline from `RelationEdge` data, expand/collapse, inline create-child,
  multi-parent (a node can appear under more than one parent), detail peek
- `<SubjectDetail>` peek with Overview / Relationships / Tags / Journal / History
  (tags and relationships kept visually separate)
- Focus dashboard and Inbox keyboard triage (↑/↓, Enter; Promote / Relate / Dismiss / Drop)
- Live/mock swap behind `VITE_API_BASE_URL`; forest/dashboard composition in `src/lib/derive.ts`

## Layout

```
web/src/
  api/schema.d.ts              OpenAPI-generated (or hand-bootstrapped) client types
  lib/production-ui-types.ts   vocabulary + write-client interface (not in OpenAPI)
  lib/derive.ts                alignment forest + dashboard composition (shared)
  lib/client.ts                mock vs live factory
  lib/mock/                    in-memory store, generators, write client
  lib/live/                    OpenAPI-fetch reads + writes
  lib/queries.ts               TanStack Query hooks + targeted invalidation
  components/ui/               shadcn/ui (Radix) primitives
  components/primitives/       LifeOS design-system widgets
  components/detail/           reusable SubjectDetail peek
  pages/                       route screens
```

## Swapping mock data for the real API

`VITE_API_BASE_URL` unset → mock (default). Set → live. Do not call Postgres or
`bsk.exe` from the browser. Production reads and writes go through the .NET API only.

## Non-goals (left as seams)

BROWSE-1 graph view, rich-text journals, appointments calendar, real Supabase Auth,
binary artifact storage.
