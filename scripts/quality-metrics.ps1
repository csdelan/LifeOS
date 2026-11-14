<#
.SYNOPSIS
    Counts the measured half of the code quality scorecard (docs/agents/quality-scorecard.md) at
    any commit: lines per area, big files, and how much test code there is per line of app code.

.DESCRIPTION
    Reads files straight from git at -Rev, so it works for old commits without a checkout and
    touches nothing. Generated files (web/src/api/schema.d.ts, db/schema/current.sql) and data
    (JSON, SVG) aren't counted. A "big" file is over 500 lines.

    Areas:
      kernel       src/LifeOs.Domain, Application, Infrastructure (.cs)
      api          src/LifeOs.Api, and src/LifeOs.Contracts, its shapes, which were in
                   src/LifeOs.Api/Read until code review CR89 (.cs)
      cli          src/LifeOs.Cli (.cs)
      pilot        src/LifeOs.Pilot (.cs)
      web-app      web/src, not tests, not the mock (.ts, .tsx, .css)
      web-mock     web/src/lib/mock, not tests
      dotnet-tests tests/ (.cs)
      web-tests    web/src *.test.ts(x), web/src/test, web/e2e
      db           db/migrations (.sql)
      scripts      scripts/ (.ps1, .psm1, .mjs)

    "Prod" is kernel + api + cli + pilot + web-app. The .NET test ratio is dotnet-tests over
    kernel + api + cli (the Pilot has no tests by design); the web ratio is web-tests over web-app
    plus web-mock.

.EXAMPLE
    ./scripts/quality-metrics.ps1
.EXAMPLE
    ./scripts/quality-metrics.ps1 -Rev 2eac0a3~1 -Areas
.EXAMPLE
    ./scripts/quality-metrics.ps1 -Rev HEAD -Json
#>
[CmdletBinding()]
param(
    [string]$Rev = 'HEAD',
    [int]$BigFile = 500,
    [switch]$Areas,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
# Every git call runs at the repo root, without moving the caller's own folder.
$root = Split-Path -Parent $PSScriptRoot

$sha = (git -C $root rev-parse --short $Rev).Trim()
$date = (git -C $root show -s --format=%cs $Rev).Trim()

$generated = @('web/src/api/schema.d.ts', 'db/schema/current.sql')

function Get-Area([string]$path) {
    if ($generated -contains $path) { return $null }
    switch -Regex ($path) {
        '^src/LifeOs\.(Domain|Application|Infrastructure)/.*\.cs$' { return 'kernel' }
        '^src/LifeOs\.(Api|Contracts)/.*\.cs$' { return 'api' }
        '^src/LifeOs\.Cli/.*\.cs$' { return 'cli' }
        '^src/LifeOs\.Pilot/.*\.cs$' { return 'pilot' }
        '^tests/.*\.cs$' { return 'dotnet-tests' }
        '^web/e2e/.*\.ts$' { return 'web-tests' }
        '^web/src/.*\.test\.tsx?$' { return 'web-tests' }
        '^web/src/test/.*\.tsx?$' { return 'web-tests' }
        '^web/src/lib/mock/.*\.(ts|tsx)$' { return 'web-mock' }
        '^web/src/.*\.(ts|tsx|css)$' { return 'web-app' }
        '^db/migrations/.*\.sql$' { return 'db' }
        '^scripts/.*\.(ps1|psm1|mjs)$' { return 'scripts' }
        default { return $null }
    }
}

# git grep -c with an empty pattern counts every line of every text file at the revision.
$files = foreach ($line in git -C $root grep -c -I -e '' $Rev -- src tests web/src web/e2e db/migrations scripts) {
    $rest = $line.Substring($line.IndexOf(':') + 1)
    $cut = $rest.LastIndexOf(':')
    $path = $rest.Substring(0, $cut)
    $area = Get-Area $path
    if ($area) {
        [pscustomobject]@{ Path = $path; Area = $area; Lines = [int]$rest.Substring($cut + 1) }
    }
}

$order = 'kernel', 'api', 'cli', 'pilot', 'web-app', 'web-mock', 'dotnet-tests', 'web-tests', 'db', 'scripts'
$byArea = foreach ($name in $order) {
    $in = @($files | Where-Object Area -eq $name)
    $lines = [int](($in | Measure-Object Lines -Sum).Sum ?? 0)
    $big = @($in | Where-Object Lines -gt $BigFile)
    $bigLines = [int](($big | Measure-Object Lines -Sum).Sum ?? 0)
    $largest = $in | Sort-Object Lines -Descending | Select-Object -First 1
    [pscustomobject]@{
        Area = $name
        Files = $in.Count
        Lines = $lines
        BigFiles = $big.Count
        BigShare = if ($lines) { [math]::Round(100 * $bigLines / $lines, 1) } else { 0 }
        Largest = if ($largest) { "$(Split-Path -Leaf $largest.Path) ($($largest.Lines))" } else { '' }
    }
}

function Sum-Lines([string[]]$names) { [int]((($byArea | Where-Object { $names -contains $_.Area }) | Measure-Object Lines -Sum).Sum ?? 0) }

$prodNames = 'kernel', 'api', 'cli', 'pilot', 'web-app'
$prod = @($files | Where-Object { $prodNames -contains $_.Area })
$prodLines = Sum-Lines $prodNames
$prodBig = @($prod | Where-Object Lines -gt $BigFile)
$prodBigLines = [int](($prodBig | Measure-Object Lines -Sum).Sum ?? 0)
$top = $prod | Sort-Object Lines -Descending | Select-Object -First 1

$dotnetCode = Sum-Lines 'kernel', 'api', 'cli'
$webCode = Sum-Lines 'web-app', 'web-mock'
$todo = @(git -C $root grep -I -E '\b(TODO|FIXME|HACK)\b' $Rev -- src web/src | Where-Object {
        $path = ($_ -split ':', 3)[1]
        $prodNames -contains (Get-Area $path)
    }).Count

$summary = [pscustomobject]@{
    Commit = $sha
    Date = $date
    ProdLines = $prodLines
    DotnetTestRatio = if ($dotnetCode) { [math]::Round((Sum-Lines 'dotnet-tests') / $dotnetCode, 2) } else { 0 }
    WebTestRatio = if ($webCode) { [math]::Round((Sum-Lines 'web-tests') / $webCode, 2) } else { 0 }
    BigFiles = $prodBig.Count
    BigShare = if ($prodLines) { [math]::Round(100 * $prodBigLines / $prodLines, 1) } else { 0 }
    Largest = if ($top) { "$(Split-Path -Leaf $top.Path) ($($top.Lines))" } else { '' }
    MockLines = Sum-Lines 'web-mock'
    Todos = $todo
}

if ($Json) {
    [pscustomobject]@{ Summary = $summary; Areas = $byArea } | ConvertTo-Json -Depth 4
    return
}

$summary | Format-List
if ($Areas) {
    $byArea | Format-Table -AutoSize
}
