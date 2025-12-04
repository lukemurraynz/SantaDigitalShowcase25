# New Environment Deployment Script
# Ensures clean deployment with VNet peering for new azd environments

param(
    [Parameter(Mandatory=$false)]
    [string]$EnvironmentName,
    [string]$Location = "australiaeast"
)

$ErrorActionPreference = 'Stop'

Write-Host "
=== New Environment Deployment ===" -ForegroundColor Cyan

if (-not $EnvironmentName) {
    $EnvironmentName = Read-Host "Enter environment name"
}

Write-Host "
1. Creating azd environment..." -ForegroundColor Yellow
azd env new $EnvironmentName
azd env set AZURE_LOCATION $Location

Write-Host "
2. Provisioning infrastructure..." -ForegroundColor Yellow
azd provision

Write-Host "
3. Deploying applications..." -ForegroundColor Yellow
azd deploy

Write-Host "
4. Running verification..." -ForegroundColor Yellow
.\scripts\fix-and-verify-unattended.ps1

Write-Host "
✅ Deployment complete!" -ForegroundColor Green
