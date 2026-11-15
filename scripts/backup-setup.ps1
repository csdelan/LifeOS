#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Sets up the nightly LifeOS backup on this PC: the bsk_backup login, the age key, the settings
    and the Task Scheduler task.

.DESCRIPTION
    Run once per database (staging now, production later), and again to change a setting or
    rotate the password. Needs migration 0046 on that database (CI applies it on merge to main),
    Docker, and age (winget install --id FiloSottile.age). See docs/pilot/backup-restore.md.

      1. The age key: -AgeRecipient (age1…) uses an existing public key. Without it, the key from
         the last setup is kept, or a new pair is made in -KeyFile. Move that file into your
         password manager, print a copy, then delete it: only it can open the backups.
      2. The folders: -Destination, and the notes vault (-VaultPath) whose Git history is backed
         up too, which must be the top of a Git repository; its bundles go to -VaultDestination.
         Both are kept from the last setup unless passed. Checked before the password changes.
      3. bsk_backup: a new random password, set over the owner connection (BSK_CONNECTION_STRING
         in -EnvFile) as a SCRAM verifier, so the plaintext never reaches the server's logs. The
         login is checked before anything is saved.
      4. Settings: the bsk_backup connection string, the Supabase URL and secret key (from
         -EnvFile, for the photos), the age public key, the backup folders and the vault, saved
         to %USERPROFILE%\.lifeos\backup\<Target>.settings.xml. The secrets in it are encrypted
         with DPAPI: only this Windows account on this PC can read them.
      5. The task "\LifeOS\Backup (<Target>)": daily at -At, while you're signed in. A run
         missed while the PC was off or asleep starts as soon as it's back. It runs a copy of
         backup.ps1 and its modules installed in %USERPROFILE%\.lifeos\bin\backup, so it
         doesn't depend on the checkout (maybe a worktree, since gone) that set it up, and builds
         bsk from origin/main in this repository's main checkout. Run setup again after a push
         that changes the backup scripts.

.EXAMPLE
    ./scripts/backup-setup.ps1
    Sets up staging's backup from .env, making a new key in ~\lifeos-backup.key.

.EXAMPLE
    ./scripts/backup-setup.ps1 -VaultPath C:\Users\csdel\Vaults\Chris2.0 -HealthcheckUrl <ping URL>
    Keeps staging's key and folder, backs up the vault's Git history too, and turns on the
    dead-man's switch.

.EXAMPLE
    ./scripts/backup-setup.ps1 -Target production -EnvFile .env.prod -AgeRecipient age1...
    Sets up production's backup with the same public key as staging.
#>
[CmdletBinding()]
param(
    # A name for this database's backups: the settings file, the folder and the task.
    [ValidatePattern('^[a-z][a-z0-9-]*$')]
    [string] $Target = 'staging',

    # The dotenv file with BSK_CONNECTION_STRING (the owner) and Storage__SupabaseSecretKey.
    [string] $EnvFile = '.env',

    # The age public key (age1…). Default: the last setup's, else a new key pair in -KeyFile.
    [string] $AgeRecipient,

    # Where a new private key is written. Never inside OneDrive.
    [string] $KeyFile = (Join-Path $HOME 'lifeos-backup.key'),

    # Where the backups go. Default: OneDrive\Backups\LifeOS\<Target>.
    [string] $Destination,

    # A dead-man's switch ping URL (healthchecks.io): emails you when a night passes without a backup.
    [string] $HealthcheckUrl,

    # The Obsidian vault whose Git history is backed up too, such as C:\Users\csdel\Vaults\Chris2.0:
    # the top folder of a Git repository. Default: the last setup's. '' stops backing it up.
    [string] $VaultPath,

    # Where the vault's bundles and snapshots go. Default: a vault folder beside -Destination,
    # so OneDrive\Backups\LifeOS\vault.
    [string] $VaultDestination,

    # The vault's read-only snapshots, each copied once: the files named "<vault name> …" in it.
    # Default: the _snapshots folder beside the vault, when there is one. '' copies none.
    [string] $VaultSnapshots,

    # When the task runs each day, in local time.
    [string] $At = '03:17',

    # Daily copies older than this are deleted. Monthly copies are kept for good.
    [ValidateRange(7, 3650)]
    [int] $KeepDailyDays = 35,

    # Save the settings without registering the scheduled task.
    [switch] $NoTask
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LifeOsBackup.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LifeOsBsk.psm1') -Force
$repo = Split-Path -Parent $PSScriptRoot

$existing = if (Test-Path (Get-BackupSettingsPath $Target)) { Read-BackupSettings -Target $Target } else { $null }
$age = Find-AgeTool 'age'
$ageKeygen = Find-AgeTool 'age-keygen'
Assert-Docker

$envPath = if ([IO.Path]::IsPathRooted($EnvFile)) { $EnvFile } else { Join-Path $repo $EnvFile }
$dotenv = Read-DotEnv $envPath
function Get-Setting([string] $Name) {
    $fromProcess = [Environment]::GetEnvironmentVariable($Name)
    if ($fromProcess) { return $fromProcess }
    $dotenv[$Name]
}

# --- 1. The age key ------------------------------------------------------------------------------
$keyOnDisk = $false
if (-not $AgeRecipient) { $AgeRecipient = $existing ? $existing['AgeRecipient'] : $null }
if (-not $AgeRecipient) {
    if ($env:OneDrive -and [IO.Path]::GetFullPath($KeyFile).StartsWith([IO.Path]::GetFullPath($env:OneDrive), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Keep the private key out of OneDrive: it would sit beside the backups it opens.'
    }
    # A key file already there (say, from a setup that stopped part way) is used, not replaced.
    if (-not (Test-Path $KeyFile)) { Invoke-Process $ageKeygen @('-o', $KeyFile) -What 'age-keygen' | Out-Null }
    $AgeRecipient = (Invoke-Process $ageKeygen @('-y', $KeyFile) -What 'age-keygen -y').Out.Trim()
    $keyOnDisk = $true
}
if ($AgeRecipient -notmatch '^age1[0-9a-z]+$') { throw "'$AgeRecipient' isn't an age public key (age1…)." }

# --- 2. Where the backups go, and the vault: checked before the password changes, since a setup
# that stops after that leaves the saved settings with a password that no longer works. ----------
if (-not $Destination) { $Destination = $existing ? $existing['Destination'] : (Get-DefaultBackupRoot $Target) }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

# The vault: named here, or the last setup's three settings as they were.
if ($PSBoundParameters.ContainsKey('VaultPath')) {
    if ($VaultPath) { $VaultPath = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($VaultPath) }
    if ($VaultPath -and -not $PSBoundParameters.ContainsKey('VaultSnapshots')) {
        $beside = Join-Path (Split-Path -Parent $VaultPath) '_snapshots'
        $VaultSnapshots = (Test-Path -LiteralPath $beside -PathType Container) ? $beside : $null
    }
}
elseif ($existing) {
    $VaultPath = $existing['VaultPath']
    if (-not $VaultDestination) { $VaultDestination = $existing['VaultDestination'] }
    if (-not $PSBoundParameters.ContainsKey('VaultSnapshots')) { $VaultSnapshots = $existing['VaultSnapshots'] }
}
if ($VaultPath) {
    try { Assert-VaultRepository $VaultPath }
    catch { throw "$($_.Exception.Message) Pass -VaultPath to name the vault, or -VaultPath '' to stop backing one up." }
    if (-not $VaultDestination) { $VaultDestination = Join-Path (Split-Path -Parent $Destination) 'vault' }
    $VaultDestination = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($VaultDestination)
    $vaultRoot = $VaultPath.TrimEnd('\')
    if ($VaultDestination.TrimEnd('\') -eq $vaultRoot -or $VaultDestination.StartsWith("$vaultRoot\", [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Keep the vault''s backups out of the vault: they would sync to every device and change the vault each night.'
    }
    if ($VaultSnapshots) { $VaultSnapshots = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($VaultSnapshots) }
    New-Item -ItemType Directory -Force -Path $VaultDestination | Out-Null
}
else {
    $VaultPath = $null
    $VaultDestination = $null
    $VaultSnapshots = $null
}

# --- 3. The bsk_backup login -----------------------------------------------------------------------
$owner = Get-Setting 'BSK_CONNECTION_STRING'
if (-not $owner) { throw "BSK_CONNECTION_STRING (the owner connection) isn't in $EnvFile." }
$ownerPg = ConvertTo-PgEnvironment $owner
Write-Host "Database: $($ownerPg.PGHOST) / $($ownerPg.PGDATABASE)" -ForegroundColor Cyan

$hasRole = @(Invoke-RemotePsql $ownerPg "SELECT count(*) FROM pg_roles WHERE rolname = 'bsk_backup';" -What 'Looking for bsk_backup')
if ($hasRole[0] -ne '1') {
    throw 'The role bsk_backup doesn''t exist yet. Apply migration 0046 first (push to main, or scripts/migrate-staging.ps1).'
}
$password = New-RandomHex 32
Invoke-RemotePsql $ownerPg "ALTER ROLE bsk_backup WITH LOGIN PASSWORD '$(New-ScramVerifier $password)';" -What 'Setting the bsk_backup password' | Out-Null

$backupUser = 'bsk_backup' + (Get-PoolerSuffix $ownerPg.PGUSER)
$connection = Set-ConnectionValues $owner @{ Username = $backupUser; Password = $password }
$backupPg = ConvertTo-PgEnvironment $connection
$whoami = $null
for ($attempt = 1; $attempt -le 6 -and -not $whoami; $attempt++) {
    try { $whoami = @(Invoke-RemotePsql $backupPg 'SELECT current_user;' -What 'Signing in as bsk_backup')[0] }
    catch { if ($attempt -eq 6) { throw } ; Start-Sleep -Seconds 5 }
}
if ($whoami -ne 'bsk_backup') { throw "Signed in as '$whoami', not bsk_backup." }
Write-Host 'bsk_backup can sign in with its new password.' -ForegroundColor Green

# --- 4. Settings -----------------------------------------------------------------------------------
$secretKey = Get-Setting 'Storage__SupabaseSecretKey'
$supabaseUrl = Get-Setting 'Storage__SupabaseUrl'
if (-not $supabaseUrl) { $supabaseUrl = Get-Setting 'VITE_SUPABASE_URL' }
$poolerRef = (Get-PoolerSuffix $ownerPg.PGUSER).TrimStart('.')
if (-not $supabaseUrl -and $poolerRef) { $supabaseUrl = "https://$poolerRef.supabase.co" }
if (-not $secretKey) { Write-Warning "No Storage__SupabaseSecretKey in $EnvFile`: photos stored in Supabase won't be backed up." }

if (-not $HealthcheckUrl -and $existing) { $HealthcheckUrl = ConvertFrom-SecureText $existing['HealthcheckUrl'] }

$settingsPath = Save-BackupSettings @{
    Format            = 1
    Target            = $Target
    ConnectionString  = ConvertTo-SecureText $connection
    SupabaseUrl       = $supabaseUrl
    SupabaseSecretKey = ConvertTo-SecureText $secretKey
    AgeRecipient      = $AgeRecipient
    Destination       = $Destination
    HealthcheckUrl    = ConvertTo-SecureText $HealthcheckUrl
    KeepDailyDays     = $KeepDailyDays
    VaultPath         = $VaultPath
    VaultDestination  = $VaultDestination
    VaultSnapshots    = $VaultSnapshots
}
Write-Host "Saved the settings to $settingsPath." -ForegroundColor Green

# --- 5. The scheduled task ---------------------------------------------------------------------------
if (-not $NoTask) {
    # A path that survives PowerShell updates: the machine install, else pwsh on PATH. (This
    # process may be dotnet.exe, when PowerShell is a .NET tool.)
    $pwsh = @("$env:ProgramFiles\PowerShell\7\pwsh.exe", (Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty Source), (Join-Path $PSHOME 'pwsh.exe')) |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    # A copy of the scripts the task runs, outside any checkout (code review CR118): a worktree
    # that set it up may be gone by tonight. bsk is built from origin/main in the main checkout.
    $installed = Join-Path $HOME '.lifeos' 'bin' 'backup'
    New-Item -ItemType Directory -Force $installed | Out-Null
    foreach ($file in 'backup.ps1', 'LifeOsBackup.psm1', 'LifeOsBsk.psm1') {
        Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $installed $file) -Force
    }
    $sourceCommit = (git -C $repo rev-parse --short HEAD).Trim()
    if (git -C $repo status --porcelain --untracked-files=no -- scripts) { $sourceCommit += ' (with local changes)' }
    Set-Content -Path (Join-Path $installed 'commit.txt') -Value $sourceCommit
    $mainCheckout = Get-MainCheckout $repo
    $script = Join-Path $installed 'backup.ps1'
    # conhost --headless runs it with no console window popping up. Its exit code is always 0,
    # so Task Scheduler's Last Run Result only says the run started: the log, the notification
    # and the dead-man's switch say how it went.
    $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\conhost.exe') `
        -Argument "--headless `"$pwsh`" -NoProfile -NonInteractive -File `"$script`" -Target $Target -Repo `"$mainCheckout`"" `
        -WorkingDirectory $installed
    $trigger = New-ScheduledTaskTrigger -Daily -At ([datetime]::ParseExact($At, 'HH:mm', $null))
    $taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Hours 1) -MultipleInstances IgnoreNew
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskPath '\LifeOS\' -TaskName "Backup ($Target)" -Action $action -Trigger $trigger `
        -Settings $taskSettings -Principal $principal -Force `
        -Description "Backs up LifeOS ($Target) to $Destination. scripts/backup.ps1; docs/pilot/backup-restore.md." | Out-Null
    Write-Host "Registered the task \LifeOS\Backup ($Target): daily at $At, or as soon as the PC is back if it was off." -ForegroundColor Green
    Write-Host "It runs the copy in $installed and builds bsk from origin/main in $mainCheckout." -ForegroundColor Green
}

Write-Host ''
Write-Host "Backups go to $Destination, encrypted to $AgeRecipient."
if ($VaultPath) {
    $snapshotNote = if ($VaultSnapshots) { ", with its snapshots from $VaultSnapshots" } else { '' }
    Write-Host "The Git history of the vault $VaultPath goes to $VaultDestination$snapshotNote."
}
else {
    Write-Host 'No vault is backed up. Pass -VaultPath to back up a notes vault''s Git history too.' -ForegroundColor Yellow
}
if (-not $HealthcheckUrl) {
    Write-Host 'No dead-man''s switch is set up: a night with no backup alerts no one. Pass -HealthcheckUrl.' -ForegroundColor Yellow
}
if ($keyOnDisk) {
    Write-Host ''
    Write-Host "Your private key is in $KeyFile. Only it can open the backups." -ForegroundColor Yellow
    Write-Host '  1. Put its contents in your password manager.' -ForegroundColor Yellow
    Write-Host '  2. Print a copy and keep it somewhere safe.' -ForegroundColor Yellow
    Write-Host '  3. Delete the file from this PC.' -ForegroundColor Yellow
    Write-Host 'Lose it and every backup is unreadable. The restore drill checks you still have it.' -ForegroundColor Yellow
}
Write-Host "Run one now with:  ./scripts/backup.ps1 -Target $Target"
