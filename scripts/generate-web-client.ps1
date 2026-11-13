#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates the web client's API types: web/src/api/openapi.json, then schema.d.ts from it.

.DESCRIPTION
    Runs OpenApiDocumentTests with LIFEOS_WRITE_OPENAPI=1, which starts the API in the test host
    and writes its OpenAPI document to web/src/api/openapi.json, then `npm run gen:api`, which
    generates web/src/api/schema.d.ts from that file with a pinned openapi-typescript. Run it
    after changing an endpoint or a read model and commit both files: a .NET test fails while
    openapi.json is stale, and CI's web job fails while schema.d.ts is. Needs Docker, as the
    tests do. No API needs to be running.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:LIFEOS_WRITE_OPENAPI = "1"
try {
    & (Join-Path $root "test.ps1") --filter-class LifeOs.Tests.OpenApiDocumentTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Remove-Item Env:LIFEOS_WRITE_OPENAPI -ErrorAction SilentlyContinue
}

Push-Location (Join-Path $root "web")
try {
    npm run gen:api
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally {
    Pop-Location
}
