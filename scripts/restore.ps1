#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Restores a LifeOS backup from OneDrive into an empty database, and checks it.

.DESCRIPTION
    For the restore drill and for a real disaster (docs/pilot/backup-restore.md). It:

      1. Checks the copy's files against its manifest and decrypts them with your private key.
      2. Restores them into an empty database: by default a new database, lifeos_restore, on
         the local Docker Postgres (docker compose up -d). Pass -ConnectionString (an owner
         connection) for another server, such as a new Supabase project. A database that
         already has the bsk schema is refused.
      3. Creates any role the dump grants to (bsk_reader, bsk_backup, …) without a login, and puts
         back the database's bsk.* settings (the home time zone).
      4. Checks every table's row count against the manifest, brings the schema up to date with
         `bsk migrate` (nothing to do when the backup is recent), and refreshes the derived
         tables with `bsk rebuild` (they're usually behind in a backup: only rebuild writes them).
      5. With -ArtifactsFolder, decrypts the photos into that folder, each checked against its
         sha256. With -UploadArtifacts, uploads them to the Supabase project named in -EnvFile
         (Storage__SupabaseUrl or VITE_SUPABASE_URL, and Storage__SupabaseSecretKey).

    Nothing is written to the backup folder.

.EXAMPLE
    ./scripts/restore.ps1 -KeyFile C:\keys\lifeos-backup.key
    The drill: restores last night's staging copy into lifeos_restore on the local Docker Postgres.

.EXAMPLE
    ./scripts/restore.ps1 -KeyFile .\lifeos-backup.key -ConnectionString $newOwner -UploadArtifacts -EnvFile .env.new
    A disaster: restores into a new Supabase project and uploads the photos to its Storage.
#>
[CmdletBinding()]
param(
    # Your private key file (AGE-SECRET-KEY-…).
    [Parameter(Mandatory)]
    [string] $KeyFile,

    # Which database's backups: the folder under OneDrive\Backups\LifeOS.
    [string] $Target = 'staging',

    # The backups' root folder. Default: OneDrive\Backups\LifeOS\<Target>.
    [string] $BackupRoot,

    # One copy's folder (under db\daily or db\monthly). Default: the newest daily copy.
    [string] $From,

    # An owner connection string for the database to restore into. Default: the local Docker
    # Postgres, database -Database (made if it doesn't exist).
    [string] $ConnectionString,

    # The local database to restore into when -ConnectionString isn't given.
    [string] $Database = 'lifeos_restore',

    # Decrypt the photos into this folder.
    [string] $ArtifactsFolder,

    # Upload the photos to the Supabase project in -EnvFile.
    [switch] $UploadArtifacts,

    # The dotenv file for -UploadArtifacts.
    [string] $EnvFile
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LifeOsBackup.psm1') -Force

$age = Find-AgeTool 'age'
Assert-Docker
if (-not (Test-Path $KeyFile)) { throw "The key file $KeyFile doesn't exist." }
if (-not $BackupRoot) { $BackupRoot = Get-DefaultBackupRoot $Target }

# --- 1. The copy ------------------------------------------------------------------------------------
if (-not $From) {
    $From = Get-ChildItem (Join-Path $BackupRoot 'db\daily') -Directory -ErrorAction SilentlyContinue |
        Where-Object Name -Match '^\d{8}T\d{6}Z$' | Sort-Object Name | Select-Object -Last 1 -ExpandProperty FullName
    if (-not $From) { throw "There are no daily copies in $BackupRoot\db\daily." }
}
$manifestFile = Get-ChildItem $From -Filter 'manifest-*.json' | Select-Object -First 1
if (-not $manifestFile) { throw "$From has no manifest." }
$manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json -AsHashtable -DateKind String
Write-Host "Restoring $($manifest['target']) as of $($manifest['takenAt']) (migration $($manifest['latestMigration']))." -ForegroundColor Cyan

$work = New-WorkFolder
try {
    $plainFiles = @()
    foreach ($file in $manifest['files']) {
        $encrypted = Join-Path $From $file['name']
        if (-not (Test-Path $encrypted)) { throw "$($file['name']) is missing from $From." }
        if ((Get-FileSha256 $encrypted) -ne $file['sha256']) { throw "$($file['name']) doesn't match its manifest: it was changed or damaged." }
        $plain = Join-Path $work ($file['name'] -replace '\.age$', '')
        Invoke-Process $age @('--decrypt', '--identity', $KeyFile, '--output', $plain, $encrypted) -What "Decrypting $($file['name'])" | Out-Null
        $plainFiles += Split-Path $plain -Leaf
    }
    # The bsk schemas first, then the migration history.
    $plainFiles = @($plainFiles | Sort-Object { $_ -like 'schema-migrations-*' })
    Write-Host 'Decrypted the copy with your key; its files match the manifest.' -ForegroundColor Green

    # --- 2. The target ----------------------------------------------------------------------------
    $local = -not $ConnectionString
    if ($local) {
        $ConnectionString = Set-ConnectionValues (Get-LocalDevelopmentConnectionString) @{ Database = $Database }
        $serverPg = ConvertTo-PgEnvironment (Set-ConnectionValues $ConnectionString @{ Database = 'postgres' })
        try { Invoke-RemotePsql $serverPg 'SELECT 1;' -What 'Reaching the local Postgres' | Out-Null }
        catch { throw "The local Postgres isn't reachable. Start it with: docker compose up -d`n$($_.Exception.Message)" }
        $exists = @(Invoke-RemotePsql $serverPg "SELECT count(*) FROM pg_database WHERE datname = '$($Database.Replace("'", "''"))';")
        if ($exists[0] -eq '0') {
            Invoke-RemotePsql $serverPg "CREATE DATABASE `"$($Database.Replace('"', '""'))`";" -What "Creating $Database" | Out-Null
            Write-Host "Created the database $Database on the local Postgres." -ForegroundColor Green
        }
    }
    $pg = ConvertTo-PgEnvironment $ConnectionString
    $hasBsk = @(Invoke-RemotePsql $pg "SELECT count(*) FROM pg_namespace WHERE nspname = 'bsk';" -What 'Checking the target')
    if ($hasBsk[0] -ne '0') { throw "$($pg.PGDATABASE) at $($pg.PGHOST) already has the bsk schema. Restore into an empty database." }

    # --- 3. Roles, settings, then the data ----------------------------------------------------------
    $image = "postgres:$($manifest['pgMajor'])-alpine"
    $dockerRun = @('run', '--rm') + (Get-DockerEnvArguments $pg) + @('-v', "${work}:/work:ro", $image)
    $schemaSql = ($plainFiles | ForEach-Object {
            (Invoke-Process docker (@('run', '--rm', '-v', "${work}:/work:ro", $image, 'pg_restore', '--schema-only', '--file=-', "/work/$_")) -What 'Reading the dump').Out
        }) -join "`n"
    Invoke-RemotePsql $pg (Get-CreateRolesSql (Get-DumpRoleNames $schemaSql)) -Image $image -What 'Creating roles' | Out-Null
    foreach ($file in $plainFiles) {
        Invoke-Process docker ($dockerRun + @('pg_restore', '--no-owner', '--exit-on-error', "--dbname=$($pg.PGDATABASE)", "/work/$file")) `
            -Environment $pg -What "Restoring $file" | Out-Null
    }
    Write-Host "Restored into $($pg.PGDATABASE) at $($pg.PGHOST)." -ForegroundColor Green

    $settings = $manifest['databaseSettings']
    if ($settings -and $settings.Count) {
        try {
            Invoke-RemotePsql $pg (Get-DatabaseSettingsSql $settings) -Image $image -What 'Setting the database settings' | Out-Null
            Write-Host "Set $(@($settings.Keys) -join ', ') on the database." -ForegroundColor Green
        }
        catch {
            Write-Warning "Couldn't set the database settings; run these as the database owner: $(@($settings.Keys | ForEach-Object { "ALTER DATABASE <db> SET $_ = '$($settings[$_])';" }) -join ' ')"
        }
    }

    # --- 4. Checks ---------------------------------------------------------------------------------
    $restored = ConvertFrom-CountLines (Invoke-RemotePsql $pg (Get-RowCountSql) -Image $image -What 'Counting rows')
    $mismatch = Compare-RowCounts $manifest['rowCounts'] $restored
    if ($mismatch) { throw "The restored database doesn't match the manifest: $($mismatch -join '; ')." }
    Write-Host "Every table's row count matches the manifest ($($restored.Count) tables)." -ForegroundColor Green

    Build-Cli
    $migrate = Invoke-Bsk $ConnectionString @('migrate')
    if ($migrate.ExitCode -ne 0) { throw "bsk migrate failed: $($migrate.Text)" }
    $applied = @($migrate.Json.applied)
    if ($applied.Count) { Write-Host "bsk migrate applied $($applied.Count) migration(s) newer than the copy: $(@($applied | ForEach-Object { $_.version }) -join ', ')." -ForegroundColor Yellow }
    else { Write-Host 'bsk migrate: the schema is up to date, nothing to apply.' -ForegroundColor Green }
    $rebuild = Invoke-Bsk $ConnectionString @('rebuild')
    if ($rebuild.ExitCode -ne 0) { throw "bsk rebuild failed: $($rebuild.Text)" }
    $verify = Invoke-Bsk $ConnectionString @('rebuild', '--verify')
    if ($verify.ExitCode -ne 0) { throw "bsk rebuild --verify found the derived tables don't match the source after a rebuild: $($verify.Text)" }
    Write-Host "bsk rebuild: $($rebuild.Json.rows) derived rows, matching the source." -ForegroundColor Green

    # --- 5. Photos -----------------------------------------------------------------------------------
    if ($ArtifactsFolder -or $UploadArtifacts) {
        $rows = Invoke-RemotePsql $pg -Image $image -What 'Listing photos' -Sql @'
SELECT DISTINCT bucket, object_key, sha256, content_type FROM bsk.artifact_object ORDER BY sha256, bucket, object_key;
'@
        $storageUrl = $null
        $secret = $null
        if ($UploadArtifacts) {
            if (-not $EnvFile) { throw '-UploadArtifacts needs -EnvFile, with the new project''s Supabase URL and secret key.' }
            $dotenv = Read-DotEnv $EnvFile
            $storageUrl = $dotenv['Storage__SupabaseUrl'] ?? $dotenv['VITE_SUPABASE_URL']
            $secret = $dotenv['Storage__SupabaseSecretKey']
            if (-not $storageUrl -or -not $secret) { throw "$EnvFile needs Storage__SupabaseUrl (or VITE_SUPABASE_URL) and Storage__SupabaseSecretKey." }
        }
        $folder = if ($ArtifactsFolder) { $ArtifactsFolder } else { Join-Path $work 'artifacts' }
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $missing = @()
        $uploaded = 0
        foreach ($row in $rows) {
            $bucket, $key, $sha, $contentType = $row.Split('|')
            $encrypted = Join-Path $BackupRoot "artifacts\$sha.age"
            if (-not (Test-Path $encrypted)) { $missing += $key; continue }
            $plain = Join-Path $folder $sha
            if (-not (Test-Path $plain)) {
                Invoke-Process $age @('--decrypt', '--identity', $KeyFile, '--output', $plain, $encrypted) -What "Decrypting $sha" | Out-Null
            }
            if ((Get-FileSha256 $plain) -ne $sha) { throw "The photo $sha doesn't hash to its name: its backup is damaged." }
            if ($UploadArtifacts) {
                Send-StorageObject $storageUrl $secret $bucket $key $plain $contentType | Out-Null
                $uploaded++
            }
        }
        if ($ArtifactsFolder) { Write-Host "Decrypted $(@($rows).Count - $missing.Count) photo(s) into $ArtifactsFolder." -ForegroundColor Green }
        if ($UploadArtifacts) { Write-Host "Uploaded $uploaded photo(s) to $storageUrl." -ForegroundColor Green }
        if ($missing.Count) { Write-Warning "$($missing.Count) photo(s) have no backup: $($missing -join ', ')" }
    }

    Write-Host ''
    Write-Host "Done. $($pg.PGDATABASE) at $($pg.PGHOST) holds the copy taken $($manifest['takenAt'])." -ForegroundColor Cyan
    if ($local) {
        Write-Host "Read it with:  dotnet run --project src/LifeOs.Cli -- check -c `"$ConnectionString`""
        Write-Host "Drop it with:  docker exec lifeos-postgres dropdb -U lifeos --force $Database"
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
