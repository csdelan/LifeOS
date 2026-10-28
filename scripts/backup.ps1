#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Backs up a LifeOS database and its photos to OneDrive: dumped, restore-tested, encrypted.

.DESCRIPTION
    The nightly backup (OPS-1; docs/pilot/backup-restore.md). Windows Task Scheduler runs it on
    this PC; scripts/backup-setup.ps1 registers the task and writes the settings it reads. A run:

      1. Dumps bsk, bsk_derived, bsk_ops and the migration history as the read-only bsk_backup
         role, counting every table's rows before and after. If a row changed during the dump,
         it's taken again (up to three times).
      2. Restores the dumps into a scratch Postgres in Docker and checks every table's row count
         matches, then runs `bsk rebuild --verify` and `bsk check` against the copy.
      3. Encrypts the dumps to the age public key and saves them with a manifest in
         <Destination>\db\daily\<stamp>. The month's first copy also goes to db\monthly\<yyyy-mm>.
      4. Copies each photo not yet backed up, checked against its sha256 and encrypted, to
         <Destination>\artifacts\<sha256>.age.
      5. Deletes daily copies older than 35 days. Monthly copies and photos are kept for good.

    The plaintext dumps stay in a temporary folder that's deleted at the end. A copy that fails
    the restore or the row counts isn't saved. If the copy restores but a later check fails (the
    derived tables drifted, a photo couldn't be copied), the copy is saved and the run still
    fails. A failed run exits 1, shows a Windows notification, and pings the dead-man's switch's
    /fail URL when one is set. Logs: %USERPROFILE%\.lifeos\backup\logs.

.EXAMPLE
    ./scripts/backup.ps1
    Backs up staging now, as the nightly task does.
#>
[CmdletBinding()]
param(
    # The settings to use: %USERPROFILE%\.lifeos\backup\<Target>.settings.xml.
    [string] $Target = 'staging'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LifeOsBackup.psm1') -Force
$repo = Split-Path -Parent $PSScriptRoot

Start-BackupLog -Name $Target | Out-Null
$mutex = [Threading.Mutex]::new($false, "LifeOS-backup-$Target")
try { $acquired = $mutex.WaitOne(0) }
catch [Threading.AbandonedMutexException] { $acquired = $true }   # a run that was killed; it's ours now
if (-not $acquired) {
    Write-BackupLog "Another backup of $Target is running; leaving it to finish."
    exit 0
}

$exitCode = 0
$work = $null
$scratch = $null
$settings = $null
# Problems found after the copy is saved: the run still fails, with all of them.
$problems = [Collections.Generic.List[string]]::new()

try {
    $settings = Read-BackupSettings -Target $Target
    $pg = ConvertTo-PgEnvironment (ConvertFrom-SecureText $settings['ConnectionString'])
    $destination = $settings['Destination']
    $recipient = $settings['AgeRecipient']
    $keepDays = [int] ($settings['KeepDailyDays'] ?? 35)
    $age = Find-AgeTool 'age'
    if (-not (Test-Path $destination)) {
        throw "The backup folder $destination doesn't exist. Is OneDrive still set up on this PC?"
    }
    Assert-Docker
    Send-HealthcheckPing $settings 'start'

    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
    $work = New-WorkFolder
    Write-BackupLog "Backing up $Target ($($pg.PGUSER) at $($pg.PGHOST)) as $stamp."

    # --- 1. The live database: version, latest migration, its bsk.* settings ------------------
    $facts = @{}
    $databaseSettings = [ordered]@{}
    $factLines = Invoke-RemotePsql $pg -What 'Reading the database' -Sql @'
SELECT 'server_version', current_setting('server_version');
SELECT 'server_version_num', current_setting('server_version_num');
SELECT 'latest_migration', coalesce(max(version), 0)::text FROM public.schema_migrations;
SELECT 'setting', cfg
FROM pg_db_role_setting s, unnest(s.setconfig) AS cfg
WHERE s.setdatabase = (SELECT oid FROM pg_database WHERE datname = current_database())
  AND s.setrole = 0 AND cfg LIKE 'bsk.%';
'@
    foreach ($line in $factLines) {
        $key, $value = $line.Split('|', 2)
        if ($key -eq 'setting') {
            $name, $setting = $value.Split('=', 2)
            $databaseSettings[$name] = $setting
        }
        else { $facts[$key] = $value }
    }
    $major = [int] ([int] $facts['server_version_num'] / 10000)
    $image = "postgres:$major-alpine"
    $pgDumpVersion = (Invoke-Process docker @('run', '--rm', $image, 'pg_dump', '--version') -What 'pg_dump --version').Out.Trim()

    # --- 2. Dump, with every table counted before and after ----------------------------------
    $dumpFile = "lifeos-$stamp.dump"
    $historyFile = "schema-migrations-$stamp.dump"
    $dockerDump = @('run', '--rm') + (Get-DockerEnvArguments $pg) + @('-v', "${work}:/work", $image, 'pg_dump',
        '--format=custom', '--no-password', '--lock-wait-timeout=60s')
    $counts = $null
    for ($attempt = 1; ; $attempt++) {
        $before = ConvertFrom-CountLines (Invoke-RemotePsql $pg (Get-RowCountSql) -What 'Counting rows')
        # Without --enable-row-security: if a bsk table ever had row level security, pg_dump
        # fails rather than quietly leaving rows out.
        Invoke-Process docker ($dockerDump + @('--schema=bsk', '--schema=bsk_derived', '--schema=bsk_ops', "--file=/work/$dumpFile")) `
            -Environment $pg -What 'pg_dump of the bsk schemas' | Out-Null
        # pg_dump's --table overrides --schema, so the history needs its own run. It has row level
        # security on, with a policy that lets bsk_backup read it all.
        Invoke-Process docker ($dockerDump + @('--enable-row-security', '--table=public.schema_migrations', "--file=/work/$historyFile")) `
            -Environment $pg -What 'pg_dump of the migration history' | Out-Null
        $after = ConvertFrom-CountLines (Invoke-RemotePsql $pg (Get-RowCountSql) -What 'Counting rows')

        $changed = Compare-RowCounts $before $after
        if (-not $changed) { $counts = $after; break }
        if ($attempt -ge 3) { throw "The database kept changing during the dump, 3 times: $($changed -join '; ')." }
        Write-BackupLog "Rows changed during the dump ($($changed -join '; ')); taking it again." -Level Warn
    }
    $rowTotal = ($counts.Values | Measure-Object -Sum).Sum
    Write-BackupLog "Dumped $($counts.Count) tables, $rowTotal rows (server $($facts['server_version']), migration $($facts['latest_migration']))."

    # --- 3. Restore into a scratch Postgres and check it -------------------------------------
    $scratch = Start-ScratchPostgres -Image $image -WorkFolder $work
    $schemaSql = (Invoke-Process docker @('exec', $scratch.Name, 'pg_restore', '--schema-only', '--file=-', "/work/$dumpFile") -What 'Reading the dump').Out +
        "`n" + (Invoke-Process docker @('exec', $scratch.Name, 'pg_restore', '--schema-only', '--file=-', "/work/$historyFile") -What 'Reading the dump').Out
    Invoke-ScratchPsql $scratch (Get-CreateRolesSql (Get-DumpRoleNames $schemaSql)) | Out-Null
    if ($databaseSettings.Count) { Invoke-ScratchPsql $scratch (Get-DatabaseSettingsSql $databaseSettings) | Out-Null }
    foreach ($file in $dumpFile, $historyFile) {
        # Triggers and foreign keys come after the data, so the append-only triggers don't fire.
        Invoke-Process docker @('exec', $scratch.Name, 'pg_restore', '-U', 'postgres', '-d', 'restore',
            '--no-owner', '--exit-on-error', "/work/$file") -What "Restoring $file" | Out-Null
    }
    $restored = ConvertFrom-CountLines (Invoke-ScratchPsql $scratch (Get-RowCountSql))
    $mismatch = Compare-RowCounts $counts $restored
    if ($mismatch) { throw "The restored copy doesn't match the database: $($mismatch -join '; ')." }
    Write-BackupLog 'Restored into a scratch Postgres: every table''s row count matches.'

    $artifacts = @(Invoke-ScratchPsql $scratch @'
SELECT DISTINCT store, bucket, object_key, sha256 FROM bsk.artifact_object ORDER BY sha256, bucket, object_key;
'@ | ForEach-Object {
            $store, $bucket, $key, $sha = $_.Split('|')
            [pscustomobject]@{ Store = $store; Bucket = $bucket; Key = $key; Sha256 = $sha }
        })

    # The app's own checks, from this working copy's CLI. They can't make the copy unusable, so
    # a failure here is reported after the copy is saved.
    $restoreTest = [ordered]@{ rowCountsMatch = $true; derivedDrift = $null; checkFindings = $null }
    try {
        Build-Cli
        $connection = Get-ScratchConnectionString $scratch
        $verify = Invoke-Bsk $connection @('rebuild', '--verify')
        if ($verify.ExitCode -eq 0) { $restoreTest.derivedDrift = $false }
        elseif ($verify.Json -and $verify.Json.PSObject.Properties['drift']) {
            $restoreTest.derivedDrift = $true
            $problems.Add('The derived tables have drifted from the source (bsk rebuild --verify). Run `bsk rebuild` on the live database.')
        }
        else { $problems.Add("bsk rebuild --verify failed on the copy: $($verify.Text)") }

        $check = Invoke-Bsk $connection @('check')
        if ($check.ExitCode -eq 0 -and $check.Json) { $restoreTest.checkFindings = $check.Json.findingCount }
        else { $problems.Add("bsk check failed on the copy: $($check.Text)") }
    }
    catch {
        $problems.Add("The app's checks didn't run: $($_.Exception.Message)")
    }
    Remove-ScratchPostgres $scratch
    $scratch = $null

    # --- 4. Encrypt and save -------------------------------------------------------------------
    $daily = Join-Path $destination "db\daily\$stamp"
    $partial = "$daily.partial"
    New-Item -ItemType Directory -Force -Path $partial | Out-Null
    $files = @()
    foreach ($file in $dumpFile, $historyFile) {
        $encrypted = Join-Path $partial "$file.age"
        Invoke-Process $age @('--encrypt', '--recipient', $recipient, '--output', $encrypted, (Join-Path $work $file)) -What "Encrypting $file" | Out-Null
        $files += [ordered]@{ name = "$file.age"; bytes = (Get-Item $encrypted).Length; sha256 = Get-FileSha256 $encrypted }
    }

    $commit = (Invoke-Process git @('-C', $repo, 'rev-parse', '--short', 'HEAD') -AllowFailure).Out.Trim()
    if ((Invoke-Process git @('-C', $repo, 'status', '--porcelain') -AllowFailure).Out.Trim()) { $commit += ' (with local changes)' }
    $manifest = [ordered]@{
        format           = 1
        target           = $Target
        stamp            = $stamp
        takenAt          = (Get-Date).ToUniversalTime().ToString('o')
        host             = $pg.PGHOST
        database         = $pg.PGDATABASE
        user             = $pg.PGUSER
        serverVersion    = $facts['server_version']
        pgDumpVersion    = $pgDumpVersion
        pgMajor          = $major
        scriptCommit     = $commit
        latestMigration  = [int] $facts['latest_migration']
        databaseSettings = $databaseSettings
        rowCounts        = $counts
        files            = $files
        artifacts        = [ordered]@{ count = $artifacts.Count; folder = 'artifacts' }
        restoreTest      = $restoreTest
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $partial "manifest-$stamp.json") -Encoding utf8
    Rename-Item -Path $partial -NewName $stamp
    Write-BackupLog "Saved $daily."

    $month = $stamp.Substring(0, 4) + '-' + $stamp.Substring(4, 2)
    $monthly = Join-Path $destination "db\monthly\$month"
    if (-not (Test-Path $monthly)) {
        Copy-Item -Path $daily -Destination "$monthly.partial" -Recurse
        Rename-Item -Path "$monthly.partial" -NewName $month
        Write-BackupLog "Kept it as $month's monthly copy."
    }

    # --- 5. Photos not yet backed up -------------------------------------------------------------
    $artifactFolder = Join-Path $destination 'artifacts'
    New-Item -ItemType Directory -Force -Path $artifactFolder | Out-Null
    Get-ChildItem $artifactFolder -Filter '*.partial' | Remove-Item -Force
    $missing = @($artifacts | Where-Object { -not (Test-Path (Join-Path $artifactFolder "$($_.Sha256).age")) } |
        Sort-Object Sha256 -Unique)
    if ($missing.Count) {
        $url = $settings['SupabaseUrl']
        $secret = ConvertFrom-SecureText $settings['SupabaseSecretKey']
        $copied = 0
        foreach ($artifact in $missing) {
            try {
                if ($artifact.Store -ne 'supabase') { throw "it's in the store '$($artifact.Store)', which the backup can't read" }
                if (-not $url -or -not $secret) { throw 'the settings have no Supabase URL and secret key' }
                $plain = Join-Path $work "artifact-$($artifact.Sha256)"
                Save-StorageObject $url $secret $artifact.Bucket $artifact.Key $plain
                $actual = Get-FileSha256 $plain
                if ($actual -ne $artifact.Sha256) { throw "its bytes hash to $actual, so it was changed outside bsk" }
                $final = Join-Path $artifactFolder "$($artifact.Sha256).age"
                Invoke-Process $age @('--encrypt', '--recipient', $recipient, '--output', "$final.partial", $plain) -What 'Encrypting a photo' | Out-Null
                Move-Item "$final.partial" $final
                Remove-Item $plain -Force
                $copied++
            }
            catch {
                $problems.Add("Photo $($artifact.Key) wasn't backed up: $($_.Exception.Message.TrimEnd('.')).")
            }
        }
        Write-BackupLog "Copied $copied of $($missing.Count) new photo(s); $($artifacts.Count) in all."
    }
    else {
        Write-BackupLog "No new photos; $($artifacts.Count) already backed up."
    }

    # --- 6. Prune daily copies older than $keepDays days (never monthly copies or photos) ---------
    $cutoff = (Get-Date).ToUniversalTime().AddDays(-$keepDays)
    foreach ($folder in Get-ChildItem (Join-Path $destination 'db\daily') -Directory) {
        $taken = [datetime]::MinValue
        $isStamp = [datetime]::TryParseExact($folder.Name, "yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal, [ref] $taken)
        $leftover = $folder.Name.EndsWith('.partial') -and $folder.LastWriteTimeUtc -lt (Get-Date).ToUniversalTime().AddDays(-1)
        if (($isStamp -and $taken -lt $cutoff) -or $leftover) {
            Remove-Item $folder.FullName -Recurse -Force
            Write-BackupLog "Deleted the daily copy $($folder.Name)."
        }
    }

    if ($problems.Count) { throw "Saved $stamp, but: $($problems -join ' ')" }
    Write-BackupLog "Backup $stamp done."
    Send-HealthcheckPing $settings ''
}
catch {
    $exitCode = 1
    $message = $_.Exception.Message
    Write-BackupLog "Backup failed: $message" -Level Error
    if ($settings) { Send-HealthcheckPing $settings 'fail' $message }
    Show-BackupToast "LifeOS backup failed ($Target)" $message
}
finally {
    Remove-ScratchPostgres $scratch
    if ($work -and (Test-Path $work)) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}

exit $exitCode
