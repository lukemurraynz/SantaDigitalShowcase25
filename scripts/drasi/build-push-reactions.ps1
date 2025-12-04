<#
.SYNOPSIS
    Build and push Drasi reaction & source provider images to the container registry.

.DESCRIPTION
    Uses the selected azd environment to discover AZURE_CONTAINER_REGISTRY_ENDPOINT,
    or accepts explicit Version/Registry parameters. Reads image list from image-tags.json
    in the repository root.

    Builds each reaction/provider image with a version tag (default from image-tags.json
    or override via -Version).

    Pushes images and outputs a manifest patch suggestion.

.PARAMETER Version
    Optional semantic version tag to use (e.g., 'v0.1.0'). If not provided, reads from image-tags.json.

.PARAMETER Registry
    Optional container registry endpoint. If not provided, attempts to read from azd environment
    (AZURE_CONTAINER_REGISTRY_ENDPOINT or containerRegistryEndpoint).

.EXAMPLE
    # Auto-discover settings from azd environment and image-tags.json
    pwsh ./scripts/drasi/build-push-reactions.ps1

.EXAMPLE
    # Explicit version and registry
    pwsh ./scripts/drasi/build-push-reactions.ps1 -Version v0.1.1 -Registry myregistry.azurecr.io

.NOTES
    - Uses 'drasi apply' (not kubectl) for Drasi resources.
    - Build contexts are expected at drasi/sources/<image-name>/ with a Dockerfile.
#>
[CmdletBinding()]
param(
    [Parameter(HelpMessage = "Semantic version tag for images (e.g., v0.1.0)")]
    [string]$Version,

    [Parameter(HelpMessage = "Container registry endpoint (e.g., myregistry.azurecr.io)")]
    [string]$Registry
)

$ErrorActionPreference = 'Stop'

# Resolve repository root (this script is in scripts/drasi/)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

# Load image-tags.json for image list and default values
$tagsFile = Join-Path $repoRoot 'image-tags.json'
if (Test-Path $tagsFile) {
    $json = Get-Content $tagsFile -Raw | ConvertFrom-Json
    $images = $json.images
    if (-not $Version) { $Version = $json.version }
}
else {
    Write-Warning "image-tags.json not found at $tagsFile"
    $images = @()
}

# Resolve registry from azd environment if not provided
if (-not $Registry) {
    Write-Host "🔍 Resolving ACR endpoint from azd environment..." -ForegroundColor Cyan
    $Registry = azd env get-value AZURE_CONTAINER_REGISTRY_ENDPOINT 2>$null
    if (-not $Registry) { $Registry = azd env get-value containerRegistryEndpoint 2>$null }
    if (-not $Registry) { $Registry = azd env get-value containerRegistryLoginServer 2>$null }
}

# Validate required parameters
if (-not $Registry) {
    throw "Registry not found. Provide -Registry parameter or set AZURE_CONTAINER_REGISTRY_ENDPOINT in azd environment."
}
if (-not $Version) {
    $Version = 'v0.1.0'
    Write-Warning "Version not specified; using default: $Version"
}
if (-not $images -or $images.Count -eq 0) {
    Write-Warning "No images defined to build."
    exit 0
}

Write-Host "✅ Registry: $Registry" -ForegroundColor Green
Write-Host "✅ Version: $Version" -ForegroundColor Green
Write-Host "Building $($images.Count) image(s)..." -ForegroundColor Cyan

# Detect build contexts: expect folder under drasi/sources/<name>
$srcRoot = Join-Path $repoRoot 'drasi' 'sources'
if (-not (Test-Path $srcRoot)) {
    Write-Warning "Sources directory not found at $srcRoot. Build contexts may be missing."
}

$built = @()
$skipped = @()
$buildFailed = @()
$pushFailed = @()

foreach ($img in $images) {
    $ctx = Join-Path $srcRoot $img
    if (-not (Test-Path $ctx)) {
        Write-Warning "Build context not found for $img at $ctx (skipping)"
        $skipped += $img
        continue
    }
    $dockerfile = Join-Path $ctx 'Dockerfile'
    if (-not (Test-Path $dockerfile)) {
        Write-Warning "Dockerfile missing for $img (expected $dockerfile)"
        $skipped += $img
        continue
    }

    $fullName = "$Registry/$img`:$Version"
    Write-Host "🐳 Building $fullName" -ForegroundColor Yellow
    docker build -q -t $fullName $ctx | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Build failed for $img"
        $buildFailed += $img
        continue
    }

    Write-Host "⬆️  Pushing $fullName" -ForegroundColor Yellow
    docker push $fullName | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Push failed for $img"
        $pushFailed += $img
        continue
    }

    $built += $fullName
}

# Summary
Write-Host "`n✅ Build & push complete" -ForegroundColor Green
if ($built.Count -gt 0) {
    Write-Host "`nImages built successfully:" -ForegroundColor Cyan
    $built | ForEach-Object { Write-Host "  $_" }
}

if ($skipped.Count -gt 0) {
    Write-Host "`n⚠️  Images skipped (missing context/Dockerfile):" -ForegroundColor Yellow
    $skipped | ForEach-Object { Write-Host "  $_" }
}

if ($buildFailed.Count -gt 0) {
    Write-Host "`n❌ Images with build failures:" -ForegroundColor Red
    $buildFailed | ForEach-Object { Write-Host "  $_" }
}

if ($pushFailed.Count -gt 0) {
    Write-Host "`n❌ Images with push failures:" -ForegroundColor Red
    $pushFailed | ForEach-Object { Write-Host "  $_" }
}

Write-Host "`n📝 Suggested manifest patch (replace image fields):" -ForegroundColor Magenta
foreach ($img in $images) {
    $fullName = "$Registry/$img`:$Version"
    Write-Host "  image: $fullName" -ForegroundColor DarkGray
}

Write-Host "`nRun 'drasi apply -f drasi/resources/drasi-resources.yaml' after patching." -ForegroundColor Cyan
Write-Host "Note: Use 'drasi apply' (not kubectl) for Drasi resources." -ForegroundColor Yellow
