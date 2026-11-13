#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Applies the Life Kernel migrations to a remote (e.g. Supabase) database.

.DESCRIPTION
    Reads BSK_CONNECTION_STRING from a dotenv file (default: .env at the repo
    root), then builds and runs `bsk migrate` against it. A file named with
    -EnvFile always wins; without one, a BSK_CONNECTION_STRING already in the
    shell is used only if .env doesn't name a different database. The connection string reaches `bsk migrate` only:
    the caller's shell keeps whatever it had before, so a later `bsk` or
    `run.ps1` in the same terminal can't write to the remote database by
    accident.

    `bsk migrate` is the single source of truth for schema and is safe to
    re-run: already-applied migrations are skipped, and one whose SQL changed
    after it was applied is rejected rather than silently re-run. It builds bsk
    from this working tree, so it refuses unless HEAD is origin/main and nothing
    under db/ or src/ is uncommitted: staging gets a migration only from main,
    and a branch's migration that later changes or is renumbered breaks every
    later deploy. Use this to
    promote schema to staging by hand (e.g. the very first apply, or from a
    machine that isn't CI). GitHub Actions runs the same command on merge to
    main — see .github/workflows/deploy.yml. For production, use
    scripts/migrate-production.ps1 or the gated `production` environment job.

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

git -C $root fetch --quiet origin
if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }
$head = (git -C $root rev-parse HEAD).Trim()
$main = (git -C $root rev-parse origin/main).Trim()
if ($head -ne $main) {
    throw "HEAD ($($head.Substring(0, 7))) isn't origin/main ($($main.Substring(0, 7))). " +
        'Migrate from a checkout of origin/main: a remote database gets only what main has.'
}
$uncommitted = git -C $root status --porcelain -- db src Directory.Build.props Directory.Packages.props global.json
if ($uncommitted) {
    throw "Uncommitted changes would be built into bsk:`n$($uncommitted -join "`n")"
}

# A dotenv file named with -EnvFile wins over BSK_CONNECTION_STRING in the shell, and the shell's
# variable is refused when the default .env names a different database (code review CR59;
# LifeOsConnection.psm1 says why). Only that one key is read, and it is never written to this
# process's environment, which is the caller's shell.
Import-Module (Join-Path $PSScriptRoot 'LifeOsConnection.psm1') -Force
$conn = Resolve-MigrationConnection -EnvPath (Join-Path $root $EnvFile) -EnvFileName $EnvFile `
    -EnvFileNamed:($PSBoundParameters.ContainsKey('EnvFile'))

# Say which database without its password. The pooler host is shared, so the user is shown too.
Write-Host "Migrating $(Get-ConnectionTarget $conn)" -ForegroundColor Yellow

$cliProject = Join-Path $root 'src' 'LifeOs.Cli' 'LifeOs.Cli.csproj'
$argList = @('migrate')
if ($MigrateArgs) { $argList += $MigrateArgs }

Write-Host "Running: bsk $($argList -join ' ')" -ForegroundColor Cyan
# bsk reads the variable, so set it for the run and put the caller's value back after.
$before = $env:BSK_CONNECTION_STRING
$env:BSK_CONNECTION_STRING = $conn
try {
    dotnet run --project $cliProject --configuration Release -- @argList
    $code = $LASTEXITCODE
}
finally {
    if ($null -eq $before) { Remove-Item Env:BSK_CONNECTION_STRING -ErrorAction SilentlyContinue }
    else { $env:BSK_CONNECTION_STRING = $before }
}
exit $code
