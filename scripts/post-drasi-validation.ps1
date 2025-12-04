<#
.SYNOPSIS
    Validates Drasi deployment configuration and captures service URLs
.DESCRIPTION
    Runs post-deployment validation to ensure Drasi services are accessible
    and automatically configures Container App environment variables
#>
param(
    [string]$Namespace = "drasi-system",
    [switch]$UpdateContainerApp = $true
)

$ErrorActionPreference = "Continue"

Write-Host "`n🔍 Drasi Post-Deployment Validation" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Gray

# 1. Check Drasi View Service LoadBalancer
Write-Host "`n1️⃣  Checking Drasi View Service..." -ForegroundColor Yellow
$viewSvc = kubectl get svc default-view-svc-public -n $Namespace -o json 2>$null
if ($viewSvc) {
    $svc = $viewSvc | ConvertFrom-Json
    $externalIp = $svc.status.loadBalancer.ingress[0].ip
    if ($externalIp) {
        $viewUrl = "http://$externalIp"
        Write-Host "   ✅ View Service IP: $externalIp" -ForegroundColor Green
        Write-Host "   URL: $viewUrl" -ForegroundColor Gray
        
        # Save to azd env
        azd env set DRASI_VIEW_SERVICE_URL $viewUrl
        Write-Host "   ✅ Saved to azd env: DRASI_VIEW_SERVICE_URL" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  External IP not assigned yet (may take a few minutes)" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ❌ View service not found in namespace $Namespace" -ForegroundColor Red
}

# 2. Check SignalR Gateway (optional)
Write-Host "`n2️⃣  Checking SignalR Gateway (optional)..." -ForegroundColor Yellow
$signalrSvcs = kubectl get svc -n $Namespace -o json 2>$null | ConvertFrom-Json |
    Select-Object -ExpandProperty items |
    Where-Object { $_.metadata.name -match "signalr.*gateway" -and $_.spec.type -eq "LoadBalancer" }

if ($signalrSvcs) {
    $signalrIp = $signalrSvcs[0].status.loadBalancer.ingress[0].ip
    if ($signalrIp) {
        $signalrUrl = "http://$signalrIp"
        Write-Host "   ✅ SignalR Gateway IP: $signalrIp" -ForegroundColor Green
        azd env set DRASI_SIGNALR_URL $signalrUrl
        Write-Host "   ✅ Saved to azd env: DRASI_SIGNALR_URL" -ForegroundColor Green
    }
} else {
    Write-Host "   ℹ️  SignalR gateway not deployed (using in-process hub)" -ForegroundColor Gray
}

# 3. Update Container App if requested
if ($UpdateContainerApp) {
    Write-Host "`n3️⃣  Updating Container App with Drasi URLs..." -ForegroundColor Yellow
    
    $envName = azd env get-value AZURE_ENV_NAME 2>$null
    $rg = azd env get-value AZURE_RESOURCE_GROUP 2>$null
    
    if ($envName -and $rg) {
        $appName = "santaworkshop-$envName-api"
        $viewUrl = azd env get-value DRASI_VIEW_SERVICE_URL 2>$null
        $signalrUrl = azd env get-value DRASI_SIGNALR_URL 2>$null
        
        $envVars = @()
        if ($viewUrl) { $envVars += "DRASI_VIEW_SERVICE_BASE_URL=$viewUrl" }
        if ($signalrUrl) { $envVars += "DRASI_SIGNALR_BASE_URL=$signalrUrl" }
        
        if ($envVars.Count -gt 0) {
            Write-Host "   Updating $appName..." -ForegroundColor Gray
            az containerapp update -n $appName -g $rg --set-env-vars $envVars --output none 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "   ✅ Container App updated successfully" -ForegroundColor Green
            } else {
                Write-Host "   ⚠️  Update failed - run: azd deploy api" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "   ⚠️  Environment not configured, skipping" -ForegroundColor Yellow
    }
}

Write-Host "`n" + ("=" * 60) -ForegroundColor Gray
Write-Host "✅ Validation complete!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "  1. Verify API: azd deploy api (if Container App wasn't updated)" -ForegroundColor Gray
Write-Host "  2. Test site: Open https://<your-containerapp>.azurecontainerapps.io/" -ForegroundColor Gray
Write-Host "`n📝 Note: Frontend is now served from the same Container App as the API." -ForegroundColor Yellow
