#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Installs the bsk the Pilot writes with, built from origin/main.

.DESCRIPTION
    Fetches origin, checks the commit out into a temporary git worktree, publishes
    src/LifeOs.Cli from there to %USERPROFILE%\.lifeos\bin\bsk, and removes the
    worktree (Get-MainBsk, in LifeOsBsk.psm1). Building from a clean checkout of main
    means nothing half-written in this working tree (by Chris or by an agent) can become
    the writer for staging. A build of that commit already there is kept.

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
Import-Module (Join-Path $PSScriptRoot 'LifeOsBsk.psm1') -Force

# The same build migrate-staging, backup and restore run (LifeOsBsk.psm1), in the Pilot's own folder.
$target = Join-Path $HOME '.lifeos' 'bin' 'bsk'
$bsk = Get-MainBsk -Repo (Split-Path -Parent $PSScriptRoot) -Ref $Ref -Target $target

Write-Host "Installed $(Join-Path $target 'bsk.exe') from $($bsk.Commit.Substring(0, 7))" -ForegroundColor Green
