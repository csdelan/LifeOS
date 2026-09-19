#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Applies the Life Kernel migrations to a remote (e.g. Supabase) database.

.DESCRIPTION
    Loads BSK_CONNECTION_STRING from a dotenv file (default: .env at the repo
    root) unless it is already set in the environment, then builds and runs
    `bsk migrate` against it.

    `bsk migrate` is the single source of truth for schema and is safe to
    re-run: already-applied migrations are skipped, and one whose SQL changed
    after it was applied is rejected rather than silently re-run. Use this to
    promote schema to staging by hand (e.g. the very first apply, or from a
    machine that isn't CI). GitHub Actions runs the same command on merge to
    main — see .github/workflows/migrate-staging.yml.

.EXAMPLE
    ./scripts/migrate-staging.ps1
    Loads .env, then applies pending migrations to the configured database.

.EXAMPLE
    ./scripts/migrate-staging.ps1 -EnvFile .env.prod --json
    Uses a different dotenv file and forwards --json to `bsk migrate`.
#>
[CmdletBinding()]
param(
    # Forwarded verbatim to `bsk migrate` (e.g. --json). Declared positional so
    # bare arguments flow here; because it takes Position 0, $EnvFile below has
    # no position and is therefore name-only — stray args never bind to it.
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $MigrateArgs,

    # The dotenv file to load. Name-only: pass as  -EnvFile .env.prod .
    [string] $EnvFile = '.env'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Load KEY=VALUE lines from the dotenv file into the process environment, but
# never clobber a variable already set in the environment (so CI's secret wins).
$envPath = Join-Path $root $EnvFile
if (Test-Path $envPath) {
    Write-Host "Loading environment from $EnvFile" -ForegroundColor Cyan
    foreach ($line in Get-Content $envPath) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $trimmed.Substring(0, $eq).Trim()
        $value = $trimmed.Substring($eq + 1).Trim().Trim('"')
        if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($key))) {
            [Environment]::SetEnvironmentVariable($key, $value)
        }
    }
}

$conn = [Environment]::GetEnvironmentVariable('BSK_CONNECTION_STRING')
if ([string]::IsNullOrWhiteSpace($conn)) {
    throw "BSK_CONNECTION_STRING is not set. Put it in $EnvFile or export it before running this script."
}

# Show the target host without leaking the password.
if ($conn -match 'Host=([^;]+)') {
    Write-Host "Migrating database at host: $($Matches[1])" -ForegroundColor Yellow
}

$cliProject = Join-Path $root 'src' 'LifeOs.Cli' 'LifeOs.Cli.csproj'
$argList = @('migrate')
if ($MigrateArgs) { $argList += $MigrateArgs }

Write-Host "Running: bsk $($argList -join ' ')" -ForegroundColor Cyan
dotnet run --project $cliProject --configuration Release -- @argList
exit $LASTEXITCODE
