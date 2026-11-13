#!/usr/bin/env pwsh
#Requires -Version 7.4
<#
.SYNOPSIS
    Starts, or stops, a live check: the API on a throwaway database with fakes for every outside
    service, and the web against it if asked (code review CR40).

.DESCRIPTION
    Does the recipe in docs/agents/verifying.md ("Live check") in one step:

    1. Makes the database lifeos_check_<Name> in the dev container (lifeos-postgres, started if
       it's stopped) and migrates it. Never Chris's dev database (lifeos), never staging.
    2. Starts scripts/live-check/fakes.mjs: Supabase Auth's JWKS, Oura from the test fixtures,
       Unsplash from the test fixtures, and Supabase Storage in memory.
    3. Builds the API into the check's own folder, so it never locks src/LifeOs.Api/bin and the
       tests still build while it runs, and starts it as staging runs (environment "Check": no
       user secrets), with every outside service pointed at the fakes.
    4. Signs a token as Google sign-in would, for an allowed email, and checks /api/me takes it.
    5. With -Web, starts Vite against the API and prints the line that signs the browser in.

    Everything it starts, and where its logs are, goes in %USERPROFILE%\.lifeos\live-check\<Name>.
    state.json there holds the URLs, the token and the browser session, for a script to read.
    -Stop stops only what it started, drops the database and deletes what it wrote.

.PARAMETER Name
    Names the check, its database (lifeos_check_<Name>) and its folder. Lowercase letters,
    digits and underscores. Two checks with different names and ports can run at once.

.PARAMETER Seed
    Seeds the database with scripts/seed.ps1's example data after migrating it.

.PARAMETER Web
    Also starts Vite on -WebPort, in mode check-<Name> (web/.env.check-<Name>.local).

.PARAMETER Stop
    Stops the check named -Name and cleans up after it.

.EXAMPLE
    ./scripts/live-check.ps1 -Name food -Seed -Web
    ./scripts/live-check.ps1 -Name food -Stop
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9_]{1,40}$')]
    [string] $Name = 'check',
    [int] $ApiPort = 5281,
    [int] $FakePort = 5399,
    [int] $WebPort = 5175,
    [string] $Email = 'live-check@lifeos.test',
    [switch] $Seed,
    [switch] $Web,
    [switch] $Stop
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$container = 'lifeos-postgres'
$database = "lifeos_check_$Name"
$stateDir = Join-Path $HOME '.lifeos' 'live-check' $Name
$statePath = Join-Path $stateDir 'state.json'
$webMode = "check-$Name"
$envFile = Join-Path $root 'web' ".env.$webMode.local"
$envMarker = '# Written by scripts/live-check.ps1'

function Invoke-Psql([string] $sql) {
    $out = docker exec $container psql -U lifeos -d postgres -v ON_ERROR_STOP=1 -Atc $sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
    $out
}

function Test-ContainerRunning {
    $running = docker inspect -f '{{.State.Running}}' $container 2>$null
    if ($LASTEXITCODE -ne 0) { throw "There's no Docker container '$container'. Is Docker running?" }
    $running -eq 'true'
}

function Test-PortFree([int] $port) {
    # Vite on "localhost" may listen on ::1 alone, so try both loopbacks.
    foreach ($address in [System.Net.IPAddress]::Loopback, [System.Net.IPAddress]::IPv6Loopback) {
        $listener = [System.Net.Sockets.TcpListener]::new($address, $port)
        try { $listener.Start() }
        catch [System.Net.Sockets.SocketException] {
            if ($_.Exception.SocketErrorCode -in 'AddressAlreadyInUse', 'AccessDenied') { return $false }
        }
        finally { $listener.Stop() }
    }
    $true
}

# The dev connection strings, read from the code so they can't drift, on the check's database.
function Get-Connection([string] $file) {
    $source = Get-Content -Raw (Join-Path $root 'src' 'LifeOs.Infrastructure' $file)
    if ($source -notmatch 'LocalDevelopmentDefault\s*=\s*"([^"]+)"') { throw "No LocalDevelopmentDefault in $file." }
    $connection = $Matches[1] -replace 'Database=[^;]+', "Database=$database"
    if ($connection -notmatch "Host=localhost;" -or $connection -notmatch "Database=$database(;|$)") {
        throw "The connection string from $file isn't a localhost one: $connection"
    }
    $connection
}

function ConvertTo-Base64Url([byte[]] $bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# An HS256 access token shaped like Supabase's for a Google sign-in (tests/LifeOs.Tests/SupabaseTokens.cs).
function New-Token([string] $secret, [string] $issuer, [long] $now, [long] $expires) {
    $header = [ordered]@{ alg = 'HS256'; typ = 'JWT'; kid = 'supabase-legacy-hs256' }
    $claims = [ordered]@{
        iss = $issuer; aud = 'authenticated'; sub = 'live-check-user'; email = $Email
        role = 'authenticated'; aal = 'aal1'; session_id = 'live-check'; is_anonymous = $false
        iat = $now; exp = $expires
        app_metadata = [ordered]@{ provider = 'google'; providers = @('google') }
        user_metadata = [ordered]@{ email = $Email }
        amr = @([ordered]@{ method = 'oauth'; timestamp = $now })
    }
    $utf8 = [Text.Encoding]::UTF8
    $signed = (ConvertTo-Base64Url $utf8.GetBytes(($header | ConvertTo-Json -Compress))) + '.' +
        (ConvertTo-Base64Url $utf8.GetBytes(($claims | ConvertTo-Json -Compress -Depth 5)))
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($utf8.GetBytes($secret))
    try { "$signed." + (ConvertTo-Base64Url $hmac.ComputeHash($utf8.GetBytes($signed))) } finally { $hmac.Dispose() }
}

function Stop-Started([string] $label, $process) {
    if (-not $process) { return }
    $running = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    # A PID can be reused: stop it only while it's still the program the check started.
    if ($running -and $running.ProcessName -eq $process.Name) {
        Stop-Process -Id $process.Id -Force
        Write-Host "Stopped the $label (PID $($process.Id))."
    }
}

function Stop-Check {
    $state = if (Test-Path $statePath) { Get-Content -Raw $statePath | ConvertFrom-Json } else { $null }
    if ($state) {
        Stop-Started 'web' $state.processes.web
        Stop-Started 'API' $state.processes.api
        Stop-Started 'fakes' $state.processes.fakes
    }

    if (Test-ContainerRunning) {
        Invoke-Psql "DROP DATABASE IF EXISTS $database WITH (FORCE)" | Out-Null
        Write-Host "Dropped $database."
        if ($state -and $state.startedContainer) {
            docker stop $container | Out-Null
            Write-Host "Stopped $container again, as it was before."
        }
    }
    elseif (-not $state) {
        Write-Warning "$container isn't running, so $database (if any) wasn't dropped."
    }

    if ((Test-Path $envFile) -and (Get-Content -TotalCount 1 $envFile) -like "$envMarker*") {
        Remove-Item $envFile
        Write-Host "Deleted $envFile."
    }
    if (Test-Path $stateDir) { Remove-Item -Recurse -Force $stateDir }
}

if ($Stop) {
    Stop-Check
    return
}

if (Test-Path $statePath) {
    throw "A live check named '$Name' is already up. Stop it first: ./scripts/live-check.ps1 -Name $Name -Stop"
}
$ports = @($ApiPort, $FakePort) + @(if ($Web) { $WebPort })
foreach ($port in $ports) {
    # Another session's check may hold it: pick another port rather than stopping its process.
    if (-not (Test-PortFree $port)) { throw "Port $port is taken. Pick another with -ApiPort, -FakePort or -WebPort." }
}

New-Item -ItemType Directory -Force $stateDir | Out-Null
$state = [ordered]@{
    name = $Name; database = $database; startedContainer = $false
    processes = [ordered]@{}
}
function Save-State { $state | ConvertTo-Json -Depth 6 | Set-Content $statePath }
function Watch([string] $label, $process) {
    $state.processes[$label] = [ordered]@{ Id = $process.Id; Name = $process.ProcessName }
    Save-State
}

try {
    Save-State

    # 1. The database
    if (-not (Test-ContainerRunning)) {
        Write-Host "Starting $container ..." -ForegroundColor Cyan
        docker start $container | Out-Null
        $state.startedContainer = $true
        Save-State
        $ready = $false
        for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
            docker exec $container pg_isready -U lifeos -q 2>$null
            $ready = $LASTEXITCODE -eq 0
            if (-not $ready) { Start-Sleep -Milliseconds 500 }
        }
        if (-not $ready) { throw "$container didn't get ready." }
    }
    $owner = Get-Connection 'KernelConnectionString.cs'
    $reader = Get-Connection 'ReaderConnectionString.cs'
    Write-Host "Making $database ..." -ForegroundColor Cyan
    Invoke-Psql "CREATE DATABASE $database" | Out-Null
    dotnet run --project (Join-Path $root 'src' 'LifeOs.Cli') -- migrate --connection $owner
    if ($LASTEXITCODE -ne 0) { throw "bsk migrate failed ($LASTEXITCODE)." }
    if ($Seed) {
        & (Join-Path $PSScriptRoot 'seed.ps1') -Connection $owner -NoBuild
    }

    # 2. The fakes
    $fakesUrl = "http://127.0.0.1:$FakePort"
    $fakes = Start-Process node -PassThru -WindowStyle Hidden `
        -ArgumentList (Join-Path $PSScriptRoot 'live-check' 'fakes.mjs'), $FakePort, (Join-Path $root 'tests' 'LifeOs.Tests') `
        -RedirectStandardOutput (Join-Path $stateDir 'fakes.log') -RedirectStandardError (Join-Path $stateDir 'fakes.err.log')
    Watch 'fakes' $fakes

    # 3. The API, built into the check's folder
    $apiOut = Join-Path $stateDir 'api'
    Write-Host "Building the API ..." -ForegroundColor Cyan
    dotnet build (Join-Path $root 'src' 'LifeOs.Api' 'LifeOs.Api.csproj') -o $apiOut --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "The API didn't build ($LASTEXITCODE)." }

    $apiUrl = "http://127.0.0.1:$ApiPort"
    $issuer = "$fakesUrl/auth/v1"
    $jwtSecret = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $apiEnv = @{
        ASPNETCORE_ENVIRONMENT        = 'Check'
        ASPNETCORE_URLS               = $apiUrl
        ConnectionStrings__Owner      = $owner
        ConnectionStrings__Reader     = $reader
        ALLOWED_EMAILS                = $Email
        Auth__Issuer                  = $issuer
        Auth__Audience                = 'authenticated'
        Auth__JwksUrl                 = "$issuer/.well-known/jwks.json"
        Auth__JwtSecret               = $jwtSecret
        VITE_SUPABASE_URL             = $fakesUrl
        VITE_SUPABASE_ANON_KEY        = 'fake-anon-key'
        Storage__SupabaseUrl          = $fakesUrl
        Storage__SupabaseSecretKey    = 'sb_secret_live_check'
        Cors__AllowedOrigins__0       = "http://127.0.0.1:$WebPort"
        Cors__AllowedOrigins__1       = "http://localhost:$WebPort"
        Oura__ClientId                = 'fake-oura-client'
        Oura__ClientSecret            = 'fake-oura-secret'
        Oura__AuthorizeUrl            = "$fakesUrl/oauth/authorize"
        Oura__TokenUrl                = "$fakesUrl/oauth/token"
        Oura__RevokeUrl               = "$fakesUrl/oauth/revoke"
        Oura__CollectionsUrl          = "$fakesUrl/v2/usercollection/"
        Integrations__TokenKey        = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        Unsplash__AccessKey           = 'fake-unsplash-key'
        Unsplash__ApiUrl              = "$fakesUrl/"
    }
    $api = Start-Process dotnet -PassThru -WindowStyle Hidden -WorkingDirectory $apiOut -Environment $apiEnv `
        -ArgumentList (Join-Path $apiOut 'LifeOs.Api.dll') `
        -RedirectStandardOutput (Join-Path $stateDir 'api.log') -RedirectStandardError (Join-Path $stateDir 'api.err.log')
    Watch 'api' $api

    Write-Host "Waiting for the API ..." -ForegroundColor Cyan
    $up = $false
    for ($i = 0; $i -lt 120 -and -not $up; $i++) {
        if ($api.HasExited) {
            $log = (Get-Content (Join-Path $stateDir 'api.log'), (Join-Path $stateDir 'api.err.log') -Tail 20 -ErrorAction SilentlyContinue) -join "`n"
            throw "The API stopped as it started (exit $($api.ExitCode)):`n$log"
        }
        try { $up = (Invoke-WebRequest "$apiUrl/health" -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $up) { throw "The API didn't answer /health in a minute. Its log is in $stateDir." }

    # 4. Sign in
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $expires = $now + 12 * 3600
    $token = New-Token $jwtSecret $issuer $now $expires
    $me = Invoke-RestMethod "$apiUrl/api/me" -Headers @{ Authorization = "Bearer $token" }
    Write-Host "/api/me took the token: $($me | ConvertTo-Json -Compress)"
    $session = [ordered]@{
        access_token = $token; token_type = 'bearer'; expires_in = $expires - $now; expires_at = $expires
        refresh_token = 'fake-refresh-token'
        user = [ordered]@{
            id = 'live-check-user'; aud = 'authenticated'; role = 'authenticated'; email = $Email
            app_metadata = [ordered]@{ provider = 'google'; providers = @('google') }
            user_metadata = [ordered]@{ email = $Email }
            created_at = [DateTimeOffset]::UtcNow.ToString('o')
        }
    }
    # supabase-js keeps the session under sb-<first label of the Supabase host>-auth-token.
    $storageKey = 'sb-127-auth-token'
    $sessionJson = $session | ConvertTo-Json -Compress -Depth 5
    $state.api = $apiUrl
    $state.fakes = $fakesUrl
    $state.token = $token
    $state.storageKey = $storageKey
    $state.session = $session
    Save-State

    # 5. The web
    if ($Web) {
        @(
            "$envMarker -Name $Name; -Stop deletes it."
            "VITE_API_BASE_URL=$apiUrl"
            "VITE_SUPABASE_URL=$fakesUrl"
            'VITE_SUPABASE_ANON_KEY=fake-anon-key'
        ) | Set-Content $envFile
        $vite = Join-Path $root 'web' 'node_modules' 'vite' 'bin' 'vite.js'
        $webProcess = Start-Process node -PassThru -WindowStyle Hidden -WorkingDirectory (Join-Path $root 'web') `
            -ArgumentList $vite, '--mode', $webMode, '--host', '127.0.0.1', '--port', $WebPort, '--strictPort' `
            -RedirectStandardOutput (Join-Path $stateDir 'web.log') -RedirectStandardError (Join-Path $stateDir 'web.err.log')
        Watch 'web' $webProcess
        $state.web = "http://127.0.0.1:$WebPort"
        Save-State
        $webUp = $false
        for ($i = 0; $i -lt 60 -and -not $webUp; $i++) {
            if ($webProcess.HasExited) { throw "Vite stopped as it started. Its log is in $stateDir." }
            try { $webUp = (Invoke-WebRequest $state.web -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $webUp) { throw "Vite didn't answer in 30 seconds. Its log is in $stateDir." }
    }
}
catch {
    Write-Warning "The live check didn't start, so it's being cleaned up."
    Stop-Check
    throw
}

Write-Host ""
Write-Host "Live check '$Name' is up." -ForegroundColor Green
Write-Host "  API       $apiUrl   (logs: $stateDir)"
Write-Host "  Fakes     $fakesUrl"
Write-Host "  Database  $database"
if ($Web) {
    Write-Host "  Web       $($state.web)"
    Write-Host ""
    Write-Host "Sign the browser in: open $($state.web), run this in its console, and reload."
    Write-Host "  localStorage.setItem('$storageKey', JSON.stringify($sessionJson))"
}
Write-Host ""
Write-Host "The token, URLs and browser session are in $statePath."
Write-Host "Stop it: ./scripts/live-check.ps1 -Name $Name -Stop"
