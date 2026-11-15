# The bsk that scripts writing to a real database, or checking a copy of one, run (code review
# CR118). A working tree may be behind staging, ahead of it, or hold a branch's migrations, so it
# never builds that bsk: it's published from a clean checkout of a commit on origin/main, as
# staging's own deploy is. install-bsk.ps1 (the Pilot's writer), migrate-staging.ps1, backup.ps1
# and restore.ps1 all take it from here; ScriptBskTests holds the other scripts to that.

<#
.SYNOPSIS
    Publishes bsk from a commit on origin/main into -Target, unless it's already there, and
    answers its bsk.dll and commit.
#>
function Get-MainBsk {
    [CmdletBinding()]
    param(
        # A checkout of LifeOS to fetch and build from: any worktree of the repository.
        [Parameter(Mandatory)]
        [string] $Repo,

        # The commit to build. Defaults to origin/main after a fetch; any other must be on it.
        [string] $Ref = 'origin/main',

        # Where to publish it. Outside AppData on purpose: the Claude desktop app redirects its
        # AppData writes, so other programs couldn't see it.
        [string] $Target = (Join-Path $HOME '.lifeos' 'bin' 'bsk-main'),

        # Publish again even when -Target already holds this commit's build.
        [switch] $Force
    )

    git -C $Repo fetch --quiet origin
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed in $Repo." }

    $commit = (git -C $Repo rev-parse --verify "$Ref^{commit}").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Can't resolve $Ref to a commit." }

    git -C $Repo merge-base --is-ancestor $commit origin/main
    if ($LASTEXITCODE -ne 0) { throw "$Ref ($commit) isn't on origin/main. bsk for a real database is built only from main." }

    $dll = Join-Path $Target 'bsk.dll'
    $stamp = Join-Path $Target 'commit.txt'
    if (-not $Force -and (Test-Path $dll) -and (Test-Path $stamp) -and (Get-Content $stamp -Raw).Trim() -eq $commit) {
        Write-Host "bsk from $($commit.Substring(0, 7)) is already in $Target." -ForegroundColor DarkGray
        return [pscustomobject]@{ Path = $dll; Commit = $commit }
    }

    $checkout = Join-Path $HOME '.lifeos' 'tmp' ('bsk-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Write-Host "Building bsk from $Ref ($($commit.Substring(0, 7)))" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force (Split-Path -Parent $checkout) | Out-Null
    git -C $Repo worktree add --quiet --detach $checkout $commit
    if ($LASTEXITCODE -ne 0) { throw 'git worktree add failed.' }

    try {
        $staged = "$Target.new"
        if (Test-Path $staged) { Remove-Item -Recurse -Force $staged }

        dotnet publish (Join-Path $checkout 'src' 'LifeOs.Cli' 'LifeOs.Cli.csproj') `
            --configuration Release --output $staged --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

        Set-Content -Path (Join-Path $staged 'commit.txt') -Value $commit
        if (Test-Path $Target) { Remove-Item -Recurse -Force $Target }
        Move-Item $staged $Target
    }
    finally {
        git -C $Repo worktree remove --force $checkout | Out-Null
    }

    [pscustomobject]@{ Path = $dll; Commit = $commit }
}

<#
.SYNOPSIS
    The main checkout of the repository -Repo is in, which outlives any worktree of it.
#>
function Get-MainCheckout([Parameter(Mandatory)][string] $Repo) {
    $common = (git -C $Repo rev-parse --path-format=absolute --git-common-dir).Trim()
    if ($LASTEXITCODE -ne 0) { throw "$Repo isn't in a git repository." }
    Split-Path -Parent $common
}

Export-ModuleMember -Function Get-MainBsk, Get-MainCheckout
