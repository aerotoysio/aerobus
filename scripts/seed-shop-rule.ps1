# Seeds the offer shop-bundles rule (and its reference sets) into a running
# AeroBus + RuleForge + DocumentForge stack, then publishes to the given
# environment so RuleForge binds it live.
#
# PowerShell 5.1-compatible (no && chains, no ternary).
#
# Prereqs (all running):
#   - DocumentForge   (dfdb)      default http://localhost:4300
#   - RuleForge                    default http://localhost:5050
#   - AeroBus API                  default http://localhost:5080
#
# The /rules and /rules/reference-sets endpoints require an authenticated
# principal — pass a bearer token via -Token (a user JWT or an ab_ API key).
#
# Usage:
#   ./seed-shop-rule.ps1 -Token "<jwt-or-apikey>"
#   ./seed-shop-rule.ps1 -Token "<...>" -AeroBusUrl http://localhost:5080 -Env dev

param(
    [Parameter(Mandatory = $true)] [string] $Token,
    [string] $AeroBusUrl = "http://localhost:5080",
    [string] $Env = "dev"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RulesDir  = Join-Path (Split-Path -Parent $ScriptDir) "rules"

$Headers = @{
    "Authorization" = "Bearer $Token"
    "Content-Type"  = "application/json"
}

function Put-Json($url, $file) {
    $body = Get-Content -Raw -Path $file
    Write-Host "PUT  $url  (<- $([System.IO.Path]::GetFileName($file)))"
    return Invoke-RestMethod -Method Put -Uri $url -Headers $Headers -Body $body
}

function Post-Empty($url) {
    Write-Host "POST $url"
    return Invoke-RestMethod -Method Post -Uri $url -Headers $Headers -Body "{}"
}

Write-Host "== Seeding reference sets =="
# ref-basefares: O&D base fares (makes the base-fare lookup clean rather than a
# hard-coded constant). ref-bundle-markups: per-tier markup multipliers.
Put-Json  "$AeroBusUrl/rules/reference-sets/ref-basefares"      (Join-Path $RulesDir "ref-basefares.json")      | Out-Null
Post-Empty "$AeroBusUrl/rules/reference-sets/ref-basefares/publish"                                             | Out-Null
Put-Json  "$AeroBusUrl/rules/reference-sets/ref-bundle-markups" (Join-Path $RulesDir "ref-bundle-markups.json") | Out-Null
Post-Empty "$AeroBusUrl/rules/reference-sets/ref-bundle-markups/publish"                                        | Out-Null

Write-Host ""
Write-Host "== Seeding shop-bundles rule =="
Put-Json "$AeroBusUrl/rules/rule-shop-bundles" (Join-Path $RulesDir "rule-shop-bundles.json") | Out-Null

Write-Host ""
Write-Host "== Publishing rule to env '$Env' (writes ruleversions, binds environment, refreshes RuleForge) =="
$published = Invoke-RestMethod -Method Post -Uri "$AeroBusUrl/rules/rule-shop-bundles/publish?env=$Env" -Headers $Headers -Body "{}"
Write-Host ("Published rule-shop-bundles v{0} to env '{1}' (RuleForge refreshed: {2})" -f $published.version, $published.env, $published.refreshed)

Write-Host ""
Write-Host "== Environment binding =="
$environment = Invoke-RestMethod -Method Get -Uri "$AeroBusUrl/rules/environments/$Env" -Headers $Headers
$environment | ConvertTo-Json -Depth 6

Write-Host ""
Write-Host "Done. RuleForge now serves POST /v1/offer/shop-bundles from rule-shop-bundles."
