#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    One-time Fly.io setup for a LifeOS environment: creates the Fly app, loads
    its runtime secrets, and gives GitHub Actions what it needs to migrate and
    deploy it.

.DESCRIPTION
    Run once per environment, after `fly auth login` and `gh auth login`. Safe
    to re-run: the app is created only if missing, secrets are re-imported, and
    a fresh deploy token replaces the GitHub secret.

    Secret values travel from local files to flyctl / gh on stdin. Nothing is
    echoed or passed on a command line.

    Reads:
      -EnvFile (default .env)   BSK_CONNECTION_STRING, BSK_READER_CONNECTION_STRING,
                                and optionally VITE_SUPABASE_URL,
                                VITE_SUPABASE_ANON_KEY, ALLOWED_EMAILS,
                                Storage__SupabaseSecretKey (or SUPABASE_SECRET_KEY),
                                and the Oura app: Oura__ClientId, Oura__ClientSecret,
                                Integrations__TokenKey
      web/.env.local            VITE_SUPABASE_URL / VITE_SUPABASE_ANON_KEY when
                                the env file does not set them
      LifeOs.Api user-secrets   ALLOWED_EMAILS when the env file does not set it

    Auth__Issuer and Auth__JwksUrl are derived from VITE_SUPABASE_URL. The
    script stops unless every value points at the same Supabase project, so
    staging and production values cannot be mixed.

    Storage__SupabaseSecretKey is the server-only key (sb_secret_...) that keeps
    food photos in Supabase Storage rather than the database. Without it the
    API refuses photos. The script refuses a publishable or anon key there.

    Oura__ClientId and Oura__ClientSecret come from the app registered at
    developer.ouraring.com; Integrations__TokenKey (32 random bytes, base64) seals
    Oura's tokens in the database. All three or none: without them the Activity
    page says Oura isn't set up. Names match without regard to case.

    Sets:
      Fly app secrets           BSK_CONNECTION_STRING, BSK_READER_CONNECTION_STRING,
                                Auth__Issuer, Auth__JwksUrl, ALLOWED_EMAILS,
                                VITE_SUPABASE_URL, VITE_SUPABASE_ANON_KEY,
                                Storage__SupabaseSecretKey (when given),
                                the Oura settings (when given)
      GitHub environment        BSK_CONNECTION_STRING (for bsk migrate) and
      secrets                   FLY_API_TOKEN (a deploy token scoped to this app)

.EXAMPLE
    ./scripts/fly-setup.ps1
    Staging: fly.staging.toml, GitHub environment `staging`, values from .env.

.EXAMPLE
    ./scripts/fly-setup.ps1 -FlyConfig fly.toml -GitHubEnvironment production -EnvFile .env.prod
    Production, once its own Supabase project exists and .env.prod holds all of
    its values (including VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY).
#>
[CmdletBinding()]
param(
    # Fly config whose `app =` names the app to set up.
    [string] $FlyConfig = 'fly.staging.toml',

    # GitHub Environment that the deploy workflow uses for this app.
    [string] $GitHubEnvironment = 'staging',

    # Dotenv file holding this environment's connection strings.
    [string] $EnvFile = '.env',

    # Fly organization to create the app in, if it does not exist yet.
    [string] $Org = 'personal',

    # Skip the confirmation prompt.
    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-DotEnv([string] $path) {
    $values = @{}
    if (-not (Test-Path $path)) { return $values }
    foreach ($line in Get-Content $path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 1) { continue }
        $values[$trimmed.Substring(0, $eq).Trim()] = $trimmed.Substring($eq + 1).Trim().Trim('"')
    }
    return $values
}

# The claims of a Supabase JWT key (the legacy anon / service_role keys), or $null.
function Get-JwtClaims([string] $jwt) {
    $parts = $jwt.Split('.')
    if ($parts.Count -ne 3) { return $null }
    $payload = $parts[1].Replace('-', '+').Replace('_', '/')
    $payload = $payload.PadRight($payload.Length + (4 - $payload.Length % 4) % 4, '=')
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
}

# Runs a native command with exactly $text on stdin. Piping a string from
# PowerShell appends CRLF, which would end up inside the last secret value.
function Invoke-WithStdin([string] $file, [string[]] $arguments, [string] $text) {
    $psi = [Diagnostics.ProcessStartInfo]::new($file)
    foreach ($argument in $arguments) { $psi.ArgumentList.Add($argument) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $process = [Diagnostics.Process]::Start($psi)
    $process.StandardInput.Write($text)
    $process.StandardInput.Close()
    $process.WaitForExit()
    return $process.ExitCode
}

# ---------- tools ----------
$fly = (Get-Command fly, flyctl -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $fly) {
    throw "flyctl is not on PATH. Install it (pwsh -Command `"iwr https://fly.io/install.ps1 -useb | iex`"), open a new terminal, then run 'fly auth login'."
}
$gh = (Get-Command gh -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $gh) { throw "The GitHub CLI (gh) is not on PATH." }

& $fly auth whoami *> $null
if ($LASTEXITCODE -ne 0) { throw "flyctl is not signed in. Run 'fly auth login' first." }
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "gh is not signed in. Run 'gh auth login' first." }

# ---------- target ----------
$configPath = Join-Path $root $FlyConfig
if (-not (Test-Path $configPath)) { throw "Fly config not found: $FlyConfig" }
$appMatch = Select-String -Path $configPath -Pattern '^\s*app\s*=\s*["'']([^"'']+)["'']' | Select-Object -First 1
if (-not $appMatch) { throw "No app = ""..."" line in $FlyConfig." }
$app = $appMatch.Matches[0].Groups[1].Value

# ---------- values ----------
$envPath = if ([IO.Path]::IsPathRooted($EnvFile)) { $EnvFile } else { Join-Path $root $EnvFile }
if (-not (Test-Path $envPath)) { throw "Env file not found: $EnvFile" }
$fromEnvFile = Read-DotEnv $envPath
$fromWeb = Read-DotEnv (Join-Path $root 'web' '.env.local')

$values = @{}
$sourceOf = @{}
foreach ($name in 'BSK_CONNECTION_STRING', 'BSK_READER_CONNECTION_STRING', 'VITE_SUPABASE_URL', 'VITE_SUPABASE_ANON_KEY', 'ALLOWED_EMAILS') {
    if ($fromEnvFile[$name]) { $values[$name] = $fromEnvFile[$name]; $sourceOf[$name] = $EnvFile }
}
foreach ($name in 'VITE_SUPABASE_URL', 'VITE_SUPABASE_ANON_KEY') {
    if (-not $values[$name] -and $fromWeb[$name]) { $values[$name] = $fromWeb[$name]; $sourceOf[$name] = 'web/.env.local' }
}
if (-not $values['ALLOWED_EMAILS']) {
    $line = dotnet user-secrets list --project (Join-Path $root 'src' 'LifeOs.Api') 2>$null |
        Where-Object { $_ -like 'ALLOWED_EMAILS = *' } | Select-Object -First 1
    if ($line) {
        $values['ALLOWED_EMAILS'] = $line.Substring('ALLOWED_EMAILS = '.Length).Trim()
        $sourceOf['ALLOWED_EMAILS'] = 'LifeOs.Api user-secrets'
    }
}

$required = 'BSK_CONNECTION_STRING', 'BSK_READER_CONNECTION_STRING', 'VITE_SUPABASE_URL', 'VITE_SUPABASE_ANON_KEY', 'ALLOWED_EMAILS'
$missing = $required | Where-Object { [string]::IsNullOrWhiteSpace($values[$_]) }
if ($missing) { throw "Missing value(s): $($missing -join ', '). Put them in $EnvFile." }

# Every value must name the same Supabase project.
$supabaseUrl = $values['VITE_SUPABASE_URL'].TrimEnd('/')
if ($supabaseUrl -notmatch '^https://([a-z0-9]+)\.supabase\.co$') {
    throw "VITE_SUPABASE_URL must look like https://<project-ref>.supabase.co"
}
$ref = $Matches[1]
foreach ($name in 'BSK_CONNECTION_STRING', 'BSK_READER_CONNECTION_STRING') {
    if ($values[$name] -notmatch '(Username|User ID)=[^.;]+\.([a-z0-9]+)') {
        throw "$name has no Supabase pooler username (<role>.<project-ref>). Use the session pooler string."
    }
    if ($Matches[2] -ne $ref) {
        throw "$name points at Supabase project '$($Matches[2])' but VITE_SUPABASE_URL is '$ref'. Do not mix environments."
    }
    if ($values[$name] -match 'Host=(localhost|127\.0\.0\.1)') { throw "$name points at a local database." }
}
if ($values['BSK_READER_CONNECTION_STRING'] -match 'Password=bsk_reader(;|$)') {
    throw "BSK_READER_CONNECTION_STRING still uses the public local-dev password. Reset bsk_reader first (docs/pilot/supabase-hosting.md)."
}

# The SPA key is served to every browser, so it must be the public anon key.
$anonKey = $values['VITE_SUPABASE_ANON_KEY']
if ($anonKey.StartsWith('sb_secret_')) { throw "VITE_SUPABASE_ANON_KEY is a secret key. Use the anon (publishable) key." }
$claims = Get-JwtClaims $anonKey
if ($claims -and $claims.role -ne 'anon') {
    throw "VITE_SUPABASE_ANON_KEY is a '$($claims.role)' key. Use the anon key, never service_role."
}

# Optional: the server-only key for Supabase Storage (food photos). It is a Fly
# secret only, never a VITE_ value, so it never reaches a browser.
$storageKey = $null
foreach ($name in 'Storage__SupabaseSecretKey', 'SUPABASE_SECRET_KEY') {
    if (-not $storageKey -and $fromEnvFile[$name]) {
        $storageKey = $fromEnvFile[$name]
        $sourceOf['Storage__SupabaseSecretKey'] = if ($name -eq 'SUPABASE_SECRET_KEY') { "$EnvFile (SUPABASE_SECRET_KEY)" } else { $EnvFile }
    }
}
if ($storageKey) {
    if ($storageKey -eq $anonKey -or $storageKey.StartsWith('sb_publishable_')) {
        throw "Storage__SupabaseSecretKey is the publishable key. Storage needs a secret key (sb_secret_...) from Supabase -> Project Settings -> API Keys."
    }
    $storageClaims = Get-JwtClaims $storageKey
    if ($storageClaims) {
        if ($storageClaims.role -ne 'service_role') {
            throw "Storage__SupabaseSecretKey is a '$($storageClaims.role)' key. Use a secret key (sb_secret_...)."
        }
        if ($storageClaims.ref -and $storageClaims.ref -ne $ref) {
            throw "Storage__SupabaseSecretKey is for Supabase project '$($storageClaims.ref)' but VITE_SUPABASE_URL is '$ref'. Do not mix environments."
        }
    } elseif (-not $storageKey.StartsWith('sb_secret_')) {
        throw "Storage__SupabaseSecretKey doesn't look like a Supabase secret key (sb_secret_...)."
    }
}

# Optional: the Oura app (activity plan §3.1). Server-only, never a VITE_ value.
# The hashtable ignores case, so Oura__ClientID is found too.
$ouraNames = 'Oura__ClientId', 'Oura__ClientSecret', 'Integrations__TokenKey'
$ouraGiven = @($ouraNames | Where-Object { -not [string]::IsNullOrWhiteSpace($fromEnvFile[$_]) })
if ($ouraGiven.Count -gt 0 -and $ouraGiven.Count -lt $ouraNames.Count) {
    $ouraMissing = $ouraNames | Where-Object { $ouraGiven -notcontains $_ }
    throw "$EnvFile sets $($ouraGiven -join ', ') but not $($ouraMissing -join ', '). Set all three to switch Oura on, or none to leave it off."
}
$oura = $ouraGiven.Count -eq $ouraNames.Count
if ($oura) {
    try { $keyBytes = [Convert]::FromBase64String($fromEnvFile['Integrations__TokenKey']) } catch { $keyBytes = @() }
    if ($keyBytes.Length -ne 32) { throw "Integrations__TokenKey must be 32 random bytes in base64." }
    $keyBytes = $null
    if ($fromEnvFile['Oura__ClientId'] -match '\s' -or $fromEnvFile['Oura__ClientSecret'] -match '\s') {
        throw "Oura__ClientId and Oura__ClientSecret can't contain spaces."
    }
}

$flySecrets = [ordered]@{
    BSK_CONNECTION_STRING        = $values['BSK_CONNECTION_STRING']
    BSK_READER_CONNECTION_STRING = $values['BSK_READER_CONNECTION_STRING']
    Auth__Issuer                 = "$supabaseUrl/auth/v1"
    Auth__JwksUrl                = "$supabaseUrl/auth/v1/.well-known/jwks.json"
    ALLOWED_EMAILS               = $values['ALLOWED_EMAILS']
    VITE_SUPABASE_URL            = $supabaseUrl
    VITE_SUPABASE_ANON_KEY       = $anonKey
}
if ($storageKey) { $flySecrets['Storage__SupabaseSecretKey'] = $storageKey }
$storageKey = $null
if ($oura) {
    foreach ($name in $ouraNames) { $flySecrets[$name] = $fromEnvFile[$name] }
}

# ---------- confirm ----------
$repo = gh repo view --json nameWithOwner --jq .nameWithOwner
$poolerHost = if ($values['BSK_CONNECTION_STRING'] -match 'Host=([^;]+)') { $Matches[1] } else { '?' }
$emailCount = @($values['ALLOWED_EMAILS'].Split(',') | Where-Object { $_.Trim() }).Count

Write-Host ''
Write-Host "Fly app            $app ($FlyConfig, org $Org)" -ForegroundColor Cyan
Write-Host "GitHub environment $GitHubEnvironment ($repo)" -ForegroundColor Cyan
Write-Host "Supabase project   $ref via $poolerHost" -ForegroundColor Cyan
Write-Host "Allowlist          $emailCount address(es)" -ForegroundColor Cyan
foreach ($name in $required) { Write-Host ("  {0,-29} from {1}" -f $name, $sourceOf[$name]) }
if ($flySecrets.Contains('Storage__SupabaseSecretKey')) {
    Write-Host ("  {0,-29} from {1}" -f 'Storage__SupabaseSecretKey', $sourceOf['Storage__SupabaseSecretKey'])
    Write-Host "Food photos        Supabase Storage, private bucket lifeos-artifacts (made on first upload)" -ForegroundColor Cyan
} else {
    Write-Host "Food photos        OFF: no Storage__SupabaseSecretKey in $EnvFile, so the API refuses photos" -ForegroundColor Yellow
}
if ($oura) {
    Write-Host "Oura               ON: client id and secret from $EnvFile, tokens sealed with Integrations__TokenKey" -ForegroundColor Cyan
} else {
    Write-Host "Oura               OFF: no Oura__ClientId in $EnvFile, so the Activity page says Oura isn't set up" -ForegroundColor Yellow
}
Write-Host ''
if (-not $Yes) {
    $answer = Read-Host 'Proceed? [y/N]'
    if ($answer -notmatch '^(y|yes)$') { Write-Host 'Stopped; nothing changed.'; exit 1 }
}

# ---------- Fly app ----------
$apps = & $fly apps list --json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "'fly apps list' failed." }
if ($apps | Where-Object { $_.Name -eq $app }) {
    Write-Host "Fly app $app already exists." -ForegroundColor Green
} else {
    Write-Host "Creating Fly app $app ..." -ForegroundColor Cyan
    & $fly apps create $app --org $Org
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create Fly app '$app'. If the name is taken, change app = in $FlyConfig and re-run."
    }
}

# --stage: store without restarting; the next deploy picks them up.
Write-Host "Importing $($flySecrets.Count) runtime secrets into $app ..." -ForegroundColor Cyan
$secretLines = ($flySecrets.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n"
if ((Invoke-WithStdin $fly @('secrets', 'import', '--app', $app, '--stage') "$secretLines`n") -ne 0) {
    throw "'fly secrets import' failed."
}

# ---------- GitHub environment ----------
Write-Host "Setting BSK_CONNECTION_STRING on GitHub environment $GitHubEnvironment ..." -ForegroundColor Cyan
if ((Invoke-WithStdin $gh @('secret', 'set', 'BSK_CONNECTION_STRING', '--env', $GitHubEnvironment, '--repo', $repo) $values['BSK_CONNECTION_STRING']) -ne 0) {
    throw "'gh secret set BSK_CONNECTION_STRING' failed. Does the '$GitHubEnvironment' environment exist?"
}

Write-Host "Minting a deploy token scoped to $app and storing it as FLY_API_TOKEN ..." -ForegroundColor Cyan
$token = (& $fly tokens create deploy --app $app --name "github-actions-$GitHubEnvironment" | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $token -notmatch '^FlyV1 ') { throw "'fly tokens create deploy' did not return a token." }
if ((Invoke-WithStdin $gh @('secret', 'set', 'FLY_API_TOKEN', '--env', $GitHubEnvironment, '--repo', $repo) $token) -ne 0) {
    throw "'gh secret set FLY_API_TOKEN' failed."
}
$token = $null

Write-Host ''
Write-Host 'Done. Remaining by hand:' -ForegroundColor Green
Write-Host "  1. Supabase ($ref) -> Authentication -> URL Configuration: Site URL = https://$app.fly.dev (no trailing slash);"
Write-Host "     keep http://localhost:5173 under Redirect URLs for local Vite."
Write-Host "  2. Deploy: gh workflow run deploy.yml   (or push to main), then open https://$app.fly.dev"
if ($oura) {
    Write-Host "  3. developer.ouraring.com -> your app -> Redirect URIs: add https://$app.fly.dev/activity/connected,"
    Write-Host "     then Connect Oura on https://$app.fly.dev/activity."
}
