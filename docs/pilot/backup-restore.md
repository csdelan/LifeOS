# Backups and restores (OPS-1)

LifeOS backs itself up every night to Chris's **OneDrive**, from his PC, along with the Git
history of his notes vault. Each copy is restore-tested before it's kept and encrypted to a key
only Chris holds. This is Phase 0 of the [agent access plan](agent-access-plan.md) (§3). This
page covers setup, checking, the quarterly drill and a real restore.

## What a backup holds

| What | Where it comes from | In the backup |
|---|---|---|
| The life record, derived tables, operational state | schemas `bsk`, `bsk_derived`, `bsk_ops` | `lifeos-<stamp>.dump.age` |
| Migration history | `public.schema_migrations` | `schema-migrations-<stamp>.dump.age` |
| The database's `bsk.*` settings (the home time zone) | `ALTER DATABASE … SET` | the manifest |
| Photo bytes | Supabase Storage, `lifeos-artifacts/artifacts/<sha256>` | `artifacts/<sha256>.age` |
| The notes vault's Git history: every commit, branch and tag | `C:\Users\csdel\Vaults\Chris2.0\.git` | `vault-<stamp>.bundle.age` |
| The vault's read-only snapshots | `Vaults\_snapshots\Chris2.0 …` (the 2026-11-12 cutover's zip and inventory) | `snapshots\<name>.age` |

The vault's Git history matters because nothing else has it. Obsidian Sync copies the notes to
Jupiter but skips `.git`, and the vault has no remote ([notes plan](../notes-system-plan.md),
"Git on the hub"). Chris chose an encrypted bundle here over a private GitHub repository (his
writing stays off GitHub) and a bare repository on `\\bart` (the same house) on 2026-11-15.

Not backed up:
- **Supabase Auth's users.** Nothing in `bsk` points at them, and after a restore Chris signs
  in with Google again.
- **Secrets.** `Integrations__TokenKey` and the other keys live in Fly and `.env`. Keep
  `Integrations__TokenKey` in the password manager beside the backup key: without it a
  restore still works, but Oura has to be reconnected.
- **Vault edits since the last commit.** A bundle holds commits. The nightly sweep commits
  Chris's own edits each night, and Obsidian Sync has the notes as they are now.
- **The vault's videos and big files.** They live in `OneDrive\Vault Media`, outside Git.
- **The vault's Git config, hooks and reflogs.** A bundle has none. The manifest records the
  `core.*` settings a restore needs (`coreConfig`).

## Where it goes

```
OneDrive\Backups\LifeOS\staging\
  db\daily\<stamp>\     lifeos-<stamp>.dump.age, schema-migrations-<stamp>.dump.age, manifest-<stamp>.json
  db\monthly\<yyyy-mm>\ the month's first copy, the same three files
  artifacts\<sha256>.age   one per photo
OneDrive\Backups\LifeOS\vault\
  daily\<stamp>\        vault-<stamp>.bundle.age, vault-manifest-<stamp>.json
  monthly\<yyyy-mm>\    the month's first bundle, the same two files
  snapshots\            <snapshot name>.age for each snapshot, and snapshots.json
```

- `<stamp>` is the UTC time the copy was taken, such as `20261029T101700Z`.
- Daily copies are kept for 35 days. Monthly copies and photos are kept for good.
- A night when the vault's HEAD and refs haven't changed takes no bundle, so the newest daily
  bundle is never deleted, however old. Monthly bundles and snapshots are kept for good.
- The manifest isn't encrypted. It holds no life data: the time, server and dump versions,
  the latest migration, every table's row count, each file's sha256, the photo count and how
  the restore test went. Anyone can use it to check a copy is whole without the key.
- The vault's manifest holds no notes either: the time, Git's version, the branch, HEAD,
  every ref's name and commit, the commit count, the vault's `core.*` settings, the bundle's
  sha256 and how the clone check went. `snapshots.json` holds each snapshot's size, time and
  sha256.

OneDrive syncs the folder like any other. OneDrive can't lock a file the way Backblaze's
Object Lock would. If a backup file is deleted, by mistake or by malware on the PC, it sits
in OneDrive's recycle bin for 30 days. Microsoft 365's **Restore your OneDrive** can roll the
whole OneDrive back up to 30 days.

## Every night

Task Scheduler runs `\LifeOS\Backup (staging)` at 3:17 am while Chris is signed in. If the
PC was off or asleep, it runs as soon as it's back. A run takes about 20 seconds, 5 of them
the vault's:

1. **Dump.** `pg_dump` runs as `bsk_backup`, a role that reads everything and writes nothing
   (migration 0046). Every table's rows are counted before and after the dump. If a row
   changed in between, the dump is taken again (up to three times).
2. **Restore test.** The dump is restored into a scratch Postgres in Docker. Every table's
   row count must match. Then `bsk rebuild` runs on the copy and must write exactly what its
   source folds to, and `bsk check` must finish. The live derived table is normally behind
   its source, because only `bsk rebuild` writes it and the app reads status straight from
   the source. That's noted in the manifest (`derivedWasCurrent`), not treated as a failure.
3. **Save.** The dumps are encrypted to the public key with [age](https://age-encryption.org)
   and saved with the manifest. The month's first copy is also kept as its monthly copy.
4. **Photos.** Each photo not yet backed up is downloaded, checked against its sha256 and
   encrypted.
5. **Prune.** Daily copies older than 35 days are deleted.
6. **The vault.** If its HEAD and refs match the newest bundle kept, the log says so and no
   bundle is taken. Otherwise `git bundle create --all` takes its whole history; it only reads
   the repository. The bundle is checked before it's kept: `git bundle verify`, then a mirror
   clone that must hold every ref the bundle lists and pass `git fsck --full`, then a clone that
   must check out clean at the bundle's HEAD. The clones are deleted, and the bundle is
   encrypted to the same key. Each snapshot not yet copied, or changed since, is encrypted to
   `snapshots\`. Then daily bundles older than 35 days are deleted, except the newest. The
   vault's sweep starts about 3:09; a commit it makes after the backup has bundled goes in the
   next night's bundle.

The plaintext dumps and bundle never leave a temporary folder, which is deleted at the end of
the run.

**When something fails:**
- If a copy fails the restore or the row counts, it **isn't saved**.
- If it restores but a later check fails, it **is saved** and the run still fails. That
  covers a failed rebuild or `bsk check` on the copy, and a photo that couldn't be copied.
- A bundle that fails its checks, or can't be encrypted, isn't saved.
- The vault's step runs whether or not the database's worked. A vault failure never costs
  the night's database copy, nor the other way round.
- A failed run shows a Windows notification ("LifeOS backup failed"), writes the reason to the
  log, and pings the dead-man's switch's `/fail` URL if one is set. For a vault failure,
  `/fail` says only that the vault wasn't backed up. The reason stays in the log on PICARD,
  since it can name a note.
- The log is `%USERPROFILE%\.lifeos\backup\logs\<target>-<date>.log`. Logs are kept for 60
  days.

Task Scheduler's **Last Run Result** always reads 0x0. The task runs through
`conhost --headless` so no window pops up, and that hides the exit code. Read the log
instead.

**A night with no run** sends no notification. For that, set up the optional dead-man's
switch. Create a free check at [healthchecks.io](https://healthchecks.io) with a one-day period
and a few hours' grace, then pass its ping URL to setup (`-HealthcheckUrl`). It emails Chris
when a day passes with no successful ping. The URL is encrypted in the settings, so the log
says whether it's on: each run without one warns "No dead-man's switch is set up", and each
run with one ends "Told the dead-man's switch the run succeeded" (or failed).

The app's own checks (`bsk rebuild`, `bsk check`) run the bsk built from `origin/main`, as
staging runs it (`Get-MainBsk` in `scripts/LifeOsBsk.psm1`, published to
`%USERPROFILE%\.lifeos\bin\bsk-main` and kept until main moves), never a working copy, which
may be behind staging. The task runs a copy of `backup.ps1` and its modules that setup installs
in `%USERPROFILE%\.lifeos\bin\backup`, building bsk in the repository's main checkout, so it
doesn't depend on the checkout that set it up; run setup again after a push that changes the
backup scripts. If bsk doesn't build at 3 am, the copy is still saved and the run reports why.

## Setting it up

Once per database: staging now, production when it exists.

1. **Install the tools.** Docker Desktop (already used for dev) and age:
   `winget install --id FiloSottile.age`.
2. **Apply migration 0046.** CI applies migrations to staging on every push to main. For
   another database, use `scripts/migrate-staging.ps1` or `migrate-production.ps1`.
3. **Run setup** from the repo root:

   ```powershell
   ./scripts/backup-setup.ps1
   ```

   It reads the owner connection (`BSK_CONNECTION_STRING`) and the Storage secret key
   (`Storage__SupabaseSecretKey`) from `.env`, then:
   - makes a key pair in `%USERPROFILE%\lifeos-backup.key`, unless an age public key is passed
     with `-AgeRecipient` or one was set up before;
   - turns on `bsk_backup`'s login with a new random password, sent as a SCRAM hash so the
     plaintext never reaches the server's logs, and checks it can sign in;
   - saves the settings to `%USERPROFILE%\.lifeos\backup\staging.settings.xml`. The
     connection string and Storage key in it are encrypted with Windows DPAPI, so only Chris's
     Windows account on this PC can read them;
   - registers the scheduled task.

   To back up the notes vault's Git history too, add `-VaultPath C:\Users\csdel\Vaults\Chris2.0`.
   Setup checks it's the top of a Git repository with a commit, before it changes the password.
   Its bundles go to `OneDrive\Backups\LifeOS\vault` (`-VaultDestination` to change that), and
   its snapshots come from the `_snapshots` folder beside it (`-VaultSnapshots` to change that,
   `''` for none). With no vault set, each night's log says the vault isn't backed up, and
   `-VaultPath ''` turns it off again. The first night copies the 4.8 GB snapshot, so OneDrive
   needs that much room (its videos are also in `Vault Media`), and that run takes longer.
4. **Put the private key away.** Copy the contents of `lifeos-backup.key` into the password
   manager, print a copy and keep it somewhere safe, then delete the file. **Lose it and every
   backup is unreadable.** Never keep it in OneDrive, beside the backups it opens.
5. **Run one now** and check it: `./scripts/backup.ps1`.

Rerun setup whenever something changes: a new password (it always sets one), a new
`-HealthcheckUrl`, a different `-Destination` or `-VaultPath`, or `-At` for another time. It
keeps the key, the folders, the vault and the switch from last time unless they're passed again.

For production: `./scripts/backup-setup.ps1 -Target production -EnvFile .env.prod -AgeRecipient age1…`
(the same public key). That's a second task and a second folder.

To pause backups, disable the task in Task Scheduler. To stop them for good:
`Unregister-ScheduledTask -TaskPath '\LifeOS\' -TaskName 'Backup (staging)'`.

## Checking on it

- The newest folder in `db\daily` should be from last night. Its manifest's `restoreTest`
  shows `rowCountsMatch: true` and `rebuildMatches: true`.
- The newest folder in `vault\daily` is from the last night the vault changed, and its
  manifest's `restoreTest` shows `checkoutClean: true`. Each night's log has a line for the
  vault: `Saved the vault's Git history to …`, `… hasn't changed since the bundle <stamp> …`,
  or `No vault is set up …`.
- A good night's log ends `Backup <stamp> done.`, then `Told the dead-man's switch the run
  succeeded.` when the switch is set up.
- `Get-ScheduledTaskInfo -TaskPath '\LifeOS\' -TaskName 'Backup (staging)'` gives the last and
  next run times.

## The restore drill (every three months)

The drill is the only proof that the private key still works and a copy can be brought back.
Do it once right after setup, then every three months. A repeating Task in LifeOS is the
natural reminder.

1. Start the local Postgres: `docker compose up -d`.
2. Get the private key from the password manager into a file outside OneDrive.
3. Restore last night's copy into a new local database, `lifeos_restore`:

   ```powershell
   ./scripts/restore.ps1 -KeyFile C:\path\to\lifeos-backup.key
   ```

   It checks each file against the manifest and decrypts it, then restores into the empty
   database. It recreates the roles the dump grants to (without logins) and puts back the
   home time zone. Then it checks every row count against the manifest, runs `bsk migrate`
   (which should have nothing to apply) and `bsk rebuild` to bring the derived table up to
   date. Add
   `-ArtifactsFolder C:\temp\photos` to decrypt the photos too, each checked against its
   sha256.
4. Look at it through the app. Run `bsk check` against
   `Host=localhost;Port=5432;Database=lifeos_restore;Username=lifeos;Password=lifeos`, or run
   the API locally against it with `BSK_CONNECTION_STRING` and
   `BSK_READER_CONNECTION_STRING` (`Username=bsk_reader;Password=bsk_reader`) pointed at
   `lifeos_restore`.
5. Bring the vault's history back. Decrypt the newest bundle and clone it into a scratch
   folder:

   ```powershell
   $bundle = Get-ChildItem "$env:OneDrive\Backups\LifeOS\vault\daily\*\vault-*.bundle.age" |
       Sort-Object Name | Select-Object -Last 1
   age --decrypt --identity C:\path\to\lifeos-backup.key --output C:\temp\vault.bundle $bundle.FullName
   git clone --config core.autocrlf=false --branch master C:\temp\vault.bundle C:\temp\vault-drill
   git -C C:\temp\vault-drill log -1 --format='%h %cs %s'
   git -C C:\temp\vault-drill status --short
   ```

   The last commit should be the manifest's `head`, normally the latest nightly sweep, and
   `status` should print nothing.
6. Clean up. Drop the database (`docker exec lifeos-postgres dropdb -U lifeos --force lifeos_restore`),
   delete the vault's copies (`Remove-Item -Recurse -Force C:\temp\vault.bundle, C:\temp\vault-drill`:
   they hold Chris's notes unencrypted), and delete the key file.

`-From <folder>` restores an older copy, daily or monthly.

## A real restore

For when the Supabase project is lost or its data is damaged.

1. Make a new Supabase project. Note its session-pooler owner string, its URL and a secret
   key (`sb_secret_…`) in a new `.env` file, say `.env.new` ([supabase-hosting.md](supabase-hosting.md)).
2. Restore into it and upload the photos to its Storage:

   ```powershell
   ./scripts/restore.ps1 -KeyFile .\lifeos-backup.key `
       -ConnectionString '<new project owner string>' -UploadArtifacts -EnvFile .env.new
   ```

   It refuses a database that already has `bsk`, so a project with data in it can't be
   overwritten. `bsk migrate` brings the schema up to the current migration.
3. If it couldn't set the home time zone (it says so), run the `ALTER DATABASE` it prints in
   the SQL editor.
4. Point the app at it:
   - set `bsk_reader`'s password (`alter role bsk_reader with login password '…'`);
   - update the Fly secrets ([deploy.md](deploy.md));
   - sign in with Google;
   - reconnect Oura if `Integrations__TokenKey` was lost.
5. Rerun `./scripts/backup-setup.ps1` with the new `.env` so the backups follow the new project.

To repair a few mistaken rows instead of replacing everything, restore into `lifeos_restore`
as in the drill and copy what's needed from there.

### The vault

For when the vault's folder or its `.git` is lost or damaged on PICARD. The notes also come
back from Obsidian Sync, since Jupiter has them; the bundle brings back their history.

1. Get the private key into a file outside OneDrive, and decrypt the bundle: the newest in
   `vault\daily`, as in the drill, or an older daily or monthly one.

   ```powershell
   age --decrypt --identity C:\path\to\lifeos-backup.key --output C:\temp\vault.bundle <the bundle's .age file>
   ```

2. Move what's left of the old vault aside (`git clone` won't fill a folder that isn't empty),
   then clone the bundle where the vault lives. Pass each of the manifest's `coreConfig`
   settings. As the cutover set them they're these, so Git never rewrites a note's line
   endings; without `core.autocrlf=false`, Git for Windows' default would rewrite them all:

   ```powershell
   git clone --branch master --config core.autocrlf=false --config core.safecrlf=false `
       --config core.quotepath=false --config core.longpaths=true `
       C:\temp\vault.bundle C:\Users\csdel\Vaults\Chris2.0
   git -C C:\Users\csdel\Vaults\Chris2.0 remote remove origin
   ```

   The clone's `origin` is the bundle file. Removing it leaves the vault as it was, with no
   remote.
3. Point Obsidian at it: **Open another vault → Open folder as vault**, and choose the
   folder. Connect it to the same remote vault in Obsidian's Sync settings, so the notes
   edited since the bundle's last commit come back. `git status` then shows them, and the next
   nightly sweep commits them.
4. Delete `C:\temp\vault.bundle` and the key file.

A snapshot comes back the same way:
`age --decrypt --identity <key> --output 'C:\temp\<name>' '<OneDrive>\Backups\LifeOS\vault\snapshots\<name>.age'`.
Check it against its sha256 in `snapshots.json` with `Get-FileHash`.

## The scripts

| File | What |
|---|---|
| `scripts/backup.ps1` | The nightly run |
| `scripts/backup-setup.ps1` | Key, `bsk_backup` login, settings, the vault, scheduled task |
| `scripts/restore.ps1` | Decrypt, restore, check; photos to a folder or a new project |
| `scripts/LifeOsBackup.psm1` | What the three share, and the vault's part (`Backup-Vault`) |
| `tests/LifeOs.Tests/VaultBackupTests.cs` | `Backup-Vault` on a throwaway repository: the checks, skipping, keeping, snapshots, refusals |
| `scripts/LifeOsBsk.psm1` | `Get-MainBsk`: the bsk built from `origin/main`, which restore, backup, `migrate-staging.ps1` and `install-bsk.ps1` run |
| `db/migrations/0046__backup_role.sql` | The read-only `bsk_backup` role |

Postgres's tools run from the `postgres:<major>-alpine` image that matches the server, so the
PC needs Docker but no Postgres install. Settings and logs live under `%USERPROFILE%\.lifeos`,
not AppData, because a packaged app such as the Claude desktop app writes AppData to a private
copy that the scheduled task never sees.
