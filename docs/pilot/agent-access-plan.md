# Agent Access Plan: backups, then a read-only MCP server, then writes behind a switch, then an assistant

The design and build order for letting Chris's personal AI agents (Claude, ChatGPT and
others) read LifeOS, and later write to it, through an **MCP server**. Before any of that,
LifeOS gets its own automated, encrypted, off-site **backups**. This doc says *how* and *in
what order*, written so an agent with no session context can pick up any phase.

**Agreed with Chris on 2026-10-27.** He wants his agents to understand his goals,
identity, tasks, habits and events as they stand right now, so their advice is up to date.
He chose MCP over opening the REST API to outside agents. He asked for three protections, in
this order:

1. **Automated backups**, deployed first, even though they aren't part of MCP.
2. **A read-only MCP server** to begin with.
3. **Read/write**, which he switches on once he's comfortable.

| Phase | What ships | What it gives Chris |
|---|---|---|
| 0 | A nightly scheduled task on Chris's PC: database dump and new photos, restore-tested, encrypted to his key, saved to his OneDrive | The life record survives a bad migration, a mistaken delete, a Supabase incident or a lost account |
| 1 | `/mcp` on the existing API: read-only tools, sign-in through Supabase's OAuth server, a consent page, an access log | Any MCP-capable agent can see his life as it is now, and he can see what each one read |
| 2 | Write tools, off until `Mcp__Writes` is switched on, in two levels, for listed agents only | Agents can capture, journal, check off and update, attributed and undoable |
| 3 | An assistant: Chris assigns a Task to Claude, LifeOS wakes it, and it does the job on its own. A prototype on a desktop repo and a web repo comes first (§6) | Claude works his life and code jobs without him opening a chat, and asks in the Task when it needs him |

Each phase deploys on its own. Phase 0 doesn't depend on anything else in this doc. Phase 3's
prototype (step 3.1) runs on Phase 1; the rest of Phase 3 needs Phase 2.

## 0. Read these first

- [ontology.md](../ontology.md) §4.4 (provenance) and §5 (the invariants): append-only
  source, `bsk` as the only writer, `bsk_reader` for everyone else.
- [deploy.md](deploy.md): Fly, GitHub Environments and secrets. Production is still "later".
- [supabase-hosting.md](supabase-hosting.md): the session pooler and role usernames
  (`<role>.<project-ref>`).
- [binary-artifacts.md](binary-artifacts.md), "Object storage": photo bytes live in the
  private bucket `lifeos-artifacts` at `artifacts/<sha256>`, never overwritten.
- Migration `0036__integration.sql`: `bsk_ops` is owner-only operational state. Oura tokens
  are stored there, encrypted under `Integrations:TokenKey`.
- `MigrationRunner.EnsureHistoryTableAsync`: `public.schema_migrations` has row level
  security on and no policies, so only the owner sees it.
- [ui-requirements.md](ui-requirements.md): **GEN-15** (an AI agent is a Person with
  `person_kind` = ai) and **CAP-3** (AI-authored captures; its open decision on attribution
  is settled in §2).
- `ApiHost`, `AuthServiceCollectionExtensions`, `AllowlistedUserHandler` and `SpaFallback`
  in `src/LifeOs.Api`.
- §10: the outside facts this plan relies on, with sources.

No requirement entries exist for this work yet. Proposed IDs: **OPS-1** (backups),
**AGENT-1** (agents read LifeOS), **AGENT-2** (agents write to LifeOS), **AGENT-3** (Claude
works the jobs Chris assigns it). Chris writes their Workflow sections; this plan is the how.

## 1. Where the data is today

As far as the repo shows, production hasn't been set up yet (deploy.md, "Production
later"). The data Chris uses every day is in the **staging** Supabase project (`lifeos`,
us-west-2), served by the Fly app `lifeos-mz55-staging`. Phase 0 backs up that project
first, and production as well once it exists (§3.7).

A complete copy needs:

| What | Where | Notes |
|---|---|---|
| The life record | schema `bsk` | Append-only events and artifacts, plus subjects (changed in place) |
| Derived tables | schema `bsk_derived` | Rebuildable with `bsk rebuild`, but small, so include them |
| Operational state | schema `bsk_ops` | Oura tokens (encrypted), and later the agent access log (§4.5). `bsk_reader` can't read it |
| Migration history | `public.schema_migrations` | Lets `bsk migrate` agree with a restored database |
| Photo bytes | Storage bucket `lifeos-artifacts`, `artifacts/<sha256>` | Not in the database. Content-addressed and never overwritten |

These don't need backing up:

- **`auth.*`** (Supabase Auth's users). Nothing in `bsk` refers to them, and
  `ALLOWED_EMAILS` is the gate. After a restore to a new project, Chris signs in with
  Google again.
- **Secrets** (`Integrations__TokenKey` and the various keys) live in Fly and `.env`.
  The ones a restore needs go in Chris's password manager (§3.4), not in the backups.

The migrations create no extensions, so a dump restores into any Postgres of the same major
version.

Until Phase 0, nothing backed this up: Supabase's free plan takes no backups (§10).

## 2. Decisions (Chris)

These are the recommended choices. Chris confirms them before Phase 0 starts. Each can
change later without redoing the design.

1. **Backups go to Chris's OneDrive, from a scheduled task on his PC** (decided
   2026-10-28). He already has plenty of OneDrive space, and the PC keeps OneDrive in sync.
   This replaced the first draft's Backblaze B2 bucket with Object Lock, uploaded from
   GitHub Actions. OneDrive can't lock a file the way Object Lock does. Its 30-day recycle
   bin and Microsoft 365's Restore your OneDrive are the safety net instead (§3.1).
   - Not GitHub artifacts: they expire within 90 days, and they share an account with the
     code and the secrets.
2. **Retention:** every night's copy for 35 days, plus the first copy of each month kept for
   good (the first draft kept them a year, to hold down B2's cost).
3. **Agents read everything, and each area can be switched off.** The core is always on:
   identity, areas, goals, projects, tasks, habits, appointments, inbox, journals, people,
   alignment and diagnostics. Health (Oura), Food, Rewards and Home are on by default, and
   `Mcp__Domains` can turn any of them off. Chris's aim is advice from the whole picture,
   so the default is everything.
   - **Oura data is included, and Chris accepts the risk** (decided 2026-10-28). Oura's API
     agreement (§4(d)) lets a developer use Oura data with an AI model only through Oura's
     own MCP server, which isn't published yet. It also forbids using the API to give an AI
     model data or prompts. LifeOS reads Oura as Chris's registered app, so serving that
     data to his agents breaks the clause. He accepts that for his own data in his own app,
     as he accepted the retention risk in D14. The worst case is that Oura suspends
     LifeOS's API access: the Activity page stops syncing and paying XP, and what's already
     synced stays. Taking `health` out of `Mcp__Domains` removes Oura's records from every
     tool at once. Agents building LifeOS still work against Oura's sandbox and fixtures
     (activity-plan.md §7).
4. **How agent writes are attributed (settles CAP-3's open decision):**
   - Every event an agent writes has `source_id = mcp:<client_id>` and provenance
     `declared`: Chris said it, through that agent.
   - The agent is linked to an AI-agent Person (GEN-15), so the web can show "via Claude".
   - `derived` is kept for an agent's own inferences. Those must cite their source events
     (invariant 2), and Phase 2 doesn't include them.
5. **A write needs two things: the switch, and the agent on a list.** `Mcp__Writes` turns
   writes on, and `Mcp__WriteClients` names the agents that may use them. Every other
   agent stays read-only.

## 3. Phase 0: backups

**Built 2026-10-28.** The runbook is [backup-restore.md](backup-restore.md): setup, checking,
the drill and a real restore. This section keeps the design and why.

### 3.1 Shape

**Changed from the first draft (Chris, 2026-10-28).** The first draft ran a GitHub Actions
job that uploaded to a Backblaze B2 bucket with Object Lock. Chris already has plenty of
OneDrive space, so the backups go there instead, and the job runs on his PC, which keeps
OneDrive in sync. That drops B2, its keys and the GitHub Environment secrets. What it gives
up:
- **Object Lock.** OneDrive can't make a file undeletable. A deleted backup sits in the
  recycle bin for 30 days, and Microsoft 365's Restore your OneDrive rolls everything back
  up to 30 days (§10).
- **Running whatever the PC is doing.** A night the PC is off or asleep is caught up when
  it's back.

Everything else in the first draft stands: the read-only role, the restore test, the
encryption and the retention.

`scripts/backup.ps1` runs nightly from Windows Task Scheduler. The task is
`\LifeOS\Backup (staging)`, set for 3:17 am local time, while Chris is signed in.
`StartWhenAvailable` runs a missed night as soon as the PC is back. Each run:

1. Dumps the database (§3.3).
2. Restores the dump into a scratch Postgres in Docker and checks it (§3.5).
3. Encrypts it to Chris's public key (§3.4) and saves it to OneDrive with a manifest (§3.6).
4. Copies any photos not yet backed up, each one encrypted (§3.6).
5. Deletes daily copies past their 35 days.

**When it fails:**
- A failed run shows a Windows notification, writes the reason to the log, and pings a
  dead-man's switch's `/fail` URL if one is set.
- A night with no run is caught only by the dead-man's switch (healthchecks.io is free). It
  emails Chris when a day passes with no successful ping. It's optional; setup takes its URL.
- Task Scheduler's Last Run Result can't be trusted. The task runs through
  `conhost --headless` so no window pops up, and that always reports 0.

**The scripts:**
- `scripts/backup-setup.ps1` does the one-time setup for each database (§3.7);
  `scripts/restore.ps1` restores (§3.8); `scripts/LifeOsBackup.psm1` holds what they share.
- `pg_dump`, `pg_restore` and `psql` run in a `postgres:<major>-alpine` container, so the PC
  needs Docker but no Postgres install.
- A run takes about 15 seconds at today's size: 8 MB of tables and 39 photos (5.5 MB).

### 3.2 A read-only role for backups: `bsk_backup`

Migration `0046__backup_role.sql` creates the role:

- `CREATE ROLE bsk_backup NOLOGIN`, guarded like 0005's `bsk_reader`. The migration sets
  no password, so there's no public default to reset. Setup turns on login for each project
  with its own random password (§3.7).
- `USAGE` on `bsk`, `bsk_derived` and `bsk_ops`, and `SELECT` on all their tables and
  sequences.
- `ALTER DEFAULT PRIVILEGES` in all three schemas, so tables added later are covered. This
  works because migrations always run as the owner.
- On `public.schema_migrations`: `GRANT SELECT` plus a policy
  `schema_migrations_backup_read FOR SELECT TO bsk_backup USING (true)`. The runner's
  every-run step only enables RLS and revokes Supabase's API roles. It never drops policies,
  so this one stays.
- No write grants anywhere. A leaked backup password can read everything and change
  nothing. That's the same exposure as `bsk_reader`, plus `bsk_ops`, whose tokens are
  encrypted.

**Why a new role:**
- Not the owner: the owner can drop the append-only triggers and rewrite history. A job
  that runs unattended every night shouldn't hold that credential.
- Not `bsk_reader`: by design it can't read `bsk_ops` or the migration history.

**Tests** (`BackupRoleTests`):
- As `bsk_backup`, every table and view in the three schemas can be read, and so can every
  row of `schema_migrations`.
- Inserts, updates, deletes and `CREATE TABLE` are refused everywhere.
- A table and sequence created later by the owner can be read.
- The role can't sign in until a project sets its password.

Through the session pooler, the username is `bsk_backup.<project-ref>`, as with
`bsk_reader`.

### 3.3 The dump

The script reads the server's version (`server_version_num`) and runs `pg_dump` from the
matching `postgres:<major>-alpine` image. It runs `pg_dump` twice, both times in custom
format:

1. `pg_dump -Fc -n bsk -n bsk_derived -n bsk_ops` → `lifeos-<stamp>.dump`
2. `pg_dump -Fc --enable-row-security -t public.schema_migrations` →
   `schema-migrations-<stamp>.dump`

**Why two runs:**
- The history table needs its own run because pg_dump's `-t` overrides `-n`: one run with
  both options would dump only that table.
- `--enable-row-security` lets a role without BYPASSRLS dump a table that has RLS on. The
  first run leaves it off on purpose: if a `bsk` table ever had RLS, `pg_dump` fails rather
  than quietly leaving rows out.
- The dumps keep their grants, so access for `bsk_reader` and `bsk_backup` comes back on
  restore.

**Counting.** Every table's exact row count is taken just before and just after the dumps.
If any count changed, the database was written during the dump, so the dump is taken again,
up to three times. That makes the counts the dump's own. A row that's updated in place
doesn't change a count, but the restore test (§3.5) still checks the copy is whole.

The run also writes a **manifest**, `manifest-<stamp>.json`, containing:
- the time, host, user, server version and `pg_dump` version
- the git commit of the scripts
- the latest applied migration
- the database's `bsk.*` settings (`bsk.home_zone`), which a per-schema dump doesn't carry
- the row count of every table
- the sha256 of each encrypted file
- the number of photos, and how the restore test went

The manifest holds no life data, so it's stored unencrypted. Its row counts let anyone
check a copy is whole without the private key.

### 3.4 Encryption

The job uses [age](https://age-encryption.org) (`winget install --id FiloSottile.age`), with a
key pair:

- The PC holds only the **public** key, in the settings. The nightly job never needs the
  private key.
- Only Chris's **private** key can read a backup. Someone who gets into his Microsoft account
  sees nothing readable.

Setup makes the pair (`age-keygen`) in `%USERPROFILE%\lifeos-backup.key` and refuses to make it
inside OneDrive. The private key goes into his password manager, plus one printed copy kept
somewhere safe, and the file is deleted. **Losing it makes every backup unreadable.** The
drill in §3.8 is how he knows it's still at hand. `Integrations__TokenKey` goes in the
password manager beside it: without that key, a restore still works, but Oura has to be
reconnected.

### 3.5 A restore test every night

Before encrypting, the job restores both dumps into a scratch Postgres container that it
starts itself:

1. **Roles first.** Create every role the dump's grants, default privileges and policies
   name, without a login. That's `bsk_reader` and `bsk_backup`, plus Supabase's
   `service_role`, which is granted the history table. Without them the restore fails.
   Then set the manifest's `bsk.*` settings on the database.
2. `pg_restore --no-owner --exit-on-error` both files. Triggers and foreign keys are
   restored after the data, so loading doesn't trip the append-only triggers.
3. Compare every table's row count with the manifest. They must be equal.
4. Run `bsk rebuild --verify`, then `bsk rebuild`, against it. The rebuild must write exactly
   what the source folds to (the checksum `--verify` reported). Whether the live derived
   table was current goes in the manifest as `derivedWasCurrent`. It's normally behind:
   only `bsk rebuild` writes it, and the app reads status straight from the source.
5. Run `bsk check --json`. It must finish without error, since the diagnostics read every
   major view.

**What fails a run, and what's saved:**
- A dump that can't be restored, or whose counts don't match, fails the run and isn't
  saved. Chris hears about it the next morning, not on the day he needs it.
- Steps 4 and 5 use the CLI built from the working copy. If those fail (a rebuild that
  doesn't match, or a CLI that doesn't build), the copy is saved and the run still fails. The
  copy restored and its counts matched, so it's worth keeping.

The plaintext stays in a temporary folder that's deleted at the end of the run.

### 3.6 Saving to OneDrive

**Layout,** under `OneDrive\Backups\LifeOS\<target>\`:

```
db/daily/<stamp>/      lifeos-<stamp>.dump.age, schema-migrations-<stamp>.dump.age, manifest-<stamp>.json
db/monthly/<yyyy-mm>/  the month's first successful run, the same three files
artifacts/<sha256>.age one per photo, never removed
```

A copy is written to `<stamp>.partial` and renamed when complete, so a half-written folder
never looks like a copy. The artifacts folder mirrors the bucket's own keys
(`artifacts/<sha256>`), so a restore puts each photo back where the database expects it.

**Retention:**
- Daily copies are deleted after 35 days.
- Monthly copies are kept for good. The first draft kept them a year to hold down B2's
  cost; OneDrive space is already paid for.
- There's no limit on photos, so a photo removed in LifeOS stays recoverable.

**Photos.** Each night the job:
1. lists the photos the database refers to (`bsk.artifact_object`, read from the restored
   copy, so the list matches the dump);
2. downloads each one not yet in `artifacts/`, and checks its bytes hash to its name;
3. encrypts it and saves it.

A photo that's missing from the bucket, or whose bytes have changed, fails the run (the
store was changed outside `bsk`), and the rest are still copied. Reading the Supabase bucket
needs the Supabase secret key (`sb_secret_…`), because Supabase has no read-only key. It's
the broadest secret the job holds.

### 3.7 Settings and secrets

`scripts/backup-setup.ps1` sets up one database at a time (`-Target staging`; production
later with `-EnvFile .env.prod`). It reads the owner connection (`BSK_CONNECTION_STRING`) and
the Storage secret key (`Storage__SupabaseSecretKey`) from `.env`, then:

1. Makes or reuses the age key (§3.4).
2. Gives `bsk_backup` a login with a new random password, and checks it can sign in. The
   password is sent as a SCRAM-SHA-256 hash, as psql's `\password` does, so the plaintext
   never reaches the server or its statement log. Rerunning setup rotates it.
3. Saves the settings to `%USERPROFILE%\.lifeos\backup\<target>.settings.xml`: the
   `bsk_backup` connection string, the Supabase URL and secret key, the public key, the folder,
   the retention and the optional dead-man's switch URL. The secrets are encrypted with
   Windows DPAPI (`Export-Clixml`), so only Chris's Windows account on this PC can read them.
4. Registers the scheduled task.

The settings and logs aren't under AppData on purpose. The Claude desktop app is a packaged
app, so anything it or its terminals write to AppData goes to a private copy that the
scheduled task never sees.

### 3.8 Restore runbook and drill

`scripts/restore.ps1` and [backup-restore.md](backup-restore.md). A restore:

1. Checks the copy's files against the manifest and decrypts them with the private key.
2. Restores into an empty database. By default that's a new `lifeos_restore` on the local
   Docker Postgres, for the drill. Otherwise it's the owner connection passed in, such as a
   new Supabase project for a real disaster. A database that already has `bsk` is refused.
3. Creates the roles the dump names and sets the `bsk.*` settings, as in §3.5.
4. Checks:
   - every row count matches the manifest;
   - `bsk migrate --json` applies nothing when the copy is current, or brings an older copy
     up to date;
   - `bsk rebuild` brings the derived table up to date, and `bsk rebuild --verify` then
     reports no drift.
5. Photos: `-ArtifactsFolder` decrypts them to a folder, each checked against its sha256.
   `-UploadArtifacts` uploads them to the new project's bucket, which it creates if missing.
6. Then point the app at it:
   - set `bsk_reader`'s password;
   - update the Fly secrets (deploy.md);
   - sign in with Google;
   - reconnect Oura if `Integrations__TokenKey` is lost.

**Drill:** once right after Phase 0 ships, then every three months. A repeating Task in
LifeOS is the natural reminder. Restore last night's copy into local Docker and look at it
through the app. The drill is the only check that the private key still works.

### 3.9 Done when

- [x] Tested end to end against a throwaway database standing in for staging, with a fake
  Storage and a fake dead-man's switch, all on 2026-10-28. It covered:
  - a backup and a restore, photos included, with matching row counts and the home zone
    carried over;
  - writes during the dump (retried, then failed after three tries);
  - a wrong password, a derived table behind its source, and a photo missing from the bucket;
  - pruning, two runs at once, and a run started by Task Scheduler.
- [x] Staging: migration 0046 applied, setup run, and the first backup green (2026-10-28:
  11 tables, 5,805 rows, 39 photos, from a Task Scheduler run). The first run showed that the
  live derived table is normally behind its source, which isn't a fault, so the check became
  the rebuild test in §3.5.
- [x] The drill restores staging's copy into local Docker, the row counts match, and the
  app reads it (2026-10-28: `bsk check` gives the same 12 findings, and the photos decrypt).
  Next drill: 2027-01.
- [ ] The nightly run is green for a week. Optional: the dead-man's switch is set, and a
  skipped night trips it.
- [x] deploy.md and the README link the runbook.

## 4. Phase 1: read-only MCP server

### 4.1 Where it runs

The server runs in **`LifeOs.Api`, at `/mcp`**, beside `/api`. It doesn't run on Supabase
Edge Functions, for three reasons:

- Edge Functions are TypeScript on Deno. The readers would have to be rewritten there,
  and the Application layer would be bypassed.
- Supabase's guide to MCP servers on Edge Functions doesn't support authentication yet:
  such a server is public (§10).
- In the API, the server reuses the readers, the JWT validation and the deploy pipeline.
  It moves with the API, to DigitalOcean or anywhere else.

Supabase's only part in this phase is sign-in (§4.4).

**Package:** `ModelContextProtocol.AspNetCore`, the official C# SDK, at its current
stable version, pinned in `Directory.Packages.props`. Use the Streamable HTTP transport,
**stateless** (no server-side sessions). That suits Fly's scale-to-zero and any number of
machines.

**Code:** `src/LifeOs.Api/Mcp/`:
- `McpHost.cs`: registration, `MapMcp`, the auth wiring
- `McpOptions.cs`: the settings in §4.2
- `McpSupport.cs`: the answer's shape (`as_of`, `today`, summary plus JSON), the argument
  parsing, and `McpReads` (the clock, the change reader and a diagnostics runner, all on the
  reader connection)
- `McpGuide.cs`: the server instructions and `lifeos://guide`
- `AlignmentForest.cs`: the Map's forest, ported from `web/src/lib/derive.ts`
- `Tools/*.cs`: one class per area
- `AgentAccessStore.cs`: the allow list and the access log in §4.5 (step 1.4), on the owner
  connection
- `McpCallLog.cs`: what the log keeps of a call (its arguments, how many items it returned)

The agent rules live with the other policies, in `Auth/McpAgentHandler.cs` and
`Auth/AuthServiceCollectionExtensions.cs`.

Tools read through the existing readers on the **`bsk_reader`** connection. In Phase 1,
only the allow list and the access log touch the owner connection, and they write only to
`bsk_ops`. The tools never see them.

### 4.2 Settings

| Key | Default | Meaning |
|---|---|---|
| `Mcp__Enabled` | `false` | Off: `/mcp` and its metadata return 404. This is the kill switch |
| `Mcp__Domains` | `health,food,rewards,home` | The optional areas exposed besides the core (§4.3) |
| `Mcp__BlockedClients` | empty | Client ids refused immediately, even with a token that hasn't expired |
| `Mcp__Writes` | `off` | Phase 2 (§5.1) |
| `Mcp__WriteClients` | empty | Phase 2 (§5.1) |

- An unknown value fails startup, like the other settings.
- To change one: `fly secrets set <key>=<value> -a <app>`. The machine restarts with the
  new value; nothing is redeployed.
- They're settings, not a toggle on a page, on purpose: neither an agent nor a stolen
  browser session can turn writes on.

### 4.3 Tools

The tools are shaped around an agent's questions, not around the web's endpoints. Every
result carries:
- `as_of` (UTC) and `today` (in the home time zone, `Rewards:HomeTimeZone`), so "today"
  and "overdue" mean what Chris means;
- ids and URNs, so follow-up calls work;
- lists capped by `limit` (default 50), with `truncated: true` when cut. `recent_changes`
  then gives `next`, to pass back as `cursor` for the following page.

Results are structured JSON plus a one-line text summary. Every tool is annotated
`readOnlyHint: true`.

**Core (always on):**

| Tool | Returns | Built from |
|---|---|---|
| `life_snapshot` | The call to start with: identity statements, areas, open goals and projects (Active first, each with its status) with what each serves, today's and overdue tasks, habits due today with streaks, appointments in the next 7 days, inbox count, running timers, today's domain XP, top diagnostic findings | `SubjectReader` (`QuerySubjects`, `GetHabits`, `GetHabitOccurrences`, `GetAppointments`, `GetInboxCount`, `GetTimers`, `GetAlignmentEdges`), `RewardReader.GetDomainsAsync`, `DiagnosticRunner` |
| `find_items(query, type?)` | Items whose title or tags match | New `SubjectReader.SearchSubjects` (`ILIKE` over `v_subject` titles and tags) |
| `list_items(type?, status?, area?, tag?, due_before?, due_after?, include_archived?)` | A filtered list, with Browse's filter rules (BROWSE-2) | `QuerySubjects`, plus filters |
| `get_item(id or urn)` | Full detail: relations both ways, people, tags, the last 20 journal entries, status history, timer, repeating series | `GetSubject` / `GetSubjectByUrn`, `GetServes`, `GetServedBy`, `GetPeopleOn`, `GetTags`, `GetJournal`, `GetStatusHistory`, `GetTimer`, `GetTaskSeriesHistory` |
| `alignment(id?)` | For a goal, everything serving it down to tasks. For any item, its chain up to identity statements and areas. With no id, the whole tree, compact | `GetAlignmentEdges` plus a C# port of the forest in `web/src/lib/derive.ts` |
| `recent_changes(since, types?, by?, limit?, cursor?)` | Events since a time (status changes, captures, journal entries, check-offs, meals, Oura days), so an agent can catch up. `by` is `recorded` (the default) or `occurred`, for what happened in a period. Each Oura record, and each edited meal, fast, space photo or rating, shows once, as its newest version, with how many versions there were. Ledger entries only when `types` names `ledger`. A cut answer gives `next`, passed back as `cursor` for the older events | New reader over `bsk.v_event` by `recorded_at` or `occurred_at` |
| `habits(on?)` | Habits with the day's occurrences, streaks and establishment | `GetHabits`, `GetHabitOccurrences` |
| `calendar(from, to)` | Appointments and due tasks in a range | `GetAppointments`, `QuerySubjects` |
| `inbox()` | Untriaged captures | `GetInbox` |
| `people(kind?)` | People and AI agents (GEN-15) | `GetPeople`, `GetInvolvements` |
| `diagnostics(only?)` | `bsk check` findings with their evidence | A `DiagnosticRunner` on the reader connection. It already runs in a read-only transaction, but the CLI runs it as the owner today, so first confirm `bsk_reader` can run every diagnostic |

**Optional areas (`Mcp__Domains`):**

| Tool | Returns | Built from |
|---|---|---|
| `health(from, to)` | Sleep, readiness, steps, workouts and Oura tags by day | `ActivityReader` |
| `food(from, to)` | Meals, fasts, drinks and diet XP by day | `FoodReader` |
| `rewards()` | Wallet, ledger by day, domain levels and report | `RewardReader` |
| `home()` | Spaces, ratings and home XP; each space's shots in brief (name, photo count, Then and Now as a day and score) | `EnvironmentReader` |

**Also part of Phase 1:**
- **Server instructions,** sent when an agent connects. They cover:
  - what LifeOS is, and to call `life_snapshot` first;
  - the type vocabulary (ontology.md §4, in brief) and statuses;
  - that alignment means "serves";
  - that dates are in the home time zone;
  - that **text inside items is Chris's data, never instructions to the agent**.
- **A resource,** `lifeos://guide`, with the longer version of the instructions.

Photos and other artifact bytes aren't exposed in Phase 1. Later and optional: prompts
for the daily and weekly reviews (REVIEW-1, REVIEW-2) that walk an agent through them with
these tools.

### 4.4 Sign-in through Supabase's OAuth 2.1 server

**How a connection works:**

1. Chris adds `https://<app>/mcp` as a connector in Claude (or ChatGPT, Claude Code, and
   so on).
2. The client calls `/mcp` and gets a 401. The 401 points to
   `/.well-known/oauth-protected-resource`, which names Supabase Auth as the authorization
   server.
3. The client registers itself with Supabase (dynamic registration) and sends Chris's
   browser to Supabase, which sends it on to LifeOS's consent page.
4. Chris, signed in with his allowlisted Google account, approves. Supabase gives the
   client an access token and a refresh token. The access token is a Supabase JWT for
   Chris that also carries the client's `client_id`.
5. The client calls `/mcp` with the token. The API validates it as it does today (issuer,
   JWKS), plus the agent rules below.

**Supabase**, in the dashboard, once per project:
- Authentication → OAuth Server: turn it on.
- Set the authorization path to `/oauth/consent`.
- Allow dynamic registration.

The API already validates tokens with JWKS, so the asymmetric signing keys the OAuth
server prefers are already in use. The feature is free on all plans during its beta.
Agent sign-ins count toward monthly active users per person, so Chris counts once however
many agents he connects.

**Web: a `/oauth/consent` route.**
- It reads `authorization_id` from the URL.
- If Chris isn't signed in, it keeps the id (in `sessionStorage`) and signs in with
  Google. Sign-in returns to the origin today (`auth-provider.tsx`), so the app then sends
  him back to the consent page.
- It checks `GET /api/me`. A non-allowlisted account sees "This account can't connect
  agents to LifeOS", and can turn the request down or sign out; it can't approve.
- It shows the client's name, the host it will send Chris back to, and what it asks for.
- Approve and Deny use `supabase.auth.oauth.getAuthorizationDetails`,
  `approveAuthorization` and `denyAuthorization`, then follow the returned redirect.
- Tests: unit tests against a mocked Supabase, an e2e golden path against the mock seam,
  and a11y in light and dark, like the other pages.

**API:**
- A new policy, `McpAgent`, which `/mcp` requires: an allowlisted email on a Google
  account, a `client_id` claim on a session the OAuth server started (its `amr`; see
  "Fixes" below), and a client that isn't in `Mcp__BlockedClients`.
- **The existing `AllowlistedUser` policy refuses any token with a `client_id`.** So an
  agent's token can't call `/api/*`, which includes every write. Test both directions.
- Protected resource metadata, anonymous, published through the SDK's MCP auth handler:
  the resource is `https://<host>/mcp` and the authorization server is the Supabase
  issuer. Every 401 from `/mcp` carries `WWW-Authenticate` with `resource_metadata`.
- `SpaFallback.IsReserved` adds `/mcp` and `/.well-known`, so neither ever returns
  `index.html`.
- No CORS for `/mcp` in production. Claude and ChatGPT call it from their own servers.

**Verify on staging before building further** (step 1.3), and record the findings here:
- **The claims.** Decode a real agent token and check `iss`, `aud` (expected
  `authenticated`), `email`, `email_verified` and `client_id`. Supabase's docs list only
  the user id, role and `client_id` as defaults. If the email is missing, allowlist agent
  tokens by user id instead (`ALLOWED_USER_IDS`).
- **What the token can do at Supabase itself.** It's a Supabase token for Chris, so check
  each of Supabase's own APIs:
  - REST: the `bsk` schemas aren't exposed, and `public` holds only `schema_migrations`
    behind RLS;
  - Storage: the bucket is private and has no policies;
  - Auth's user endpoints.

**Checked on staging (2026-10-28).** Claude, as a claude.ai custom connector, connected
through the consent page. Its client id is `2c63d7f7-0ab1-47a2-9dbd-b765f66b7763`, and an
`inbox` call through it was logged ok in 1.2 s.

- **The claims.** The token itself wasn't decoded: Claude keeps it on Anthropic's servers,
  so nobody here sees it.
  - The API accepted it, so it has issuer `https://zvhxctrffbrmhbvujhzw.supabase.co/auth/v1`,
    audience `authenticated`, the project's signature, an unexpired lifetime, a `client_id`,
    and Chris's allowlisted email.
  - It was also accepted as verified, through one of three things the API takes: an
    `email_verified` claim, `user_metadata.email_verified`, or a Google sign-in in
    `app_metadata`.
  - So allowlisting agents by email works, and `ALLOWED_USER_IDS` isn't needed.
  - Supabase's docs list the claims: `sub`, `role`, `aud` (`authenticated`), `user_id`, `email`,
    `client_id`, `aal`, `amr`, `session_id`, `iss`, `iat` and `exp`. Their example has an hour
    between `iat` and `exp`.
  - There's no `scope` claim. Scopes shape only OIDC's user info, not what a token can reach.
  - Which of the three made it verified: Supabase Auth's source settles it. Every access
    token it issues, an agent's included, carries the user's `app_metadata` and
    `user_metadata`, an `amr` (how its session signed in) and no top-level
    `email_verified`. So Chris's tokens passed through `user_metadata` or the Google provider
    in `app_metadata`, and fix 2 below keeps only the second.
  - The `amr` method shows how a session signed in:
    - a Google sign-in is `oauth`;
    - a password is `password`, a magic link `otp` or `magiclink`;
    - an agent's token is `oauth_provider/authorization_code`, a session of its own that the
      OAuth server starts when the agent trades its code.
  - Refreshing keeps a session's `amr`. It names no provider (only SSO entries carry one),
    which is why the account's provider comes from `app_metadata`.
- **What it can do at Supabase itself.** Checked from outside with the public key, and in
  Supabase Auth's source (`supabase/auth` main, read 2026-10-28):
  - REST exposes only `public` and `graphql_public`. `bsk`, `bsk_derived` and `bsk_ops`
    answer `PGRST106`, `public.schema_migrations` refuses anon, and GraphQL isn't enabled
    (`pg_graphql` is off). Nothing of the life record is reachable.
  - Storage shows no buckets. The bucket is private with no policies, so a user's token
    reads nothing.
  - **Auth treats an agent's token as Chris signed in.** No user endpoint checks
    `client_id`. With an agent's token, anyone can:
    - **Set a password on Chris's account** (`PUT /user`). He has none, so no current
      password is asked for. Within 24 hours of the approval no reauthentication is asked
      either, even with secure password change on. Signing in with his email and that
      password then gives an ordinary session, and `/api` accepts that in full, writes
      included. This needs the Email provider, which **is on in staging**, with sign-ups
      open.
    - **Approve other agents' requests as Chris**
      (`POST /oauth/authorizations/{id}/consent` takes any of his tokens). An agent could
      register a new client and approve it itself, so `Mcp__BlockedClients` can't keep a
      determined agent out. `Mcp__Enabled=false` still can, and fix 3 now does too.
    - Sign him out everywhere (`POST /logout?scope=global`).
    - Add MFA factors.
    - List or revoke his agent grants.
    - Read his user record (email, Google identity) and change its `user_metadata`.
  - Changing his email still needs the confirmation emails, so a token alone can't.
- **Fixes.** Chris chose 1 and 2 on 2026-10-28, and 3 with the access log the same day.
  1. **Turn off the Email provider** in Supabase (Authentication → Sign In / Providers →
     Email), in staging and production.
     - **Done in staging (2026-10-28).** Supabase's settings list only Google, and a
       password sign-in answers `email_provider_disabled`.
     - There's no production project yet. Its setup steps in `deploy.md` now include
       this switch.
     - Supabase then refuses password and magic-link sign-in (checked in its source), so a
       password set by an agent opens nothing.
     - Nothing in LifeOS signs in by email.
     - Turning off sign-ups as well keeps strangers' Google accounts out of the user list.
  2. **The API accepts a web session only if it was signed in with Google**, and stops reading
     `email` or `email_verified` from `user_metadata`, which the user (so any agent token)
     can write.
     - Before, that fallback was used only when a token had no top-level email. That never
       happened while anonymous and phone sign-in stayed off, so there was no hole yet.
     - This is a second wall should Email ever come back on.
     - **Done.** `src/LifeOs.Api/Auth/SupabaseClaims.cs` reads only the top-level `email`,
       `amr` and `app_metadata`.
     - `AllowlistedUser` (the web and `/api`) needs a Google account (`app_metadata`) whose
       session's `amr` has `oauth`. A password or magic-link session for the same account
       is 403.
     - `McpAgent` needs a Google account whose session's `amr` has
       `oauth_provider/authorization_code`, plus the `client_id`. So a `client_id` on any
       other session is refused.
     - Refusing the allowed account for how it signed in logs a warning with the `amr`
       methods, so a lockout is easy to spot.
     - Tests mint Supabase-shaped tokens (`tests/LifeOs.Tests/SupabaseTokens.cs`). A
       hand-made test token now needs `amr` and `app_metadata` as well as `email`.
     - Checked on staging after the deploy (671641c, 2026-10-28): Claude's real token
       passed (`inbox` ok at 16:29 UTC, after the new release started), so it carries the
       Google provider in `app_metadata` and the OAuth server's `amr`. The logs show no
       refusals.
     - Chris's web session on staging also loaded fine after a reload, so a Google
       sign-in's token passes too.
  3. **LifeOS keeps its own allow list of agents** (`bsk_ops.agent_client`, migration 0047).
     `/mcp` answers only a client on it and not revoked, so a client an agent approved for
     itself at Supabase is refused (403), even with a valid token.
     - Chris chose this over a setting (`Mcp__AllowedClients`) and over relying on
       Supabase's grants. A setting would cost a `fly secrets set` and a restart per agent;
       Supabase's grants are exactly what an agent's token can add to.
     - **Approve** on LifeOS's consent page adds the agent first (`PUT /api/agents/{id}`),
       then approves at Supabase. If LifeOS can't add it, the request isn't approved.
     - Only `/api` changes the list, and only Chris's Google web session reaches `/api`
       (fix 2), so no agent can add itself or another.
     - The list is read on every `/mcp` request, not cached, so **Revoke** in Connected
       agents takes effect on the agent's next call, not when its token expires.
     - If the list can't be read, every agent is refused, and the error is logged.
     - A refused call from a client not on the list is logged (at most once a minute per
       client), so an agent approved behind Chris's back shows up in Connected agents.
     - One-time cost: Claude was approved before the list existed, so after the deploy
       Chris clicks **Allow** for it in Connected agents. A request approved before skips
       the consent page and goes straight back to the agent, so reconnecting alone won't
       add it; Allow will.
     - `Mcp__BlockedClients` stays, as an emergency override that needs no database.

**Known limits:**
- Supabase's OAuth server is in beta.
- Dynamic registration reportedly rejects custom redirect schemes like `cursor://`
  (supabase/auth #2285). That affects Cursor, not Claude or ChatGPT, which return to https
  addresses.
- Supabase's tokens aren't bound to `/mcp` as their audience, which the MCP spec
  recommends. The `client_id` rules above do that job instead.

### 4.5 Access log and Connected agents

**The tables** (migration 0047, both in `bsk_ops`: operational, owner-only, never granted
to `bsk_reader`; `bsk_backup` reads them through 0046's default privileges):
- `bsk_ops.agent_client`, the allow list (§4.4 fix 3): client id, the name and website the
  consent page showed, when Chris allowed it, when he revoked it (allowed while that's
  empty), and the AI-agent Person it acts as.
- `bsk_ops.agent_call`, the log, one row per tool call: when, `client_id`, the tool and its
  arguments as JSON trimmed to 1 KB, ok or error, how many items its lists returned, and
  milliseconds. A call refused because the client isn't allowed is a row too, with no tool,
  logged at most once a minute per client.

The call-tool filter in `McpHost.cs` writes the row on the owner connection, beside the JSON
log line it already wrote. A failed write is logged and never fails the call. Rows older than
90 days are deleted, at most once an hour, when a row is written.

**`/api/agents`** (`src/LifeOs.Api/Http/AgentEndpoints.cs`), which agent tokens can't reach:
- `GET /api/agents`: every client on the list plus any client refused in the last 90 days,
  each with its last call, its calls this week (Monday to Sunday in the home zone) and the
  same for refusals; whether `/mcp` is on; and the writes level (`off`, read-only).
- `GET /api/agents/calls?clientId=&limit=`: the latest calls, newest first (20 by default,
  at most 200).
- `PUT /api/agents/{clientId}` with `{name, clientUri, redirectUri, replaces}`: allow, or
  allow again after a revoke. Allowing one already allowed keeps when it was first allowed.
  `replaces` names earlier clients of the same agent (see "Connecting again" below).
- `POST /api/agents/{clientId}/revoke`: `/mcp` refuses its next call.
- `PUT /api/agents/{clientId}/person` with `{personId}` (or null): link it to an AI-agent
  Person. A human Person, or an archived one, is refused in words.

**"Connected agents" on the People page** (`web/src/components/people/connected-agents.tsx`,
seam in `web/src/lib/agents.ts`). It joins Supabase's grants (supabase-js `listGrants`)
with LifeOS's list, so each agent is in one of four states:
- **Connected** (approved at Supabase, allowed here): when Chris allowed it, its last call,
  its calls this week, its recent calls with their arguments, **Acts as** (link to an AI
  Person; Phase 2 requires it before the agent can write), and **Revoke**.
- **Not allowed** (approved at Supabase, not here): shown first, with a warning to allow it
  only if Chris connected it himself, and how often LifeOS refused it. **Allow** (asks
  first) or **Remove** (removes Supabase's grant).
- **Disconnected** (allowed here, but Supabase no longer lists it): **Revoke**.
- **Revoked**, or **Refused** for a client LifeOS never allowed and knows only by its
  refused calls.

**Revoke** takes the agent off LifeOS's list first, so `/mcp` refuses its next call, and
then removes Supabase's grant so it can't get new tokens. If Supabase fails, the section
says LifeOS refuses it anyway. The section also says when `/mcp` is off and that agents
only read.

If Supabase can't list the grants, the section shows LifeOS's list alone and says so. In
mock mode a stand-in plays both, seeded with Claude connected and a helper LifeOS refuses.

**Connecting again** (migration 0048, 2026-10-28). Claude registers a new client with
Supabase each time it goes through Connect again: removing and re-adding the connector,
connecting again, or after its sign-in stops working. Everyday use renews its tokens under
the same client. Before this, each reconnect left a new entry that acted as no one and an old
one still allowed. Now:
- `agent_client` keeps `redirect_uri`, the address the consent page sent the browser back to,
  and `replaced_by`. The migration fills in `redirect_uri` for clients allowed before it, from
  Supabase's `auth.oauth_clients` where the owner can read it.
- The consent page looks for clients on the list with the **same name and the same return
  address** that aren't already replaced and are still allowed or act as someone. The name is
  the client's own say, but only claude.ai receives a sign-in sent to claude.ai's address, so
  the two together identify the agent. Claude Code is a separate client (`claude://…`).
- When it finds one, it shows **Replace the earlier connection**, ticked, saying when the
  agent connected before and which Person it acts as. Approving then, in one transaction:
  - allows the new client;
  - links it to the Person the most recently allowed of them acts as, if the new client acts
    as no one yet and that Person is still an AI agent in use;
  - revokes the earlier ones and records `replaced_by`.

  It then removes their grants at Supabase. That's best effort, since LifeOS already refuses
  them. Unticked, both are kept, as before.
- The API refuses to replace a client with a different name or return address, itself, or
  one not on the list, and then changes nothing.
- The earlier clients keep their own Person link, so whatever they did stays "via" that
  Person. Connected agents shows them as **Revoked** with "Replaced …, when Claude connected
  again", and offers **Remove** if Supabase still lists one.

### 4.6 Hosting

Nothing new. Staging scales to zero on Fly, so the first agent call after an idle spell
waits a few seconds for a cold start, and a client may time out once. If that gets
annoying, set `min_machines_running = 1`, which costs a few dollars a month. Decide after
using it.

### 4.7 Build order

| Step | What | Done when |
|---|---|---|
| 1.1 | Tools and server instructions, with `Mcp__Enabled` off by default | Tests through the SDK's in-memory client, against Testcontainers with a seeded life, return the shapes in §4.3. Read tools never open the owner connection |
| 1.2 | Auth in the API: policies, metadata, fallback | Tests pass: no token → 401 with the metadata pointer; a web session token on `/mcp` → 403; an agent token on `/api/*` → 403; an agent token on `/mcp` → 200; a blocked client → 403; a non-allowlisted account → 403 |
| 1.3 | OAuth server on in staging Supabase, and the consent page | Claude connects to staging. The token's claims are checked and recorded in §4.4 |
| 1.4 | Access log and Connected agents | Every call shows up, and Revoke works |
| 1.5 | Deploy staging with `Mcp__Enabled=true` | Chris asks Claude for a review of his week, and the log shows the calls it made |
| 1.6 | Use it for a week or two | Chris decides when to start Phase 2 |

**Progress.**
- **1.1 and 1.2 done (2026-10-28),** and deployed with `Mcp__Enabled` unset, so off: `/mcp`
  and its metadata answer 404. `tests/LifeOs.Tests/McpServerTests.cs` covers both steps:
  - A seeded life in a database of its own, read through the SDK's real HTTP client with an
    agent token: every tool's shape, the instructions, the guide, and errors an agent can
    read (an unknown id, a bad type, a `since` it can't parse, a range too long).
  - The sign-in table in 1.2, plus: `/api`'s 401s keep their plain challenge, and an
    unverified account is refused too.
  - Areas switched off lose their tools, the snapshot's domain XP, and their events in
    `recent_changes` (checked with a made-up Oura reading, never real data).
  - The tools are built from the readers alone (checked by reflection), the readers are
    built on the reader connection, and `bsk_reader` runs every diagnostic. So no tool can
    open the owner connection.
- Choices made while building, for 1.3 onward to keep in mind:
  - A Reward is in the rewards area and a Space in the home area: with the area off, those
    items drop out of every tool, not only the area's own tool.
  - `recent_changes` takes `since` as an ISO instant, a home-zone day, or a span back from
    now (`24h`, `3d`, `2w`, `P3D`), defaulting to 7 days and reaching back at most a year.
  - `life_snapshot`'s lists stop at 25 rows each; the other lists take `limit` (default 50,
    at most 200). `alignment` stops at 400 nodes.
  - A 401 from `/mcp` names the metadata in `WWW-Authenticate`; the metadata names the
    Supabase issuer (`Auth__Issuer`) as the authorization server and leaves scopes out.
    Check in 1.3 that Claude's connector is happy without scopes.
  - Every tool call writes a log line (tool, client id, outcome, milliseconds), never its
    arguments. The access log table is step 1.4.
- **1.3 under way (2026-10-28).** Staging Supabase has the OAuth server on: Site URL
  `https://lifeos-mz55-staging.fly.dev`, authorization path `/oauth/consent`, dynamic
  registration allowed. Its metadata
  (`https://zvhxctrffbrmhbvujhzw.supabase.co/.well-known/oauth-authorization-server/auth/v1`)
  lists the registration endpoint and the scopes `openid email profile phone offline_access`.
- **The consent page is built:** `web/src/pages/oauth-consent-page.tsx`, with its seam in
  `web/src/lib/oauth-consent.ts`. It renders on its own, outside the app shell and its
  sign-in gate (`main.tsx`). Choices made while building:
  - Signed out, it keeps the request in `sessionStorage` for 15 minutes and signs in with
    Google. The sign-in gate, once a session exists, sends the browser back to it.
  - An account LifeOS doesn't allow sees "This account can't connect agents to LifeOS" with
    **Turn the request down** and **Sign out**, rather than an automatic deny, so signing in
    with the wrong account doesn't throw the request away.
  - It shows the agent's name, the account, the host it sends Chris back to, the agent's
    website, and the scopes in words, and warns to approve only an agent he just started
    connecting.
  - It follows only `http` and `https` addresses back to the agent.
  - A request approved before goes straight back to the agent.
  - In mock mode a pretend request from Claude stands in (`mock-expired` shows the expired
    state). Tests: unit tests against a mocked supabase-js and each page state, golden paths
    for approve, deny and expired, and a11y in both themes and on a phone.
- **Connected (2026-10-28).** `Mcp__Enabled=true` is set on staging, and Claude connected
  through the consent page.
  - What the token proves, and what it can do at Supabase, are recorded in §4.4 under
    "Checked on staging".
  - The biggest finding: with Supabase's Email provider on, an agent's token can set a
    password on Chris's account and sign in as him, around the read-only rule.
  - Fixes 1 (Email provider off) and 2 (the API takes only Google sign-ins and ignores
    `user_metadata`) are done and checked on staging. Fix 3 (an allow list of clients)
    waits for 1.4.
  - That finishes step 1.3.
  - The first real call turned up a naming problem. `inbox` returned the view's
    `triaged_at`, which is when an item came into the inbox, but an agent reads it as
    "already triaged". It's now `flagged_at`, and the tool says it lists newest first, as
    it always has.
- **1.4 built (2026-10-28):** the allow list (fix 3), the access log and Connected agents,
  as §4.4 and §4.5 describe. Migration 0047.
  - .NET: `tests/LifeOs.Tests/AgentAccessTests.cs` covers these cases:
    - a client not on the list gets 403, with its refusal logged once a minute;
    - allowing and revoking take effect on the next call, and allowing again works;
    - agent tokens are refused on every `/api/agents` route;
    - bad input, and linking to an AI Person only;
    - every agent is refused when the list is missing or can't be read;
    - call rows carry arguments, outcome, items and time, with arguments trimmed to 1 KB;
    - the Monday week, and the 90-day cleanup.

    `McpServerTests` now allows its agent first.
  - Web:
    - unit tests for the seam (the four states, the mock);
    - unit tests for the section (allow, revoke, remove, recent calls, MCP off, Supabase
      down);
    - unit tests for the consent page (it allows before approving, and doesn't approve when
      LifeOS can't add the agent);
    - a golden path, and a11y in both themes, with a dialog open, and on a phone.
  - Deployed (3942e2b, 2026-10-28; migration 0047 applied). Chris allowed Claude in
    Connected agents and reconnected it. Reconnecting registered a new client,
    `f1ec147b-08d1-4cc3-93fd-1bed4aba6b93`, which the consent page added to the list. An
    `inbox` call through it ran ok at 17:18 UTC, and the logs show no failure writing the
    call row or reading the list.
  - The first client (`2c63d7f7-…`) may still be listed. If Connected agents shows two
    Claudes, revoke the one with no recent calls.
- **Connecting again keeps the agent (2026-10-28).** Staging showed three Claude entries in
  Connected agents:
  - the first claude.ai client, no longer used;
  - Claude Code, a separate client with its own return address;
  - the live claude.ai client.

  Claude.ai had also registered two more clients that were never approved, and those don't
  show. So each reconnect added an entry that acted as no one. Migration 0048 and the
  consent page now replace the earlier client and carry its Person over (§4.5, "Connecting
  again"). Tests:
  - .NET: the replacement carries the newest Person and revokes the earlier ones, which
    `/mcp` then refuses; a different name, a different return address, itself, an unknown
    client or a bad id is refused and changes nothing; a Person of its own is kept, and an
    archived one isn't carried.
  - Web: unit tests for `replaceable`, the mock, the replaced row, and the consent page
    (ticked, unticked, several earlier clients, Supabase failing to drop a grant, the list
    unreadable, Approve waiting for the list); the golden path; and a11y in both themes and
    on a phone.
- **First feedback from a real week review (2026-10-28).** An agent reading a week through
  the tools hit four problems, all fixed in the tool layer, with no migration:
  - **Oura versions drowned `recent_changes`.** Oura re-sends a day as it changes, and each
    change is a new version of the same record. Now each record shows once, as its newest
    version in the window, with `versions` saying how many there were. The record is the
    `external_id` without its last two parts (the hash and what it replaces).
  - **A backfill counted as recent.** `since` went by when LifeOS recorded an event, so a
    month of Oura days synced today read as today. `by: "occurred"` goes by when it happened
    instead, and the description tells agents to use it for a week review. `recorded` stays
    the default, for catching up.
  - **Ledger churn.** A day of meals writes many earns and reversals. `recent_changes` now
    leaves ledger entries out unless `types` names `ledger`, and points to `rewards`, whose
    daily ledger already gives the net per currency per day.
  - **`home` repeated itself.** Each shot carried two whole photos, each with ids, an
    artifact id, the space's name and all three parts again. Each shot is now its name, its
    photo count, and Then and Now as a day and the space's score (Now left out while it's
    the baseline).

  Tests: a made-up Oura record with three versions, a month back and recorded within the
  hour, plus a ledger earn and a capture, read by either time and with and without
  `ledger`; and the brief `home` shape.
- **1.5 done (2026-10-28).** Chris had Claude check the API against his week through
  `/mcp`, and Connected agents logged the calls. It passed, with four issues:
  - **Edited meals and space photos showed twice.** Editing a meal writes it again under
    the same id, so a meal moved to another time showed at both; a photo moved to a shot of
    its own showed under shot "main" and again under its new shot. `recent_changes` now
    keeps the newest version of a meal, fast, space photo or rating as well as an Oura
    record, by the id in the payload, as their views do. It looks for the newest version
    outside the window too, so with `by: "occurred"` a meal moved out of the window no
    longer leaves its old version in it. On staging, Chris's week by when it happened went
    from 252 events to 157.
  - **`life_snapshot` showed only goals and projects marked Active** (2 projects, no goals),
    while `list_items status=open` and the diagnostics counted 5 and 2: three projects and
    two goals have no status set. The snapshot now lists every open one, `open_goals` and
    `open_projects` (renamed from `active_*`), Active first and each with its status, and its
    summary says how many are Active. The web's dashboard still picks its focus from Active
    goals only.
  - **A week came back 194 events against a 200 cap.** `recent_changes` now pages: a cut
    answer gives `next` (the last event's time and id, as `o:<ticks>:<id>`), and passing it
    back as `cursor` with the same `since`, `types` and `by` gives the older events. A
    cursor for the other `by`, or one it didn't give, is refused in words. The cap stays
    200.
  - **A timer ran 12 minutes on a completed task** (TASKS-2, a kernel and web bug, not the
    tool layer). On staging the dishwasher task shows start, stop, Completed (with its XP)
    and then a new 12-minute session. Complete sits next to Stop and looks the same, and it
    closed the task with nothing asked; the panel then still offered Start on the completed
    task, and the kernel accepted it. Now Complete asks first, a closed task's panel has no
    Start, the kernel refuses Start and Resume on a closed task, and closing a task through
    Change status stops its timer first (ui-requirements.md, task timer).

  The check also noted that "New Wardrobe" keeps the URN of an earlier title. That's by
  design: a URN is made once, at creation, and stays so links and agents' references keep
  working; the title is what changes.

  Tests: .NET for the closed-task rules (refused and reopened, closing stops a running or
  paused timer, a timer left on a closed task can still be stopped), the snapshot's open
  lists, and a fixture with an edited meal, a meal moved out of the window and a photo moved
  to its own shot, read by either time and paged two at a time (1,247 pass); web unit tests
  for the mock's rules, the panel (Complete asks, Cancel changes nothing, no Start once
  closed, only Stop for a timer left running), and `availableActions`; a golden path; and
  a11y with the Complete dialog open.

To connect, add `https://lifeos-mz55-staging.fly.dev/mcp` as a custom connector in Claude
or ChatGPT. In Claude Code:

```bash
claude mcp add --transport http lifeos https://lifeos-mz55-staging.fly.dev/mcp
```

## 5. Phase 2: writes behind a switch

### 5.1 The switch

`Mcp__Writes` has three levels: `off` (the default), `capture` and `full`. A write tool is
listed and works only when all three hold:
- the level includes it;
- the calling client is in `Mcp__WriteClients`;
- the client is linked to an AI-agent Person (§4.5).

Otherwise the tool isn't even listed, so agents don't see it, and it's refused if called
anyway.

```bash
fly secrets set Mcp__Writes=capture -a lifeos-mz55-staging
```

Setting it back to `off` the same way turns writes off again.

**Rollout:**
1. `capture` on staging for a week or two;
2. then `full` on staging;
3. production, once it exists, starts at `off`.

### 5.2 Tools by level

**`capture`** adds things and changes nothing that already exists:

| Tool | Does | Calls |
|---|---|---|
| `capture(text, kind = note \| idea \| problem, concerning?)` | An Inbox capture (CAP-1), which Chris triages like any other | the service behind `POST /api/capture` |
| `add_journal_entry(item, text)` | Appends to an item's journal (JOURNAL-1) | `POST /api/journal` |

**`full`** adds these:

| Tool | Does | Calls |
|---|---|---|
| `set_status(item, status)` | Completes, cancels, starts, and so on, within the type's own statuses | `POST /api/status` |
| `check_off_habit(habit, day, done)` | Records adherence (GEN-5) | `POST /api/adhere` |
| `timer(task, start \| stop)` | Starts or stops a task timer (TASKS-2) | `POST /api/timer` |
| `create_item(type, title, fields, parent?)` | A new item, under the type's form rules, optionally under a parent (GEN-6, GEN-7) | `POST /api/subjects` |
| `relate(from, to, kind)`, `tag(item, tags)` | Relations and tags | `POST /api/relate`, `/api/link`, `/api/tag` |
| `log_meal(...)` | A Food Log entry (FOOD-1), only if `food` is exposed | `POST /api/food/meals` |

**Never offered** (no tool exists at any level):
- archive or restore;
- any delete: meals, photos, ratings, shots;
- renaming tags;
- rewards: redeem, price, target, bounty (coins are real money);
- exception days and settle jobs;
- Oura connect, disconnect or sync;
- covers.

Every tool calls the same Application service its `/api` endpoint calls, never SQL, so
every rule and invariant holds. The API is still a door into `bsk`, the only writer
(invariant 7).

### 5.3 Attribution

- Each call writes with `source_id = mcp:<client_id>`. Today the kernel's source is
  fixed for each host (`AddLifeOsKernel(owner, KernelSources.Api, …)`), so Phase 2 needs a
  source set per request: for example, a scoped source the services ask for, which
  defaults to the host's. Add `KernelSources.Mcp` as the prefix.
- Provenance is `declared` (decision 4 in §2).
- The event records the agent's Person, so the web can show "via Claude" on inbox items,
  journal entries and history rows. Where that reference lives is settled when building
  2.1.

### 5.4 Safety details

- **Annotations.** Write tools are `readOnlyHint: false`. `capture`,
  `add_journal_entry` and `create_item` are `destructiveHint: false`, since they only add.
  `set_status` is `idempotentHint: true`. Clients like Claude ask Chris before using a tool
  that isn't read-only, unless he has allowed it.
- **Retries.** Each write tool takes an optional `request_id`, and the server
  instructions tell agents to send a new UUID for each write they intend. The id becomes
  the event's `external_id` under `mcp:<client_id>`, so a retried call writes once
  (invariant 5).
- **Undo** works the kernel's normal way: set a status back, or dismiss a capture in
  triage. Nothing an agent can do deletes anything.
- **Records.** The access log records writes like reads, and the event store keeps them
  for good.

### 5.5 Done when

Tests show:
- With writes off, no write tool is listed, and a call to one is refused.
- At `capture`, only the capture tools are listed.
- A client that isn't in `Mcp__WriteClients` stays read-only.
- Events carry the source and provenance from §5.3.
- A retried `request_id` writes once.
- An agent token still can't reach `/api/*`.

On staging: Chris captures to his Inbox by voice through Claude on his phone (CAP-3's first
step) and triages it in LifeOS.

## 6. Phase 3: an assistant that works on its own

**Asked by Chris on 2026-10-28 and 2026-10-29.** Once agents can write, he wants Claude to
act as his assistant without him opening a chat. Ideally LifeOS tells Claude when something
changes and Claude decides whether to act; at the least, Claude checks often. The jobs range
from adding a task or commenting on an item to code fixes. On 2026-10-29 he gave it a shape:
he **assigns** a LifeOS Task to Claude, and Claude does it on its own, including fixes from
his `#lifeos` fix queue, with no separate Claude Code prompt.

Phase 3 starts with a **prototype on two repos** (step 3.1 in §6.6): a desktop one,
BlueSkies.Platform, and a web one, LifeOS. It proves the loop works before LifeOS builds
anything for it. The rest needs Phase 2's writes.

### 6.1 What can wake Claude (checked 2026-10-28)

Nothing from outside can start a turn in a claude.ai chat. MCP's own change notices
(`list_changed`, resource subscriptions) don't start one either: clients use them only to
refresh a session that's already open. So something has to run Claude:

| Runner | What starts it | How soon | Needs Chris's PC | Billing | Good for |
|---|---|---|---|---|---|
| **Cloud routine, fired** | LifeOS (or anyone holding its token) POSTs to the routine's own URL | A cloud run has to start up, so minutes rather than seconds (3.1 measures it) | No | Chris's subscription. At most 30 fires an hour per routine and 100 an hour per account | Life jobs, and code jobs in repos that build on Linux |
| **Cloud routine, on a schedule** | Its schedule, at most once an hour | Up to the interval | No | Subscription | A backstop for a fire that went missing |
| **Desktop app scheduled task** | Its cron, set through chat, as often as every minute | Up to the interval | Yes, with the Claude app open | Subscription | Code jobs that need the PC. The daily report already runs this way (`lifeos-daily-report`) |
| `claude -p` from Task Scheduler | Windows Task Scheduler | Up to the interval | Yes | Subscription, unless `ANTHROPIC_API_KEY` is set, which silently bills the API instead | A fallback for the desktop task. claude.ai connectors may not load |
| Channels | Events pushed into an interactive Claude Code session that's kept open | Seconds | Yes, with a session kept open | Subscription | Not chosen: a research preview that needs a development flag |
| Managed Agents (API) | LifeOS sends events to an agent session that keeps running | Seconds | No | The API, separate from the subscription, plus $0.08 for each hour a session runs | Later, if minutes turn out too slow |

About routines, which are a research preview:
- A routine's API trigger is a URL and a bearer token of its own. A fire can carry up to
  65,536 characters of `text`. The run treats it as untrusted, so the routine's prompt says
  what to do with it.
- A fire has no idempotency key, so a retried fire starts a second run.
- Chris's claude.ai connectors, LifeOS included, attach to a routine by default. Servers added
  with `claude mcp add` don't.
- A routine can have GitHub repos attached, which each run clones.
- The docs don't settle three things, which 3.1 does: whether the connector's sign-in renews
  during a run nobody is watching, whether a routine can be fired with no repo attached, and
  which branches a run may push to.

### 6.2 Handing Claude a job

**A job is a Task assigned to Claude.** The kernel already has the role: a People-association
(`attributes.people`, `bsk involve`) with the role `assignee`, and each of Claude's clients
acts as an AI-agent Person (§4.5). The web has `writes.involve(subject, person, role)`, but
only Commitments use it today, so Tasks need an **Assign to** control (step 3.2).

- **Only Chris's own words make a job.** The job is the assigned Task's title, its
  description and the journal entries Chris wrote on it. The assignment counts only if Chris
  made it himself, not through `mcp:*`. Everything else stays data, as the server
  instructions say today: captured pages and links, other items, and anything an agent
  wrote. The instructions gain this one exception, worded that narrowly.
- **The Task's tags say what kind of job it is:**
  - A repo tag makes it a **code job**, worked in that repo by the runner the repo needs
    (§6.4): `lifeos` is `csdelan/LifeOS`, and `blueskies` is `csdelan/BlueSkies.Platform`.
  - Without one, it's a **life job**, done through `/mcp` alone: add Tasks under a Project,
    write a journal entry, set a status.
  - The map from tag to repo lives in the runner's prompt for the prototype, and in a
    setting after that.
- **The Task's status carries the conversation:**

  | Status | Means |
  |---|---|
  | Not started | Assigned, waiting for Claude |
  | In progress | Claude has picked it up. Each runner works one job at a time |
  | Waiting | Claude needs Chris: a question, or a change ready for him to approve. The journal says which |
  | Completed | Done. Claude's last journal entry says what changed, with its commits, pull request and deploy |
  | Cancelled | Chris changed his mind |

  When Chris answers in the journal of a Waiting Task, the job goes back to Claude. Chris
  closes a code job once he's checked it, unless decision 3 (§6.5) lets Claude close it.

### 6.3 What LifeOS adds

1. **The queue,** a read tool (name to settle, for example `my_jobs`). It lists Tasks
   assigned to the calling agent's Person that are Not started, or Waiting with a journal
   entry from Chris newer than the agent's last. They come oldest first, each in full detail.
   - It leaves out the agent's own writes, so Claude never reacts to itself.
   - It needs no bookmark: every run starts with no memory, and the Task's status and journal
     already hold where the job stands.
2. **Writes scoped to the job.** These are Phase 2's tools, narrowed: an assistant may set the
   status of, and write journal entries on, only Tasks assigned to it. It may create Tasks
   only under those, or under the Project they serve. An unattended run has nobody to click
   Allow, so the client's ask-first (§5.4) can't be the safety. The server has to be.
3. **The fire.** When Chris assigns a Task to an agent, or answers in the journal of one
   that's Waiting, LifeOS fires that agent's routine:
   - The body names only the Task ids that changed, never their text. Claude reads them
     through `/mcp` with its own sign-in.
   - Fires for one agent are held for a minute and sent as one, so a burst of edits starts
     one run.
   - A fire that fails is logged and left to the backstop schedule. LifeOS doesn't retry,
     since a retry could start a second run.
   - The fire URL and token are Fly secrets for each agent, for example
     `Assistant__Agents__<person>__FireUrl` and `__FireToken`. The fire log goes in `bsk_ops`
     beside `agent_call`.

   This changes §9's "LifeOS itself never pushes". It pushes a wake-up, and nothing of
   Chris's life.
4. **The web:**
   - **Assign to** on a Task's detail panel, with AI agents in the same selector as people
     (GEN-15);
   - on the Tasks page, what Claude is doing: working, asking Chris, or waiting for his
     approval.

### 6.4 The runners

- **Cloud: a routine for each assistant,** for example `lifeos-assistant`. It has the LifeOS
  connector and the repos of cloud code jobs attached. LifeOS fires it, and a schedule backs
  that up (the interval is set after 3.1). Its prompt:
  - read the queue and take the oldest job;
  - mark it In progress and do it;
  - write what happened in the journal, then set Waiting or Completed;
  - when the job is unclear, ask in the journal rather than guess.
- **PC: a desktop app scheduled task,** for repos that need the PC. BlueSkies.Platform does:
  it has Windows-only projects (`net10.0-windows`), and it publishes to `\\bart\MyNuget` and
  `\\bart\BuildShare`. Fly can't reach the PC, so this runner checks the queue on a timer,
  every 15 or 30 minutes. Each check costs some usage even when the queue is empty, which 3.1
  measures. It works in a git worktree of its own, never in Chris's checkout, since his own
  sessions commit there.
- **Code jobs follow each repo's own rules:** its AGENTS.md or CLAUDE.md, and its test
  script. Approval before anything deploys or publishes is decision 3 (§6.5):
  - in LifeOS, a push to `main` deploys staging, which holds Chris's real data;
  - in BlueSkies.Platform, publishing a package follows the `nuget-publish` skill's rules on
    when to ask.

### 6.5 Decisions (Chris)

1. **How Chris hands Claude a job.** Leaning: assign a Task (2026-10-29). The alternatives
   are an "@claude" line in any item's journal, or Claude watching every change and deciding
   for itself. Watching everything is noisier, costs more and is riskier.
2. **How soon.** Recommended: within minutes, through the fire, for cloud jobs. The other
   choices are within the hour (the schedule alone, with no fire to build) or by morning
   (folded into the daily report).
3. **What Claude may do without asking.** Recommended:
   - Life jobs: add Tasks and journal entries, and set the status of its own Task. Completing
     or cancelling anything else waits for Chris.
   - Code jobs: push a branch, open a pull request, set the Task Waiting, and let Chris merge,
     which deploys. The alternative is pushing small changes straight to `main`, as agents
     working with Chris do today. Publishing a package stays with Chris.
4. **Which repos.** The prototype uses LifeOS and BlueSkies.Platform. Others join one at a
   time.

### 6.6 Build order

| Step | What | Done when |
|---|---|---|
| 3.1 | **Prototype on two repos.** No LifeOS code, so it runs on Phase 1. Chris makes two small real Tasks. Until Assign to exists, the tag `claude` stands for the assignment. One is tagged `lifeos` (web: a fix from his queue). The other is tagged `blueskies` (desktop: a small change in BlueSkies.Platform, for example from its `FOLLOWUP-ISSUES.md`). The LifeOS job runs in a cloud routine with `csdelan/LifeOS` attached, fired by hand at the URL and token the routine shows, the way LifeOS will fire it. The BlueSkies job runs from a desktop app scheduled task on the PC. It's also tried once in the cloud routine, to see how far Linux gets. Until Phase 2, Claude reports in the pull request and by email, as the daily report does, and Chris moves the statuses | Both jobs went from tagged to a result without Chris opening a chat, and one question was asked and answered along the way. This section records what was found: whether the connector stayed signed in unattended; whether a routine fires by URL; how long a job takes from fire to result; the usage of an empty check and of a real job; whether the cloud can build and test each repo (LifeOS's database tests need Docker, and BlueSkies has Windows-only projects and the `\\bart` shares); and which branches a cloud run may push to |
| 3.2 | Assign to in the web, the queue tool, and writes scoped to the job | Tests: the queue lists only Chris's assignments, oldest first, without the agent's own writes. A write outside the agent's Tasks is refused. Assign to sets and clears the assignee |
| 3.3 | The fire from LifeOS | Tests against a stand-in endpoint: an assignment fires once; a burst fires once; an answer on a Waiting Task fires; the body has ids only; a failed fire is logged and not retried. On staging, an assignment shows up as a run of the routine |
| 3.4 | The runners for real: the routine with its backstop, and the PC task | Chris assigns real jobs of both kinds for a week. From what he saw, he settles the backstop interval and decision 3 |

## 7. Risks

- **Prompt injection and leaks.** An agent that can read LifeOS and also send email or
  browse the web could be tricked by a page or a message into sending Chris's data
  somewhere. Text inside LifeOS can carry instructions too (captured pages, CAP-5 URLs).
  Mitigations:
  - read-only first, and writes kept narrow;
  - connect only agents Chris trusts;
  - optional areas can be switched off;
  - the access log shows every read;
  - the server instructions mark item text as data, which helps but doesn't guarantee it.
- **Open registration.** Anyone can register a client with Supabase. Only Chris's
  allowlisted account can approve one, and the API checks the allowlist on every call.
- **A beta sign-in.** If Supabase's OAuth server changes or breaks, `/mcp` stops working,
  and nothing else does. The API validates standard JWTs, so another OAuth provider could
  replace it.
- **Oura's agreement.** Agents read Oura data, which Oura's API agreement forbids outside
  Oura's own MCP server (§2, decision 3). Oura can suspend an app's access at its
  discretion. The Activity page would stop syncing, and what's synced stays. Taking
  `health` out of `Mcp__Domains` stops agents reading it at once. The backups' monthly copies
  also keep Oura data for good, under the same risk Chris accepted for retention in D14; the
  unbuilt Oura purge will have to deal with them.
- **Backups hold everything.** Only Chris's private key can read them, and losing it loses
  them. That's what the drill is for.
- **Missed nights.** The backup runs only while Chris's PC is on and he's signed in. A
  missed night runs when the PC is back, and a long absence means no backups for that time.
  The dead-man's switch catches a night with no backup.
- **Deleted backups.** Unlike B2's Object Lock, nothing stops malware on the PC from deleting
  or encrypting the OneDrive folder. OneDrive's recycle bin and Restore your OneDrive reach
  back 30 days.
- **An assistant that works alone** (Phase 3). It writes, and pushes code, with nobody
  watching. Mitigations:
  - only Chris's own assignments are jobs, and other text stays data;
  - writes are scoped to its own Tasks;
  - code goes through a pull request unless decision 3 (§6.5) says otherwise;
  - one job at a time per runner, each in a worktree of its own;
  - the access log, the fire log, and the routine's own runs at claude.ai show what it did.
- **Fire tokens.** Whoever holds a routine's token can start a run. That run still works only
  Chris's queue, and treats the fire's body as untrusted. The tokens are Fly secrets, and
  making a new token in the routine replaces a leaked one.
- **Research previews.** Routines and their limits may change. The PC task is the fallback,
  and Managed Agents would replace both if they go away.

## 8. Cost

- **Phase 0:** nothing. It uses OneDrive space Chris already has and runs on his PC.
- **Phases 1 and 2:** no Supabase plan change. The OAuth server is free during its beta,
  and Chris counts as one monthly active user. Fly is unchanged, unless an always-on
  machine is wanted (a few dollars a month).
- **Phase 3:** no new hosting. Runs use Chris's Claude subscription. A fired routine runs only
  when there's a job, but a PC task that checks every 15 minutes is 96 runs a day even when
  idle; 3.1 measures what that costs. Managed Agents, if ever used, bill the API on top.
- **Supabase Pro ($25 a month)** isn't needed for anything here. It would add Supabase's
  own daily backups (7 days) as a second layer and stop free-plan pausing. Worth weighing
  when production is set up.

## 9. Later (not planned here)

- **Proactive coaching.** Under way outside this plan: since 2026-10-28 a desktop app
  scheduled task reads `/mcp` at 4 AM and emails Chris a daily report. Beyond Phase 3's
  wake-ups (§6.3), LifeOS itself never pushes.
- **More agent features:**
  - prompts for the reviews (REVIEW-1, REVIEW-2);
  - voice capture through an agent (CAP-3);
  - help with inbox triage (promote);
  - an agent's own inferences, written as `derived` with their sources.
- **A move to DigitalOcean.** The MCP server moves with the API, and the backups don't
  care where the API runs. Supabase stays the sign-in unless Auth moves too.

## 10. Sources (checked 2026-10-27)

- Supabase OAuth 2.1 Server, [overview](https://supabase.com/docs/guides/auth/oauth-server)
  and [getting started](https://supabase.com/docs/guides/auth/oauth-server/getting-started):
  in beta, on all plans, free during the beta; agent sign-ins count toward MAUs; you build
  the consent page; default token claims include `client_id`.
- [Supabase: MCP authentication](https://supabase.com/docs/guides/auth/oauth-server/mcp-authentication):
  discovery at `/.well-known/oauth-authorization-server/auth/v1`; dynamic registration.
- [supabase-js `revokeGrant`](https://supabase.com/docs/reference/javascript/oauth-server-revokegrant):
  users list and revoke their grants.
- [Supabase: deploy MCP servers](https://supabase.com/docs/guides/ai-tools/byo-mcp): servers
  on Edge Functions are public; authentication isn't supported yet.
- [supabase/auth #2285](https://github.com/supabase/auth/issues/2285): dynamic registration
  rejects custom URI schemes (not checked for a fix).
- [Supabase backups](https://supabase.com/docs/guides/platform/backups): daily backups
  start on Pro (7 days); free projects should export their own.
- [C# MCP SDK, `ModelContextProtocol.AspNetCore`](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.AspNetCore.html):
  `WithHttpTransport`, `MapMcp`, authorization on tools.
- OneDrive (checked 2026-10-28): [the recycle bin](https://support.microsoft.com/en-us/office/restore-deleted-files-or-folders-in-onedrive-949ada80-0026-4db3-a953-c99083e6a84f)
  keeps a personal account's deleted files for 30 days, and
  [Restore your OneDrive](https://support.microsoft.com/en-us/onedrive/restore-your-onedrive-files)
  lets Microsoft 365 subscribers roll everything back up to 30 days.
- [age](https://age-encryption.org): public-key file encryption.
- [Oura API and MCP Agreement](https://cloud.ouraring.com/legal/api-agreement) (checked
  2026-10-28): §4(d), Oura data reaches a large language model only through Oura's MCP
  server, and the API may not be used to give data or prompts to an AI model; §5(f),
  deletion within 72 hours of a request; no exception for personal use. Oura's developer
  portal doesn't publish its MCP server yet.
- Claude (checked 2026-10-28, for Phase 3):
  - [Routines](https://code.claude.com/docs/en/routines.md) and
    [firing a routine](https://platform.claude.com/docs/en/api/claude-code/routines-fire): a
    research preview; schedule (at most hourly), API and GitHub triggers; an API fire is a
    POST with the routine's own bearer token and an optional `text` of up to 65,536
    characters, treated as untrusted, with no idempotency key; claude.ai connectors attach by
    default; subscription usage, with 30 fires an hour per routine and 100 API fires and 100
    scheduled runs an hour per account.
  - [Desktop scheduled tasks](https://code.claude.com/docs/en/desktop-scheduled-tasks.md): run
    only while the app is open and the PC awake; a missed run catches up once; connectors
    work, and a tool that needs a click stalls.
  - [Channels](https://code.claude.com/docs/en/channels-reference.md): a research preview;
    events reach only an interactive session started with a development flag.
  - [Headless mode](https://code.claude.com/docs/en/headless.md) and
    [authentication](https://code.claude.com/docs/en/authentication.md): `claude -p` uses the
    subscription sign-in, but an `ANTHROPIC_API_KEY` in its environment always wins and bills
    the API.
  - [Managed Agents](https://platform.claude.com/docs/en/managed-agents/overview): beta,
    API-billed, $0.08 for each hour a session runs; remote MCP servers sign in through stored
    credentials, not claude.ai connectors.
  - [MCP in Claude Code](https://code.claude.com/docs/en/mcp.md): `list_changed` only
    refreshes the tool list; nothing documents a change notice starting a turn.
