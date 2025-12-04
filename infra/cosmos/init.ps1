param(
    [string]$SubscriptionId,
    [string]$ResourceGroup,
    [string]$AccountName,
    [string]$Location = "eastus",
    [string]$DatabaseName = "elves_demo",
    [switch]$UseEmulator
)

$ErrorActionPreference = 'Stop'

if ($UseEmulator) {
    Write-Host "Using Cosmos DB Emulator: the application will auto-create database/containers on startup if COSMOS_CONNECTION_STRING is set." -ForegroundColor Cyan
    Write-Host "No direct CLI is available to create emulator containers here. Start the app once with the emulator connection string to initialize." -ForegroundColor Yellow
    return
}

if (-not $SubscriptionId -or -not $ResourceGroup -or -not $AccountName) {
    Write-Error "SubscriptionId, ResourceGroup, and AccountName are required unless -UseEmulator is specified."
    exit 1
}

Write-Host "Setting subscription $SubscriptionId" -ForegroundColor Cyan
az account set --subscription $SubscriptionId | Out-Null

Write-Host "Ensuring resource group $ResourceGroup in $Location" -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location | Out-Null

Write-Host "Ensuring Cosmos DB account $AccountName" -ForegroundColor Cyan
az cosmosdb create --name $AccountName --resource-group $ResourceGroup --locations regionName=$Location failoverPriority=0 isZoneRedundant=false | Out-Null

Write-Host "Ensuring SQL database $DatabaseName" -ForegroundColor Cyan
az cosmosdb sql database create --account-name $AccountName --name $DatabaseName --resource-group $ResourceGroup | Out-Null

$containers = @(
    # Core domain containers (all partitioned by childId)
    @{ name = 'children';         pk = '/childId' },
    @{ name = 'wishlists';        pk = '/childId' },
    @{ name = 'profiles';         pk = '/childId' },
    @{ name = 'recommendations';  pk = '/childId'; ttl = 2592000 }, # 30 days retention
    @{ name = 'assessments';      pk = '/childId'; ttl = 2592000 }, # 30 days retention
    @{ name = 'notifications';    pk = '/childId'; ttl = 2592000 }, # 30 days retention
    # Auxiliary / reliability
    @{ name = 'dlq';              pk = '/childId' }
)

foreach ($c in $containers) {
    $name = $c.name
    $pk = $c.pk
    $ttl = $c.ttl
    Write-Host "Ensuring container $name (pk $pk)" -ForegroundColor Green
    $args = @(
        'cosmosdb','sql','container','create',
        '--account-name', $AccountName,
        '--resource-group', $ResourceGroup,
        '--database-name', $DatabaseName,
        '--name', $name,
        '--partition-key-path', $pk
    )
    if ($null -ne $ttl) { $args += @('--ttl', $ttl) }
    az @args | Out-Null
}

Write-Host "Cosmos initialization complete." -ForegroundColor Green
