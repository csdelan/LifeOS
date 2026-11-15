#Requires -Version 7.0
<#
Shared pieces of the LifeOS backup scripts (backup.ps1, backup-setup.ps1, restore.ps1).
See docs/pilot/backup-restore.md. Postgres tools run in a postgres:<major>-alpine container,
so this PC needs Docker but no Postgres install.
#>

Set-StrictMode -Version 3.0

# The schemas a backup holds, besides public.schema_migrations.
$script:BackupSchemas = @('bsk', 'bsk_derived', 'bsk_ops')

# psql and pg_dump/pg_restore of this major talk to any server we use; the live server's own
# major is used for the dump itself.
$script:ProbeImage = 'postgres:17-alpine'

$script:LogPath = $null

# ---------------------------------------------------------------------------------------------
# Settings
# ---------------------------------------------------------------------------------------------

# Settings and logs. Not under AppData: a packaged app (the Claude desktop app, and any terminal it
# opens) writes AppData to a private copy that the scheduled task never sees.
function Get-BackupHome { Join-Path $HOME '.lifeos\backup' }

function Get-BackupSettingsPath([Parameter(Mandatory)][string] $Target) {
    Join-Path (Get-BackupHome) "$Target.settings.xml"
}

function Get-DefaultBackupRoot([Parameter(Mandatory)][string] $Target) {
    if (-not $env:OneDrive) { throw "OneDrive isn't set up for this Windows account (no OneDrive folder)." }
    Join-Path $env:OneDrive "Backups\LifeOS\$Target"
}

<#
The settings file holds the bsk_backup connection string and the Supabase secret key as
SecureStrings, which Export-Clixml encrypts with DPAPI: only this Windows account on this PC can
read them.
#>
function Read-BackupSettings([Parameter(Mandatory)][string] $Target) {
    $path = Get-BackupSettingsPath $Target
    if (-not (Test-Path $path)) {
        throw "There are no backup settings for '$Target' ($path). Run scripts/backup-setup.ps1 -Target $Target first."
    }
    Import-Clixml $path
}

function Save-BackupSettings([Parameter(Mandatory)][hashtable] $Settings) {
    $path = Get-BackupSettingsPath $Settings['Target']
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    $Settings | Export-Clixml -Path $path -Force
    $path
}

function ConvertFrom-SecureText($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [securestring]) { return ConvertFrom-SecureString $Value -AsPlainText }
    [string] $Value
}

function ConvertTo-SecureText([string] $Value) {
    if ([string]::IsNullOrEmpty($Value)) { return $null }
    ConvertTo-SecureString $Value -AsPlainText -Force
}

# KEY=VALUE lines, as scripts/migrate-staging.ps1 reads them.
function Read-DotEnv([Parameter(Mandatory)][string] $Path) {
    $values = @{}
    if (-not (Test-Path $Path)) { throw "$Path doesn't exist." }
    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $values[$trimmed.Substring(0, $eq).Trim()] = $trimmed.Substring($eq + 1).Trim().Trim('"')
    }
    $values
}

# ---------------------------------------------------------------------------------------------
# Logging and alerts
# ---------------------------------------------------------------------------------------------

function Start-BackupLog([Parameter(Mandatory)][string] $Name) {
    $folder = Join-Path (Get-BackupHome) 'logs'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $script:LogPath = Join-Path $folder "$Name-$((Get-Date).ToString('yyyyMMdd')).log"
    Get-ChildItem $folder -Filter '*.log' | Where-Object LastWriteTime -lt (Get-Date).AddDays(-60) |
        Remove-Item -Force -ErrorAction SilentlyContinue
    $script:LogPath
}

function Write-BackupLog([Parameter(Mandatory)][string] $Message, [ValidateSet('Info', 'Warn', 'Error')][string] $Level = 'Info') {
    $line = "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) [$Level] $Message"
    if ($script:LogPath) { Add-Content -Path $script:LogPath -Value $line -Encoding utf8 }
    $color = @{ Info = 'Gray'; Warn = 'Yellow'; Error = 'Red' }[$Level]
    Write-Host $line -ForegroundColor $color
}

# A Windows notification, through Windows PowerShell's WinRT access (pwsh has none). Best effort.
function Show-BackupToast([string] $Title, [string] $Message) {
    try {
        $xmlTitle = [Security.SecurityElement]::Escape($Title)
        $xmlMessage = [Security.SecurityElement]::Escape($Message.Substring(0, [Math]::Min(250, $Message.Length)))
        $toast = @"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
`$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
`$xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>$($xmlTitle.Replace("'", "''"))</text><text>$($xmlMessage.Replace("'", "''"))</text></binding></visual></toast>')
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe').Show((New-Object Windows.UI.Notifications.ToastNotification `$xml))
"@
        $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($toast))
        Invoke-Process $windowsPowerShell @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded) -AllowFailure | Out-Null
    }
    catch {
        Write-BackupLog "Couldn't show a notification: $($_.Exception.Message)" -Level Warn
    }
}

# The dead-man's switch (healthchecks.io or alike): '' on success, 'start', or 'fail'.
# The log says whether it's set up, since the URL itself is encrypted in the settings.
function Send-HealthcheckPing([hashtable] $Settings, [string] $Suffix, [string] $Body = '') {
    $url = ConvertFrom-SecureText $Settings['HealthcheckUrl']
    if (-not $url) {
        if ($Suffix -eq 'start') {
            Write-BackupLog "No dead-man's switch is set up, so a night with no backup alerts no one. See docs/pilot/backup-restore.md." -Level Warn
        }
        return
    }
    $uri = if ($Suffix) { "$($url.TrimEnd('/'))/$Suffix" } else { $url }
    try {
        Invoke-RestMethod -Method Post -Uri $uri -Body $Body -TimeoutSec 15 -MaximumRetryCount 2 -RetryIntervalSec 5 | Out-Null
        if ($Suffix -ne 'start') { Write-BackupLog "Told the dead-man's switch the run $(if ($Suffix) { 'failed' } else { 'succeeded' })." }
    }
    catch {
        Write-BackupLog "Couldn't ping the dead-man's switch: $($_.Exception.Message)" -Level Warn
    }
}

# ---------------------------------------------------------------------------------------------
# Processes and tools
# ---------------------------------------------------------------------------------------------

<#
Runs a program with exact arguments (no shell quoting), optional extra environment variables and
standard input, and returns its exit code and output. Throws on a non-zero exit unless
-AllowFailure. Secrets go in -Environment or -InputText, never in arguments.
#>
function Invoke-Process {
    param(
        [Parameter(Mandatory, Position = 0)][string] $FilePath,
        [Parameter(Position = 1)][string[]] $ArgumentList = @(),
        [hashtable] $Environment = @{},
        [string] $InputText,
        [string] $What,
        [switch] $AllowFailure
    )
    $psi = [Diagnostics.ProcessStartInfo]::new($FilePath)
    foreach ($argument in $ArgumentList) { $psi.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $psi.Environment[$key] = [string] $Environment[$key] }
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    # Always its own stdin: inheriting ours hangs docker when we run as a background job, whose
    # stdin is PowerShell's channel to the job.
    $psi.RedirectStandardInput = $true
    $psi.StandardInputEncoding = [Text.UTF8Encoding]::new($false)

    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if ($PSBoundParameters.ContainsKey('InputText')) { $process.StandardInput.Write($InputText) }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            Out      = $stdout.GetAwaiter().GetResult()
            Err      = $stderr.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }

    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        $detail = @($result.Err.Trim(), $result.Out.Trim()) | Where-Object { $_ } | Select-Object -First 1
        $label = if ($What) { $What } else { Split-Path $FilePath -Leaf }
        throw "$label failed (exit $($result.ExitCode)): $detail"
    }
    $result
}

function Find-AgeTool([string] $Name = 'age') {
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    # winget's portable install, for a session started before winget added it to PATH.
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $found = Get-ChildItem $packages -Directory -Filter 'FiloSottile.age*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName "age\$Name.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($found) { return $found }
    throw "$Name isn't installed. Install it with:  winget install --id FiloSottile.age"
}

# Docker Desktop may not be up yet at logon, or may not start on its own: start it and wait.
function Assert-Docker {
    if (-not (Get-Command docker -CommandType Application -ErrorAction SilentlyContinue)) {
        throw "Docker isn't installed or isn't on PATH."
    }
    if ((Invoke-Process docker @('info', '--format', '{{.ServerVersion}}') -AllowFailure).ExitCode -eq 0) { return }

    $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (Test-Path $desktop) {
        Write-BackupLog 'Docker isn''t running; starting Docker Desktop.'
        Start-Process $desktop
    }
    $deadline = (Get-Date).AddMinutes(4)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        if ((Invoke-Process docker @('info', '--format', '{{.ServerVersion}}') -AllowFailure).ExitCode -eq 0) { return }
    }
    throw "Docker isn't running and didn't start within 4 minutes."
}

# ToHexString, not ToHexStringLower: pwsh 7.4 runs on .NET 8, which lacks the second, and the
# tests run this module on whatever pwsh CI's runner has.
function New-RandomHex([int] $Bytes = 32) {
    [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes)).ToLowerInvariant()
}

function Get-FileSha256([Parameter(Mandatory)][string] $Path) {
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-WorkFolder {
    $path = Join-Path ([IO.Path]::GetTempPath()) "lifeos-backup-$(New-RandomHex 4)"
    New-Item -ItemType Directory -Path $path | Out-Null
    $path
}

# ---------------------------------------------------------------------------------------------
# Postgres connections
# ---------------------------------------------------------------------------------------------

# The docker-compose Postgres, as KernelConnectionString.LocalDevelopmentDefault (a public,
# local-only credential).
function Get-LocalDevelopmentConnectionString { 'Host=localhost;Port=5432;Database=lifeos;Username=lifeos;Password=lifeos' }

function Get-ConnectionValue($Builder, [string[]] $Keys) {
    foreach ($key in $Keys) {
        if ($Builder.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace([string] $Builder[$key])) {
            return [string] $Builder[$key]
        }
    }
    $null
}

<#
libpq environment variables (PGHOST, PGUSER, PGPASSWORD, …) for an Npgsql connection string, for
psql, pg_dump and pg_restore in a container. localhost becomes host.docker.internal, since inside
the container localhost is the container.
#>
function ConvertTo-PgEnvironment([Parameter(Mandatory)][string] $ConnectionString) {
    $builder = [Data.Common.DbConnectionStringBuilder]::new()
    # PowerShell treats the builder as a dictionary, so .ConnectionString =  would add a key.
    $builder.set_ConnectionString($ConnectionString)
    $server = Get-ConnectionValue $builder 'Host', 'Server'
    if (-not $server) { throw 'The connection string has no Host.' }
    if ($server -in 'localhost', '127.0.0.1', '::1') { $server = 'host.docker.internal' }
    $sslMode = switch -Regex (((Get-ConnectionValue $builder 'SSL Mode', 'SslMode', 'sslmode') ?? '').ToLowerInvariant()) {
        '^require$' { 'require' }
        '^verify-?ca$' { 'verify-ca' }
        '^verify-?full$' { 'verify-full' }
        '^disable$' { 'disable' }
        '^allow$' { 'allow' }
        default { 'prefer' }
    }
    @{
        PGHOST            = $server
        PGPORT            = (Get-ConnectionValue $builder 'Port') ?? '5432'
        PGDATABASE        = (Get-ConnectionValue $builder 'Database', 'Initial Catalog') ?? 'postgres'
        PGUSER            = Get-ConnectionValue $builder 'Username', 'User ID', 'UserID', 'User', 'User Name'
        PGPASSWORD        = (Get-ConnectionValue $builder 'Password', 'PWD') ?? ''
        PGSSLMODE         = $sslMode
        PGAPPNAME         = 'lifeos-backup'
        PGCONNECT_TIMEOUT = '30'
    }
}

# The same connection string with another user, password or database.
function Set-ConnectionValues([Parameter(Mandatory)][string] $ConnectionString, [hashtable] $Values) {
    $builder = [Data.Common.DbConnectionStringBuilder]::new()
    # PowerShell treats the builder as a dictionary, so .ConnectionString =  would add a key.
    $builder.set_ConnectionString($ConnectionString)
    $aliases = @{
        Username = @('Username', 'User ID', 'UserID', 'User', 'User Name')
        Password = @('Password', 'PWD')
        Database = @('Database', 'Initial Catalog')
    }
    foreach ($name in $Values.Keys) {
        $existing = $aliases[$name] | Where-Object { $builder.ContainsKey($_) } | Select-Object -First 1
        $builder[($existing ?? $name)] = $Values[$name]
    }
    $builder.get_ConnectionString()
}

# Supabase's pooler wants <role>.<project-ref>; a plain server takes the role alone.
function Get-PoolerSuffix([string] $User) {
    if ($User -and $User.Contains('.')) { return $User.Substring($User.IndexOf('.')) }
    ''
}

function Get-DockerEnvArguments([hashtable] $Environment) {
    $arguments = @()
    foreach ($key in $Environment.Keys) { $arguments += @('-e', $key) }
    $arguments
}

# Runs SQL (from stdin) with psql in a throwaway container; returns the unaligned output lines.
function Invoke-RemotePsql([Parameter(Mandatory)][hashtable] $PgEnvironment, [Parameter(Mandatory)][string] $Sql,
    [string] $Image = $script:ProbeImage, [string] $What = 'psql') {
    $arguments = @('run', '--rm', '-i') + (Get-DockerEnvArguments $PgEnvironment) +
        @($Image, 'psql', '-X', '-q', '-A', '-t', '-F', '|', '-v', 'ON_ERROR_STOP=1')
    $result = Invoke-Process docker $arguments -Environment $PgEnvironment -InputText $Sql -What $What
    @($result.Out -split "`r?`n" | Where-Object { $_ -ne '' })
}

# Every table's exact row count: the three schemas and the migration history.
function Get-RowCountSql {
    $schemas = ($script:BackupSchemas | ForEach-Object { "'$_'" }) -join ', '
    @"
SELECT format('%I.%I', n.nspname, c.relname),
       (xpath('/row/c/text()',
              query_to_xml(format('SELECT count(*) AS c FROM %I.%I', n.nspname, c.relname), false, true, '')))[1]::text
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND (n.nspname IN ($schemas) OR (n.nspname = 'public' AND c.relname = 'schema_migrations'))
ORDER BY 1;
"@
}

function ConvertFrom-CountLines([string[]] $Lines) {
    $counts = [ordered]@{}
    foreach ($line in $Lines) {
        $table, $count = $line.Split('|', 2)
        $counts[$table] = [long] $count
    }
    $counts
}

# Differences between two row-count maps, as "table a → b" strings; empty when they match.
function Compare-RowCounts($Expected, $Actual) {
    $differences = @()
    $tables = @($Expected.Keys) + @($Actual.Keys) | Sort-Object -Unique
    foreach ($table in $tables) {
        $a = if ($Expected.Contains($table)) { $Expected[$table] } else { 'missing' }
        $b = if ($Actual.Contains($table)) { $Actual[$table] } else { 'missing' }
        if ("$a" -ne "$b") { $differences += "$table $a → $b" }
    }
    $differences
}

<#
The roles a dump's grants, default privileges and policies name. A restore into a server that
lacks one fails, so each is created (NOLOGIN) first: bsk_reader and bsk_backup, and on a dump
from Supabase its own roles (the history table is granted to service_role).
#>
function Get-DumpRoleNames([Parameter(Mandatory)][string] $SchemaSql) {
    $names = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    $identifier = '(?:"(?:[^"]|"")+"|[A-Za-z_][A-Za-z0-9_$]*)'
    foreach ($line in $SchemaSql -split "`r?`n") {
        if ($line -cnotmatch '^(GRANT|REVOKE|ALTER DEFAULT PRIVILEGES|CREATE POLICY)\b') { continue }
        foreach ($match in [regex]::Matches($line, "\b(?:TO|FROM|FOR ROLE|GRANTED BY)\s+($identifier(?:\s*,\s*$identifier)*)")) {
            foreach ($name in [regex]::Matches($match.Groups[1].Value, $identifier)) {
                $value = $name.Value
                if ($value.StartsWith('"')) { $value = $value.Substring(1, $value.Length - 2).Replace('""', '"') }
                elseif ($value -ceq 'PUBLIC') { continue }
                else { $value = $value.ToLowerInvariant() }
                [void] $names.Add($value)
            }
        }
    }
    @($names)
}

function Get-CreateRolesSql([string[]] $Roles) {
    if (-not $Roles -or $Roles.Count -eq 0) { return 'SELECT 1;' }
    $list = ($Roles | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', '
    @"
SELECT format('CREATE ROLE %I NOLOGIN', r)
FROM unnest(ARRAY[$list]::text[]) AS r
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r)
\gexec
"@
}

# ALTER DATABASE … SET for the bsk.* settings a manifest recorded (bsk.home_zone).
function Get-DatabaseSettingsSql($Settings) {
    $statements = foreach ($name in @($Settings.Keys)) {
        if ($name -notmatch '^bsk\.[a-z_]+$') { throw "Unexpected database setting '$name' in the manifest." }
        $value = ([string] $Settings[$name]).Replace("'", "''")
        "DO `$`$ BEGIN EXECUTE format('ALTER DATABASE %I SET $name = %L', current_database(), '$value'); END `$`$;"
    }
    ($statements -join "`n")
}

# ---------------------------------------------------------------------------------------------
# A scratch Postgres in Docker, for the restore test
# ---------------------------------------------------------------------------------------------

function Start-ScratchPostgres([Parameter(Mandatory)][string] $Image, [Parameter(Mandatory)][string] $WorkFolder) {
    $name = "lifeos-backup-$(New-RandomHex 4)"
    $password = New-RandomHex 16
    Invoke-Process docker @('run', '-d', '--name', $name, '-e', 'POSTGRES_PASSWORD', '-e', 'POSTGRES_DB=restore',
        '-p', '127.0.0.1::5432', '-v', "${WorkFolder}:/work:ro", $Image) `
        -Environment @{ POSTGRES_PASSWORD = $password } -What 'Starting a scratch Postgres' | Out-Null
    $scratch = [pscustomobject]@{ Name = $name; Password = $password; Port = $null }

    # Over TCP, so the image's first, socket-only start doesn't count as ready.
    $deadline = (Get-Date).AddSeconds(90)
    while ($true) {
        $ready = Invoke-Process docker @('exec', $name, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres', '-d', 'restore') -AllowFailure
        if ($ready.ExitCode -eq 0) { break }
        if ((Get-Date) -gt $deadline) { throw "The scratch Postgres didn't start: $($ready.Out.Trim())" }
        Start-Sleep -Milliseconds 500
    }
    $mapping = (Invoke-Process docker @('port', $name, '5432/tcp') -What 'docker port').Out.Trim() -split "`r?`n" | Select-Object -First 1
    $scratch.Port = [int] ($mapping -replace '^.*:', '')
    $scratch
}

function Remove-ScratchPostgres($Scratch) {
    if ($Scratch) { Invoke-Process docker @('rm', '-f', '-v', $Scratch.Name) -AllowFailure | Out-Null }
}

function Invoke-ScratchPsql($Scratch, [Parameter(Mandatory)][string] $Sql, [string] $What = 'psql (restore test)') {
    $result = Invoke-Process docker @('exec', '-i', $Scratch.Name, 'psql', '-X', '-q', '-A', '-t', '-F', '|',
        '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'restore') -InputText $Sql -What $What
    @($result.Out -split "`r?`n" | Where-Object { $_ -ne '' })
}

function Get-ScratchConnectionString($Scratch) {
    "Host=127.0.0.1;Port=$($Scratch.Port);Database=restore;Username=postgres;Password=$($Scratch.Password)"
}

# ---------------------------------------------------------------------------------------------
# The bsk CLI, built from origin/main (code review CR118)
# ---------------------------------------------------------------------------------------------

Import-Module (Join-Path $PSScriptRoot 'LifeOsBsk.psm1') -Force
$script:Bsk = $null

# Finds or builds the bsk Invoke-Bsk runs: from origin/main, as staging runs it, never a working
# copy, which may be behind staging (so rebuild --verify would differ) or hold a branch's migrations.
function Initialize-Bsk([Parameter(Mandatory)][string] $Repo) {
    $script:Bsk = Get-MainBsk -Repo $Repo
    $script:Bsk
}

# Runs `bsk <arguments> --json` against a connection string; returns exit code, parsed JSON and text.
function Invoke-Bsk([Parameter(Mandatory)][string] $ConnectionString, [Parameter(Mandatory)][string[]] $Arguments) {
    if (-not $script:Bsk) { throw 'Run Initialize-Bsk before Invoke-Bsk.' }
    $result = Invoke-Process dotnet (@($script:Bsk.Path) + $Arguments + @('--json')) `
        -Environment @{ BSK_CONNECTION_STRING = $ConnectionString } -AllowFailure
    $json = $null
    $last = @($result.Out -split "`r?`n" | Where-Object { $_.Trim() -ne '' }) | Select-Object -Last 1
    if ($last) { try { $json = $last | ConvertFrom-Json } catch { $json = $null } }
    [pscustomobject]@{ ExitCode = $result.ExitCode; Json = $json; Text = ($result.Err.Trim(), $result.Out.Trim() | Where-Object { $_ } | Select-Object -First 1) }
}

# ---------------------------------------------------------------------------------------------
# Supabase Storage (photo bytes)
# ---------------------------------------------------------------------------------------------

function Get-StorageHeaders([Parameter(Mandatory)][string] $SecretKey) {
    $headers = @{ apikey = $SecretKey }
    # A legacy service_role JWT also goes as the bearer token, as SupabaseObjectStorage sends it.
    if ($SecretKey.StartsWith('eyJ')) { $headers['Authorization'] = "Bearer $SecretKey" }
    $headers
}

function Get-StorageObjectUri([string] $Url, [string] $Bucket, [string] $Key) {
    $path = (@($Bucket) + $Key.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    "$($Url.TrimEnd('/'))/storage/v1/object/$path"
}

function Save-StorageObject([string] $Url, [string] $SecretKey, [string] $Bucket, [string] $Key, [string] $OutFile) {
    $uri = Get-StorageObjectUri $Url $Bucket $Key
    try {
        $response = Invoke-WebRequest -Uri $uri -Headers (Get-StorageHeaders $SecretKey) -OutFile $OutFile -PassThru `
            -SkipHttpErrorCheck -TimeoutSec 120 -MaximumRetryCount 2 -RetryIntervalSec 5
    }
    catch {
        throw "Couldn't reach Supabase Storage for $Key`: $($_.Exception.Message)"
    }
    $status = [int] $response.StatusCode
    if ($status -ge 200 -and $status -lt 300) { return }

    # The error body landed in the file; Storage puts the real status (404) in it.
    $body = Get-Content $OutFile -Raw -ErrorAction SilentlyContinue
    Remove-Item $OutFile -Force -ErrorAction SilentlyContinue
    try {
        $parsed = $body | ConvertFrom-Json -ErrorAction Stop
        if ($parsed.PSObject.Properties['statusCode'] -and "$($parsed.statusCode)" -match '^\d{3}$') { $status = [int] "$($parsed.statusCode)" }
    }
    catch { }
    $reason = if ($status -eq 404) { "it isn't in the bucket $Bucket" } else { "Supabase Storage answered HTTP $status" }
    throw "Couldn't download $Key`: $reason"
}

# Uploads bytes to a key, never overwriting; an object already there counts as uploaded (the keys
# are content-addressed). A missing bucket is created, private. Mirrors SupabaseObjectStorage.
function Send-StorageObject([string] $Url, [string] $SecretKey, [string] $Bucket, [string] $Key, [string] $InFile, [string] $ContentType) {
    $headers = Get-StorageHeaders $SecretKey
    for ($attempt = 1; ; $attempt++) {
        $upload = $headers.Clone()
        $upload['x-upsert'] = 'false'
        $response = Invoke-WebRequest -Method Post -Uri (Get-StorageObjectUri $Url $Bucket $Key) -Headers $upload `
            -InFile $InFile -ContentType $ContentType -SkipHttpErrorCheck -TimeoutSec 120
        $status = Get-StorageStatus $response
        if ($status -ge 200 -and $status -lt 300) { return 'uploaded' }
        if ($status -eq 409) { return 'already there' }
        if ($status -eq 404 -and $attempt -eq 1) {
            $body = @{ id = $Bucket; name = $Bucket; public = $false } | ConvertTo-Json
            $created = Invoke-WebRequest -Method Post -Uri "$($Url.TrimEnd('/'))/storage/v1/bucket" -Headers $headers `
                -Body $body -ContentType 'application/json' -SkipHttpErrorCheck -TimeoutSec 60
            $createdStatus = Get-StorageStatus $created
            if (($createdStatus -lt 200 -or $createdStatus -ge 300) -and $createdStatus -ne 409) {
                throw "Supabase Storage refused to create the bucket $Bucket (HTTP $createdStatus)."
            }
            continue
        }
        throw "Supabase Storage refused to store $Key in $Bucket (HTTP $status): $($response.Content)"
    }
}

# Storage answers some errors as HTTP 400 with the real status in the body's statusCode.
function Get-StorageStatus($Response) {
    $status = [int] $Response.StatusCode
    try {
        $body = $Response.Content | ConvertFrom-Json -ErrorAction Stop
        if ($body.PSObject.Properties['statusCode'] -and "$($body.statusCode)" -match '^\d{3}$') { $status = [int] "$($body.statusCode)" }
    }
    catch { }
    $status
}

# ---------------------------------------------------------------------------------------------
# Daily copies and their stamps
# ---------------------------------------------------------------------------------------------

$script:StampFormat = "yyyyMMdd'T'HHmmss'Z'"

# A copy's name: the UTC time it was taken, such as 20261029T101700Z.
function ConvertTo-BackupStamp([Parameter(Mandatory)][datetime] $Time) {
    $Time.ToUniversalTime().ToString($script:StampFormat, [Globalization.CultureInfo]::InvariantCulture)
}

# The UTC time a <stamp> name stands for, or $null for any other name.
function ConvertFrom-BackupStamp([string] $Name) {
    $taken = [datetime]::MinValue
    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    if ([datetime]::TryParseExact($Name, $script:StampFormat, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref] $taken)) {
        return $taken
    }
    $null
}

<#
Deletes the daily copies in -Folder taken more than -KeepDays before -Now, and a .partial left by
a run that stopped more than a day ago. Monthly copies live in another folder and are kept for
good. -KeepNewest spares the newest copy whatever its age: the vault's is skipped on nights the
vault doesn't change, so its newest may be the only copy of what the vault holds now.
#>
function Remove-OldDailyCopies {
    param(
        [Parameter(Mandatory)][string] $Folder,
        [Parameter(Mandatory)][int] $KeepDays,
        [datetime] $Now = [datetime]::UtcNow,
        [string] $What = 'daily copy',
        [switch] $KeepNewest
    )
    if (-not (Test-Path -LiteralPath $Folder)) { return }
    $folders = @(Get-ChildItem -LiteralPath $Folder -Directory)
    $newest = $folders | Where-Object { ConvertFrom-BackupStamp $_.Name } | Sort-Object Name -Descending | Select-Object -First 1
    $cutoff = $Now.ToUniversalTime().AddDays(-$KeepDays)
    $stale = [datetime]::UtcNow.AddDays(-1)
    foreach ($item in $folders) {
        if ($KeepNewest -and $newest -and $item.FullName -eq $newest.FullName) { continue }
        $taken = ConvertFrom-BackupStamp $item.Name
        $leftover = $item.Name.EndsWith('.partial') -and $item.LastWriteTimeUtc -lt $stale
        if (($taken -and $taken -lt $cutoff) -or $leftover) {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force
            Write-BackupLog "Deleted the $What $($item.Name)."
        }
    }
}

function Format-Bytes([long] $Bytes) {
    if ($Bytes -ge 1GB) { return '{0:N1} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N0} KB' -f ($Bytes / 1KB) }
    "$Bytes bytes"
}

# ---------------------------------------------------------------------------------------------
# The notes vault's Git history (notes plan, "Git on the hub"). Obsidian Sync copies the notes to
# Chris's other devices but skips .git, so without this the vault's commits exist only on this PC.
# ---------------------------------------------------------------------------------------------

# Non-empty lines, trimmed, in ordinal order. The comma keeps a one-line answer an array.
function Get-SortedLines([string] $Text) {
    [string[]] $lines = @($Text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    , $lines
}

<#
Throws unless -VaultPath is the top folder of a Git repository with a commit, so a folder inside
another repository, whose history isn't the vault's, is never taken for the vault.
#>
function Assert-VaultRepository([Parameter(Mandatory)][string] $VaultPath) {
    if (-not (Test-Path -LiteralPath $VaultPath -PathType Container)) { throw "The vault folder $VaultPath doesn't exist." }
    $inside = Invoke-Process git @('-C', $VaultPath, 'rev-parse', '--is-inside-work-tree') -AllowFailure
    if ($inside.ExitCode -ne 0 -or $inside.Out.Trim() -ne 'true') { throw "The vault folder $VaultPath isn't a Git repository." }
    if ((Invoke-Process git @('-C', $VaultPath, 'rev-parse', '--show-prefix') -What 'git rev-parse').Out.Trim()) {
        $top = (Invoke-Process git @('-C', $VaultPath, 'rev-parse', '--show-toplevel') -What 'git rev-parse').Out.Trim()
        throw "The vault folder $VaultPath is inside the Git repository $top, not the top of one."
    }
    if ((Invoke-Process git @('-C', $VaultPath, 'rev-parse', '--verify', '--quiet', 'HEAD') -AllowFailure).ExitCode -ne 0) {
        throw "The vault at $VaultPath has no commits yet."
    }
}

# Every ref in the vault and its HEAD, as sorted "<sha> <ref>" lines: what `git bundle list-heads`
# prints of a bundle made with --all.
function Get-VaultRefs([Parameter(Mandatory)][string] $VaultPath) {
    $refs = (Invoke-Process git @('-C', $VaultPath, 'for-each-ref', '--format=%(objectname) %(refname)') -What 'Reading the vault''s refs').Out
    $head = Invoke-Process git @('-C', $VaultPath, 'rev-parse', '--verify', '--quiet', 'HEAD') -AllowFailure
    if ($head.ExitCode -eq 0) { $refs += "`n$($head.Out.Trim()) HEAD" }
    Get-SortedLines $refs
}

# The newest vault manifest kept, daily or monthly, as its stamp and contents; $null when none.
function Get-NewestVaultManifest([Parameter(Mandatory)][string] $Destination) {
    $found = foreach ($kind in 'daily', 'monthly') {
        $root = Join-Path $Destination $kind
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Directory | Where-Object { -not $_.Name.EndsWith('.partial') } |
            ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -File -Filter 'vault-manifest-*.json' }
    }
    $newest = $found | Sort-Object -Property BaseName -Descending | Select-Object -First 1
    if (-not $newest) { return $null }
    try {
        [pscustomobject]@{
            Stamp    = $newest.BaseName.Substring('vault-manifest-'.Length)
            Manifest = Get-Content -LiteralPath $newest.FullName -Raw | ConvertFrom-Json
        }
    }
    catch {
        Write-BackupLog "Couldn't read $($newest.FullName), so tonight's bundle is taken whatever changed: $($_.Exception.Message)" -Level Warn
        $null
    }
}

<#
Proves a bundle restores, as the database dump is restore-tested before it's kept: `git bundle
verify` in an empty repository; a mirror clone that holds exactly the refs the bundle lists and
passes `git fsck --full`; and a clone of -Branch, line endings as committed, that checks out clean
at the bundle's HEAD. The clones are deleted after. Its errors name no file in the vault.
#>
function Test-VaultBundle {
    param(
        [Parameter(Mandatory)][string] $Bundle,
        [string] $Branch,
        [Parameter(Mandatory)][string] $WorkFolder
    )
    $check = Join-Path $WorkFolder "vault-check-$(New-RandomHex 4)"
    New-Item -ItemType Directory -Path $check | Out-Null
    try {
        $empty = Join-Path $check 'empty.git'
        Invoke-Process git @('init', '--quiet', '--bare', $empty) -What 'git init' | Out-Null
        Invoke-Process git @('-C', $empty, 'bundle', 'verify', '--quiet', $Bundle) -What 'git bundle verify' | Out-Null
        $heads = Get-SortedLines (Invoke-Process git @('-C', $empty, 'bundle', 'list-heads', $Bundle) -What 'git bundle list-heads').Out
        $headLines = @($heads | Where-Object { $_.EndsWith(' HEAD') })
        if ($headLines.Count -ne 1) { throw 'the bundle has no HEAD' }
        $head = $headLines[0].Split(' ')[0]

        $mirror = Join-Path $check 'mirror.git'
        Invoke-Process git @('clone', '--quiet', '--mirror', $Bundle, $mirror) -What 'A mirror clone of the bundle' | Out-Null
        $cloned = Get-SortedLines (Invoke-Process git @('-C', $mirror, 'for-each-ref', '--format=%(objectname) %(refname)') -What 'Reading the clone''s refs').Out
        $listed = @($heads | Where-Object { -not $_.EndsWith(' HEAD') })
        if (($cloned -join "`n") -cne ($listed -join "`n")) {
            throw "a clone of the bundle has $($cloned.Count) refs that don't match the $($listed.Count) it lists"
        }
        Invoke-Process git @('-C', $mirror, 'fsck', '--full', '--no-dangling', '--no-progress') -What 'git fsck of a clone of the bundle' | Out-Null
        $commits = [int] (Invoke-Process git @('-C', $mirror, 'rev-list', '--all', '--count') -What 'Counting commits').Out.Trim()

        $checkout = Join-Path $check 'checkout'
        $clone = @('clone', '--quiet', '--config', 'core.autocrlf=false', '--config', 'core.longpaths=true')
        if ($Branch) { $clone += @('--branch', $Branch) }
        Invoke-Process git ($clone + @($Bundle, $checkout)) -What 'A clone of the bundle' | Out-Null
        $checkedOut = (Invoke-Process git @('-C', $checkout, 'rev-parse', 'HEAD') -What 'git rev-parse').Out.Trim()
        if ($checkedOut -ne $head) { throw "a clone of the bundle checks out $checkedOut, not its HEAD $head" }
        # Get-SortedLines answers an array already: @() around it would nest it.
        $dirty = Get-SortedLines (Invoke-Process git @('-C', $checkout, 'status', '--porcelain', '--untracked-files=all') -What 'git status of a clone of the bundle').Out
        if ($dirty.Count) { throw "a clone of the bundle doesn't check out clean: $($dirty.Count) file(s) differ from its HEAD" }
        $files = (Get-SortedLines (Invoke-Process git @('-C', $checkout, 'ls-files') -What 'git ls-files').Out).Count

        [pscustomobject]@{ Refs = $heads; Head = $head; Commits = $commits; Files = $files }
    }
    finally {
        Remove-Item -LiteralPath $check -Recurse -Force -ErrorAction SilentlyContinue
    }
}

<#
Bundles the vault's whole Git history (`git bundle create --all`, which only reads the
repository), checks the bundle with Test-VaultBundle, then saves it encrypted with a manifest in
<Destination>\daily\<stamp>, and the month's first in monthly\<yyyy-mm>. A night whose HEAD and
refs match the newest bundle kept is skipped, and the log says so.
#>
function Save-VaultBundle {
    param(
        [Parameter(Mandatory)][string] $VaultPath,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][scriptblock] $Encrypt,
        [Parameter(Mandatory)][string] $WorkFolder,
        [datetime] $Now = [datetime]::UtcNow
    )
    Assert-VaultRepository $VaultPath
    $current = Get-VaultRefs $VaultPath
    $last = Get-NewestVaultManifest $Destination
    if ($last -and ((Get-SortedLines (@($last.Manifest.refs) -join "`n")) -join "`n") -ceq ($current -join "`n")) {
        $head = @($current | Where-Object { $_.EndsWith(' HEAD') })[0].Substring(0, 7)
        Write-BackupLog "The vault's Git history hasn't changed since the bundle $($last.Stamp) (HEAD $head), so tonight's is skipped."
        return
    }

    $stamp = ConvertTo-BackupStamp $Now
    $bundle = Join-Path $WorkFolder "vault-$stamp.bundle"
    $branch = (Invoke-Process git @('-C', $VaultPath, 'symbolic-ref', '--quiet', '--short', 'HEAD') -AllowFailure).Out.Trim()
    Invoke-Process git @('-C', $VaultPath, 'bundle', 'create', '--quiet', $bundle, '--all') -What 'git bundle create' | Out-Null
    $check = Test-VaultBundle -Bundle $bundle -Branch $branch -WorkFolder $WorkFolder
    # A bundle holds no config. These say how the vault checks out (the vault sets autocrlf false,
    # where Git for Windows' install sets it true), so a restore can clone it the same way.
    $coreConfig = [ordered]@{}
    $config = Invoke-Process git @('-C', $VaultPath, 'config', '--local', '--get-regexp',
        '^core\.(autocrlf|eol|safecrlf|longpaths|quotepath|ignorecase|symlinks|filemode|precomposeunicode)$') -AllowFailure
    # Assigned first: foreach over Get-SortedLines itself would take its array as one item.
    $lines = Get-SortedLines $config.Out
    foreach ($line in $lines) {
        $key, $value = $line.Split(' ', 2)
        $coreConfig[$key] = $value
    }

    $daily = Join-Path $Destination 'daily' $stamp
    $partial = "$daily.partial"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $partial | Out-Null
    $name = "vault-$stamp.bundle.age"
    $encrypted = Join-Path $partial $name
    & $Encrypt $bundle $encrypted | Out-Null
    if (-not (Test-Path -LiteralPath $encrypted)) { throw 'encrypting the bundle wrote nothing' }

    $manifest = [ordered]@{
        format      = 1
        kind        = 'vault'
        stamp       = $stamp
        takenAt     = $Now.ToUniversalTime().ToString('o')
        vault       = $VaultPath
        gitVersion  = (Invoke-Process git @('--version') -What 'git --version').Out.Trim()
        branch      = if ($branch) { $branch } else { $null }
        coreConfig  = $coreConfig
        head        = $check.Head
        commits     = $check.Commits
        bundleBytes = (Get-Item -LiteralPath $bundle).Length
        files       = @([ordered]@{ name = $name; bytes = (Get-Item -LiteralPath $encrypted).Length; sha256 = Get-FileSha256 $encrypted })
        refs        = $check.Refs
        restoreTest = [ordered]@{ bundleVerified = $true; refsMatch = $true; fsckClean = $true; checkoutClean = $true; trackedFiles = $check.Files }
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $partial "vault-manifest-$stamp.json") -Encoding utf8
    Rename-Item -LiteralPath $partial -NewName $stamp
    Remove-Item -LiteralPath $bundle -Force
    $commits = if ($check.Commits -eq 1) { '1 commit' } else { "$($check.Commits) commits" }
    Write-BackupLog ("Saved the vault's Git history to $daily`: $commits, HEAD $($check.Head.Substring(0, 7))" +
        "$(if ($branch) { " on $branch" }), checked by a clone that checked out its $($check.Files) files clean.")

    $month = $Now.ToUniversalTime().ToString('yyyy-MM', [Globalization.CultureInfo]::InvariantCulture)
    $monthly = Join-Path $Destination 'monthly' $month
    if (-not (Test-Path -LiteralPath $monthly)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $monthly) | Out-Null
        if (Test-Path -LiteralPath "$monthly.partial") { Remove-Item -LiteralPath "$monthly.partial" -Recurse -Force }
        Copy-Item -LiteralPath $daily -Destination "$monthly.partial" -Recurse
        Rename-Item -LiteralPath "$monthly.partial" -NewName $month
        Write-BackupLog "Kept it as $month's monthly vault bundle."
    }
}

function Save-VaultSnapshotIndex([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)] $Index) {
    [ordered]@{ format = 1; files = @($Index.Values) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$Path.new" -Encoding utf8
    Move-Item -LiteralPath "$Path.new" -Destination $Path -Force
}

<#
Copies each of the vault's read-only snapshots (the files in -SnapshotFolder named "<vault name>
…", such as the zip and inventory the 2026-11-12 cutover left), encrypted, to
<Destination>\snapshots: once, and again only if one changes. snapshots.json there records what
was copied (sizes, times and sha256, nothing of what's inside). A snapshot deleted at the source
keeps its copy.
#>
function Save-VaultSnapshots {
    param(
        [Parameter(Mandatory)][string] $SnapshotFolder,
        [Parameter(Mandatory)][string] $VaultName,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][scriptblock] $Encrypt,
        [datetime] $Now = [datetime]::UtcNow
    )
    if (-not (Test-Path -LiteralPath $SnapshotFolder -PathType Container)) {
        Write-BackupLog "The vault's snapshots folder $SnapshotFolder isn't there, so no snapshot was checked." -Level Warn
        return
    }
    $sources = @(Get-ChildItem -LiteralPath $SnapshotFolder -File |
            Where-Object { $_.Name.StartsWith("$VaultName ", [StringComparison]::OrdinalIgnoreCase) } | Sort-Object Name)
    if (-not $sources.Count) {
        Write-BackupLog "There are no snapshots of $VaultName in $SnapshotFolder."
        return
    }

    $folder = Join-Path $Destination 'snapshots'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $indexPath = Join-Path $folder 'snapshots.json'
    $index = [ordered]@{}
    if (Test-Path -LiteralPath $indexPath) {
        foreach ($entry in @((Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json).files)) { $index[$entry.name] = $entry }
    }

    $copied = 0
    foreach ($source in $sources) {
        $target = Join-Path $folder "$($source.Name).age"
        $record = $index[$source.Name]
        $present = $record -and (Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -eq [long] $record.encryptedBytes
        $ticks = $source.LastWriteTimeUtc.Ticks
        if ($present -and [long] $record.bytes -eq $source.Length -and [long] $record.lastWriteTicks -eq $ticks) { continue }

        $sha = Get-FileSha256 $source.FullName
        if ($present -and $record.sha256 -eq $sha) {
            # Only its time changed (copied back from somewhere, say): the copy still matches it.
            $record.lastWriteTicks = $ticks
            Save-VaultSnapshotIndex $indexPath $index
            continue
        }
        if ($present) {
            # ConvertFrom-Json may have read copiedAt as a date already.
            $copiedOn = try { ([datetime] $record.copiedAt).ToUniversalTime().ToString('yyyy-MM-dd') } catch { "$($record.copiedAt)" }
            Write-BackupLog "The vault snapshot $($source.Name) changed since it was copied on $copiedOn; copying it again. A snapshot shouldn't change, so check why." -Level Warn
        }
        elseif ($record) {
            Write-BackupLog "The backup's copy of the vault snapshot $($source.Name) is missing or the wrong size; copying it again." -Level Warn
        }

        $length = $source.Length
        $partial = "$target.partial"
        & $Encrypt $source.FullName $partial | Out-Null
        $after = Get-Item -LiteralPath $source.FullName
        if ($after.Length -ne $length -or $after.LastWriteTimeUtc.Ticks -ne $ticks) {
            Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
            throw "$($source.Name) changed while it was being copied"
        }
        Move-Item -LiteralPath $partial -Destination $target -Force
        $index[$source.Name] = [ordered]@{
            name             = $source.Name
            bytes            = $length
            lastWriteTimeUtc = $source.LastWriteTimeUtc.ToString('o')
            lastWriteTicks   = $ticks
            sha256           = $sha
            copiedAt         = $Now.ToUniversalTime().ToString('o')
            encrypted        = "$($source.Name).age"
            encryptedBytes   = (Get-Item -LiteralPath $target).Length
            encryptedSha256  = Get-FileSha256 $target
        }
        Save-VaultSnapshotIndex $indexPath $index
        $copied++
        Write-BackupLog "Copied the vault snapshot $($source.Name) ($(Format-Bytes $length)), encrypted, to $folder."
    }
    if (-not $copied) { Write-BackupLog "The vault's $($sources.Count) snapshot file(s) were already backed up." }
}

<#
The vault's part of a night (docs/pilot/backup-restore.md): Save-VaultBundle, Save-VaultSnapshots
when there's a -SnapshotFolder, and daily bundles older than -KeepDailyDays deleted, except the
newest. -Encrypt { param($In, $Out) … } writes $Out encrypted from $In; backup.ps1 passes age.
Each part runs whatever the other did, and it throws at the end with what failed.
#>
function Backup-Vault {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $VaultPath,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][scriptblock] $Encrypt,
        [string] $SnapshotFolder,
        [int] $KeepDailyDays = 35,
        [datetime] $Now = [datetime]::UtcNow
    )
    if (-not (Test-Path -LiteralPath $Destination -PathType Container)) {
        throw "The vault's backup folder $Destination doesn't exist. Is OneDrive still set up on this PC? Setup makes it."
    }
    $problems = [Collections.Generic.List[string]]::new()
    $work = New-WorkFolder
    try {
        $bundled = $false
        try {
            Save-VaultBundle -VaultPath $VaultPath -Destination $Destination -Encrypt $Encrypt -WorkFolder $work -Now $Now
            $bundled = $true
        }
        catch {
            $problems.Add("The vault's Git history wasn't backed up: $($_.Exception.Message.TrimEnd('.')).")
        }

        if ($SnapshotFolder) {
            try {
                Save-VaultSnapshots -SnapshotFolder $SnapshotFolder -VaultName (Split-Path -Leaf $VaultPath) -Destination $Destination `
                    -Encrypt $Encrypt -Now $Now
            }
            catch {
                $problems.Add("The vault's snapshots weren't all backed up: $($_.Exception.Message.TrimEnd('.')).")
            }
        }
        else {
            Write-BackupLog 'No snapshots folder is set up for the vault, so no snapshot is copied.'
        }

        # As the database's: only after a night that worked (here, also one skipped as unchanged).
        if ($bundled) {
            Remove-OldDailyCopies -Folder (Join-Path $Destination 'daily') -KeepDays $KeepDailyDays -Now $Now -What 'daily vault bundle' -KeepNewest
        }
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($problems.Count) { throw ($problems -join ' ') }
}

<#
backup.ps1's vault step, from the settings backup-setup.ps1 saved: VaultPath, VaultDestination
(default: a vault folder beside the database's) and VaultSnapshots. With no vault set up, it says
so and returns.
#>
function Invoke-VaultBackup([Parameter(Mandatory)][hashtable] $Settings, [Parameter(Mandatory)][scriptblock] $Encrypt) {
    $vault = $Settings['VaultPath']
    if (-not $vault) {
        Write-BackupLog "No vault is set up, so the notes vault's Git history isn't backed up (backup-setup.ps1 -VaultPath)."
        return
    }
    $destination = $Settings['VaultDestination']
    if (-not $destination) { $destination = Join-Path (Split-Path -Parent $Settings['Destination']) 'vault' }
    Write-BackupLog "Backing up the vault $vault to $destination."
    Backup-Vault -VaultPath $vault -Destination $destination -Encrypt $Encrypt -SnapshotFolder $Settings['VaultSnapshots'] `
        -KeepDailyDays ([int] ($Settings['KeepDailyDays'] ?? 35))
}

# ---------------------------------------------------------------------------------------------
# A login password stored as a SCRAM-SHA-256 verifier, so the plaintext never reaches the
# server or its statement log (what psql's \password does).
# ---------------------------------------------------------------------------------------------

function New-ScramVerifier([Parameter(Mandatory)][string] $Password, [int] $Iterations = 4096) {
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $salted = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
        [Text.Encoding]::UTF8.GetBytes($Password), $salt, $Iterations, [Security.Cryptography.HashAlgorithmName]::SHA256, 32)
    $clientKey = [Security.Cryptography.HMACSHA256]::HashData($salted, [Text.Encoding]::ASCII.GetBytes('Client Key'))
    $storedKey = [Security.Cryptography.SHA256]::HashData($clientKey)
    $serverKey = [Security.Cryptography.HMACSHA256]::HashData($salted, [Text.Encoding]::ASCII.GetBytes('Server Key'))
    'SCRAM-SHA-256$' + $Iterations + ':' + [Convert]::ToBase64String($salt) + '$' +
        [Convert]::ToBase64String($storedKey) + ':' + [Convert]::ToBase64String($serverKey)
}

Export-ModuleMember -Function *
