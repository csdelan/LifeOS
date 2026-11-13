#Requires -Version 7.0
# Which database a script that writes to a remote one should use, and how to name it without its
# password. scripts/migrate-staging.ps1 (and migrate-production.ps1 through it) uses this;
# tests/LifeOs.Tests/MigrateConnectionTests.cs checks it (code review CR59).

Set-StrictMode -Version Latest

# The value of one KEY=VALUE line in a dotenv file, or $null. '#' lines are comments, the first
# '=' splits the line (connection strings hold more), and surrounding quotes are dropped, as the
# Pilot's DotEnv does.
function Read-DotEnvValue([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Key) {
    if (-not (Test-Path $Path)) { return $null }
    $value = $null
    foreach ($line in Get-Content $Path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        if ($trimmed.Substring(0, $eq).Trim() -eq $Key) {
            $value = $trimmed.Substring($eq + 1).Trim().Trim('"', "'")
        }
    }
    $value
}

# Where a connection string points, as user@host:port/database, without its password. Supabase's
# pooler serves every project from one host, so the user (which carries the project ref) is what
# tells staging from production. Matches KernelConnectionString.Describe in bsk.
function Get-ConnectionTarget([Parameter(Mandatory)][string] $ConnectionString) {
    $parts = @{}
    foreach ($pair in $ConnectionString -split ';') {
        $eq = $pair.IndexOf('=')
        if ($eq -lt 1) { continue }
        $parts[($pair.Substring(0, $eq) -replace '\s', '').ToLowerInvariant()] = $pair.Substring($eq + 1).Trim()
    }
    $user = $parts['username'] ?? $parts['userid'] ?? $parts['user']
    $server = $parts['host'] ?? $parts['server']
    $port = $parts['port'] ?? '5432'
    $database = $parts['database'] ?? $parts['db']
    "$user@${server}:$port/$database"
}

# The connection string to migrate with.
#  - A dotenv file the caller named (-EnvFile, as migrate-production.ps1 names .env.prod) wins over
#    BSK_CONNECTION_STRING in the shell, which is only warned about. Before CR59 the shell's
#    variable won, so a staging string left in a terminal sent "production" migrations to staging.
#  - Otherwise the shell's variable is used, unless the default dotenv file names a different
#    database: then nothing runs until the caller says which one with -EnvFile.
#  - Otherwise the default dotenv file.
function Resolve-MigrationConnection {
    param(
        [Parameter(Mandatory)][string] $EnvPath,
        [Parameter(Mandatory)][string] $EnvFileName,
        [switch] $EnvFileNamed,
        [AllowNull()][AllowEmptyString()][string] $Ambient = $env:BSK_CONNECTION_STRING
    )

    $fromFile = Read-DotEnvValue -Path $EnvPath -Key 'BSK_CONNECTION_STRING'
    $hasAmbient = -not [string]::IsNullOrWhiteSpace($Ambient)
    $hasFile = -not [string]::IsNullOrWhiteSpace($fromFile)

    if ($EnvFileNamed) {
        if (-not $hasFile) { throw "$EnvFileName doesn't exist or has no BSK_CONNECTION_STRING." }
        if ($hasAmbient -and (Get-ConnectionTarget $Ambient) -ne (Get-ConnectionTarget $fromFile)) {
            Write-Warning ("Ignoring BSK_CONNECTION_STRING in this shell ($(Get-ConnectionTarget $Ambient)): " +
                "-EnvFile $EnvFileName names the database.")
        }
        return $fromFile
    }

    if ($hasAmbient) {
        if ($hasFile -and (Get-ConnectionTarget $Ambient) -ne (Get-ConnectionTarget $fromFile)) {
            throw ("BSK_CONNECTION_STRING in this shell points at $(Get-ConnectionTarget $Ambient), but " +
                "$EnvFileName points at $(Get-ConnectionTarget $fromFile). Pass -EnvFile to choose the file, " +
                'or clear the shell''s variable.')
        }
        return $Ambient
    }

    if (-not $hasFile) {
        throw "BSK_CONNECTION_STRING is not set. Put it in $EnvFileName, or pass -EnvFile."
    }
    $fromFile
}

Export-ModuleMember -Function Read-DotEnvValue, Get-ConnectionTarget, Resolve-MigrationConnection
