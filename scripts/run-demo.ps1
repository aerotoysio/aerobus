# Run AeroBus locally against the DEMO environment (Keycloak + DocumentForge on
# the demo box). This is the supported way to launch for demo work - launching
# with no configuration points at a LOCAL DocumentForge (:4300) per
# appsettings.Development.json, which is a different (empty) datastore.
#
#   ./scripts/run-demo.ps1
#
# Secrets/endpoints come from scripts/.env.demo (git-ignored; copy
# scripts/.env.demo.example and fill in the two secrets - see
# docs/configuration.md for where each value comes from).
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less files as ANSI,
# and smart punctuation can mangle into quote characters that break parsing.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $PSScriptRoot ".env.demo"

if (-not (Test-Path $envFile)) {
    Write-Error "Missing $envFile - copy scripts/.env.demo.example and fill in the secrets."
}

foreach ($line in Get-Content $envFile) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
    $idx = $trimmed.IndexOf("=")
    if ($idx -lt 1) { continue }
    $name = $trimmed.Substring(0, $idx).Trim()
    $value = $trimmed.Substring($idx + 1).Trim()
    [Environment]::SetEnvironmentVariable($name, $value)
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5080"

Write-Host "AeroBus -> DocumentForge: $env:DocumentForge__BaseUrl"
Write-Host "AeroBus -> Keycloak:      $env:Keycloak__BaseUrl (realm $env:Keycloak__Realm)"
Write-Host ""

dotnet run --project (Join-Path $root "src/AeroBus.Api") --no-launch-profile
