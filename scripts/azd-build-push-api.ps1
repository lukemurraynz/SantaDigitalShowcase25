param(
    [string]$EnvName = $(azd env get-value AZURE_ENV_NAME 2>$null),
    [string]$ResourceGroup = $(azd env get-value AZURE_RESOURCE_GROUP 2>$null),
    [string]$AcrServer = $(azd env get-value AZURE_CONTAINER_REGISTRY_ENDPOINT 2>$null)
)

if (-not $EnvName) { $EnvName = "dev" }
if (-not $ResourceGroup) { $ResourceGroup = "santadigitalshowcase-$EnvName-rg" }

Write-Host "🔧 Fallback: local docker build & push for API" -ForegroundColor Yellow
if (-not $AcrServer) {
    $AcrServer = $(azd env get-value containerRegistryLoginServer 2>$null)
}
if (-not $AcrServer) {
    Write-Error "ACR server not found in azd env. Aborting."; exit 1
}

$repo = "$AcrServer/santadigitalshowcase/api-$EnvName"
$tag = (Get-Date -Format "yyyyMMdd-HHmmss")
$image = "${repo}:$tag"

Write-Host "Logging into ACR: $AcrServer" -ForegroundColor Cyan
az acr login --name ($AcrServer.Split('.')[0]) | Out-Null

Write-Host "Building image: $image" -ForegroundColor Cyan
$build = docker build -t $image -f Dockerfile.multi . 2>&1
if ($LASTEXITCODE -ne 0) { Write-Error "Docker build failed"; Write-Host $build; exit 1 }

Write-Host "Pushing image: $image" -ForegroundColor Cyan
$push = docker push $image 2>&1
if ($LASTEXITCODE -ne 0) { Write-Error "Docker push failed"; Write-Host $push; exit 1 }

$appName = "santadigitalshowcase-$EnvName-api"
Write-Host "Updating Container App $appName to image $image" -ForegroundColor Cyan
az containerapp update -n $appName -g $ResourceGroup --image $image | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Container App updated to $image" -ForegroundColor Green
}
else {
    Write-Warning "Container App update failed. Please update manually: az containerapp update -n $appName -g $ResourceGroup --image $image"
}
