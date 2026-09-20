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

## Auth (Google + allowlist)

Live mode is a **private single-user app**. The browser signs in with Google via
Supabase Auth; `LifeOs.Api` validates the JWT on every request and rejects anyone
whose verified email is not in `ALLOWED_EMAILS` (403). Mock mode (`VITE_API_BASE_URL`
unset) still skips sign-in.

**Never commit real secrets.** Local API values go in user-secrets; the SPA uses
`web/.env.local` (gitignored). Deployment uses the platform env / secret store.

### Web (`web/.env.local`, from `.env.example`)

| Variable | Purpose |
|---|---|
| `VITE_API_BASE_URL` | LifeOs.Api origin, e.g. `http://localhost:5280`. Unset = mock mode, no auth. Production image uses `/` (same origin). |
| `VITE_SUPABASE_URL` | `https://<project-ref>.supabase.co` |
| `VITE_SUPABASE_ANON_KEY` | Supabase anon key (public by design; the API still enforces the allowlist) |

Copy `web/.env.example` to `web/.env.local` and fill in the three values.

Enable **Google** under Supabase → Authentication → Providers. Add the SPA origin
(`http://localhost:5173` for local Vite) to Auth → URL configuration (Site URL and
Redirect URLs).

### API (user-secrets locally)

From the repo root:

```
dotnet user-secrets --project src/LifeOs.Api set "Auth:Issuer" "https://<project-ref>.supabase.co/auth/v1"
dotnet user-secrets --project src/LifeOs.Api set "Auth:Audience" "authenticated"
dotnet user-secrets --project src/LifeOs.Api set "Auth:JwksUrl" "https://<project-ref>.supabase.co/auth/v1/.well-known/jwks.json"
dotnet user-secrets --project src/LifeOs.Api set "ALLOWED_EMAILS" "you@gmail.com"
```

Optional legacy HS256 fallback (Dashboard → Settings → API → JWT Secret), only if
the project still signs with the shared secret:

```
dotnet user-secrets --project src/LifeOs.Api set "Auth:JwtSecret" "<jwt-secret>"
```

Equivalent environment variables (deployment): `Auth__Issuer`, `Auth__Audience`,
`Auth__JwksUrl`, `Auth__JwtSecret`, `ALLOWED_EMAILS`. Local Vite needs CORS
(`appsettings.Development.json` lists `http://localhost:5173`). Production serves
the SPA from the API (same origin) and leaves CORS empty. Never set a wildcard
origin. See [docs/pilot/deploy.md](../docs/pilot/deploy.md).

The API will not start until issuer, audience, a signing source (JWKS and/or JWT
secret), and at least one allowed email are set. Outside Development it also
refuses to start without `BSK_CONNECTION_STRING` and `BSK_READER_CONNECTION_STRING`.
OpenAPI at `/openapi/v1.json` is Development-only (anonymous for `npm run gen:api`);
every `/api/*` route requires an allowlisted user. `/health` and `/health/ready`
are anonymous.

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
- Google sign-in via Supabase Auth; API JWT validation + email allowlist

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
  lib/supabase.ts              Supabase browser client (live mode)
  lib/auth-token.ts            Bearer token + refresh for API calls
  components/auth/             sign-in / allowlist gate
  components/ui/               shadcn/ui (Radix) primitives
  components/primitives/       LifeOS design-system widgets
  components/detail/           reusable SubjectDetail peek
  pages/                       route screens
```

## Swapping mock data for the real API

`VITE_API_BASE_URL` unset → mock (default). Set → live. Do not call Postgres or
`bsk.exe` from the browser. Production reads and writes go through the .NET API only.

## Non-goals (left as seams)

BROWSE-1 graph view, rich-text journals, appointments calendar,
binary artifact storage. Auth is single Google user + allowlist — no roles,
orgs, or password/email sign-in.
