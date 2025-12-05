# Validates end-to-end interoperability:
# Container App (API + Frontend served from same origin) -> Drasi endpoints
# Requires Azure Developer CLI (azd) and Azure CLI (az) installed and logged in.

param(
    [string] $EnvName = $env:AZURE_ENV_NAME,
    [switch] $VerboseOutput
)

$ErrorActionPreference = "Stop"

function Write-Section($text) {
    Write-Host "`n==== $text ====" -ForegroundColor Cyan
}

function Write-Pass($text) {
    Write-Host "✅ $text" -ForegroundColor Green
}

function Write-Fail($text) {
    Write-Host "❌ $text" -ForegroundColor Red
}

function Invoke-HttpPing($url) {
    try {
        $resp = Invoke-WebRequest -Uri $url -Method GET -TimeoutSec 20 -UseBasicParsing
        return $resp.StatusCode
    }
    catch {
        return $_.Exception.Response.StatusCode
    }
}

function Get-AzdValue([string] $key) {
    try {
        # Capture both stdout/stderr; azd sometimes writes errors to stdout
        $out = (azd env get-value $key 2>&1 | Out-String).Trim()
        if (-not $out) { return $null }
        if ($out -like 'ERROR:*') { return $null }
        return $out
    }
    catch { return $null }
}

# 1) Resolve environment values from azd
Write-Section "Resolving environment values"
$apiHost = Get-AzdValue 'apiHost'
if (-not $apiHost) {
    Write-Host "apiHost not found in azd env; endpoint checks may fail." -ForegroundColor Yellow
}
$apiUrl = if ($apiHost) { "https://$apiHost" } else { $null }

Write-Host "apiHost: $apiHost"
Write-Host "apiUrl:  $apiUrl"
Write-Host "Note: Frontend is served from the same Container App (same-origin)" -ForegroundColor Yellow

# 2) Container App API endpoint checks (frontend and API are same-origin)
Write-Section "Checking Container App API endpoints"
if ($apiUrl) {
    $pingUrl = "$apiUrl/api/v1/ping"
    $drasiInsightsUrl = "$apiUrl/api/v1/drasi/insights"
    $healthUrl = "$apiUrl/health"

    $healthStatus = Invoke-HttpPing $healthUrl
    if ($healthStatus -eq 200) { Write-Pass "Health endpoint OK: $healthUrl" } else { Write-Fail "Health endpoint FAILED ($healthStatus): $healthUrl" }

    $pingStatus = Invoke-HttpPing $pingUrl
    if ($pingStatus -eq 200) { Write-Pass "API ping OK: $pingUrl" } else { Write-Fail "API ping FAILED ($pingStatus): $pingUrl" }

    $drasiStatus = Invoke-HttpPing $drasiInsightsUrl
    if ($drasiStatus -eq 200) { Write-Pass "Drasi insights OK: $drasiInsightsUrl" } else { Write-Fail "Drasi insights FAILED ($drasiStatus): $drasiInsightsUrl" }
} else {
    Write-Host "Skipping endpoint checks - apiUrl not available" -ForegroundColor Yellow
}

# 3) Container App environment variables on active revision
Write-Section "Checking Container App environment variables"
try {
    $rgName = azd env get-value AZURE_RESOURCE_GROUP 2>$null
}
catch {
    $rgName = $null
}

if (-not $rgName) {
    Write-Fail "Failed to retrieve AZURE_RESOURCE_GROUP via azd. Ensure 'azd env select' is set."
    throw
}

# Prefer env value for apiAppName; if missing, derive from naming pattern
$appName = Get-AzdValue 'apiAppName'
if (-not $appName) {
    $envName = $env:AZURE_ENV_NAME
    if (-not $envName) { $envName = Get-AzdValue 'AZURE_ENV_NAME' }
    if (-not $envName) {
        Write-Fail "Missing 'apiAppName' and 'AZURE_ENV_NAME' in azd environment."
        throw
    }
    $appName = "santadigitalshowcase-$envName-api"
    Write-Host "apiAppName not set; defaulting to '$appName' based on naming pattern" -ForegroundColor Yellow
}

try {
    $envJson = az containerapp show -n $appName -g $rgName --query 'properties.template.containers[0].env' -o json
    $envVars = $envJson | ConvertFrom-Json
}
catch {
    Write-Fail "Failed to read Container App env vars. Ensure Azure CLI is logged in and you have access."
    throw
}

$required = @('COSMOS_ENDPOINT', 'DRASI_SIGNALR_BASE_URL', 'DRASI_VIEW_SERVICE_BASE_URL')
$missing = @()
foreach ($name in $required) {
    if (-not ($envVars | Where-Object { $_.name -eq $name })) { $missing += $name }
}

if ($missing.Count -eq 0) {
    Write-Pass "All required env vars present: ${required}"
}
else {
    Write-Fail "Missing required env vars: ${missing}"
    Write-Host "Tip: Run 'azd provision' (or 'azd up') to re-apply Bicep and env vars after an 'azd deploy api'." -ForegroundColor Yellow
}

if ($VerboseOutput) {
    Write-Host "Current env vars:" -ForegroundColor DarkCyan
    $envVars | Format-Table name, value
}

# 4) Backend → Drasi reachability (optional quick checks)
Write-Section "Checking backend → Drasi reachability"
$drasiSignalR = ($envVars | Where-Object { $_.name -eq 'DRASI_SIGNALR_BASE_URL' }).value
$drasiViewSvc = ($envVars | Where-Object { $_.name -eq 'DRASI_VIEW_SERVICE_BASE_URL' }).value

if ($drasiSignalR) {
    $srStatus = Invoke-HttpPing $drasiSignalR
    if ($srStatus -ge 200 -and $srStatus -lt 500) { Write-Pass "Drasi SignalR base reachable ($srStatus): $drasiSignalR" } else { Write-Fail "Drasi SignalR base unreachable ($srStatus): $drasiSignalR" }
}
else { Write-Host "Drasi SignalR URL not set in Container App env." -ForegroundColor Yellow }

if ($drasiViewSvc) {
    $vsStatus = Invoke-HttpPing $drasiViewSvc
    if ($vsStatus -ge 200 -and $vsStatus -lt 500) { Write-Pass "Drasi View Service reachable ($vsStatus): $drasiViewSvc" } else { Write-Fail "Drasi View Service unreachable ($vsStatus): $drasiViewSvc" }
}
else { Write-Host "Drasi View Service URL not set in Container App env." -ForegroundColor Yellow }

# 5) Summary
Write-Section "Summary"
Write-Host "• API/Frontend URL: $apiUrl"
Write-Host "• Resource Group: $rgName"
Write-Host "• Container App: $appName"
Write-Host "• Note: Frontend is served from the same Container App (no SWA)" -ForegroundColor Yellow
Write-Host "Done." -ForegroundColor Cyan
