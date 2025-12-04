<#
.SYNOPSIS
    Pre-provision cleanup script to prevent role assignment collisions.
.DESCRIPTION
    Removes stale role assignments from previous deployments that might conflict
    with new role assignments during azd provision.
.NOTES
    Called automatically by azure.yaml preprovision hook.
#>

$ErrorActionPreference = 'Continue'

Write-Host "`n=== Pre-Provision Cleanup ===" -ForegroundColor Cyan

# Get environment context
$envName = azd env get-value AZURE_ENV_NAME 2>$null
$rg = azd env get-value AZURE_RESOURCE_GROUP 2>$null

if (-not $envName) {
    Write-Host "No azd environment active - skipping cleanup" -ForegroundColor Gray
    exit 0
}

if (-not $rg) {
    Write-Host "No resource group found - assuming fresh environment" -ForegroundColor Gray
    exit 0
}

# Check if resource group exists
$rgExists = az group show -n $rg 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Resource group does not exist yet - skipping cleanup" -ForegroundColor Gray
    exit 0
}

Write-Host "Checking for stale role assignments in: $rg" -ForegroundColor Yellow

# Get all principal IDs from Container Apps in this resource group
$principalIds = az containerapp list -g $rg --query "[].identity.principalId" -o tsv 2>$null

if (-not $principalIds -or $principalIds.Count -eq 0) {
    Write-Host "No Container Apps found - skipping role assignment cleanup" -ForegroundColor Gray
    exit 0
}

$cleanedCount = 0
foreach ($principalId in $principalIds) {
    if ([string]::IsNullOrWhiteSpace($principalId)) { continue }
    
    Write-Host "  Checking principal: $principalId" -ForegroundColor Gray
    $assignments = az role assignment list --assignee $principalId --all -o json 2>$null | ConvertFrom-Json
    
    if ($assignments -and $assignments.Count -gt 0) {
        Write-Host "  Found $($assignments.Count) role assignment(s) - deleting..." -ForegroundColor Yellow
        foreach ($assignment in $assignments) {
            az role assignment delete --ids $assignment.id 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                $cleanedCount++
            }
        }
    }
}

if ($cleanedCount -gt 0) {
    Write-Host "`n✅ Cleaned up $cleanedCount stale role assignment(s)" -ForegroundColor Green
    Write-Host "   Waiting 10s for Azure RBAC cleanup propagation..." -ForegroundColor Gray
    Start-Sleep -Seconds 10
} else {
    Write-Host "✅ No stale role assignments found" -ForegroundColor Green
}

exit 0
