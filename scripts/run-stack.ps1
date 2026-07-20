<#
.SYNOPSIS
  Start the full AeroBus dev stack from source, in the background:
  DocumentForge (dfdb) + RuleForge + AeroBus.

.DESCRIPTION
  Brings the three services up locally without Docker so a tester can then run
  scripts/smoke.ps1 against them. Ordering matters and encodes two gotchas from
  earlier phases:

    1. RuleForge must be launched with --no-launch-profile, otherwise its
       launchSettings profiles force RULE_SOURCE=sqlite/local and it never reads
       rules from DocumentForge.

    2. RuleForge enumerates `environments[*].ruleBindings` for RULEFORGE_ENV
       ONCE at boot. So the shop-bundles rule must be published to the `dev`
       environment (which writes the binding into dfdb) BEFORE RuleForge starts.
       This script therefore: starts dfdb -> starts AeroBus -> mints a token +
       seeds & publishes the rule (via scripts/seed-shop-rule.ps1, which writes
       the dfdb binding; the RuleForge refresh call fails harmlessly since
       RuleForge isn't up yet) -> THEN starts RuleForge so it binds the rule at
       boot.

  PowerShell 5.1 compatible: no && chains, no ternary. Each service runs as a
  background Job; a stack manifest is written to the data dir so stop-stack (or
  the smoke script) can find/kill them. Re-run with -Stop to tear everything down.

.PARAMETER Stop
  Stop a previously started stack (kills the jobs + frees the ports) and exit.

.EXAMPLE
  ./scripts/run-stack.ps1
  ./scripts/smoke.ps1
  ./scripts/run-stack.ps1 -Stop
#>
[CmdletBinding()]
param(
    [string] $DfdbUrl        = "http://localhost:4300",
    [int]    $DfdbPort       = 4300,
    [string] $RuleForgeUrl   = "http://localhost:5050",
    [int]    $RuleForgePort  = 5050,
    [string] $AeroBusUrl     = "http://localhost:5080",
    [int]    $AeroBusPort    = 5080,

    [string] $DocumentForgeCli = "C:\DATA\01. documentforge\src\DocumentForge.Cli",
    [string] $RuleForgeApi     = "C:\DATA\14. Aerotoys RuleForge\ruleforge\src\RuleForge.Api",

    [string] $RuleForgeApiKey  = "aerobus-dev-ruleforge-key",
    [string] $DataDir,
    [switch] $Stop
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$AeroBusApi = Join-Path $RepoRoot "src\AeroBus.Api"

if (-not $DataDir) { $DataDir = Join-Path $env:TEMP "aerobus-stack" }
$Manifest = Join-Path $DataDir "stack.json"

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    $msg" -ForegroundColor Yellow }

# -- Free a TCP port by killing whatever owns it (stale listeners from a prior run) --
function Stop-Port([int]$port) {
    $lines = (netstat -ano | Select-String -Pattern "LISTENING" | Select-String -Pattern (":{0}\s" -f $port))
    foreach ($l in $lines) {
        $parts = ($l.ToString() -split "\s+") | Where-Object { $_ -ne "" }
        $procId = $parts[-1]
        if ($procId -match '^\d+$') {
            try { Stop-Process -Id ([int]$procId) -Force -ErrorAction Stop; Write-Warn2 "killed stale PID $procId on port $port" } catch {}
        }
    }
}

function Wait-Http([string]$url, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { return $true }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    return $false
}

# -- Teardown ---------------------------------------------------------------
function Stop-Stack {
    Write-Step "Stopping AeroBus stack"
    if (Test-Path $Manifest) {
        try {
            $m = Get-Content -Raw $Manifest | ConvertFrom-Json
            foreach ($jobId in @($m.jobIds)) {
                $j = Get-Job -Id $jobId -ErrorAction SilentlyContinue
                if ($j) { Stop-Job $j -ErrorAction SilentlyContinue; Remove-Job $j -Force -ErrorAction SilentlyContinue; Write-Ok "removed job $jobId" }
            }
        } catch { Write-Warn2 "manifest unreadable: $($_.Exception.Message)" }
        Remove-Item $Manifest -Force -ErrorAction SilentlyContinue
    }
    # Belt and braces: free the ports regardless of job bookkeeping.
    Stop-Port $DfdbPort
    Stop-Port $RuleForgePort
    Stop-Port $AeroBusPort
    Write-Ok "stack stopped"
}

if ($Stop) { Stop-Stack; return }

# A fresh start always tears down any previous stack first.
Stop-Stack
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

$jobIds = @()

# -- 1. DocumentForge ---------------------------------------------------------
Write-Step "Starting DocumentForge (dfdb) on port $DfdbPort"
$dfData = Join-Path $DataDir "dfdb-data"
New-Item -ItemType Directory -Force -Path $dfData | Out-Null
$dfJob = Start-Job -Name "aerobus-dfdb" -ScriptBlock {
    param($cli, $port, $data)
    dotnet run --project $cli -- serve --port $port --insecure-dev-mode --data-dir $data
} -ArgumentList $DocumentForgeCli, $DfdbPort, $dfData
$jobIds += $dfJob.Id

if (-not (Wait-Http "$DfdbUrl/health" 90)) {
    Write-Host "DocumentForge failed to come up on $DfdbUrl" -ForegroundColor Red
    Receive-Job $dfJob | Select-Object -Last 20
    Stop-Stack; exit 1
}
Write-Ok "dfdb healthy at $DfdbUrl"

# -- 2. AeroBus (needs dfdb; RuleForge optional at boot) ----------------------
Write-Step "Starting AeroBus on port $AeroBusPort"
$abJob = Start-Job -Name "aerobus-api" -ScriptBlock {
    param($proj, $url, $dfUrl, $rfUrl, $rfKey)
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $url
    $env:DocumentForge__BaseUrl = $dfUrl
    $env:RuleForge__BaseUrl = $rfUrl
    $env:RuleForge__ApiKey = $rfKey
    dotnet run --project $proj --no-launch-profile
} -ArgumentList $AeroBusApi, $AeroBusUrl, $DfdbUrl, $RuleForgeUrl, $RuleForgeApiKey
$jobIds += $abJob.Id

if (-not (Wait-Http "$AeroBusUrl/health" 90)) {
    Write-Host "AeroBus failed to come up on $AeroBusUrl" -ForegroundColor Red
    Receive-Job $abJob | Select-Object -Last 30
    Stop-Stack; exit 1
}
Write-Ok "AeroBus healthy at $AeroBusUrl"

# -- 3. Publish the shop rule BEFORE RuleForge boots --------------------------
# We need a bearer token to call /rules. User auth lives in Keycloak behind
# /identity now, so seed a throwaway admin.all ab_ API key directly in dfdb and
# use it for seed-shop-rule.ps1. The RuleForge refresh it attempts will fail
# harmlessly (RuleForge isn't up) but the dfdb env binding is written, which is
# what RuleForge reads at boot.
Write-Step "Publishing shop-bundles rule to env 'dev' (before RuleForge boot)"
try {
    $co   = [guid]::NewGuid().ToString()
    $slug = "stackboot-" + $co.Substring(0, 8)

    $dfHeaders = @{ "Content-Type" = "application/json" }
    Invoke-RestMethod -Method Post -Uri "$DfdbUrl/collections/admin.companies" -Headers $dfHeaders -Body (@{ id=$co; name="Stack Boot"; slug=$slug; status="Active" } | ConvertTo-Json) | Out-Null

    $prefix = ($co -replace "[^a-z0-9]", "").Substring(0, 8)
    $secret = New-Object byte[] 32
    (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($secret)
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($secret)
    $token = "ab_" + $prefix + "_" + [Convert]::ToBase64String($secret).TrimEnd("=").Replace("+", "-").Replace("/", "_")
    Invoke-RestMethod -Method Post -Uri "$DfdbUrl/collections/admin.apitokens" -Headers $dfHeaders -Body (@{ id=[guid]::NewGuid().ToString(); companyId=$co; name="stackboot-key"; prefix=$prefix; hash=[Convert]::ToBase64String($hash); scopes="admin.all"; created=(Get-Date).ToUniversalTime().ToString("o") } | ConvertTo-Json) | Out-Null

    & (Join-Path $ScriptDir "seed-shop-rule.ps1") -Token $token -AeroBusUrl $AeroBusUrl -Env "dev"
    Write-Ok "rule published to dev (dfdb binding written)"
} catch {
    Write-Warn2 "rule publish step failed (stack still usable, offer will degrade): $($_.Exception.Message)"
}

# -- 4. RuleForge (df source, dev env) - reads the dev bindings at boot --------
Write-Step "Starting RuleForge on port $RuleForgePort (--no-launch-profile, df source)"
$rfJob = Start-Job -Name "aerobus-ruleforge" -ScriptBlock {
    param($proj, $url, $dfUrl, $rfKey)
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $url
    $env:RULEFORGE_RULE_SOURCE = "df"
    $env:RULEFORGE_DF_BASE_URL = $dfUrl
    $env:RULEFORGE_DF_API_KEY = ""
    $env:RULEFORGE_ENV = "dev"
    $env:RULEFORGE_API_KEY = $rfKey
    dotnet run --project $proj --no-launch-profile
} -ArgumentList $RuleForgeApi, $RuleForgeUrl, $DfdbUrl, $RuleForgeApiKey
$jobIds += $rfJob.Id

if (Wait-Http "$RuleForgeUrl/health" 90) {
    Write-Ok "RuleForge healthy at $RuleForgeUrl"
    # Now that it's up, refresh so it re-reads the freshly-published rule/refs.
    try {
        Invoke-RestMethod -Method Post -Uri "$RuleForgeUrl/admin/refresh" -Headers @{ "X-AERO-Key" = $RuleForgeApiKey } | Out-Null
        Write-Ok "RuleForge caches refreshed"
    } catch { Write-Warn2 "RuleForge refresh failed (bindings still loaded at boot): $($_.Exception.Message)" }
} else {
    Write-Warn2 "RuleForge did NOT come up - offer/shop will degrade gracefully (empty bundles + warnings)."
}

# -- Manifest -----------------------------------------------------------------
@{
    jobIds     = $jobIds
    dfdbUrl    = $DfdbUrl
    ruleForge  = $RuleForgeUrl
    aeroBusUrl = $AeroBusUrl
    dataDir    = $DataDir
    started    = (Get-Date).ToString("o")
} | ConvertTo-Json | Set-Content -Path $Manifest -Encoding UTF8

Write-Host ""
Write-Step "Stack is up"
Write-Ok "DocumentForge : $DfdbUrl"
Write-Ok "RuleForge     : $RuleForgeUrl"
Write-Ok "AeroBus       : $AeroBusUrl   (Swagger: $AeroBusUrl/swagger)"
Write-Host ""
Write-Host "Next:  ./scripts/smoke.ps1" -ForegroundColor White
Write-Host "Stop:  ./scripts/run-stack.ps1 -Stop" -ForegroundColor White
