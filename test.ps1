#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the Life Kernel test suite. Integration tests use Testcontainers, so a
    working Docker daemon must be reachable.

.DESCRIPTION
    The tests are xUnit v3 running on Microsoft.Testing.Platform (MTP). They are
    executed by running the test project directly — xUnit's recommended way to
    run an MTP test application — rather than through `dotnet test`, whose MTP
    orchestrator does not currently discover xUnit v3 tests on this toolchain.

.EXAMPLE
    ./test.ps1
    Runs every test.

.EXAMPLE
    ./test.ps1 --filter-class LifeOs.Tests.SmokeTests
    Passes extra arguments straight through to the test runner. See the runner's
    options with:  ./test.ps1 --help
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $TestArgs
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$testProject = Join-Path $root 'tests' 'LifeOs.Tests' 'LifeOs.Tests.csproj'

# Quieten first-run telemetry banners in the runner output.
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

Write-Host "Testing $testProject ..." -ForegroundColor Cyan
dotnet run --project $testProject -- @TestArgs
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
