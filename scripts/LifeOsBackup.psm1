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
function Send-HealthcheckPing([hashtable] $Settings, [string] $Suffix, [string] $Body = '') {
    $url = ConvertFrom-SecureText $Settings['HealthcheckUrl']
    if (-not $url) { return }
    $uri = if ($Suffix) { "$($url.TrimEnd('/'))/$Suffix" } else { $url }
    try {
        Invoke-RestMethod -Method Post -Uri $uri -Body $Body -TimeoutSec 15 -MaximumRetryCount 2 -RetryIntervalSec 5 | Out-Null
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

function New-RandomHex([int] $Bytes = 32) {
    [Convert]::ToHexStringLower([Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes))
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
# The bsk CLI, run from this working copy
# ---------------------------------------------------------------------------------------------

function Get-CliProject { Join-Path (Split-Path -Parent $PSScriptRoot) 'src\LifeOs.Cli\LifeOs.Cli.csproj' }

function Build-Cli {
    Invoke-Process dotnet @('build', (Get-CliProject), '-c', 'Release', '--nologo', '-v', 'quiet') -What 'Building the bsk CLI' | Out-Null
}

# Runs `bsk <arguments> --json` against a connection string; returns exit code, parsed JSON and text.
function Invoke-Bsk([Parameter(Mandatory)][string] $ConnectionString, [Parameter(Mandatory)][string[]] $Arguments) {
    $result = Invoke-Process dotnet (@('run', '--no-build', '--no-launch-profile', '--project', (Get-CliProject),
            '-c', 'Release', '--') + $Arguments + @('--json')) `
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
