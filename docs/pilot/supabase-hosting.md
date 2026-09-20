# Hosting the Life Kernel on Supabase

This is the runbook for hosting the Life Kernel's PostgreSQL database on
Supabase Cloud so it can be reached from multiple computers. It covers the
first-time setup and the day-to-day promotion flow.

## The model

Three environments, **one schema authority**:

| Environment | Database | Who writes schema |
|---|---|---|
| **Dev** | Local Docker `lifeos-postgres` (`docker-compose.yml`) | You, while developing |
| **Staging** | Supabase cloud project | `bsk migrate` (via CI on merge to main) |
| **Prod** | (later) a second Supabase project | `bsk migrate`, gated |

`bsk migrate` is the single source of truth for schema everywhere. Supabase is
**just the Postgres host** — you point the same runner at it. We deliberately do
**not** use Supabase's own migration system (`supabase db push`); letting two
systems own the schema causes drift and duplicate-apply errors.

The Supabase CLI is still useful, just not for schema: linking the cloud
project, snapshotting it (`supabase db pull` for a read-only safety diff), and
later Storage / Auth / Edge Functions. **CI does not use the Supabase CLI** — it
runs `bsk migrate` directly with only the .NET SDK.

Connections use the **session pooler** (port 5432), never the transaction pooler
(6543): the session pooler is IPv4-friendly (GitHub runners and most home
networks are IPv4) and holds the session for the connection's life, so Npgsql's
prepared statements work. The transaction pooler breaks both.

---

## One-time setup

### 1. Create the Supabase project (dashboard)

You must do this yourself — it needs your account and a database password.

1. Go to <https://supabase.com/dashboard>, sign in (or sign up).
2. **New project** → pick an org, name it e.g. `lifeos-staging`, choose a region
   near you, and set a **strong database password**. Save that password in your
   password manager — you'll need it below and it can't be shown again.
3. Wait for the project to finish provisioning.

### 2. Grab the session-pooler connection string

In the project dashboard, click **`Connect`** at the top of the page (green
button in the top bar), then choose **Session pooler**. Supabase moved these
strings out of Project Settings — the `Connect` dialog is now the only place
they live. The dialog shows a URI like:

```
postgresql://postgres.<project-ref>:[YOUR-PASSWORD]@aws-0-<region>.pooler.supabase.com:5432/postgres
```

Npgsql does **not** accept that URI form — translate it to the key-value form
the kernel expects (append SSL):

```
Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db-password>;SSL Mode=Require;Trust Server Certificate=true
```

`.env.example` has this template. The project **ref** is the string in the
`postgres.<ref>` username (also in your project URL).

### 3. Apply the schema to staging for the first time

From this repo, with the connection string set (either export
`BSK_CONNECTION_STRING`, or copy `.env.example` to `.env` and fill it in):

```powershell
./scripts/migrate-staging.ps1
```

This builds and runs `bsk migrate` against Supabase. It creates the `bsk` and
`bsk_derived` schemas, the tables, the `public.schema_migrations` history, and
the `bsk_reader` role. Re-running is safe.

> Migration `0005` creates `bsk_reader` with a **local-development** password.
> Reset it on staging before pointing any app at it (next step).

### 4. Reset the read-only role's password on staging

The `bsk_reader` password baked into migration `0005` is a public local-dev
credential. Give staging its own. In the Supabase dashboard **SQL Editor**, run:

```sql
alter role bsk_reader with password '<a-strong-random-password>';
```

Save that password too. The pilot reads through this role. (We keep this out of
migrations on purpose — migrations are immutable and shared across every
environment, so a per-environment secret can't live in one.)

### 5. Point the WinForms pilot at staging

The pilot reads via `BSK_READER_CONNECTION_STRING`. Through the pooler the
username is `bsk_reader.<project-ref>`:

```
BSK_READER_CONNECTION_STRING=Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=bsk_reader.<project-ref>;Password=<bsk-reader-password>;SSL Mode=Require;Trust Server Certificate=true
```

Set it as a user/machine environment variable on each computer that runs the
pilot (PowerShell, persistent):

```powershell
[Environment]::SetEnvironmentVariable('BSK_READER_CONNECTION_STRING', '<value>', 'User')
```

To write from a machine (via `bsk`), set `BSK_CONNECTION_STRING` the same way
with the `postgres.<ref>` string from step 2.

### 6. Wire GitHub Actions to promote on merge

The workflow `.github/workflows/migrate-staging.yml` runs `bsk migrate` against
staging whenever migration/CLI/infrastructure files land on `main`.

1. In GitHub: **Settings → Environments → New environment → `staging`**.
   (Optionally add yourself as a required reviewer to gate applies.)
2. In that environment: **Add secret** `BSK_CONNECTION_STRING` = the
   `postgres.<ref>` session-pooler string from step 2.
3. Push to `main` (or run the workflow manually via **Actions → Migrate staging
   → Run workflow**). Watch it apply.

### 7. (Optional) Install the Supabase CLI locally

Not needed for migrations; useful for Storage/Functions later and for
`supabase db pull` snapshots. On Windows:

```powershell
scoop install supabase        # if you have Scoop
```

No Scoop? Grab the standalone binary from
<https://github.com/supabase/cli/releases>, or run it ad hoc with
`npx supabase <command>`. Then, once inside the repo:

```powershell
supabase login
supabase link --project-ref <project-ref>
```

`supabase link` writes `supabase/config.toml` (commit it) and local state under
`supabase/.temp/` (git-ignored). Leave the `[db]` migration settings unused —
schema stays with `bsk migrate`.

---

## Day-to-day

**Develop against local Docker (unchanged):**

```powershell
docker compose up -d
./run.ps1 migrate      # applies to localhost
```

**Add a schema change:** write the next `db/migrations/NNNN__name.sql`, apply it
locally, commit, open a PR. On merge to `main`, CI applies it to staging.

**Promote by hand if needed** (e.g. before CI is set up, or a hotfix):

```powershell
./scripts/migrate-staging.ps1
```

**Check staging matches source** (drift report, changes nothing): point
`bsk rebuild --verify` at staging by setting `BSK_CONNECTION_STRING` first.

---

## Going to production

Production is a **second** Supabase project plus a Fly.io app. Schema is still
`bsk migrate` only — CI applies it under a `production` GitHub Environment with
a **required reviewer**, then deploys the image. The API never migrates on
startup.

Full architecture, env vars, pipeline, rollback, and the manual checklist
(prod project, `bsk_reader` password, Fly secrets, GitHub Environment, Google
OAuth / Supabase URL config): **[deploy.md](deploy.md)**.

## Gotchas

- **Port 6543 (transaction pooler)** will fail or misbehave for migrations. Use
  **5432 (session pooler)**.
- **Direct connection** (`db.<ref>.supabase.co:5432`) is IPv6-only on the free
  tier. Prefer the session pooler unless you know your network and the runner
  are IPv6-capable.
- **SSL is required.** Always include `SSL Mode=Require;Trust Server
  Certificate=true` (or supply Supabase's CA for full verification).
- **Never commit a real connection string.** `.env` and `.env.*` are git-ignored
  (except `.env.example`); the CI secret lives in the GitHub Environment.
