<#
.SYNOPSIS
Populates demo data to trigger all Drasi continuous query events

.DESCRIPTION
Creates realistic test data to showcase:
- Duplicate wishlist item detection
- Inactive children detection (3+ days)
- Naughty/Nice status changes
- Trending items

.EXAMPLE
.\populate-drasi-demo-data.ps1

.NOTES
Run this before demos to ensure Drasi events are visible
#>

param(
    [string]$ApiUrl = "https://santadigitalshowcase-uhgsd-api.greenocean-92276740.australiaeast.azurecontainerapps.io"
)

$ErrorActionPreference = 'Continue'

Write-Host @"
╔══════════════════════════════════════════════════════════════╗
║                                                              ║
║      🎄  DRASI DEMO DATA POPULATOR  🎅                      ║
║                                                              ║
║  Generates events to trigger all Drasi continuous queries   ║
║                                                              ║
╚══════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

Write-Host "`nAPI Endpoint: $ApiUrl" -ForegroundColor Gray
Write-Host ""

# Helper function to submit wishlist item
function Submit-WishlistItem {
    param(
        [string]$ChildId,
        [string]$ItemText,
        [string]$Category = "toys",
        [decimal]$Budget = 49.99,
        [string]$RequestType = $null,
        [string]$StatusChange = $null
    )

    $payload = @{
        text           = $ItemText
        category       = $Category
        budgetEstimate = $Budget
    }

    # Add optional fields for behavior updates
    if ($RequestType) {
        $payload.requestType = $RequestType
    }
    if ($StatusChange) {
        $payload.statusChange = $StatusChange
    }

    $json = $payload | ConvertTo-Json

    try {
        # Increase timeout to 30s because the endpoint generates AI recommendations
        $response = Invoke-RestMethod "$ApiUrl/api/v1/children/$ChildId/wishlist-items" `
            -Method Post -ContentType 'application/json' -Body $json `
            -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 30 -ErrorAction Stop

        Write-Host "  ✅  Submitted: $ItemText" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "  ⚠️  Failed to submit for $ChildId : $ItemText" -ForegroundColor Yellow
        Write-Host "      Error: $($_.Exception.Message)" -ForegroundColor DarkGray
        return $false
    }
}

# 1. CREATE DUPLICATE ITEMS (triggers wishlist-duplicates-by-child query)
Write-Host "[1/4] Creating duplicate wishlist items..." -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray

$duplicateChildren = @("child-liam-2016", "child-noah-2014")
$popularItems = @("LEGO Star Wars Set", "Nintendo Switch")

foreach ($child in $duplicateChildren) {
    $item = $popularItems | Get-Random
    Write-Host "  Creating duplicates for $child : $item" -ForegroundColor Cyan

    # Submit the same item 2 times to trigger duplicate detection (COUNT > 1)
    # Note: Each submission takes ~3-5s due to AI recommendation generation
    for ($i = 1; $i -le 2; $i++) {
        $success = Submit-WishlistItem -ChildId $child -ItemText $item -Category "toys" -Budget 59.99
        if (-not $success) {
            Write-Host "      Skipping remaining duplicates due to error" -ForegroundColor Yellow
            break
        }
        Start-Sleep -Milliseconds 500
    }
}

Write-Host "`n  ✓ Duplicate entries created (will appear in Drasi within 5 seconds)" -ForegroundColor Green

# 2. CREATE STATUS CHANGES (triggers behavior-status-changes query)
Write-Host "`n[2/4] Creating behavior status changes..." -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray

$statusChildren = @(
    @{ id = "child-demo-01"; status = "Nice"; reason = "Helped clean the house" },
    @{ id = "child-demo-02"; status = "Naughty"; reason = "Broke sibling's toy" }
)

foreach ($child in $statusChildren) {
    Write-Host "  Updating $($child.id) → $($child.status)" -ForegroundColor Cyan
    Submit-WishlistItem -ChildId $child.id -ItemText $child.reason `
        -RequestType 'behavior-update' -StatusChange $child.status -Category "behavior" | Out-Null
}

Write-Host "  ✓ Status changes submitted" -ForegroundColor Green

# 3. CREATE TRENDING ITEMS (multiple children requesting same item)
Write-Host "`n[3/4] Creating trending items..." -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray

$trendingItem = "PlayStation 5"
$trendingChildren = @("child-trend-01", "child-trend-02", "child-trend-03", "child-trend-04")

Write-Host "  Making '$trendingItem' trending..." -ForegroundColor Cyan
foreach ($child in $trendingChildren) {
    Submit-WishlistItem -ChildId $child -ItemText $trendingItem -Category "electronics" -Budget 499.99 | Out-Null
    Start-Sleep -Milliseconds 100
}
Write-Host "  ✓ $($trendingChildren.Count) children requested '$trendingItem'" -ForegroundColor Green

# 4. NOTE ABOUT INACTIVE CHILDREN
Write-Host "`n[4/4] Inactive children detection..." -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  ℹ️  Inactive children require events older than 3 days" -ForegroundColor Cyan
Write-Host "  ℹ️  These will appear automatically if you have historical data" -ForegroundColor Cyan
Write-Host "  ℹ️  Or run this script again in 3+ days to see current children become inactive" -ForegroundColor Cyan

# SUMMARY
Write-Host "`n╔══════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  ✅ DEMO DATA POPULATION COMPLETE                ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Green

Write-Host "`nDrasi Queries That Will Now Show Data:" -ForegroundColor Cyan
Write-Host "  🔁 Duplicate Items: child-liam-2016, child-noah-2014" -ForegroundColor White
Write-Host "  📊 Status Changes: child-demo-01 (Nice), child-demo-02 (Naughty)" -ForegroundColor White
Write-Host "  🔥 Trending: PlayStation 5 (4 requests)" -ForegroundColor White
Write-Host "  �� Inactive: (requires 3+ days old data)" -ForegroundColor White

Write-Host "`nVerify in Demo Script:" -ForegroundColor Yellow
Write-Host "  Run: .\scripts\demo-interactive.ps1" -ForegroundColor White
Write-Host "  Select: [5] Agent Tools Showcase" -ForegroundColor White
Write-Host ""
