#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates web/src/api/schema.d.ts from a running LifeOs.Api OpenAPI document.
#>
[CmdletBinding()]
param(
    [string] $OpenApiUrl = "http://localhost:5280/openapi/v1.json"
)

$ErrorActionPreference = "Stop"
$web = Join-Path $PSScriptRoot ".." "web"
Push-Location $web
try {
    npx --yes openapi-typescript@7 $OpenApiUrl -o src/api/schema.d.ts
} finally {
    Pop-Location
}
