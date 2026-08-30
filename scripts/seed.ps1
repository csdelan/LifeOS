#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    M5.1 (issue #22) — the reproducible seed for a fresh Life Kernel.

    Populates an empty kernel with a starting state: Values, Goals linked to
    those Values, the initial Constraint set (the capacity/interaction `scope`
    split), and the current Season. Every write goes through `bsk` — the sole
    write path (ontology invariant 9) — so the seed cannot violate an invariant
    that a raw INSERT could.

.DESCRIPTION
    THE CONTENT BELOW IS EXAMPLE PLACEHOLDER DATA, not anyone's real life.
    It exists to (a) show the shape of a real seed and (b) give `bsk check`
    something sensible to find. Replace every title, limit, focus, and date in
    the "SEED CONTENT" region with your own before treating this as your kernel.

    The example is arranged so that a `bsk check` immediately after seeding is
    "not empty, not all-noise" (issue #22 acceptance): four diagnostics fire and
    three correctly stay silent. Expected findings on the example content:

        neglect              (none)  — every subject is fresh; the clock starts at
                                       creation, so nothing is stale on day one.
        breach               (none)  — no violating events have been logged yet.
        wishes                  2    — "Protect two family evenings each week" and
                                       "Reach working competence in Rust" have no
                                       active Project serving them.
        drift                   1    — "Kitchen remodel" serves no Goal.
        unclosed_loops       (none)  — no Decisions with a past review date.
        decorative_identity     2    — "Integrity" and "Focused autonomy" have no
                                       Goal beneath them.
        constraint              1    — "At most three active projects": 4 active
                                       Projects against a stated limit of 3.
        -----------------------------------------------------------------
        total                   6 findings across 4 of 7 diagnostics.

    The Constraint set is 8 rows on the single-table `scope` split (issue #22
    acceptance: ~5-10 rows). Only scope=capacity limits whose units the constraint
    diagnostic understands (projects, hours) can fire; the "open commitments" and
    "spending" capacity limits are deliberately included to show that an
    uninterpretable limit is skipped rather than guessed at, and the four
    scope=interaction rows are policy the diagnostic ignores by design.

.PARAMETER Connection
    PostgreSQL connection string. Omitted -> bsk's own resolution (the
    BSK_CONNECTION_STRING env var, then the local-development default).

.PARAMETER Migrate
    Run `bsk migrate` before seeding. Use this to seed a brand-new database in
    one step (create the database, then `seed.ps1 -Migrate`).

.PARAMETER NoBuild
    Skip building the CLI; assume src/LifeOs.Cli is already built.

.EXAMPLE
    # Seed the local development database (docker compose up -d; already migrated)
    ./scripts/seed.ps1

.EXAMPLE
    # Replay against a fresh throwaway database in one step
    ./scripts/seed.ps1 -Connection 'Host=localhost;Port=5432;Database=lifeos_fresh;Username=lifeos;Password=lifeos' -Migrate

.NOTES
    Replay model: this seed is written to run against a freshly-migrated (empty)
    kernel. Re-running it against a database that already holds a seed appends a
    second, independently-identified set (URNs carry a unique short id), so start
    from an empty database when you want to reproduce the exact starting state.
#>
[CmdletBinding()]
param(
    [string] $Connection,
    [switch] $Migrate,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $root 'src' 'LifeOs.Cli' 'LifeOs.Cli.csproj'
$dll = Join-Path $root 'src' 'LifeOs.Cli' 'bin' 'Debug' 'net10.0' 'bsk.dll'

if (-not $NoBuild) {
    Write-Host "Building the bsk CLI ..." -ForegroundColor Cyan
    dotnet build $cliProject --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
}
if (-not (Test-Path $dll)) {
    throw "bsk.dll not found at $dll. Build the CLI (drop -NoBuild) or check the output path."
}

# --- bsk plumbing ------------------------------------------------------------
# Every call threads --connection through when one was supplied, and stops the
# whole seed on the first failure rather than leaving a half-built graph.
$connArgs = if ($Connection) { @('--connection', $Connection) } else { @() }

function Invoke-Bsk {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $BskArgs)
    & dotnet $dll @BskArgs @connArgs
    if ($LASTEXITCODE -ne 0) { throw "bsk $($BskArgs -join ' ') failed ($LASTEXITCODE)." }
}

# Create a subject and return its URN, captured from --json so later links point
# at an unambiguous identity rather than a title (titles can collide).
function New-Subject {
    param(
        [Parameter(Mandatory)] [string] $Type,
        [Parameter(Mandatory)] [string] $Title,
        [string[]] $Extra = @()
    )
    $out = & dotnet $dll new $Type $Title '--json' @Extra @connArgs
    if ($LASTEXITCODE -ne 0) { throw "bsk new $Type '$Title' failed ($LASTEXITCODE)." }
    $urn = ($out | ConvertFrom-Json).urn
    Write-Host ("  + {0,-11} {1}" -f $Type, $Title) -ForegroundColor DarkGray
    return $urn
}

function Add-Link {
    param([Parameter(Mandatory)] [string] $From, [Parameter(Mandatory)] [string] $Relation, [Parameter(Mandatory)] [string] $To)
    Invoke-Bsk link $From $Relation $To | Out-Null
}

if ($Migrate) {
    Write-Host "Migrating ..." -ForegroundColor Cyan
    Invoke-Bsk migrate
}

Write-Host "Seeding the kernel ..." -ForegroundColor Cyan

# ============================================================================
#  SEED CONTENT — EXAMPLE PLACEHOLDER DATA. Replace with your own.
# ============================================================================

# --- Values: enduring principles, the top of the alignment graph -------------
# A Value is an identity statement (migration 0010): the title is a short handle
# (it drives the URN slug and is what edges reference) and --statement carries the
# full first-person "who I've chosen to be". The statement is REQUIRED — the kernel
# rejects a Value without one. decorative_identity quotes the statement, so write
# each one as a real sentence, not a restated handle.
$vCraft     = New-Subject Value 'Craftsmanship'          -Extra @('--slot', 'work', '--statement', "I am a person who does work I can be proud of and finishes it to a standard I would sign my name to.")
$vHealth    = New-Subject Value 'Health and vitality'    -Extra @('--slot', 'body', '--statement', "I am a person who protects my health and energy so I can show up fully for what matters.")
$vRelate    = New-Subject Value 'Deep relationships'     -Extra @('--statement', "I am a person who invests in a few deep relationships rather than many shallow ones.")
$vMoney     = New-Subject Value 'Financial independence' -Extra @('--statement', "I am a person who lives below my means and builds the freedom to choose my own work.")
$vLearn     = New-Subject Value 'Lifelong learning'      -Extra @('--statement', "I am a person who keeps learning deliberately and stays a beginner often enough to grow.")
# left with no Goal beneath them -> decorative_identity fires, quoting the statement.
$vIntegrity = New-Subject Value 'Integrity'              -Extra @('--statement', "I am a person who does what I said I would, especially when it is inconvenient.")
$vAutonomy  = New-Subject Value 'Focused autonomy'       -Extra @('--statement', "I am a person who guards my attention and decides for myself where it goes.")

# --- Goals: desired outcomes; each serves a Value ----------------------------
$gVerdict = New-Subject Goal 'Deliver a verdict on the LifeOS ontology' -Extra @('--cadence', 'weekly', '--end-state', 'a written keep-or-discard verdict after four weeks of use')
$gWeight  = New-Subject Goal 'Reach and hold a healthy weight'          -Extra @('--cadence', 'weekly')
$gFamily  = New-Subject Goal 'Protect two family evenings each week'
$gRunway  = New-Subject Goal 'Build six months of runway'
$gRust    = New-Subject Goal 'Reach working competence in Rust'

Add-Link $gVerdict serves $vCraft
Add-Link $gWeight  serves $vHealth
Add-Link $gFamily  serves $vRelate
Add-Link $gRunway  serves $vMoney
Add-Link $gRust    serves $vLearn

# --- Projects: active work; each should serve a Goal -------------------------
# committed_hours feeds the capacity/hours dimension of the constraint check.
$pBuild   = New-Subject Project 'LifeOS Stage 1 build'  -Extra @('--attr', 'committed_hours=8')
$pC25k    = New-Subject Project 'Couch to 5K'           -Extra @('--attr', 'committed_hours=3')
$pRunway  = New-Subject Project 'Runway and budget plan' -Extra @('--attr', 'committed_hours=4')
$pKitchen = New-Subject Project 'Kitchen remodel'        # serves no Goal -> drift fires

Add-Link $pBuild  serves $gVerdict
Add-Link $pC25k   serves $gWeight
Add-Link $pRunway serves $gRunway
# $pKitchen is intentionally left unlinked.
# $gFamily and $gRust are intentionally left with no serving Project -> wishes fires.

# --- Constraints: the single-table scope split (capacity | interaction) ------
# capacity — a ceiling checked against reality by the constraint diagnostic.
New-Subject Constraint 'At most three active projects'   -Extra @('--scope', 'capacity', '--limit', '3 active projects')      | Out-Null
New-Subject Constraint 'Twenty focused hours a week'     -Extra @('--scope', 'capacity', '--limit', '20 focused hours per week') | Out-Null
New-Subject Constraint 'At most four open commitments'   -Extra @('--scope', 'capacity', '--limit', '4 open commitments')     | Out-Null
New-Subject Constraint 'Monthly discretionary spend cap' -Extra @('--scope', 'capacity', '--limit', '1200 per month')        | Out-Null
# interaction — policy on how the system may reach me; the diagnostic ignores these.
New-Subject Constraint 'Quiet hours overnight'           -Extra @('--scope', 'interaction', '--limit', 'no notifications 21:00-07:00') | Out-Null
New-Subject Constraint 'No work topics at weekends'      -Extra @('--scope', 'interaction', '--limit', 'no work topics Sat-Sun')       | Out-Null
New-Subject Constraint 'At most one nudge per day'       -Extra @('--scope', 'interaction', '--limit', '1 intervention per day')        | Out-Null
New-Subject Constraint 'Propose, do not act, above a threshold' -Extra @('--scope', 'interaction', '--limit', 'propose only above 250') | Out-Null

# --- Season: the current bounded period that contextualizes everything -------
New-Subject Season 'H2 2026: ship the verdict' -Extra @('--focus', 'prove or disprove the LifeOS ontology', '--ends', '2026-12-31') | Out-Null

# ============================================================================
#  END SEED CONTENT
# ============================================================================

# Materialize the derived layer so subject_current reflects the seed and a
# `bsk rebuild --verify` immediately reports byte-identical (source == derived).
Write-Host "Rebuilding the derived layer ..." -ForegroundColor Cyan
Invoke-Bsk rebuild

Write-Host ""
Write-Host "Seed complete. Run 'bsk check' to see the starting diagnostics." -ForegroundColor Green
