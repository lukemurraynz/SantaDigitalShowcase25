<#
.SYNOPSIS
Demo Readiness Validation Script - Tests all components for Santa's Workshop demo

.DESCRIPTION
Validates infrastructure, Drasi, Agent Framework, and API endpoints
Run this after 'azd up' or before a demo to ensure everything is working correctly

.PARAMETER ApiUrl
The base URL of the API (Container App). If not provided, will attempt to get from azd environment.
Note: Frontend is now served from the same Container App.

.PARAMETER ResourceGroup
Azure resource group name. If not provided, will attempt to get from azd environment.

.PARAMETER SkipDrasi
Skip Drasi-specific tests (useful if Drasi is not deployed yet)

.PARAMETER Verbose
Show detailed output for all tests

.EXAMPLE
.\test-demo-readiness.ps1

.EXAMPLE
.\test-demo-readiness.ps1 -ApiUrl "https://your-api.azurecontainerapps.io"

.EXAMPLE
.\test-demo-readiness.ps1 -ResourceGroup "rg-myenv" -Verbose
#>

param(
    [string]$ApiUrl = "http://localhost:8080",
    [switch]$SkipDrasi,
    [switch]$Verbose
)

$ErrorActionPreference = 'Continue'
$script:passCount = 0
$script:failCount = 0
$script:warnCount = 0

function Write-TestResult {
    param([string]$Test, [string]$Status, [string]$Message = "")
    $color = switch ($Status) {
        "PASS" { "Green"; $script:passCount++ }
        "FAIL" { "Red"; $script:failCount++ }
        "WARN" { "Yellow"; $script:warnCount++ }
        "INFO" { "Cyan" }
    }
    $symbol = switch ($Status) {
        "PASS" { "PASS" }
        "FAIL" { "FAIL" }
        "WARN" { "WARN" }
        "INFO" { "INFO" }
    }
    Write-Host "$symbol [$Status] $Test" -ForegroundColor $color
    if ($Message -and ($Verbose -or $Status -eq "FAIL")) {
        Write-Host "  -> $Message" -ForegroundColor DarkGray
    }
}

Write-Host "`nSanta's Workshop - Demo Readiness Check" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor DarkGray

# ===== SECTION 1: Infrastructure =====
Write-Host "`n[1/6] Infrastructure Components" -ForegroundColor Yellow

# Check kubectl
try {
    $null = kubectl version --client=true 2>&1
    Write-TestResult "kubectl installed" "PASS"
}
catch {
    Write-TestResult "kubectl installed" "FAIL" "kubectl not found in PATH"
}

# Check AKS connectivity
try {
    $nodes = kubectl get nodes --no-headers 2>&1
    if ($nodes -match "NotFound|error|Unable") {
        Write-TestResult "AKS cluster accessible" "WARN" "No cluster context configured"
    }
    else {
        $nodeCount = ($nodes | Measure-Object).Count
        Write-TestResult "AKS cluster accessible" "PASS" "$nodeCount node(s) ready"
    }
}
catch {
    Write-TestResult "AKS cluster accessible" "WARN" "kubectl context not set"
}

# Check Drasi CLI
if (-not $SkipDrasi) {
    try {
        $drasiVersion = drasi version 2>&1
        if ($drasiVersion -match "0\.10") {
            Write-TestResult "Drasi CLI installed" "PASS" ($drasiVersion -split "`n")[0]
        }
        else {
            Write-TestResult "Drasi CLI installed" "WARN" "Version may be outdated"
        }
    }
    catch {
        Write-TestResult "Drasi CLI installed" "FAIL" "drasi command not found"
    }
}

# Check azd
try {
    $azdVersion = azd version 2>&1
    Write-TestResult "Azure Developer CLI (azd)" "PASS" "Version: $($azdVersion -replace '.*azd version ', '')"
}
catch {
    Write-TestResult "Azure Developer CLI (azd)" "WARN" "azd not installed (optional)"
}

# ===== SECTION 1.5: Deployment Configuration Validation =====
Write-Host "`n[1.5/6] Deployment Configuration" -ForegroundColor Yellow

# Try to get resource group from azd environment
$resourceGroup = ""
try {
    $resourceGroup = azd env get-value AZURE_RESOURCE_GROUP 2>$null
    if ($resourceGroup) {
        Write-TestResult "Azure Resource Group detected" "PASS" "$resourceGroup"
    }
}
catch {
    Write-TestResult "Azure Resource Group detection" "INFO" "Using local development mode"
}

# If we have a resource group, validate Container App configuration
if ($resourceGroup) {
    # Get Container App name from azd or construct it
    $envName = azd env get-value AZURE_ENV_NAME 2>$null
    if ($envName) {
        $containerAppName = "santaworkshop-$envName-api"

        # Check if Container App exists and validate configuration
        try {
            $appConfig = az containerapp show -n $containerAppName -g $resourceGroup --query "{image:properties.template.containers[0].image, authEnabled:properties.configuration.ingress.external}" -o json 2>$null | ConvertFrom-Json

            if ($appConfig) {
                # Check if using bootstrap image
                if ($appConfig.image -match "quickstart") {
                    Write-TestResult "Container App image" "WARN" "Using bootstrap image - run 'azd deploy api' to deploy actual application"
                }
                else {
                    Write-TestResult "Container App image" "PASS" "Application image deployed"
                }

                # Check auth configuration (should be disabled for RoleTrackingMiddleware)
                try {
                    $authConfig = az containerapp auth show -n $containerAppName -g $resourceGroup --query "platform.enabled" -o tsv 2>$null
                    if ($authConfig -eq "false") {
                        Write-TestResult "Container App authentication" "PASS" "Built-in auth disabled (using RoleTrackingMiddleware)"
                    }
                    elseif ($authConfig -eq "true") {
                        Write-TestResult "Container App authentication" "FAIL" "Built-in auth enabled - should be disabled. Run: az containerapp auth update -n $containerAppName -g $resourceGroup --enabled false"
                    }
                }
                catch {
                    Write-TestResult "Container App authentication" "INFO" "Could not check auth config"
                }
            }
        }
        catch {
            Write-TestResult "Container App configuration" "INFO" "Could not validate Container App"
        }

        # Validate Cosmos DB RBAC role assignments
        try {
            $cosmosName = az cosmosdb list -g $resourceGroup --query "[0].name" -o tsv 2>$null
            if ($cosmosName) {
                # Check for Cosmos DB role assignments
                $roleAssignments = az cosmosdb sql role assignment list --account-name $cosmosName -g $resourceGroup 2>$null | ConvertFrom-Json

                $hasContributorRole = $roleAssignments | Where-Object {
                    $_.roleDefinitionId -match "00000000-0000-0000-0000-000000000002"
                }

                if ($hasContributorRole) {
                    Write-TestResult "Cosmos DB RBAC (Data Contributor)" "PASS" "Built-in Data Contributor role assigned"
                }
                else {
                    $hasReaderRole = $roleAssignments | Where-Object {
                        $_.roleDefinitionId -match "00000000-0000-0000-0000-000000000001"
                    }
                    if ($hasReaderRole) {
                        Write-TestResult "Cosmos DB RBAC (Data Contributor)" "FAIL" "Using Data Reader (0001) instead of Data Contributor (0002) - change feed processors will fail!"
                    }
                    else {
                        Write-TestResult "Cosmos DB RBAC (Data Contributor)" "WARN" "No built-in role assignments found"
                    }
                }
            }
        }
        catch {
            Write-TestResult "Cosmos DB RBAC validation" "INFO" "Could not validate Cosmos RBAC"
        }
    }
}

# ===== SECTION 2: Drasi Components =====
if (-not $SkipDrasi) {
    Write-Host "`n[2/6] Drasi Real-Time Detection" -ForegroundColor Yellow

    # Check Drasi namespace
    try {
        $drasiPods = kubectl get pods -n drasi-system --no-headers 2>&1
        if ($drasiPods -match "No resources found|NotFound") {
            Write-TestResult "Drasi namespace exists" "FAIL" "drasi-system namespace not found"
        }
        else {
            $runningPods = ($drasiPods | Select-String "Running").Count
            $totalPods = ($drasiPods | Measure-Object).Count
            Write-TestResult "Drasi namespace exists" "PASS" "$runningPods/$totalPods pods running"
        }
    }
    catch {
        Write-TestResult "Drasi namespace exists" "FAIL" "Cannot query drasi-system"
    }

    # Check Drasi sources
    try {
        $sources = drasi list source 2>&1
        if ($sources -match "wishlist-eh") {
            $available = $sources -match "Available.*true"
            if ($available) {
                Write-TestResult "Drasi EventHub source" "PASS" "wishlist-eh available"
            }
            else {
                Write-TestResult "Drasi EventHub source" "WARN" "wishlist-eh not available"
            }
        }
        else {
            Write-TestResult "Drasi EventHub source" "FAIL" "wishlist-eh not found"
        }
    }
    catch {
        Write-TestResult "Drasi EventHub source" "WARN" "Cannot list sources"
    }

    # Check continuous queries
    try {
        $queries = drasi list query 2>&1
        if ($queries -match "wishlist-updates") {
            $running = $queries -match "Running"
            if ($running) {
                Write-TestResult "Drasi continuous queries" "PASS" "wishlist-updates running"
            }
            else {
                Write-TestResult "Drasi continuous queries" "WARN" "wishlist-updates not running"
            }
        }
        else {
            Write-TestResult "Drasi continuous queries" "FAIL" "wishlist-updates not found"
        }
    }
    catch {
        Write-TestResult "Drasi continuous queries" "WARN" "Cannot list queries"
    }
}
else {
    Write-Host "`n[2/6] Drasi Real-Time Detection - SKIPPED" -ForegroundColor DarkGray
}

# ===== SECTION 3: Backend API =====
Write-Host "`n[3/6] Backend API (Agent Framework)" -ForegroundColor Yellow

# Health check
try {
    $health = Invoke-WebRequest "$ApiUrl/healthz" -UseBasicParsing -TimeoutSec 5
    if ($health.StatusCode -eq 200) {
        Write-TestResult "API health endpoint" "PASS" "$ApiUrl/healthz → 200"
    }
    else {
        Write-TestResult "API health endpoint" "FAIL" "Status: $($health.StatusCode)"
    }
}
catch {
    Write-TestResult "API health endpoint" "FAIL" "Cannot reach $ApiUrl/healthz"
}

# Readiness check
try {
    $ready = Invoke-WebRequest "$ApiUrl/readyz" -UseBasicParsing -TimeoutSec 5
    if ($ready.StatusCode -eq 200) {
        Write-TestResult "API readiness endpoint" "PASS" "Service ready"
    }
    else {
        Write-TestResult "API readiness endpoint" "WARN" "Status: $($ready.StatusCode)"
    }
}
catch {
    Write-TestResult "API readiness endpoint" "WARN" "Cannot reach $ApiUrl/readyz"
}

# Diagnostics endpoint
try {
    $ping = Invoke-RestMethod "$ApiUrl/api/pingz" -TimeoutSec 5
    $cosmosReady = $ping.cosmosReady
    if ($cosmosReady) {
        Write-TestResult "Cosmos DB connectivity" "PASS" "cosmosReady: true"
    }
    else {
        Write-TestResult "Cosmos DB connectivity" "WARN" "cosmosReady: false"
    }
}
catch {
    Write-TestResult "Cosmos DB connectivity" "WARN" "Cannot reach /api/pingz"
}

# Check change feed processor status (if we have resource group)
if ($resourceGroup -and $containerAppName) {
    try {
        # Get recent Container App logs to check for change feed processor status
        $logQuery = "ContainerAppConsoleLogs_CL | where ContainerAppName_s == '$containerAppName' | where Log_s contains 'change feed' | project TimeGenerated, Log_s | order by TimeGenerated desc | limit 10"

        # Try to get workspace ID
        $workspaceId = az monitor log-analytics workspace list -g $resourceGroup --query "[0].customerId" -o tsv 2>$null

        if ($workspaceId) {
            $logResults = az monitor log-analytics query -w $workspaceId --analytics-query $logQuery -o tsv 2>$null

            if ($logResults -match "started for wishlist" -and $logResults -match "started for recommendation") {
                Write-TestResult "Cosmos change feed processors" "PASS" "Both processors started successfully"
            }
            elseif ($logResults -match "Failed to start.*change feed") {
                Write-TestResult "Cosmos change feed processors" "FAIL" "Change feed processors failed - check Cosmos RBAC permissions (need Data Contributor 0002)"
            }
            elseif ($logResults) {
                Write-TestResult "Cosmos change feed processors" "WARN" "Partial change feed processor logs found"
            }
            else {
                Write-TestResult "Cosmos change feed processors" "INFO" "No recent change feed logs (may not have deployed yet)"
            }
        }
    }
    catch {
        Write-TestResult "Cosmos change feed processors" "INFO" "Could not check change feed processor logs"
    }
}

# ===== SECTION 4: Agent Framework Features =====
Write-Host "`n[4/6] Microsoft Agent Framework Features" -ForegroundColor Yellow

# Agent tools endpoint
try {
    $tools = Invoke-RestMethod "$ApiUrl/api/v1/agent-tools" -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 5
    $toolCount = ($tools | Measure-Object).Count
    if ($toolCount -ge 6) {
        Write-TestResult "Agent tools registered" "PASS" "$toolCount tools available"
    }
    elseif ($toolCount -gt 0) {
        Write-TestResult "Agent tools registered" "WARN" "Only $toolCount tools (expected 6)"
    }
    else {
        Write-TestResult "Agent tools registered" "FAIL" "No tools registered"
    }
}
catch {
    Write-TestResult "Agent tools registered" "FAIL" "Cannot reach /api/v1/agent-tools"
}

# Test recommendation endpoint
try {
    $testChild = "demo-test-$(Get-Random -Max 9999)"
    $rec = Invoke-RestMethod "$ApiUrl/api/v1/children/$testChild/recommendations" `
        -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 15 -ErrorAction Stop

    if ($rec.items -or $rec.suggestion) {
        Write-TestResult "Recommendation generation" "PASS" "Agent Framework responding"
    }
    else {
        Write-TestResult "Recommendation generation" "WARN" "Empty response"
    }
}
catch {
    $errMsg = $_.Exception.Message
    if ($errMsg -match "404") {
        Write-TestResult "Recommendation generation" "PASS" "Endpoint exists (404 expected for missing child)"
    }
    elseif ($errMsg -match "timeout") {
        Write-TestResult "Recommendation generation" "WARN" "Response timeout (may be slow)"
    }
    else {
        Write-TestResult "Recommendation generation" "FAIL" $errMsg
    }
}

# Test streaming endpoint
try {
    $streamTest = Start-Job -ScriptBlock {
        param($url)
        $resp = Invoke-WebRequest "$url/api/v1/children/test-stream/recommendations/stream?status=Nice" `
            -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
        return $resp.StatusCode
    } -ArgumentList $ApiUrl

    Wait-Job $streamTest -Timeout 5 | Out-Null
    $streamResult = Receive-Job $streamTest -ErrorAction SilentlyContinue

    if ($streamResult -eq 200 -or $streamTest.State -eq "Running") {
        Write-TestResult "Streaming SSE endpoint" "PASS" "Streaming capability available"
        Stop-Job $streamTest -ErrorAction SilentlyContinue | Out-Null
    }
    else {
        Write-TestResult "Streaming SSE endpoint" "WARN" "Streaming may not be working"
    }
    Remove-Job $streamTest -Force -ErrorAction SilentlyContinue
}
catch {
    Write-TestResult "Streaming SSE endpoint" "WARN" "Cannot test streaming"
}

# ===== SECTION 5: Frontend =====
# Note: Frontend is now served from the same Container App as the API (same-origin)
Write-Host "`n[5/6] Frontend Dashboard" -ForegroundColor Yellow

if ($ApiUrl) {
    try {
        # Frontend is served from the API Container App root
        $fe = Invoke-WebRequest $ApiUrl -UseBasicParsing -TimeoutSec 5
        if ($fe.StatusCode -eq 200) {
            $hasVite = $fe.Content -match "vite|react"
            if ($hasVite) {
                Write-TestResult "Frontend accessible" "PASS" "$ApiUrl → 200 (served from Container App)"
            }
            else {
                Write-TestResult "Frontend accessible" "WARN" "Response doesn't look like Vite app"
            }
        }
    }
    catch {
        Write-TestResult "Frontend accessible" "FAIL" "Cannot reach $ApiUrl"
    }

    # Check for React/Vite bundle indicators
    if ($fe.Content -match "react|vite|src/main") {
        Write-TestResult "Frontend bundle" "PASS" "React/Vite bundle detected"
    }
    else {
        Write-TestResult "Frontend bundle" "WARN" "React/Vite bundle not detected in HTML"
    }
}
else {
    Write-TestResult "Frontend accessible" "INFO" "API URL not provided (-ApiUrl)"
}

# ===== SECTION 5.5: Interoperability Smoke (Container App → Drasi) =====
Write-Host "`n[5.5/6] Interoperability Smoke Test" -ForegroundColor Yellow
try {
    $interopScript = Join-Path (Split-Path $PSScriptRoot) "scripts\test-interoperability.ps1"
    if (Test-Path $interopScript) {
        Write-Host "Running test-interoperability.ps1..." -ForegroundColor DarkGray
        pwsh -File $interopScript | Write-Host
        Write-TestResult "Interoperability script executed" "PASS" "See output above"
    }
    else {
        Write-TestResult "Interoperability script available" "WARN" "scripts/test-interoperability.ps1 not found"
    }
}
catch {
    Write-TestResult "Interoperability script execution" "WARN" "Failed to run test-interoperability.ps1"
}

# ===== SECTION 6: Demo Scripts =====
Write-Host "`n[6/6] Demo Scripts Availability" -ForegroundColor Yellow

$scripts = @(
    "simulate.ps1",
    "simulate-naughty-nice.ps1",
    "send-wishlist-event.ps1",
    "test-us1.ps1"
)

foreach ($script in $scripts) {
    $path = Join-Path (Split-Path $PSScriptRoot) "scripts\$script"
    if (Test-Path $path) {
        Write-TestResult "Script: $script" "PASS" "Available"
    }
    else {
        Write-TestResult "Script: $script" "WARN" "Not found at $path"
    }
}

# Check demo guide
$demoGuide = Join-Path (Split-Path $PSScriptRoot) "DEMO-GUIDE.md"
if (Test-Path $demoGuide) {
    Write-TestResult "Demo guide documentation" "PASS" "DEMO-GUIDE.md exists"
}
else {
    Write-TestResult "Demo guide documentation" "WARN" "DEMO-GUIDE.md not found"
}

# ===== SUMMARY =====
Write-Host "`n" + ("=" * 60) -ForegroundColor DarkGray
Write-Host "Demo Readiness Summary" -ForegroundColor Cyan
Write-Host "  Passed:   $script:passCount" -ForegroundColor Green
Write-Host "  Warnings: $script:warnCount" -ForegroundColor Yellow
Write-Host "  Failed:   $script:failCount" -ForegroundColor Red

$totalTests = $script:passCount + $script:warnCount + $script:failCount
$readiness = [math]::Round(($script:passCount / $totalTests) * 100, 1)

Write-Host "`nOverall Readiness: $readiness%" -ForegroundColor $(
    if ($readiness -ge 80) { "Green" }
    elseif ($readiness -ge 60) { "Yellow" }
    else { "Red" }
)

if ($script:failCount -eq 0 -and $script:warnCount -le 3) {
    Write-Host "`nREADY FOR DEMO! All critical components operational." -ForegroundColor Green
}
elseif ($script:failCount -eq 0) {
    Write-Host "`nMOSTLY READY - Some warnings, but demo should work." -ForegroundColor Yellow
}
else {
    Write-Host "`nNOT READY - Critical failures detected. Check logs above." -ForegroundColor Red
}

Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "  1. Review any failures or warnings above" -ForegroundColor White
Write-Host "  2. Run: Get-Content DEMO-GUIDE.md" -ForegroundColor White
Write-Host "  3. Test a scenario: .\scripts\simulate.ps1 -Url $ApiUrl" -ForegroundColor White

exit $(if ($script:failCount -eq 0) { 0 } else { 1 })
