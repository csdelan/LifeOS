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
          (production is skipped until the repo variable DEPLOY_PRODUCTION=true)
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
| [`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml) | merge to `main`, tag `v*` | CI, then staging migrate+deploy, then production migrate+deploy (skipped unless repo variable `DEPLOY_PRODUCTION` is `true`) |
| [`.github/workflows/migrate.yml`](../../.github/workflows/migrate.yml) | reusable + manual dispatch | `bsk migrate` against the chosen GitHub Environment; optional `flyctl deploy` **after** migrate |

Staging uses GitHub Environment `staging` and [`fly.staging.toml`](../../fly.staging.toml)
(Fly app `lifeos-mz55-staging`; plain `lifeos-staging` is someone else's app).
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

## First deploy: staging (Chris only)

Staging is the existing Supabase project (`lifeos`, us-west-2) served by the
Fly app `lifeos-mz55-staging`. Production stays off until
[Production later](#production-later). CI cannot sign in to Fly or Supabase for
you, so these steps are yours; the script does the wiring in between.

1. **Use the anon key in the SPA.** `VITE_SUPABASE_ANON_KEY` in
   `web/.env.local` must be the public **anon** key (Supabase → Project
   Settings → API Keys → `anon` `public`), never `service_role`. That value is
   served to every browser. `fly-setup.ps1` refuses a `service_role` or
   `sb_secret_` key.
2. **Install flyctl and sign in** (open a new terminal after installing so
   `fly` is on PATH):

   ```powershell
   pwsh -Command "iwr https://fly.io/install.ps1 -useb | iex"
   fly auth login
   ```

3. **Run the setup script** from the repo root:

   ```powershell
   ./scripts/fly-setup.ps1
   ```

   It reads the app name from `fly.staging.toml`, the connection strings from
   `.env`, the Supabase URL/anon key from `web/.env.local`, and
   `ALLOWED_EMAILS` from the API user-secrets. It checks that they all name the
   same Supabase project, shows what it will do, and asks before it:
   - creates the Fly app if it does not exist,
   - imports the seven runtime secrets (`fly secrets import --stage`; values go
     over stdin, never a command line),
   - sets `BSK_CONNECTION_STRING` and `FLY_API_TOKEN` (a deploy token scoped to
     this one app) on the `staging` GitHub Environment.

   Re-running is safe. If the app name is taken, change `app =` in
   `fly.staging.toml` and re-run.
4. **Allow the Fly origin to receive sign-ins.** Supabase → Authentication →
   URL Configuration:
   - **Site URL** = `https://lifeos-mz55-staging.fly.dev` (no trailing slash).
     Supabase accepts any return address on the Site URL's host.
   - **Redirect URLs** includes `http://localhost:5173`, so local Vite keeps
     working.
   - Save each field.

   The SPA asks to return to `window.location.origin` (no trailing slash).
   Redirect URLs entries must match it exactly: `https://host/` or
   `https://host/**` does not match `https://host`. When nothing matches,
   Supabase silently sends the browser to the Site URL instead, which shows up
   as "localhost refused to connect". Google Cloud Console needs no change:
   Google returns to the Supabase callback, which is already registered.
5. **Deploy:** `gh workflow run deploy.yml` (or push to `main`). The run
   migrates staging, deploys, and skips production. Then open
   `https://lifeos-mz55-staging.fly.dev`:
   - `/health` and `/health/ready` are green
   - Google sign-in returns to the Fly origin and the allowlisted account can
     read and write
   - a request to `/api/*` with no bearer token is 401; a non-allowlisted
     Google account is 403
   - Fly logs show no `bsk migrate` on boot

## Production later

Production is a **second** Supabase project. (When you get here, the
alternative is to promote the current project to production and create a fresh
staging project instead. Decide then.) Once per production setup:

1. [Supabase dashboard](https://supabase.com/dashboard) → **New project**,
   region `us-west-2` to sit near Fly `sjc`. Save the database password.
2. Put its session-pooler owner and reader strings, its URL and anon key, and
   `ALLOWED_EMAILS` in `.env.prod` (git-ignored). Apply schema with
   `./scripts/migrate-production.ps1`, then reset `bsk_reader` (migration `0005`
   ships the public local-dev password):

   ```sql
   alter role bsk_reader with password '<a-strong-random-password>';
   ```

3. Supabase → Authentication → Providers → Google: enable it with the same
   Google OAuth client, and add the new project's callback
   `https://<prod-ref>.supabase.co/auth/v1/callback` to that client's
   authorized redirect URIs in Google Cloud Console. Turn off open sign-ups
   (the API allowlist is the real gate; this is defense in depth).
4. GitHub → Settings → Environments → `production`: add yourself as a
   **required reviewer**. Without it, a merge promotes schema and an image to
   production unattended.
5. Run the setup script for production:

   ```powershell
   ./scripts/fly-setup.ps1 -FlyConfig fly.toml -GitHubEnvironment production -EnvFile .env.prod
   ```

6. Supabase (prod) → URL Configuration: Site URL and Redirect URLs =
   `https://lifeos-mz55-w.fly.dev`.
7. Turn the production job on: `gh variable set DEPLOY_PRODUCTION --body true`.
   The next merge waits for your approval after staging, then migrates and
   deploys production in one job. Run the same checks as staging step 5.

Optional: `fly certs add your.domain` for a custom hostname.

## Local image check

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
