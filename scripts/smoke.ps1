<#
.SYNOPSIS
  End-to-end smoke test for AeroBus - exercises EVERY module against a running
  stack and prints a PASS/FAIL line per step plus a final summary. Exits non-zero
  if any step fails.

.DESCRIPTION
  Assumes the stack is ALREADY RUNNING. Start it first with:

      ./scripts/run-stack.ps1        # dfdb :4300, RuleForge :5050, AeroBus :5080

  (or `docker compose up -d`, though the local run-stack is what these defaults
  target). This script does NOT manage Docker or the services - it only drives
  them and asserts.

  It seeds a fresh company (random Guid) every run via direct DocumentForge
  inserts, so reruns are isolated and repeatable. Covered modules:
    health/version, admin auth (JWT) + RBAC 401/200, ab_ API tokens,
    catalogue reference data + fleet + layout, schedule + flight-builder +
    per-compartment inventory, products + bundles, rules publish (+ RuleForge
    refresh if up), offer/shop (priced or gracefully degraded), order
    create/retrieve/change-cancel with inventory decrement/restore, oversell
    409, and the event backbone (signed webhooks + audit + SSE replay).

  PowerShell 5.1 compatible: no && chains, no ternary; Invoke-RestMethod
  throughout; failures recorded and the run continues where safe.

.EXAMPLE
  ./scripts/smoke.ps1
  ./scripts/smoke.ps1 -AeroBusUrl http://localhost:5080 -DfdbUrl http://localhost:4300
#>
[CmdletBinding()]
param(
    [string] $AeroBusUrl   = "http://localhost:5080",
    [string] $DfdbUrl      = "http://localhost:4300",
    [string] $RuleForgeUrl = "http://localhost:5050",
    [int]    $ReceiverPort = 5099
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# -- result tracking ----------------------------------------------------------
$script:Passed = 0
$script:Failed = 0
$script:Results = @()

function Pass([string]$name, [string]$detail = "") {
    $script:Passed++
    $script:Results += [pscustomobject]@{ Ok = $true; Name = $name; Detail = $detail }
    $line = "  [PASS] $name"
    if ($detail) { $line += "  ($detail)" }
    Write-Host $line -ForegroundColor Green
}

function Fail([string]$name, [string]$detail = "") {
    $script:Failed++
    $script:Results += [pscustomobject]@{ Ok = $false; Name = $name; Detail = $detail }
    $line = "  [FAIL] $name"
    if ($detail) { $line += "  ($detail)" }
    Write-Host $line -ForegroundColor Red
}

function Info([string]$msg) { Write-Host $msg -ForegroundColor Cyan }
function Note([string]$msg) { Write-Host "    $msg" -ForegroundColor DarkGray }

# Assert a boolean; records PASS/FAIL. Returns the condition so callers can branch.
function Assert([bool]$cond, [string]$name, [string]$detail = "") {
    if ($cond) { Pass $name $detail } else { Fail $name $detail }
    return $cond
}

# HTTP helpers ----------------------------------------------------------------
function Invoke-Json {
    param(
        [string] $Method,
        [string] $Uri,
        $Body = $null,
        [hashtable] $Headers = @{},
        [switch] $Raw
    )
    $h = @{}
    foreach ($k in $Headers.Keys) { $h[$k] = $Headers[$k] }
    if (-not $h.ContainsKey("Content-Type")) { $h["Content-Type"] = "application/json" }
    $json = $null
    if ($null -ne $Body) {
        if ($Body -is [string]) { $json = $Body } else { $json = ($Body | ConvertTo-Json -Depth 12) }
    }
    if ($json) {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $h -Body $json
    } else {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $h
    }
}

# Returns @{ Status = <int>; Body = <string> } even for non-2xx (no throw on HTTP error).
function Try-Http {
    param(
        [string] $Method,
        [string] $Uri,
        $Body = $null,
        [hashtable] $Headers = @{}
    )
    $h = @{}
    foreach ($k in $Headers.Keys) { $h[$k] = $Headers[$k] }
    if (-not $h.ContainsKey("Content-Type")) { $h["Content-Type"] = "application/json" }
    $json = $null
    if ($null -ne $Body) {
        if ($Body -is [string]) { $json = $Body } else { $json = ($Body | ConvertTo-Json -Depth 12) }
    }
    try {
        $params = @{ Method = $Method; Uri = $Uri; Headers = $h; UseBasicParsing = $true }
        if ($json) { $params["Body"] = $json }
        $r = Invoke-WebRequest @params
        return @{ Status = [int]$r.StatusCode; Body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($resp -and $resp.StatusCode) {
            $code = [int]$resp.StatusCode
            $bodyText = ""
            try {
                $stream = $resp.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $bodyText = $reader.ReadToEnd()
            } catch {}
            return @{ Status = $code; Body = $bodyText }
        }
        return @{ Status = 0; Body = $_.Exception.Message }
    }
}

function Df-Insert([string]$collection, $doc) {
    return Invoke-Json -Method Post -Uri "$DfdbUrl/collections/$collection" -Body $doc
}

# Query a dfdb collection with SQL; returns the documents array.
function Df-Query([string]$sql) {
    $res = Invoke-Json -Method Post -Uri "$DfdbUrl/query" -Body @{ sql = $sql }
    if ($res.documents) { return $res.documents }
    return @()
}

# HMAC-SHA256 signature exactly as WebhookSignature.Compute does it.
function Compute-Signature([string]$secret, [string]$body) {
    $h = New-Object System.Security.Cryptography.HMACSHA256
    $h.Key = [System.Text.Encoding]::UTF8.GetBytes($secret)
    $hash = $h.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($body))
    $h.Dispose()
    return "sha256=" + (([System.BitConverter]::ToString($hash)) -replace '-', '').ToLower()
}

Write-Host ""
Write-Host "================= AeroBus END-TO-END SMOKE TEST =================" -ForegroundColor White
Write-Host "  AeroBus:   $AeroBusUrl"
Write-Host "  dfdb:      $DfdbUrl"
Write-Host "  RuleForge: $RuleForgeUrl"
Write-Host "================================================================" -ForegroundColor White

# Shared state across steps
$state = @{}

# ============================================================================
# STEP 1 - Diagnostics: /health, /health/documentforge, /version
# ============================================================================
Info "`n[1] Diagnostics"
try {
    $r = Try-Http -Method Get -Uri "$AeroBusUrl/health"
    Assert ($r.Status -eq 200) "GET /health -> 200" "status=$($r.Status)" | Out-Null
} catch { Fail "GET /health -> 200" $_.Exception.Message }

try {
    $r = Try-Http -Method Get -Uri "$AeroBusUrl/health/documentforge"
    Assert ($r.Status -eq 200) "GET /health/documentforge -> 200" "status=$($r.Status)" | Out-Null
} catch { Fail "GET /health/documentforge -> 200" $_.Exception.Message }

try {
    $r = Try-Http -Method Get -Uri "$AeroBusUrl/version"
    $ok = ($r.Status -eq 200) -and ($r.Body -match '"sha"')
    Assert $ok "GET /version -> 200 (has sha)" "status=$($r.Status)" | Out-Null
} catch { Fail "GET /version -> 200" $_.Exception.Message }

# ============================================================================
# STEP 2 - Seed company + admin.all ab_ API key (direct dfdb)
# User management lives in Keycloak behind /identity now; the smoke stack runs
# without Keycloak, so it authenticates with an agent-style API key instead of
# the removed HS256 /admin/users authenticate flow.
# ============================================================================
Info "`n[2] Seed tenant (fresh company + ab_ admin key via direct DocumentForge inserts)"
$companyId = [guid]::NewGuid().ToString()
$slug      = "smoke-" + $companyId.Substring(0, 8)
$state.CompanyId = $companyId
$state.Slug = $slug

$abPrefix = ($companyId -replace "[^a-z0-9]", "").Substring(0, 8)
$abSecret = New-Object byte[] 32
(New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($abSecret)
$abHash   = [System.Security.Cryptography.SHA256]::Create().ComputeHash($abSecret)
$abKey    = "ab_" + $abPrefix + "_" + [Convert]::ToBase64String($abSecret).TrimEnd("=").Replace("+", "-").Replace("/", "_")
try {
    Df-Insert "companies" @{ Id = $companyId; Name = "Smoke Airlines"; Slug = $slug; Status = "Active"; OperatingCurrency = "AED"; DefaultExpectedLoadFactor = 0.8 } | Out-Null
    Df-Insert "apitokens" @{ Id = [guid]::NewGuid().ToString(); CompanyId = $companyId; Name = "smoke-admin-key"; Prefix = $abPrefix; Hash = [Convert]::ToBase64String($abHash); Scopes = "admin.all"; Created = (Get-Date).ToUniversalTime().ToString("o") } | Out-Null
    Pass "Seeded company + admin.all ab_ key" "slug=$slug prefix=$abPrefix"
} catch {
    Fail "Seed tenant" $_.Exception.Message
    Info "`nCannot continue without a seeded tenant. Is dfdb reachable at $DfdbUrl?"
    Write-Host "SMOKE ABORTED" -ForegroundColor Red
    exit 1
}

# ============================================================================
# STEP 3 - Auth: 401 without a token, 200 + admin.all with the seeded ab_ key
# ============================================================================
Info "`n[3] Auth (ab_ key) + RBAC"

# WITHOUT token = 401
$r = Try-Http -Method Get -Uri "$AeroBusUrl/identity/me"
Assert ($r.Status -eq 401) "GET /identity/me WITHOUT token -> 401" "status=$($r.Status)" | Out-Null

# WITH the seeded ab_ key = 200
$r = Try-Http -Method Get -Uri "$AeroBusUrl/identity/me" -Headers @{ Authorization = "Bearer $abKey" }
Assert ($r.Status -eq 200) "GET /identity/me WITH ab_ key -> 200" "status=$($r.Status)" | Out-Null

$authHeader = @{ Authorization = "Bearer $abKey" }

# Helper to POST a catalogue /save (all catalogue creates take CompanyId in body)
function Cat-Save([string]$resource, $doc) {
    return Invoke-Json -Method Post -Uri "$AeroBusUrl/catalogue/$resource/save" -Headers $authHeader -Body $doc
}

# ============================================================================
# STEP 5 - Catalogue: continent/country/region/airport (2, an O&D), equipment,
#          layout (J/Y with seats), schedule (date range + DoW)
# ============================================================================
Info "`n[5] Catalogue (reference data, fleet, layout, schedule)"
# O&D chosen to match rules/ref-basefares.json (SYD-MEL has a base fare of 180)
# so the shop-bundles rule prices bundles with a realistic (non-zero) fare.
$origin = "SYD"
$dest   = "MEL"
try {
    $continent = Cat-Save "continents" @{ CompanyId = $companyId; Code = "OC"; Name = "Oceania"; Status = "Active" }
    Assert ([bool]$continent.Id) "create continent" | Out-Null
    $contId = $continent.Id

    $country = Cat-Save "countries" @{ CompanyId = $companyId; Code = "AU"; Name = "Australia"; ContinentId = $contId; Status = "Active" }
    Assert ([bool]$country.Id) "create country" | Out-Null
    $countryId = $country.Id

    $region = Cat-Save "regions" @{ CompanyId = $companyId; Code = "ANZ"; Name = "Australia/NZ"; CountryId = $countryId; Status = "Active" }
    Assert ([bool]$region.Id) "create region" | Out-Null
    $regionId = $region.Id

    $ap1 = Cat-Save "airports" @{ CompanyId = $companyId; Code = $origin; Name = "Sydney Kingsford Smith"; City = "Sydney"; RegionId = $regionId; CountryId = $countryId; TimeZoneId = "Australia/Sydney"; Status = "Active" }
    $ap2 = Cat-Save "airports" @{ CompanyId = $companyId; Code = $dest;   Name = "Melbourne Tullamarine"; City = "Melbourne"; RegionId = $regionId; CountryId = $countryId; TimeZoneId = "Australia/Melbourne"; Status = "Active" }
    Assert (($ap1.Id) -and ($ap2.Id)) "create 2 airports ($origin, $dest)" | Out-Null
} catch { Fail "catalogue reference data" $_.Exception.Message }

# Layout with 2 compartments J (4 seats) + Y (6 seats)
$layoutId = $null
$jSeats = 4
$ySeats = 6
try {
    $seatTypeJ = [guid]::NewGuid().ToString()
    $seatTypeY = [guid]::NewGuid().ToString()

    function New-Seats([int]$startRow, [int]$count, [string[]]$cols, [string]$seatType) {
        $seats = @()
        $row = $startRow
        $i = 0
        while ($i -lt $count) {
            foreach ($c in $cols) {
                if ($i -ge $count) { break }
                $seats += @{ Id = [guid]::NewGuid().ToString(); RowNumber = $row; Column = $c; Status = "Active"; SeatTypeId = $seatType }
                $i++
            }
            $row++
        }
        return $seats
    }

    $jSeatList = New-Seats 1 $jSeats @("A","F") $seatTypeJ
    $ySeatList = New-Seats 10 $ySeats @("A","B","C") $seatTypeY

    $layout = Cat-Save "layouts" @{
        CompanyId = $companyId
        Name = "SmokeJet Narrowbody"
        Type = "Standard"
        Status = "Active"
        SeatTypes = @(
            @{ Id = $seatTypeJ; Name = "Business"; Status = "Active" },
            @{ Id = $seatTypeY; Name = "Economy"; Status = "Active" }
        )
        Compartments = @(
            @{ Id = [guid]::NewGuid().ToString(); Code = "J"; Name = "Business"; Status = "Active"; Order = 1; DefaultSeatTypeId = $seatTypeJ; StockCapacity = $jSeats; Seats = $jSeatList },
            @{ Id = [guid]::NewGuid().ToString(); Code = "Y"; Name = "Economy"; Status = "Active"; Order = 2; DefaultSeatTypeId = $seatTypeY; StockCapacity = $ySeats; Seats = $ySeatList }
        )
    }
    $layoutId = $layout.Id
    Assert ([bool]$layoutId) "create layout (J=$jSeats, Y=$ySeats seats)" | Out-Null
} catch { Fail "create layout" $_.Exception.Message }

# Equipment referencing the layout
try {
    $equip = Cat-Save "equipment" @{ CompanyId = $companyId; EquipmentCode = "320"; Name = "Airbus A320"; LayoutId = $layoutId; Status = "Active" }
    Assert ([bool]$equip.Id) "create equipment" | Out-Null
} catch { Fail "create equipment" $_.Exception.Message }

# Schedule: a date range starting a few days out, all days of week, using the layout
$scheduleId = $null
$depDate = (Get-Date).Date.AddDays(14)
try {
    $schedule = Cat-Save "schedules" @{
        CompanyId = $companyId
        LayoutId = $layoutId
        CarrierCode = "SM"
        FlightNumber = "100"
        DepartureStation = $origin
        ArrivalStation = $dest
        DepartureTimeLocal = "08:00:00"
        ArrivalTimeLocal = "12:30:00"
        ArrivalOffsetDays = 0
        StartDateLocal = $depDate.ToString("yyyy-MM-ddTHH:mm:ss")
        EndDateLocal = $depDate.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss")
        Monday = $true; Tuesday = $true; Wednesday = $true; Thursday = $true; Friday = $true; Saturday = $true; Sunday = $true
        EquipmentCode = "320"
        Status = "Active"
    }
    $scheduleId = $schedule.Id
    Assert ([bool]$scheduleId) "create schedule ($origin-$dest, 3-day range, all DoW)" | Out-Null
    $state.ScheduleId = $scheduleId
} catch { Fail "create schedule" $_.Exception.Message }

# ============================================================================
# STEP 6 - Flight-builder: preview then build; assert flights + flightinventory
# ============================================================================
Info "`n[6] Flight-builder + per-compartment inventory"
$builtFlightIds = @()
if ($scheduleId) {
    try {
        $preview = Invoke-Json -Method Get -Uri "$AeroBusUrl/catalogue/flight-builder/preview/$scheduleId" -Headers $authHeader
        $previewCount = @($preview).Count
        Assert ($previewCount -gt 0) "preview schedule -> $previewCount flight(s)" | Out-Null
    } catch { Fail "flight-builder preview" $_.Exception.Message }

    try {
        $build = Invoke-Json -Method Post -Uri "$AeroBusUrl/catalogue/flight-builder/build/$scheduleId" -Headers $authHeader -Body "{}"
        $builtFlightIds = @($build.built | ForEach-Object { $_.Id })
        Assert ($builtFlightIds.Count -gt 0) "build schedule -> $($builtFlightIds.Count) flight(s) created" | Out-Null
        $state.FlightIds = $builtFlightIds
    } catch { Fail "flight-builder build" $_.Exception.Message }

    # Assert flightinventory docs exist per compartment with correct Capacity/Available
    if ($builtFlightIds.Count -gt 0) {
        $flightId = $builtFlightIds[0]
        $state.PrimaryFlightId = $flightId
        try {
            $inv = Df-Query "SELECT * FROM flightinventory WHERE FlightId = '$flightId'"
            $buckets = @($inv | ForEach-Object { $_.Bucket })
            $hasJ = $buckets -contains "J"
            $hasY = $buckets -contains "Y"
            Assert ($hasJ -and $hasY) "flightinventory has J + Y compartments" ("buckets=" + ($buckets -join ",")) | Out-Null

            $yRow = $inv | Where-Object { $_.Bucket -eq "Y" } | Select-Object -First 1
            $jRow = $inv | Where-Object { $_.Bucket -eq "J" } | Select-Object -First 1
            $okCap = ($yRow.Capacity -eq $ySeats) -and ($yRow.Available -eq $ySeats) -and ($jRow.Capacity -eq $jSeats)
            Assert $okCap "inventory Capacity/Available correct" ("Y cap=$($yRow.Capacity) avail=$($yRow.Available); J cap=$($jRow.Capacity)") | Out-Null
        } catch { Fail "assert flightinventory" $_.Exception.Message }
    }
} else {
    Fail "flight-builder (no schedule)" "schedule not created"
}

# ============================================================================
# STEP 7 - Products (2) + bundles (LITE / FLEX / FLEXPLUS via Type)
# ============================================================================
Info "`n[7] Products + bundles"
try {
    $p1 = Cat-Save "products" @{ CompanyId = $companyId; Code = "BAG20"; Name = "20kg Bag"; ProductType = "Ancillary"; Status = "Active" }
    $p2 = Cat-Save "products" @{ CompanyId = $companyId; Code = "SEATSTD"; Name = "Standard Seat"; ProductType = "Ancillary"; Status = "Active" }
    Assert (($p1.Id) -and ($p2.Id)) "create 2 products" | Out-Null
} catch { Fail "create products" $_.Exception.Message }

try {
    $b1 = Cat-Save "bundles" @{ CompanyId = $companyId; Name = "Lite"; Type = "LITE"; Status = "Active" }
    $b2 = Cat-Save "bundles" @{ CompanyId = $companyId; Name = "Flex"; Type = "FLEX"; Status = "Active" }
    $b3 = Cat-Save "bundles" @{ CompanyId = $companyId; Name = "Flex Plus"; Type = "FLEXPLUS"; Status = "Active" }
    Assert (($b1.Id) -and ($b2.Id) -and ($b3.Id)) "create 3 bundles (LITE/FLEX/FLEXPLUS)" | Out-Null
} catch { Fail "create bundles" $_.Exception.Message }

# Is RuleForge up? Governs whether we assert priced bundles or graceful degrade.
$ruleForgeUp = $false
try {
    $rf = Try-Http -Method Get -Uri "$RuleForgeUrl/health"
    $ruleForgeUp = ($rf.Status -eq 200)
} catch { $ruleForgeUp = $false }
Note ("RuleForge is " + $(if ($ruleForgeUp) { "UP - expecting priced bundles" } else { "DOWN - expecting graceful degrade" }))

# ============================================================================
# STEP 8 - Rules: PUT + publish shop-bundles rule (+ refs) to env dev
# ============================================================================
Info "`n[8] Rules publish (shop-bundles)"
$RulesDir = Join-Path (Split-Path -Parent $ScriptDir) "rules"
try {
    # Reference sets
    $refBase = Get-Content -Raw (Join-Path $RulesDir "ref-basefares.json")
    Invoke-Json -Method Put -Uri "$AeroBusUrl/rules/reference-sets/ref-basefares" -Headers $authHeader -Body $refBase | Out-Null
    Invoke-Json -Method Post -Uri "$AeroBusUrl/rules/reference-sets/ref-basefares/publish" -Headers $authHeader -Body "{}" | Out-Null

    $refMarkup = Get-Content -Raw (Join-Path $RulesDir "ref-bundle-markups.json")
    Invoke-Json -Method Put -Uri "$AeroBusUrl/rules/reference-sets/ref-bundle-markups" -Headers $authHeader -Body $refMarkup | Out-Null
    Invoke-Json -Method Post -Uri "$AeroBusUrl/rules/reference-sets/ref-bundle-markups/publish" -Headers $authHeader -Body "{}" | Out-Null
    Pass "PUT + publish reference sets"
} catch { Fail "publish reference sets" $_.Exception.Message }

try {
    $ruleDoc = Get-Content -Raw (Join-Path $RulesDir "rule-shop-bundles.json")
    Invoke-Json -Method Put -Uri "$AeroBusUrl/rules/rule-shop-bundles" -Headers $authHeader -Body $ruleDoc | Out-Null
    $published = Invoke-Json -Method Post -Uri "$AeroBusUrl/rules/rule-shop-bundles/publish?env=dev" -Headers $authHeader -Body "{}"
    # publish always 200; refreshed reflects whether RuleForge accepted the refresh.
    Assert ([bool]$published.version) "publish rule-shop-bundles v$($published.version) to dev" ("refreshed=" + $published.refreshed) | Out-Null
    if ($ruleForgeUp) {
        Assert ($published.refreshed -eq $true) "RuleForge refresh acknowledged" | Out-Null
    } else {
        Note "RuleForge down -> publish still succeeded (graceful); refreshed=$($published.refreshed)"
        Assert ($true) "publish graceful when RuleForge down" | Out-Null
    }
} catch { Fail "publish rule-shop-bundles" $_.Exception.Message }

# ============================================================================
# STEP 9 - Offer: POST /offer/shop for the built O&D, 2 pax
# ============================================================================
Info "`n[9] Offer shop"
$offerId = $null
$paxId1 = [guid]::NewGuid().ToString()
$paxId2 = [guid]::NewGuid().ToString()
try {
    $shopReq = @{
        SearchContext = @{ Channel = "web"; PointOfSale = "AE"; Currency = "AED"; Locale = "en-AE" }
        Passengers = @(
            @{ Id = $paxId1; Name = "Adult One"; Type = "ADT"; Age = 34 },
            @{ Id = $paxId2; Name = "Adult Two"; Type = "ADT"; Age = 31 }
        )
        SearchCriteria = @{
            TripType = "ONE_WAY"
            OriginDestinations = @(
                @{ OdRef = "OD1"; Origin = $origin; Destination = $dest; DepartureDate = $depDate.ToString("yyyy-MM-ddTHH:mm:ssZ") }
            )
            CabinPreferences = @("Y")
            MaxConnections = 0
            MaxResultsPerOD = 20
        }
    }
    $shop = Invoke-Json -Method Post -Uri "$AeroBusUrl/offer/shop" -Headers $authHeader -Body $shopReq

    $ods = @($shop.OriginDestinations)
    $solutions = @()
    foreach ($od in $ods) { $solutions += @($od.FlightSolutions) }
    Assert ($solutions.Count -gt 0) "offer/shop returns $($solutions.Count) solution(s)" | Out-Null

    # Bundles (priced or degraded)
    $allBundles = @()
    foreach ($sol in $solutions) { $allBundles += @($sol.Bundles) }
    if ($ruleForgeUp) {
        Assert ($allBundles.Count -gt 0) "priced bundles present (RuleForge up)" "bundles=$($allBundles.Count)" | Out-Null
    } else {
        $noFatal = ($allBundles.Count -eq 0) -and (@($shop.Warnings).Count -gt 0)
        Assert $noFatal "graceful degrade: empty bundles + warnings[], no 500" ("warnings=" + (@($shop.Warnings).Count)) | Out-Null
    }

    # Assert an offers doc persisted; get its Id from the offer.created event.
    Start-Sleep -Milliseconds 300
    $evts = @(Invoke-Json -Method Get -Uri "$AeroBusUrl/events?type=offer.created&from=0" -Headers $authHeader)
    $offerEvt = $evts | Sort-Object { $_.Seq } | Select-Object -Last 1
    if ($offerEvt) { $offerId = $offerEvt.Subject.Id }
    Assert ([bool]$offerId) "offers doc persisted (offer.created event)" "offerId=$offerId" | Out-Null
    $state.OfferId = $offerId
} catch { Fail "offer/shop" $_.Exception.Message }

# Sum a bucket's Available across a set of flights. The order picks the "first
# solution carrying bundles", which is one of the built flights but not
# necessarily $FlightIds[0] (build order isn't guaranteed). Summing across the
# whole set makes the decrement/restore assertions order-independent.
function Get-TotalAvailable([string[]]$flightIds, [string]$bucket) {
    $sum = 0
    foreach ($fid in $flightIds) {
        $rows = Df-Query "SELECT * FROM flightinventory WHERE FlightId = '$fid'"
        $row = $rows | Where-Object { $_.Bucket -eq $bucket } | Select-Object -First 1
        if ($row) { $sum += [int]$row.Available }
    }
    return $sum
}

# ============================================================================
# STEP 10 - Order: create (inventory -2), retrieve, change/Cancel (inventory +2)
# ============================================================================
Info "`n[10] Order lifecycle (create / retrieve / cancel) + inventory"
$orderCreated = $false
$orderPublicId = $null
$orderGuid = $null
$flightSet = @($state.FlightIds)
$availBefore = -1
if ($flightSet.Count -gt 0) { $availBefore = Get-TotalAvailable $flightSet "Y" }

if (-not $offerId) {
    Fail "order/create (no offer)" "offer/shop did not yield an offerId (RuleForge likely down -> no priced bundle to book)"
} else {
    try {
        $createReq = @{
            Channel = "web"
            OfferId = $offerId
            Passengers = @(
                @{ PaxType = "ADT"; Title = "Mr"; FirstName = "Test"; LastName = "Traveller"; BirthDate = "1990-05-01T00:00:00Z"; Gender = "M" },
                @{ PaxType = "ADT"; Title = "Ms"; FirstName = "Sam"; LastName = "Traveller"; BirthDate = "1992-03-01T00:00:00Z"; Gender = "F" }
            )
            Payment = @{ Provider = "Manual"; Method = "Card"; Currency = "AED"; Amount = 1000 }
        }
        # Response is an OrderView { Order, Passengers, Payments, Charges };
        # the order aggregate (public OrderId + internal Id) is nested under .Order.
        $order = Invoke-Json -Method Post -Uri "$AeroBusUrl/order/create" -Headers $authHeader -Body $createReq
        $orderPublicId = $order.Order.OrderId
        $orderGuid = $order.Order.Id
        $orderCreated = [bool]$orderGuid
        Assert $orderCreated "POST /order/create -> order created" "orderId=$orderPublicId" | Out-Null
        $state.OrderGuid = $orderGuid
        $state.OrderPublicId = $orderPublicId
    } catch {
        Fail "POST /order/create" $_.Exception.Message
    }

    # Inventory decreased by 2 on the Y compartment (across the booked O&D's flights).
    if ($orderCreated -and ($availBefore -ge 0)) {
        $availAfter = Get-TotalAvailable $flightSet "Y"
        Assert ($availAfter -eq ($availBefore - 2)) "flightinventory Y available decreased by 2" "before=$availBefore after=$availAfter" | Out-Null
    }

    # Retrieve by public OrderId + LastName
    if ($orderCreated) {
        try {
            $retrieve = Invoke-Json -Method Post -Uri "$AeroBusUrl/order/retrieve" -Headers $authHeader -Body @{ OrderId = $orderPublicId; LastName = "Traveller" }
            $found = (@($retrieve.Orders).Count -gt 0) -and ($retrieve.Success -eq $true)
            Assert $found "POST /order/retrieve -> found" | Out-Null
        } catch { Fail "POST /order/retrieve" $_.Exception.Message }
    }

    # Change / Cancel -> Cancelled + inventory restored
    if ($orderCreated) {
        try {
            $change = Invoke-Json -Method Post -Uri "$AeroBusUrl/order/change" -Headers $authHeader -Body @{ OrderId = $orderGuid; Action = "Cancel"; Reason = "smoke test" }
            $cancelled = ($change.Success -eq $true) -and ($change.NewStatus -eq "Cancelled")
            Assert $cancelled "POST /order/change Cancel -> Cancelled" "newStatus=$($change.NewStatus)" | Out-Null
            Assert ($change.InventoryReleased -eq $true) "cancel reports InventoryReleased" | Out-Null

            $availRestored = Get-TotalAvailable $flightSet "Y"
            Assert ($availRestored -eq $availBefore) "flightinventory Y available restored" "restored=$availRestored expected=$availBefore" | Out-Null
        } catch { Fail "POST /order/change Cancel" $_.Exception.Message }
    }
}

# ============================================================================
# STEP 11 - Oversell: order exceeding remaining seats -> 409, no order, intact
# ============================================================================
Info "`n[11] Oversell guard"
if ($offerId) {
    $availPre = Get-TotalAvailable $flightSet "Y"
    # One flight's Y compartment holds $ySeats seats; booking more pax than that on
    # a single solution can't be secured -> 409, and no inventory is consumed.
    $overPax = @()
    for ($i = 0; $i -lt ($ySeats + 2); $i++) {
        $overPax += @{ PaxType = "ADT"; Title = "Mr"; FirstName = "Over$i"; LastName = "Sell"; BirthDate = "1990-01-01T00:00:00Z"; Gender = "M" }
    }
    $overReq = @{
        Channel = "web"; OfferId = $offerId
        Passengers = $overPax
        Payment = @{ Provider = "Manual"; Method = "Card"; Currency = "AED"; Amount = 1 }
    }
    $r = Try-Http -Method Post -Uri "$AeroBusUrl/order/create" -Body $overReq -Headers $authHeader
    Assert ($r.Status -eq 409) "oversell -> 409 Conflict" "status=$($r.Status)" | Out-Null

    $availPost = Get-TotalAvailable $flightSet "Y"
    Assert ($availPost -eq $availPre) "inventory intact after oversell attempt" "pre=$availPre post=$availPost" | Out-Null
} else {
    Note "Skipping oversell (no offer/priced bundle)."
    Fail "oversell guard (no offer)" "requires a priced offer"
}

# ============================================================================
# STEP 12 - Events: webhook subscription -> local receiver, drive events,
#           verify signed deliveries + audit list + SSE replay
# ============================================================================
Info "`n[12] Event backbone (signed webhooks + audit + SSE)"
$recordFile = Join-Path $env:TEMP ("aerobus-smoke-hook-" + [guid]::NewGuid().ToString("N") + ".jsonl")
"" | Set-Content -Path $recordFile -Encoding UTF8
$receiverJob = $null
try {
    # Start a tiny HttpListener that appends each POST (headers + raw body) as a
    # JSON line to $recordFile, so the main script can inspect deliveries.
    $receiverJob = Start-Job -Name "aerobus-hook-receiver" -ScriptBlock {
        param($port, $file)
        $listener = New-Object System.Net.HttpListener
        $listener.Prefixes.Add("http://localhost:$port/")
        $listener.Start()
        while ($listener.IsListening) {
            try {
                $ctx = $listener.GetContext()
                $req = $ctx.Request
                $reader = New-Object System.IO.StreamReader($req.InputStream, $req.ContentEncoding)
                $body = $reader.ReadToEnd()
                $reader.Close()
                $sig = $req.Headers["X-AeroBus-Signature"]
                $evt = $req.Headers["X-AeroBus-Event"]
                $delivery = $req.Headers["X-AeroBus-Delivery"]
                $rec = @{ path = $req.Url.AbsolutePath; sig = $sig; evt = $evt; delivery = $delivery; body = $body } | ConvertTo-Json -Compress -Depth 8
                Add-Content -Path $file -Value $rec
                $resp = $ctx.Response
                $resp.StatusCode = 200
                $buf = [System.Text.Encoding]::UTF8.GetBytes("ok")
                $resp.OutputStream.Write($buf, 0, $buf.Length)
                $resp.OutputStream.Close()
            } catch { break }
        }
        $listener.Stop()
    } -ArgumentList $ReceiverPort, $recordFile

    Start-Sleep -Milliseconds 800  # let the listener bind

    # Subscribe: types order.* + flight.*, secret we control so we can recompute HMAC.
    $secret = "smoke-secret-" + [guid]::NewGuid().ToString("N")
    $sub = Invoke-Json -Method Post -Uri "$AeroBusUrl/events/subscriptions" -Headers $authHeader -Body @{
        Url = "http://localhost:$ReceiverPort/hook"
        Types = @("order.*", "flight.*")
        Secret = $secret
        Active = $true
    }
    Assert ([bool]$sub.Id) "POST /events/subscriptions (order.*, flight.*)" | Out-Null

    # Drive events: build ANOTHER schedule (flight.built) + an order create/cancel.
    $sched2 = Cat-Save "schedules" @{
        CompanyId = $companyId; LayoutId = $layoutId; CarrierCode = "SM"; FlightNumber = "200"
        DepartureStation = $origin; ArrivalStation = $dest
        DepartureTimeLocal = "18:00:00"; ArrivalTimeLocal = "22:30:00"; ArrivalOffsetDays = 0
        StartDateLocal = $depDate.ToString("yyyy-MM-ddTHH:mm:ss"); EndDateLocal = $depDate.ToString("yyyy-MM-ddTHH:mm:ss")
        Monday = $true; Tuesday = $true; Wednesday = $true; Thursday = $true; Friday = $true; Saturday = $true; Sunday = $true
        EquipmentCode = "320"; Status = "Active"
    }
    Invoke-Json -Method Post -Uri "$AeroBusUrl/catalogue/flight-builder/build/$($sched2.Id)" -Headers $authHeader -Body "{}" | Out-Null

    # A fresh order create + cancel (re-shop so the offer is unexpired).
    if ($offerId) {
        $shop2 = Invoke-Json -Method Post -Uri "$AeroBusUrl/offer/shop" -Headers $authHeader -Body @{
            SearchContext = @{ Channel = "web"; Currency = "AED" }
            Passengers = @(@{ Id = [guid]::NewGuid().ToString(); Name = "Solo"; Type = "ADT"; Age = 40 })
            SearchCriteria = @{ TripType = "ONE_WAY"; OriginDestinations = @(@{ OdRef = "OD1"; Origin = $origin; Destination = $dest; DepartureDate = $depDate.ToString("yyyy-MM-ddTHH:mm:ssZ") }); CabinPreferences = @("Y"); MaxConnections = 0; MaxResultsPerOD = 20 }
        }
        # Resolve the persisted offer deterministically by its SearchId (the offer
        # doc is written synchronously during shop; the offer.created event is
        # async, so don't race it here).
        $offer2 = $null
        if ($shop2.SearchId) {
            $offerDocs = Df-Query "SELECT * FROM offers WHERE SearchId = '$($shop2.SearchId)'"
            $offer2 = ($offerDocs | Select-Object -First 1).Id
        }
        if ($offer2) {
            try {
                $o2 = Invoke-Json -Method Post -Uri "$AeroBusUrl/order/create" -Headers $authHeader -Body @{
                    Channel = "web"; OfferId = $offer2
                    Passengers = @(@{ PaxType = "ADT"; Title = "Mr"; FirstName = "Evt"; LastName = "Driver"; BirthDate = "1985-01-01T00:00:00Z"; Gender = "M" })
                    Payment = @{ Provider = "Manual"; Method = "Card"; Currency = "AED"; Amount = 500 }
                }
                Invoke-Json -Method Post -Uri "$AeroBusUrl/order/change" -Headers $authHeader -Body @{ OrderId = $o2.Order.Id; Action = "Cancel"; Reason = "evt" } | Out-Null
            } catch { Note "event-driver order create/cancel: $($_.Exception.Message)" }
        }
    }

    # Give the dispatcher time to fan out webhooks.
    $deadline = (Get-Date).AddSeconds(20)
    $records = @()
    while ((Get-Date) -lt $deadline) {
        $lines = @(Get-Content -Path $recordFile -ErrorAction SilentlyContinue | Where-Object { $_.Trim() -ne "" })
        $records = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
        $haveFlight = $records | Where-Object { $_.evt -like "flight.*" }
        $haveOrder = $records | Where-Object { $_.evt -like "order.*" }
        if ($haveFlight -and $haveOrder) { break }
        Start-Sleep -Milliseconds 700
    }

    Assert ($records.Count -gt 0) "webhook receiver got $($records.Count) delivery(ies)" | Out-Null
    $gotFlight = @($records | Where-Object { $_.evt -like "flight.*" }).Count -gt 0
    $gotOrder = @($records | Where-Object { $_.evt -like "order.*" }).Count -gt 0
    Assert ($gotFlight -and $gotOrder) "received both flight.* and order.* webhooks" ("flight=$gotFlight order=$gotOrder") | Out-Null

    # Verify HMAC signature on a delivery by recomputing it in PS.
    if ($records.Count -gt 0) {
        $sample = $records | Select-Object -First 1
        $expected = Compute-Signature $secret $sample.body
        Assert ($sample.sig -eq $expected) "X-AeroBus-Signature verifies (HMAC-SHA256)" | Out-Null
    }

    # GET /events?from=0 lists Dispatched rows
    Start-Sleep -Milliseconds 500
    $dispatched = @(Invoke-Json -Method Get -Uri "$AeroBusUrl/events?status=Dispatched&from=0&limit=500" -Headers $authHeader)
    Assert ($dispatched.Count -gt 0) "GET /events lists Dispatched rows" "count=$($dispatched.Count)" | Out-Null

    # GET /events/stream?from=0 replays a few frames. Use HttpWebRequest so we can
    # read the SSE body incrementally (it never "completes") and bail once we've
    # seen a data: frame. HttpWebRequest is always available in PowerShell 5.1.
    try {
        $streamText = ""
        $req = [System.Net.HttpWebRequest]::Create("$AeroBusUrl/events/stream?from=0")
        $req.Method = "GET"
        $req.Headers.Add("Authorization", "Bearer $jwt")
        $req.Timeout = 6000
        $req.ReadWriteTimeout = 4000
        $resp = $req.GetResponse()
        $stream = $resp.GetResponseStream()
        $sr = New-Object System.IO.StreamReader($stream)
        $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw2.Elapsed.TotalSeconds -lt 4) {
            try { $line = $sr.ReadLine() } catch { break }
            if ($null -ne $line) { $streamText += $line + "`n" }
            if ($streamText -match "data:") { break }
        }
        $sr.Dispose()
        $resp.Close()
        Assert ($streamText -match "data:") "GET /events/stream replays (data: frames)" | Out-Null
    } catch {
        # A read timeout after we've captured frames is fine; only fail if empty.
        if ($streamText -match "data:") {
            Assert $true "GET /events/stream replays (data: frames)" | Out-Null
        } else {
            Fail "GET /events/stream replay" $_.Exception.Message
        }
    }
} catch {
    Fail "event backbone" $_.Exception.Message
} finally {
    if ($receiverJob) {
        Stop-Job $receiverJob -ErrorAction SilentlyContinue
        Remove-Job $receiverJob -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $recordFile -Force -ErrorAction SilentlyContinue
    # Free the receiver port in case the listener lingered.
    $lines = (netstat -ano | Select-String -Pattern "LISTENING" | Select-String -Pattern (":{0}\s" -f $ReceiverPort))
    foreach ($l in $lines) {
        $parts = ($l.ToString() -split "\s+") | Where-Object { $_ -ne "" }
        $procId = $parts[-1]
        if ($procId -match '^\d+$') { try { Stop-Process -Id ([int]$procId) -Force -ErrorAction SilentlyContinue } catch {} }
    }
}

# ============================================================================
# SUMMARY
# ============================================================================
$total = $script:Passed + $script:Failed
Write-Host ""
Write-Host "================================================================" -ForegroundColor White
Write-Host "  SMOKE SUMMARY: $($script:Passed)/$total passed, $($script:Failed) failed" -ForegroundColor White
Write-Host "================================================================" -ForegroundColor White
if ($script:Failed -gt 0) {
    Write-Host "  Failures:" -ForegroundColor Red
    foreach ($r in $script:Results | Where-Object { -not $_.Ok }) {
        Write-Host ("    - {0}  {1}" -f $r.Name, $r.Detail) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "RESULT: FAIL" -ForegroundColor Red
    exit 1
} else {
    Write-Host ""
    Write-Host "RESULT: PASS (all green)" -ForegroundColor Green
    exit 0
}





