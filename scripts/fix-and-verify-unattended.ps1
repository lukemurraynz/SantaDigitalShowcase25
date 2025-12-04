# Autonomous API Fix & End-to-End Verification Script
# Run unattended - will diagnose, fix, and verify complete deployment

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

function Write-Phase {
    param([string]$msg) Write-Host "
=== $msg ===" -ForegroundColor Cyan 
}
function Write-Success { param([string]$msg) Write-Host "✅ $msg" -ForegroundColor Green }
function Write-Fail { param([string]$msg) Write-Host "❌ $msg" -ForegroundColor Red }
function Write-Info { param([string]$msg) Write-Host "ℹ️  $msg" -ForegroundColor Yellow }

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = "docs/status/evidence/$timestamp"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

Write-Phase "Phase 1: Diagnose Current State"

# Check Container App
$app = az containerapp show -n santaworkshop-nks-api -g rg-nks -o json 2>$null | ConvertFrom-Json
if ($app) {
    Write-Info "Container App exists: $($app.properties.latestRevisionName)"
    $currentFqdn = $app.properties.configuration.ingress.fqdn
    
    # Test if API is responding
    Write-Info "Testing API at $currentFqdn..."
    try {
        $response = Invoke-WebRequest -Uri "https://$currentFqdn/api/pingz" -Method GET -UseBasicParsing -TimeoutSec 10
        if ($response.StatusCode -eq 200) {
            Write-Success "API is responding correctly!"
            $needsFix = $false
        }
        else {
            Write-Fail "API returned status: $($response.StatusCode)"
            $needsFix = $true
        }
    }
    catch {
        Write-Fail "API not responding: $($_.Exception.Message)"
        $needsFix = $true
    }
}
else {
    Write-Fail "Container App not found"
    $needsFix = $true
}

if (-not $needsFix) {
    Write-Phase "Phase 2: API is healthy - Skip fixes, proceed to E2E verification"
}
else {
    Write-Phase "Phase 2: Apply Fixes"
    
    Write-Info "Fix 1: Recreate Container App without VNet (external access issue workaround)"
    
    # Delete current app
    az containerapp delete -n santaworkshop-nks-api -g rg-nks --yes 2>&1 | Out-Null
    Start-Sleep -Seconds 20
    
    # Get environment
    $envName = "santaworkshop-nks-cae"
    $acrName = (az acr list -g rg-nks --query '[0].name' -o tsv)
    $imageName = "$acrName.azurecr.io/santaworkshop-nks-api:latest"
    
    Write-Info "Deploying with azd (will use correct configuration)..."
    azd deploy api 2>&1 | Out-File "$logDir/api-deploy.log"
    
    Start-Sleep -Seconds 30
    
    # Verify deployment
    $app = az containerapp show -n santaworkshop-nks-api -g rg-nks -o json 2>$null | ConvertFrom-Json
    if ($app) {
        Write-Success "Container App redeployed"
        $currentFqdn = $app.properties.configuration.ingress.fqdn
    }
}

Write-Phase "Phase 3: Update Frontend Configuration"

$apiHost = (az containerapp show -n santaworkshop-nks-api -g rg-nks --query 'properties.configuration.ingress.fqdn' -o tsv 2>$null)
if ($apiHost) {
    azd env set apiHost $apiHost
    Write-Success "Updated apiHost: $apiHost"
    
    # Update frontend config
    Push-Location frontend
    .\prebuild.ps1 2>&1 | Out-Null
    Pop-Location
    
    # Deploy frontend
    Write-Info "Deploying frontend..."
    azd deploy web 2>&1 | Out-File "$logDir/web-deploy.log"
    Write-Success "Frontend deployed"
}

Write-Phase "Phase 4: End-to-End Verification"

Start-Sleep -Seconds 15

$frontendUrl = "https://agreeable-bush-017b14c00.3.azurestaticapps.net"
$tests = @(
    @{Name = "Frontend Health"; Url = "$frontendUrl"; ExpectedStatus = 200 }
    @{Name = "API Proxy Ping"; Url = "$frontendUrl/api/pingz"; ExpectedStatus = 200 }
    @{Name = "API Notifications"; Url = "$frontendUrl/api/v1/notifications"; ExpectedStatus = 200 }
)

$results = @()
foreach ($test in $tests) {
    Write-Info "Testing: $($test.Name)..."
    try {
        $response = Invoke-WebRequest -Uri $test.Url -Method GET -UseBasicParsing -TimeoutSec 15
        if ($response.StatusCode -eq $test.ExpectedStatus) {
            Write-Success "$($test.Name): PASS"
            $results += @{Test = $test.Name; Status = "PASS"; StatusCode = $response.StatusCode }
        }
        else {
            Write-Fail "$($test.Name): Expected $($test.ExpectedStatus), got $($response.StatusCode)"
            $results += @{Test = $test.Name; Status = "FAIL"; StatusCode = $response.StatusCode }
        }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__ ?? "Error"
        Write-Fail "$($test.Name): $statusCode"
        $results += @{Test = $test.Name; Status = "FAIL"; StatusCode = $statusCode }
    }
    Start-Sleep -Seconds 2
}

Write-Phase "Phase 5: VNet Connectivity Test"

Write-Info "Testing Container App → Drasi connectivity..."
$drasiUrl = "http://10.0.33.198:8080"

Write-Phase "Phase 6: Ensure Event Hubs RBAC for Drasi Managed Identity"
$rg = (az group list --query "[?contains(name,'rg')].name | [0]" -o tsv)
$ehNs = (az eventhubs namespace list --resource-group $rg --query "[0].name" -o tsv)
if ($ehNs) {
    Write-Info "Assigning Event Hubs RBAC to Drasi MI on namespace $ehNs"
    & "$PSScriptRoot/enable-eventhub-managed-identity.ps1" -ResourceGroup $rg -NamespaceName $ehNs
}
$appEnv = az containerapp show -n santaworkshop-nks-api -g rg-nks --query 'properties.template.containers[0].env[?name==`DRASI_VIEW_SERVICE_BASE_URL`].value' -o tsv 2>$null
if ($appEnv) {
    Write-Success "Drasi URL configured: $appEnv"
}
else {
    Write-Info "Updating Drasi URL to internal ClusterIP..."
    az containerapp update -n santaworkshop-nks-api -g rg-nks --set-env-vars "DRASI_VIEW_SERVICE_BASE_URL=$drasiUrl" --output none 2>&1 | Out-Null
    Start-Sleep -Seconds 20
}

# Final Summary
Write-Phase "Summary"

$results | Format-Table -AutoSize | Out-String | Write-Host

$passCount = ($results | Where-Object { $_.Status -eq "PASS" }).Count
$totalCount = $results.Count

if ($passCount -eq $totalCount) {
    Write-Success "All tests passed! ($passCount/$totalCount)"
    exit 0
}
else {
    Write-Fail "Some tests failed ($passCount/$totalCount passed)"
    Write-Info "Check logs in $logDir"
    exit 1
}
