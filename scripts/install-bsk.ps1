#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Installs the bsk the Pilot writes with, built from origin/main.

.DESCRIPTION
    Fetches origin, checks the commit out into a temporary git worktree, publishes
    src/LifeOs.Cli from there to %USERPROFILE%\.lifeos\bin\bsk, and removes the
    worktree. Building from a clean checkout of main means nothing half-written in
    this working tree (by Chris or by an agent) can become the writer for staging.

    The Pilot writes to STAGING only with this copy, or with BSK_EXE when it is set,
    and runs its launch-time DEV migrate only with one of those. Re-run this after a
    push to main to pick up new verbs. The commit it was built from is written to
    commit.txt beside bsk.exe.

.EXAMPLE
    ./scripts/install-bsk.ps1
    Installs bsk from the newest origin/main.

.EXAMPLE
    ./scripts/install-bsk.ps1 -Ref 6a80209
    Installs bsk from a given commit (one that is already on main).
#>
[CmdletBinding()]
param(
    # The commit to build. Defaults to origin/main after a fetch.
    [string] $Ref = 'origin/main'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lifeOsHome = Join-Path $env:USERPROFILE '.lifeos'
$target = Join-Path $lifeOsHome 'bin' 'bsk'
# Outside AppData on purpose: the Claude desktop app redirects its AppData writes.
$checkout = Join-Path $lifeOsHome 'tmp' ('bsk-' + [guid]::NewGuid().ToString('N').Substring(0, 8))

git -C $root fetch --quiet origin
if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }

$commit = (git -C $root rev-parse --verify "$Ref^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw "Can't resolve $Ref to a commit." }

git -C $root merge-base --is-ancestor $commit origin/main
if ($LASTEXITCODE -ne 0) { throw "$Ref ($commit) isn't on origin/main. The Pilot writes only with bsk built from main." }

Write-Host "Building bsk from $Ref ($($commit.Substring(0, 7)))" -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Split-Path -Parent $checkout) | Out-Null
git -C $root worktree add --quiet --detach $checkout $commit
if ($LASTEXITCODE -ne 0) { throw 'git worktree add failed.' }

try {
    $staged = "$target.new"
    if (Test-Path $staged) { Remove-Item -Recurse -Force $staged }

    dotnet publish (Join-Path $checkout 'src' 'LifeOs.Cli' 'LifeOs.Cli.csproj') `
        --configuration Release --output $staged --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Set-Content -Path (Join-Path $staged 'commit.txt') -Value $commit
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Move-Item $staged $target
}
finally {
    git -C $root worktree remove --force $checkout
}

Write-Host "Installed $(Join-Path $target 'bsk.exe')" -ForegroundColor Green
