#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates db/schema/current.sql, the schema every migration adds up to.

.DESCRIPTION
    Runs SchemaSnapshotTests with LIFEOS_WRITE_SCHEMA=1, which migrates a throwaway database
    in the test container and writes each table, index, view and function as it stands. Run it
    after adding a migration and commit the file with it: CI fails while the file is stale.
    Needs Docker, as the tests do.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:LIFEOS_WRITE_SCHEMA = '1'
try {
    & (Join-Path $root 'test.ps1') --filter-class LifeOs.Tests.SchemaSnapshotTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Remove-Item Env:LIFEOS_WRITE_SCHEMA -ErrorAction SilentlyContinue
}
Write-Host "Wrote db/schema/current.sql. Commit it with the migration."
