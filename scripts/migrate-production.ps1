#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Applies the Life Kernel migrations to the production database.

.DESCRIPTION
    Same runner as staging (`bsk migrate`). Defaults to `.env.prod` so a real
    production connection string is never confused with `.env` (staging).
    Prefer the gated GitHub Action (environment `production`) once that is set up.

.EXAMPLE
    ./scripts/migrate-production.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $MigrateArgs,

    [string] $EnvFile = '.env.prod'
)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'migrate-staging.ps1'
if ($MigrateArgs) {
    & $script -EnvFile $EnvFile @MigrateArgs
} else {
    & $script -EnvFile $EnvFile
}
exit $LASTEXITCODE
