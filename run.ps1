#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the Life Kernel solution and, when arguments are supplied, runs the
    `bsk` CLI with them.

.EXAMPLE
    ./run.ps1
    Restores and builds the whole solution.

.EXAMPLE
    ./run.ps1 migrate --json
    Builds, then runs `bsk migrate --json`.
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CliArgs
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'LifeOs.slnx'

Write-Host "Building $solution ..." -ForegroundColor Cyan
dotnet build $solution --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

if ($CliArgs -and $CliArgs.Count -gt 0) {
    $cliProject = Join-Path $root 'src' 'LifeOs.Cli' 'LifeOs.Cli.csproj'
    Write-Host "Running: bsk $($CliArgs -join ' ')" -ForegroundColor Cyan
    dotnet run --project $cliProject --no-build -- @CliArgs
    exit $LASTEXITCODE
}
