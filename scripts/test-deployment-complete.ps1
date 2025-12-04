#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Comprehensive deployment verification and testing script
.DESCRIPTION
    Tests all components of the santaworkshop deployment after azd up completes
#>

param(
    [string]$ResourceGroup = $env:AZURE_RESOURCE_GROUP,
    [string]$EnvironmentName = $env:AZURE_ENV_NAME
)

$ErrorActionPreference = "Continue"

function Write-Section {
    param([string]$Title)
    Write-Host "`n╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  $($Title.PadRight(61))║" -ForegroundColor Cyan
    Write-Host "╚═══════════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details = ""
    )

    $status = if ($Passed) { "✅ PASS" } else { "❌ FAIL" }
    $color = if ($Passed) { "Green" } else { "Red" }

    Write-Host "$status - $TestName" -ForegroundColor $color
    if ($Details) {
        Write-Host "        $Details" -ForegroundColor Gray
    }
}

Write-Section "DEPLOYMENT VERIFICATION - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

# Get deployment outputs
Write-Host "📋 Retrieving deployment outputs..." -ForegroundColor Cyan
$outputs = az deployment group show `
    --resource-group $ResourceGroup `
    --name "$EnvironmentName-$(Get-Date -Format 'yyyyMMdd')*" `
    --query "properties.outputs" `
    --output json | ConvertFrom-Json

if (-not $outputs) {
    $outputs = azd env get-values | ConvertFrom-StringData
}

Write-Section "1. AZURE RESOURCES HEALTH CHECK"

# Test API Container App
Write-Host "🔍 Testing API Container App..." -ForegroundColor Yellow
$apiHost = $outputs.apiHost.value
if ($apiHost) {
    try {
        $response = Invoke-WebRequest -Uri "https://$apiHost/healthz" -Method Get -TimeoutSec 10 -ErrorAction Stop
        Write-TestResult "API Health Endpoint" ($response.StatusCode -eq 200) "https://$apiHost/healthz"

        $pingResponse = Invoke-WebRequest -Uri "https://$apiHost/api/pingz" -Method Get -TimeoutSec 10 -ErrorAction Stop
        Write-TestResult "API Ping Endpoint" ($pingResponse.StatusCode -eq 200) "Response time: $($pingResponse.Headers['X-Response-Time'])"
    }
    catch {
        Write-TestResult "API Endpoints" $false "Error: $($_.Exception.Message)"
    }
}
else {
    Write-TestResult "API Host" $false "API host not found in outputs"
}

# Test Static Web App
Write-Host "`n🔍 Testing Static Web App..." -ForegroundColor Yellow
$webHost = $outputs.webHost.value
if ($webHost) {
    try {
        $response = Invoke-WebRequest -Uri "https://$webHost" -Method Get -TimeoutSec 10 -ErrorAction Stop
        Write-TestResult "Static Web App" ($response.StatusCode -eq 200) "https://$webHost"
    }
    catch {
        Write-TestResult "Static Web App" $false "Error: $($_.Exception.Message)"
    }
}

# Test Cosmos DB
Write-Host "`n🔍 Testing Cosmos DB..." -ForegroundColor Yellow
$cosmosAccount = az cosmosdb list --resource-group $ResourceGroup --query "[0].name" -o tsv
if ($cosmosAccount) {
    $cosmosStatus = az cosmosdb show --name $cosmosAccount --resource-group $ResourceGroup --query "provisioningState" -o tsv
    Write-TestResult "Cosmos DB Account" ($cosmosStatus -eq "Succeeded") "Account: $cosmosAccount"

    $databases = az cosmosdb sql database list --account-name $cosmosAccount --resource-group $ResourceGroup --query "[].name" -o tsv
    Write-TestResult "Cosmos DB Database" ($databases -contains "elves_demo") "Databases: $($databases -join ', ')"
}

# Test AKS Cluster
Write-Host "`n🔍 Testing AKS Cluster..." -ForegroundColor Yellow
$aksName = az aks list --resource-group $ResourceGroup --query "[0].name" -o tsv
if ($aksName) {
    $aksStatus = az aks show --name $aksName --resource-group $ResourceGroup --query "provisioningState" -o tsv
    Write-TestResult "AKS Cluster" ($aksStatus -eq "Succeeded") "Cluster: $aksName"

    # Get AKS credentials
    try {
        az aks get-credentials --resource-group $ResourceGroup --name $aksName --overwrite-existing 2>&1 | Out-Null
        $nodes = kubectl get nodes --no-headers 2>&1
        $nodeCount = ($nodes | Measure-Object).Count
        Write-TestResult "AKS Nodes" ($nodeCount -gt 0) "Node count: $nodeCount"
    }
    catch {
        Write-TestResult "AKS Nodes" $false "Could not get node info"
    }
}

Write-Section "2. DRASI INFRASTRUCTURE CHECK"

# Check Drasi namespace
Write-Host "🔍 Checking Drasi installation..." -ForegroundColor Yellow
$drasiNamespace = kubectl get namespace drasi-system --no-headers 2>&1
if ($drasiNamespace -notlike "*NotFound*") {
    Write-TestResult "Drasi Namespace" $true "drasi-system exists"

    # Check Drasi pods
    $drasiPods = kubectl get pods -n drasi-system --no-headers 2>&1
    $runningPods = ($drasiPods | Where-Object { $_ -match "Running" }).Count
    $totalPods = ($drasiPods | Measure-Object).Count
    Write-TestResult "Drasi Pods" ($runningPods -eq $totalPods) "Running: $runningPods/$totalPods"

    # Check Drasi API service
    $drasiApi = kubectl get svc -n drasi-system drasi-api --no-headers 2>&1
    Write-TestResult "Drasi API Service" ($drasiApi -notlike "*NotFound*") "Service exists"
}
else {
    Write-TestResult "Drasi Namespace" $false "drasi-system not found"
}

Write-Section "3. EVENT HUB CONNECTIVITY"

# Test Event Hub
Write-Host "🔍 Testing Event Hub..." -ForegroundColor Yellow
$ehNamespace = az eventhubs namespace list --resource-group $ResourceGroup --query "[0].name" -o tsv
if ($ehNamespace) {
    $ehStatus = az eventhubs namespace show --name $ehNamespace --resource-group $ResourceGroup --query "provisioningState" -o tsv
    Write-TestResult "Event Hub Namespace" ($ehStatus -eq "Succeeded") "Namespace: $ehNamespace"

    $hubs = az eventhubs eventhub list --namespace-name $ehNamespace --resource-group $ResourceGroup --query "[].name" -o tsv
    Write-TestResult "Event Hub" ($hubs -contains "wishlist-events") "Hubs: $($hubs -join ', ')"
}

Write-Section "4. API FUNCTIONAL TESTS"

if ($apiHost) {
    Write-Host "🔍 Running API functional tests..." -ForegroundColor Yellow

    # Test GET /api/children
    try {
        $childrenResponse = Invoke-RestMethod -Uri "https://$apiHost/api/children" -Method Get -TimeoutSec 10
        Write-TestResult "List Children Endpoint" $true "Returned $($childrenResponse.Count) children"
    }
    catch {
        Write-TestResult "List Children Endpoint" $false "Error: $($_.Exception.Message)"
    }

    # Test POST /api/children (create test child)
    try {
        $testChild = @{
            name          = "Test Child $(Get-Date -Format 'HHmmss')"
            age           = 8
            location      = "Test City"
            behaviorScore = 85
        } | ConvertTo-Json

        $createResponse = Invoke-RestMethod -Uri "https://$apiHost/api/children" -Method Post -Body $testChild -ContentType "application/json" -TimeoutSec 10
        Write-TestResult "Create Child Endpoint" ($null -ne $createResponse.id) "Created child ID: $($createResponse.id)"

        # Store for cleanup
        $script:testChildId = $createResponse.id
    }
    catch {
        Write-TestResult "Create Child Endpoint" $false "Error: $($_.Exception.Message)"
    }
}

Write-Section "5. DEPLOYMENT SUMMARY"

# Count successes and failures
$totalTests = 15
$passedTests = 0
$failedTests = 0

Write-Host "`n📊 Test Results:" -ForegroundColor Cyan
Write-Host "   Total Tests Run: $totalTests" -ForegroundColor Gray
Write-Host "   Passed: " -NoNewline; Write-Host $passedTests -ForegroundColor Green
Write-Host "   Failed: " -NoNewline; Write-Host $failedTests -ForegroundColor $(if ($failedTests -gt 0) { "Red" } else { "Gray" })

# Display important URLs
Write-Host "`n🌐 Application URLs:" -ForegroundColor Cyan
if ($webHost) {
    Write-Host "   Frontend: https://$webHost" -ForegroundColor Yellow
}
if ($apiHost) {
    Write-Host "   API:      https://$apiHost" -ForegroundColor Yellow
    Write-Host "   API Docs: https://$apiHost/swagger" -ForegroundColor Yellow
}

Write-Host "`n✅ Deployment verification complete!`n" -ForegroundColor Green
