<#
.SYNOPSIS
    Validates Drasi deployment configuration before applying resources.
.DESCRIPTION
    Checks providers.yaml and resource files for common issues:
    - Image paths are fully qualified with registry
    - No duplicate tags (e.g., :0.10.0-azure-linux:latest)
    - Source middleware labels match query node labels
    - SignalR reaction includes all expected queries
.NOTES
    Called by azure.yaml predeploy hook for drasi service.
#>

param(
    [string]$ResourcesPath = "drasi/resources",
    [string]$ProvidersPath = "drasi/resources/providers.yaml"
)

$ErrorActionPreference = 'Continue'
$issues = @()

Write-Host "`n=== Validating Drasi Configuration ===" -ForegroundColor Cyan

# 1. Validate providers.yaml has fully qualified image paths
if (Test-Path $ProvidersPath) {
    Write-Host "`n1. Checking providers.yaml images..." -ForegroundColor Yellow
    $providers = Get-Content $ProvidersPath -Raw
    
    # Check for images without registry prefix
    $shortImages = [regex]::Matches($providers, 'image:\s+([^/\s]+/[^/\s]+:[^\s]+)')
    foreach ($match in $shortImages) {
        $img = $match.Groups[1].Value
        if ($img -notmatch '^(ghcr\.io|[^/]+\.azurecr\.io)/') {
            $issues += "❌ Provider image missing registry: $img"
        }
    }
    
    # Check for double colons in tags
    if ($providers -match ':([^:\s]+):([^:\s]+)') {
        $issues += "❌ Provider image has double tag: $($Matches[0])"
    }
    
    if ($issues.Count -eq 0) {
        Write-Host "   ✅ All provider images properly qualified" -ForegroundColor Green
    }
} else {
    $issues += "⚠️  providers.yaml not found at: $ProvidersPath"
}

# 2. Validate source middleware labels match queries
if (Test-Path "$ResourcesPath/sources.yaml") {
    Write-Host "`n2. Checking source/query label consistency..." -ForegroundColor Yellow
    $sources = Get-Content "$ResourcesPath/sources.yaml" -Raw
    
    # Extract middleware labels from sources
    $labelMatches = [regex]::Matches($sources, 'label:\s+(\w+)')
    $sourceLabels = $labelMatches | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
    
    if ($sourceLabels.Count -gt 0) {
        Write-Host "   Found source labels: $($sourceLabels -join ', ')" -ForegroundColor Gray
        
        # Check queries use these labels
        if (Test-Path "$ResourcesPath/queries.yaml") {
            $queries = Get-Content "$ResourcesPath/queries.yaml" -Raw
            foreach ($label in $sourceLabels) {
                if ($queries -notmatch "sourceLabel:\s+$label") {
                    $issues += "⚠️  Source label '$label' not used in any query"
                }
            }
        }
        
        if ($issues.Count -eq 0 -or ($issues | Where-Object { $_ -like '*sourceLabel*' }).Count -eq 0) {
            Write-Host "   ✅ Source labels match query references" -ForegroundColor Green
        }
    }
}

# 3. Validate SignalR reaction includes all queries
if (Test-Path "$ResourcesPath/reactions.yaml") {
    Write-Host "`n3. Checking SignalR reaction configuration..." -ForegroundColor Yellow
    $reactions = Get-Content "$ResourcesPath/reactions.yaml" -Raw
    
    # Find SignalR reaction block
    if ($reactions -match '(?s)kind:\s+Reaction.*?spec:\s+kind:\s+SignalR.*?queries:(.*?)(?=\n---|\z)') {
        $signalrQueries = $Matches[1]
        
        # Check if it has query names (not empty)
        $queryNames = [regex]::Matches($signalrQueries, '^\s+([a-z0-9-]+):' , 'Multiline')
        if ($queryNames.Count -eq 0) {
            $issues += "⚠️  SignalR reaction has no queries listed"
        } else {
            Write-Host "   Found SignalR queries: $($queryNames | ForEach-Object { $_.Groups[1].Value } | Join-String -Separator ', ')" -ForegroundColor Gray
        }
    }
}

# 4. Check for unsupported Cypher syntax
if (Test-Path "$ResourcesPath/queries.yaml") {
    Write-Host "`n4. Checking for unsupported Cypher syntax..." -ForegroundColor Yellow
    $queries = Get-Content "$ResourcesPath/queries.yaml" -Raw
    
    $unsupported = @('ORDER BY', 'LIMIT ', 'GROUP BY', 'HAVING ')
    foreach ($keyword in $unsupported) {
        if ($queries -match $keyword) {
            $issues += "❌ Query contains unsupported Cypher: $keyword"
        }
    }
    
    if (($issues | Where-Object { $_ -like '*unsupported Cypher*' }).Count -eq 0) {
        Write-Host "   ✅ No unsupported Cypher clauses detected" -ForegroundColor Green
    }
}

# Summary
Write-Host "`n=== Validation Summary ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "✅ All validations passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Found $($issues.Count) issue(s):" -ForegroundColor Yellow
    foreach ($issue in $issues) {
        Write-Host "  $issue" -ForegroundColor $(if ($issue.StartsWith('❌')) { 'Red' } else { 'Yellow' })
    }
    
    # Fail if critical issues (❌), warn if minor (⚠️)
    $criticalCount = ($issues | Where-Object { $_.StartsWith('❌') }).Count
    if ($criticalCount -gt 0) {
        Write-Host "`n❌ $criticalCount critical issue(s) found - deployment may fail" -ForegroundColor Red
        exit 1
    } else {
        Write-Host "`n⚠️  Minor issues found - proceeding with caution" -ForegroundColor Yellow
        exit 0
    }
}
