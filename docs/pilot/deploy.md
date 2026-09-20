# Deploying LifeOS

Production is one Docker image: the React SPA served by `LifeOs.Api`, talking to
a dedicated Supabase Postgres project. Schema is applied only by `bsk migrate`
in CI — **the API never migrates on startup.**

Host: **Fly.io** (one container, `fly secrets`, Docker-native). The Dockerfile
is portable to Azure Container Apps if that becomes preferable later.

```
Browser ── HTTPS ──▶ Fly.io (LifeOs.Api + wwwroot SPA, same origin)
                         │
                         ├── JWT (Supabase Auth / Google) + ALLOWED_EMAILS
                         │
                         └── session pooler :5432 ──▶ Supabase Postgres
                              owner = BSK_CONNECTION_STRING (writes)
                              reader = BSK_READER_CONNECTION_STRING (reads)

GitHub Actions
  PR:     test + web build + docker build (no push)
  main:   bsk migrate (staging) → fly deploy staging
          bsk migrate (production, required reviewer) → fly deploy production
```

Same origin means no CORS in production. Bearer tokens stay on the app origin.
The browser never holds a database credential.

## Fail closed

This app serves private life data. The process **will not start** if:

- `Auth__Issuer`, `Auth__Audience`, and a signing source (`Auth__JwksUrl` and/or
  `Auth__JwtSecret`) are missing
- `ALLOWED_EMAILS` is empty
- `BSK_CONNECTION_STRING` or `BSK_READER_CONNECTION_STRING` is missing
  outside Development
- CORS is configured with a wildcard origin
- `VITE_SUPABASE_URL` or `VITE_SUPABASE_ANON_KEY` is missing outside Development
  (public anon values, injected into the SPA at process start — never the
  service-role key)

Do not ship a pipeline that sets dummy allowlists, disables JWT validation, or
points production at the local Docker defaults. `/health` and `/health/ready`
are the only anonymous HTTP endpoints besides the static SPA. Every `/api/*`
route requires an allowlisted, verified Google account.

The API does **not** call `MigrationRunner` on startup. Staging and production
schema stay with `bsk migrate` in GitHub Actions, matching the Pilot rule that
only local DEV auto-migrates.

## Environment variables

Runtime (Fly secrets / platform env). Nested ASP.NET keys use `__`. Placeholders
only — never commit real values.

| Variable | Purpose |
|---|---|
| `BSK_CONNECTION_STRING` | Owner role, writes. Supabase **session pooler** port **5432**, `SSL Mode=Require;Trust Server Certificate=true`. Username `postgres.<project-ref>`. |
| `BSK_READER_CONNECTION_STRING` | `bsk_reader` role, reads. Same pooler/SSL. Username `bsk_reader.<project-ref>`. |
| `Auth__Issuer` | `https://<project-ref>.supabase.co/auth/v1` (prod project ≠ staging) |
| `Auth__Audience` | `authenticated` |
| `Auth__JwksUrl` | `https://<project-ref>.supabase.co/auth/v1/.well-known/jwks.json` |
| `Auth__JwtSecret` | Optional legacy HS256 fallback. Leave unset when JWKS works. |
| `ALLOWED_EMAILS` | Comma-separated Google accounts that may use the app |
| `VITE_SUPABASE_URL` | Public Supabase project URL, e.g. `https://<project-ref>.supabase.co`. Written into `/public-config.js` at startup so Fly dashboard deploys do not need Docker build-args. |
| `VITE_SUPABASE_ANON_KEY` | Supabase **anon** key (public by design). **Never** the service-role key. |
| `CORS_ALLOWED_ORIGINS` | Only if the SPA is *not* same-origin. Omit in production. |
| `PORT` | Listen port. Fly sets this; the API honors it. Container default is `8080`. |

Same-origin API base is `/` (the client uses `window.location.origin`). Optional Docker build-arg `VITE_API_BASE_URL=/` is already the image default.

Templates: [`.env.example`](../../.env.example), `src/LifeOs.Api/appsettings.json`,
[`web/.env.example`](../../web/.env.example).

## Pipeline

| Workflow | Trigger | What it does |
|---|---|---|
| [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) | pull request | `dotnet` tests (`test.ps1` equivalent), web lint/test/build, `docker build` with placeholder VITE_* (no push) |
| [`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml) | merge to `main`, tag `v*` | CI, then staging migrate+deploy, then production migrate+deploy |
| [`.github/workflows/migrate.yml`](../../.github/workflows/migrate.yml) | reusable + manual dispatch | `bsk migrate` against the chosen GitHub Environment; optional `flyctl deploy` **after** migrate |

Staging uses GitHub Environment `staging` and [`fly.staging.toml`](../../fly.staging.toml).
Production uses GitHub Environment `production` (required reviewer) and
[`fly.toml`](../../fly.toml). Approving the production job runs migrate and
then deploy in that same job, so a new image cannot go live ahead of schema.

## Rollback

Redeploy the previous Fly image; do **not** reverse migrations.

```powershell
fly releases --app lifeos-mz55-w
fly deploy --app lifeos-mz55-w --image registry.fly.io/lifeos-mz55-w:<previous-tag>
# or:
fly releases rollback --app lifeos-mz55-w
```

Kernel migrations are **forward-only**. An image rollback leaves any already-applied
SQL in place. If a release mixed a breaking schema change with an old image that
cannot read it, apply a new forward migration and deploy a compatible image.
Never run `bsk migrate` down, and never let the API migrate its way out of the
problem.

## Health

- `GET /health` — liveness (process is up). Fly's HTTP check uses this.
- `GET /health/ready` — owner + reader can `SELECT 1`. Returns 503 if either
  connection fails. Do not use this as the Fly machine check (a brief DB blip
  would take the machine out of rotation).

## Manual steps (Chris only)

CI cannot create cloud accounts or OAuth clients. Do these once per environment
before the first deploy. Staging may already exist from
[supabase-hosting.md](supabase-hosting.md); production is a **second** Supabase
project.

### 1. Production Supabase project

1. [Supabase dashboard](https://supabase.com/dashboard) → **New project** (e.g.
   `lifeos-prod`), region near the Fly region (`sjc` in `fly.toml` unless you
   change it). Save the database password.
2. **Connect → Session pooler** (port 5432). Translate the URI to Npgsql form as
   in `.env.example` (`SSL Mode=Require;Trust Server Certificate=true`).
3. Apply schema with the same runner as staging — either
   `./scripts/migrate-production.ps1` (loads `.env.prod`, git-ignored) or the
   gated `production` GitHub Action after step 3 below.
4. Reset `bsk_reader` on **prod** (migration `0005` still has the public local-dev
   password). SQL Editor:

   ```sql
   alter role bsk_reader with password '<a-strong-random-password>';
   ```

   Reader pooler username: `bsk_reader.<prod-project-ref>`.

### 2. Fly apps and secrets

Install [flyctl](https://fly.io/docs/flyctl/install/), then:

```powershell
fly auth login
fly apps create lifeos-mz55-w
fly apps create lifeos-staging
```

If those names are taken, change `app =` in `fly.toml` / `fly.staging.toml` to
match whatever you created. Pick `primary_region` to match the Supabase region
when you can.

Set **runtime** secrets on each app (prod values shown; repeat for
`lifeos-staging` with the staging project):

```powershell
fly secrets set --app lifeos-mz55-w `
  BSK_CONNECTION_STRING="Host=...;Port=5432;...;SSL Mode=Require;Trust Server Certificate=true" `
  BSK_READER_CONNECTION_STRING="Host=...;Username=bsk_reader.<ref>;..." `
  Auth__Issuer="https://<prod-ref>.supabase.co/auth/v1" `
  Auth__Audience="authenticated" `
  Auth__JwksUrl="https://<prod-ref>.supabase.co/auth/v1/.well-known/jwks.json" `
  ALLOWED_EMAILS="you@gmail.com" `
  VITE_SUPABASE_URL="https://<prod-ref>.supabase.co" `
  VITE_SUPABASE_ANON_KEY="<anon-key-not-service-role>"
```

Never put these in git, `fly.toml`, or GitHub Actions logs. `fly secrets set`
restarts machines.

Optional: `fly certs add your.domain` if you later want a custom hostname (not
required; `https://lifeos-mz55-w.fly.dev` is enough for the first cutover).

### 3. GitHub Environments

1. **Settings → Environments → `staging`** (may already exist) with secret
   `BSK_CONNECTION_STRING` (staging owner session-pooler string).
2. **New environment → `production`**. Add yourself as a **required reviewer**.
   Add secret `BSK_CONNECTION_STRING` for the **prod** owner session-pooler
   string.
3. Repository (or environment) secret `FLY_API_TOKEN` from
   `fly tokens create deploy` (or an org token that can deploy both apps).
   Public SPA values (`VITE_SUPABASE_URL`, `VITE_SUPABASE_ANON_KEY`) live on
   Fly as secrets (step 2), not in GitHub.

Without the production reviewer, a merge to `main` could promote schema and a
new image unattended. Keep the reviewer.

### 4. Google OAuth and Supabase Auth URL config

For **each** Supabase project (staging and prod):

1. Google Cloud Console → OAuth 2.0 Client (Web) → Authorized JavaScript
   origins **and** redirect URIs: add the Fly origin,
   `https://<app>.fly.dev`, plus the Supabase callback
   `https://<project-ref>.supabase.co/auth/v1/callback`.
2. Supabase → Authentication → URL configuration:
   - Site URL = `https://<app>.fly.dev`
   - Additional redirect URLs: that same origin (and `http://localhost:5173`
     on staging if you still use Vite locally)
3. Authentication → Providers → Google: enabled. **Disable** open sign-ups
   (Authentication → Providers / settings: reject identities that are not
   invited, or turn off "Allow new users" if the dashboard still offers it).
   The API allowlist is the real gate; closing sign-ups is defense in depth.

### 5. First production cutover

1. Finish steps 1–4. Confirm staging already migrates on merge.
2. Merge to `main` (or run **Actions → Deploy → Run workflow**).
3. Wait for CI and staging deploy to go green.
4. GitHub will wait on the `production` environment **after staging has
   migrated and deployed**. Review the commit, then approve. That job runs
   `bsk migrate` against prod, then `flyctl deploy`. The SPA reads Supabase
   public values from Fly secrets at runtime (`/public-config.js`).
5. Open `https://lifeos-mz55-w.fly.dev` (or your app hostname):
   - `/health` and `/health/ready` are green
   - the SPA loads from that origin
   - Google sign-in works
   - the allowlisted account can read and write
   - a non-allowlisted Google account is 403 on `/api/*`
   - a request with no bearer token is 401
6. Confirm the running app did not apply migrations (no `bsk migrate` in Fly
   logs on boot; schema history only changes during the GitHub migrate step).

Local check of the image against **staging** secrets (never prod) before the
first cutover:

```powershell
docker build -t lifeos:local .

docker run --rm -p 8080:8080 `
  -e BSK_CONNECTION_STRING="<staging owner session-pooler>" `
  -e BSK_READER_CONNECTION_STRING="<staging reader session-pooler>" `
  -e Auth__Issuer=https://<staging-ref>.supabase.co/auth/v1 `
  -e Auth__Audience=authenticated `
  -e Auth__JwksUrl=https://<staging-ref>.supabase.co/auth/v1/.well-known/jwks.json `
  -e ALLOWED_EMAILS=you@gmail.com `
  -e VITE_SUPABASE_URL=https://<staging-ref>.supabase.co `
  -e VITE_SUPABASE_ANON_KEY=<staging-anon-key> `
  lifeos:local
```

Then `http://localhost:8080/health`, `/health/ready`, and the SPA at
`http://localhost:8080`. Sign-in still needs the staging origin on the
Supabase redirect allowlist (`http://localhost:8080`) for Google to return
here. Automated tests cover the allowlist independently: missing token → 401,
non-allowlisted verified email → 403, allowlisted verified email → 200. Do not
weaken JWT validation or `ALLOWED_EMAILS` to make a local OAuth redirect easier.

## Local API development (unchanged)

`dotnet run --project src/LifeOs.Api` with user-secrets and CORS for
`http://localhost:5173` (see `web/README.md`). Development may use local
Docker Postgres; it still refuses to start without JWT config and
`ALLOWED_EMAILS`.
