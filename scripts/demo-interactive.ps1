<#
.SYNOPSIS
Comprehensive Interactive Demo for Santa's Workshop - Drasi + Agent Framework

.DESCRIPTION
Complete demonstration interface showcasing the entire solution:
- Real-time event detection with Drasi
- AI Agent Framework orchestration
- Multi-agent collaboration patterns
- Streaming SSE recommendations
- Naughty/Nice behavior detection
- Agent tool calling capabilities
- System health monitoring

This is the primary demo script for presentations and technical demonstrations.

.EXAMPLE
.\demo-interactive.ps1
Auto-discovers API endpoint from azd environment

.EXAMPLE
.\demo-interactive.ps1 -ApiUrl "https://your-api.azurecontainerapps.io"
Explicitly specify API endpoint

.EXAMPLE
.\demo-interactive.ps1 -AutoDiscover
Force re-discovery of API endpoint

.NOTES
Version: 2.0
Last Updated: 2025-11-25
Dependencies: Azure CLI (azd), curl (for SSE streaming)
#>

param(
    [string]$ApiUrl = "",
    [switch]$AutoDiscover
)

$ErrorActionPreference = 'Continue'

# Auto-discover API URL from azd
if ([string]::IsNullOrWhiteSpace($ApiUrl) -or $AutoDiscover) {
    try {
        $apiHost = azd env get-value apiHost 2>$null
        if ($apiHost) {
            $ApiUrl = "https://$apiHost"
            Write-Host "✓ Discovered API: $ApiUrl" -ForegroundColor Green
        }
    }
    catch { }
}

if ([string]::IsNullOrWhiteSpace($ApiUrl)) {
    Write-Host "❌ No API URL provided." -ForegroundColor Red
    Write-Host "Usage: .\demo-interactive.ps1 -ApiUrl 'https://your-api-url'" -ForegroundColor Yellow
    exit 1
}

function Show-Banner {
    Clear-Host
    Write-Host @"
╔════════════════════════════════════════════════════════════╗
║                                                            ║
║      🎅  SANTA'S WORKSHOP - COMPREHENSIVE DEMO  🎄        ║
║                                                            ║
║  Drasi Real-Time Detection + Agent Framework AI           ║
║  Version 2.0 - Complete Solution Showcase                 ║
║                                                            ║
╚════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

    Write-Host "`nAPI Endpoint: " -NoNewline -ForegroundColor Gray
    Write-Host $ApiUrl -ForegroundColor White
    Write-Host ""
}

function Show-Menu {
    Write-Host "`n════════════════ DEMO SCENARIOS ════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  [1] End-to-End Wishlist Processing" -ForegroundColor White
    Write-Host "      └─ Drasi detection → Agent recommendations" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [2] Multi-Agent Collaboration" -ForegroundColor White
    Write-Host "      └─ 3 specialized agents working together" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [3] Streaming Recommendations (SSE)" -ForegroundColor White
    Write-Host "      └─ Watch AI think token-by-token" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [4] Naughty/Nice Behavior Detection" -ForegroundColor White
    Write-Host "      └─ Dynamic recommendation adjustments" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [5] Agent Tools Showcase" -ForegroundColor White
    Write-Host "      └─ View 6 real tools with data access" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [6] Custom Child Test" -ForegroundColor White
    Write-Host "      └─ Enter your own child ID and wishlist" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [7] Full System Validation" -ForegroundColor White
    Write-Host "      └─ Complete pre-demo readiness check" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "══════════════════ UTILITIES ═══════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  [H] Health Check (Quick)" -ForegroundColor Cyan
    Write-Host "  [D] Documentation & Help" -ForegroundColor Cyan
    Write-Host "  [Q] Quit" -ForegroundColor Cyan
    Write-Host ""
    Write-Host -NoNewline "Select option: " -ForegroundColor Green
}

function Submit-WishlistJob {
    param(
        [string]$ChildId,
        [switch]$UseRandomWishlist
    )

    $payload = @{
        childId       = $ChildId
        schemaVersion = 'v1'
        dedupeKey     = "$ChildId:demo-$(Get-Date -Format 'yyyyMMddHHmmss')"
    }

    if ($UseRandomWishlist) {
        $items = @('Lego Set', 'Story Book', 'Board Game', 'Paint Kit', 'Puzzle', 'RC Car', 'Plush Toy', 'STEM Kit')
        $randomItems = $items | Sort-Object { Get-Random } | Select-Object -First 3
        $payload.wishlist = @{ items = $randomItems }
        Write-Host "  Items: $($randomItems -join ', ')" -ForegroundColor Gray
    }

    $payloadJson = $payload | ConvertTo-Json -Depth 5

    # Try multiple endpoint variants
    $endpoints = @("$ApiUrl/api/jobs", "$ApiUrl/api/v1/jobs")

    foreach ($endpoint in $endpoints) {
        try {
            $response = Invoke-WebRequest $endpoint -Method Post `
                -ContentType 'application/json' -Body $payloadJson `
                -Headers @{ 'X-Role' = 'operator' } `
                -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop

            if ($response.StatusCode -in @(200, 202)) {
                Write-Host "  ✓ Job accepted (status $($response.StatusCode))" -ForegroundColor Green
                return $true
            }
        }
        catch {
            if ($_.Exception.Message -notmatch "404") {
                Write-Warning "  Error at $endpoint : $($_.Exception.Message)"
            }
        }
    }

    Write-Host "  ✗ Job submission failed at all endpoints" -ForegroundColor Red
    return $false
}

function Submit-BehaviorUpdate {
    param(
        [string]$ChildId,
        [ValidateSet('nice', 'naughty')][string]$Behavior,
        [string]$Description
    )

    Write-Host "`n[1/2] Submitting initial wishlist..." -ForegroundColor Cyan
    $giftPayload = @{
        text           = "Lego Set"
        category       = "toys"
        budgetEstimate = 49.99
    } | ConvertTo-Json -Depth 5

    try {
        $res = Invoke-RestMethod "$ApiUrl/api/v1/children/$ChildId/wishlist-items" -Method Post `
            -ContentType 'application/json' -Body $giftPayload `
            -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10
        Write-Host "  ✓ Wishlist created" -ForegroundColor Green
    }
    catch {
        Write-Warning "  Wishlist creation failed: $($_.Exception.Message)"
    }

    Start-Sleep -Milliseconds 500

    Write-Host "`n[2/2] Submitting behavior update..." -ForegroundColor Cyan
    $statusValue = if ($Behavior -eq 'nice') { 'Nice' } else { 'Naughty' }
    $behaviorPayload = @{
        requestType  = 'behavior-update'
        text         = $Description
        statusChange = $statusValue
    } | ConvertTo-Json -Depth 5

    try {
        $res = Invoke-RestMethod "$ApiUrl/api/v1/children/$ChildId/wishlist-items" -Method Post `
            -ContentType 'application/json' -Body $behaviorPayload `
            -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10
        Write-Host "  ✓ Behavior updated to: $statusValue" -ForegroundColor $(if ($Behavior -eq 'nice') { 'Green' } else { 'Red' })
    }
    catch {
        Write-Warning "  Behavior update failed: $($_.Exception.Message)"
    }
}

function Test-ApiHealth {
    Write-Host "`n🏥 Running Health Checks..." -ForegroundColor Cyan

    # Health endpoint
    try {
        $health = Invoke-RestMethod "$ApiUrl/healthz" -TimeoutSec 5
        Write-Host "  ✓ Health endpoint: OK" -ForegroundColor Green
    }
    catch {
        Write-Host "  ✗ Health endpoint: FAILED" -ForegroundColor Red
    }

    # Readiness
    try {
        $ready = Invoke-RestMethod "$ApiUrl/readyz" -TimeoutSec 5
        Write-Host "  ✓ Readiness: OK" -ForegroundColor Green
    }
    catch {
        Write-Host "  ⚠ Readiness: Not ready" -ForegroundColor Yellow
    }

    # Cosmos DB
    try {
        $ping = Invoke-RestMethod "$ApiUrl/api/pingz" -TimeoutSec 5
        if ($ping.cosmosReady) {
            Write-Host "  ✓ Cosmos DB: Connected" -ForegroundColor Green
        }
        else {
            Write-Host "  ⚠ Cosmos DB: Not ready" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "  ⚠ Cosmos DB: Unknown" -ForegroundColor Yellow
    }

    Pause
}

function Demo-Scenario1 {
    Show-Banner
    Write-Host "📝 SCENARIO 1: End-to-End Wishlist Processing" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = "demo-alice-$(Get-Random -Max 999)"
    Write-Host "Child ID: $childId" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "This will:" -ForegroundColor White
    Write-Host "  1. Submit wishlist to EventHub" -ForegroundColor Gray
    Write-Host "  2. Drasi detects event (<5s)" -ForegroundColor Gray
    Write-Host "  3. Agents generate recommendations" -ForegroundColor Gray
    Write-Host ""

    Write-Host -NoNewline "Press Enter to start..." -ForegroundColor Green
    Read-Host

    # Run simulation
    Write-Host "`n🚀 Submitting wishlist..." -ForegroundColor Cyan
    Write-Host "Submitting wishlist for child: $childId" -ForegroundColor Cyan
    Submit-WishlistJob -ChildId $childId -UseRandomWishlist

    Write-Host "`n📊 View recommendations:" -ForegroundColor Yellow
    Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White

    Pause
}

function Demo-Scenario2 {
    Show-Banner
    Write-Host "🤝 SCENARIO 2: Multi-Agent Collaboration" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = "demo-bob-$(Get-Random -Max 999)"
    Write-Host "Child ID: $childId" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Three specialized elves working together:" -ForegroundColor White
    Write-Host "  1. 🔍 Behavior Analyst - Analyzes child history" -ForegroundColor Gray
    Write-Host "  2. 🎨 Creative Gift Elf - Proposes gift ideas" -ForegroundColor Gray
    Write-Host "  3. ✅ Quality Reviewer - Validates suggestions" -ForegroundColor Gray
    Write-Host ""

    Write-Host -NoNewline "Press Enter to start..." -ForegroundColor Green
    Read-Host

    Write-Host "`n🚀 Running multi-agent orchestration..." -ForegroundColor Cyan
    try {
        $result = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations/collaborative?status=Nice" `
            -Method Post -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 30

        Write-Host "`n✅ Multi-Agent Result:" -ForegroundColor Green
        $result | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor White
    }
    catch {
        Write-Host "`n⚠️  Error: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  This endpoint may not be implemented yet." -ForegroundColor Gray
    }

    Pause
}

function Demo-Scenario3 {
    Show-Banner
    Write-Host "🌊 SCENARIO 3: Streaming Recommendations" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = "demo-stream-$(Get-Random -Max 999)"
    Write-Host "Child ID: $childId" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Watch AI generate recommendations in real-time!" -ForegroundColor White
    Write-Host "You'll see:" -ForegroundColor Gray
    Write-Host "  • Thought process ('Analyzing profile...')" -ForegroundColor Gray
    Write-Host "  • Token-by-token text generation" -ForegroundColor Gray
    Write-Host "  • Progress updates" -ForegroundColor Gray
    Write-Host ""

    Write-Host -NoNewline "Press Enter to start streaming..." -ForegroundColor Green
    Read-Host

    Write-Host "`n🌊 Streaming output (Ctrl+C to stop):" -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray

    $streamUrl = "$ApiUrl/api/v1/children/$childId/recommendations/stream?status=Nice"

    # Use curl for streaming (PowerShell doesn't handle SSE well)
    try {
        Invoke-WebRequest -N $streamUrl 2>$null
    }
    catch {
        Write-Host "`n⚠️  Streaming failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    Write-Host "`n════════════════════════════════════════════════" -ForegroundColor DarkGray
    Pause
}

function Demo-Scenario4 {
    Show-Banner
    Write-Host "😇😈 SCENARIO 4: Naughty/Nice Behavior Detection" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = "demo-behavior-$(Get-Random -Max 999)"
    Write-Host "Child ID: $childId" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "Select behavior to test:" -ForegroundColor White
    Write-Host "  [1] Nice behavior (premium gifts)" -ForegroundColor Green
    Write-Host "  [2] Naughty behavior (educational gifts)" -ForegroundColor Yellow
    Write-Host ""
    $behavior = Read-Host "Choice"

    $behaviorType = if ($behavior -eq "1") { "nice" } else { "naughty" }
    $description = if ($behavior -eq "1") {
        "Helped siblings with homework without being asked"
    }
    else {
        "Ignored chores despite multiple reminders"
    }

    Write-Host "`n🚀 Submitting $behaviorType behavior event..." -ForegroundColor Cyan
    Submit-BehaviorUpdate -ChildId $childId -Behavior $behaviorType -Description $description

    Write-Host "`n⏳ Waiting for Drasi detection and recommendation update (5s)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5

    Write-Host "`n📊 Fetching updated recommendations..." -ForegroundColor Cyan
    try {
        $recs = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations" `
            -Headers @{ 'X-Role' = 'operator' }

        Write-Host "`n✅ Recommendations adjusted based on $behaviorType behavior:" -ForegroundColor Green
        $recs | ConvertTo-Json -Depth 2 | Write-Host -ForegroundColor White
    }
    catch {
        Write-Host "`n⚠️  Could not fetch recommendations: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    Pause
}

function Demo-Scenario5 {
    Show-Banner
    Write-Host "🛠️  SCENARIO 5: Agent Tools Showcase" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    Write-Host "Fetching registered agent tools..." -ForegroundColor Cyan

    try {
        $tools = Invoke-RestMethod "$ApiUrl/api/v1/agent-tools" `
            -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10

        Write-Host "`n✅ Available Tools:" -ForegroundColor Green
        $tools | Format-Table -AutoSize | Out-String | Write-Host -ForegroundColor White

        Write-Host "`nThese tools provide agents with:" -ForegroundColor Yellow
        Write-Host "  • Real-time data from Cosmos DB" -ForegroundColor Gray
        Write-Host "  • Inventory and pricing information" -ForegroundColor Gray
        Write-Host "  • Budget constraint validation" -ForegroundColor Gray
        Write-Host "  • Drasi graph query capabilities" -ForegroundColor Gray
    }
    catch {
        Write-Host "`n⚠️  Could not fetch tools: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    Pause
}

function Demo-Scenario6 {
    Show-Banner
    Write-Host "🎯 SCENARIO 6: Custom Child Test" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = Read-Host "Enter child ID (or leave blank for random)"
    if ([string]::IsNullOrWhiteSpace($childId)) {
        $childId = "demo-custom-$(Get-Random -Max 9999)"
    }

    Write-Host "`nEnter wishlist items (comma-separated, e.g., 'Lego Set, Story Book'):" -ForegroundColor Cyan
    $items = Read-Host "Items"

    if ([string]::IsNullOrWhiteSpace($items)) {
        Write-Host "Using random wishlist..." -ForegroundColor Yellow
        Submit-WishlistJob -ChildId $childId -UseRandomWishlist
    }
    else {
        Write-Host "`n🚀 Creating custom wishlist..." -ForegroundColor Cyan

        # Check if send-wishlist-event script exists
        $eventScript = Join-Path $PSScriptRoot "send-wishlist-event.ps1"
        if (Test-Path $eventScript) {
            & $eventScript -ChildId $childId -Items $items -Hub "wishlist-events"
        }
        else {
            Write-Host "  ⚠️  EventHub sender not available, using API..." -ForegroundColor Yellow
            Submit-WishlistJob -ChildId $childId -UseRandomWishlist
        }
    }

    Write-Host "`n📊 View results:" -ForegroundColor Yellow
    Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White

    Pause
}

function Demo-Scenario7 {
    Show-Banner
    Write-Host "🔍 SCENARIO 7: Full System Validation" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Running comprehensive pre-demo checks..." -ForegroundColor Cyan
    Write-Host "  • Infrastructure connectivity" -ForegroundColor Gray
    Write-Host "  • API health & readiness" -ForegroundColor Gray
    Write-Host "  • Cosmos DB connection" -ForegroundColor Gray
    Write-Host "  • Drasi integration (if not skipped)" -ForegroundColor Gray
    Write-Host "  • Agent Framework status" -ForegroundColor Gray
    Write-Host ""

    # Check if test-demo-readiness.ps1 exists
    $readinessScript = Join-Path $PSScriptRoot "test-demo-readiness.ps1"
    if (Test-Path $readinessScript) {
        Write-Host "Launching full validation script..." -ForegroundColor Green
        & $readinessScript -ApiUrl $ApiUrl
    }
    else {
        Write-Host "⚠️  Full validation script not found. Running basic checks..." -ForegroundColor Yellow
        Test-ApiHealth

        # Additional basic checks
        Write-Host "`n📊 Quick System Check:" -ForegroundColor Cyan

        # Check job submission
        try {
            $testPayload = @{
                childId       = "validation-test-$(Get-Random)"
                schemaVersion = 'v1'
                dedupeKey     = "validation:$(Get-Date -Format 'yyyyMMddHHmmss')"
            } | ConvertTo-Json

            $response = Invoke-WebRequest "$ApiUrl/api/jobs" -Method Post `
                -ContentType 'application/json' -Body $testPayload `
                -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10 -UseBasicParsing

            if ($response.StatusCode -in @(200, 202)) {
                Write-Host "  ✓ Job submission: OK" -ForegroundColor Green
            }
        }
        catch {
            Write-Host "  ✗ Job submission: FAILED" -ForegroundColor Red
            Write-Host "    $($_.Exception.Message)" -ForegroundColor DarkGray
        }

        # Check agent tools
        try {
            $tools = Invoke-RestMethod "$ApiUrl/api/v1/agent-tools" `
                -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10
            Write-Host "  ✓ Agent tools: $($tools.Count) registered" -ForegroundColor Green
        }
        catch {
            Write-Host "  ⚠ Agent tools: Unavailable" -ForegroundColor Yellow
        }
    }

    Pause
}

function Show-Documentation {
    Show-Banner
    Write-Host "📚 DOCUMENTATION & HELP" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    Write-Host "Available Resources:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  📖 Quick Start Guide:" -ForegroundColor White
    Write-Host "     drasi/QUICK-START.md" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  📋 Demo Guide:" -ForegroundColor White
    Write-Host "     DEMO-GUIDE.md" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  🏗️  Architecture:" -ForegroundColor White
    Write-Host "     docs/architecture/architecture.md" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  🔧 Troubleshooting:" -ForegroundColor White
    Write-Host "     drasi/TROUBLESHOOTING.md" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  🎯 Implementation Status:" -ForegroundColor White
    Write-Host "     docs/status/implementation-summary.md" -ForegroundColor Gray
    Write-Host ""

    Write-Host "`nDemo Scenario Descriptions:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  [1] End-to-End Processing" -ForegroundColor White
    Write-Host "      Shows complete flow from wishlist submission through" -ForegroundColor Gray
    Write-Host "      Drasi detection to AI-generated recommendations." -ForegroundColor Gray
    Write-Host "      Best for: Initial capability overview" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [2] Multi-Agent Collaboration" -ForegroundColor White
    Write-Host "      Demonstrates 3 specialized agents (Behavior Analyst," -ForegroundColor Gray
    Write-Host "      Creative Gift Elf, Quality Reviewer) working together." -ForegroundColor Gray
    Write-Host "      Best for: Agent Framework orchestration patterns" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [3] Streaming (SSE)" -ForegroundColor White
    Write-Host "      Real-time token streaming showing AI thought process." -ForegroundColor Gray
    Write-Host "      Best for: Demonstrating responsive UX capabilities" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [4] Naughty/Nice Detection" -ForegroundColor White
    Write-Host "      Dynamic behavior changes triggering recommendation" -ForegroundColor Gray
    Write-Host "      adjustments via Drasi graph queries." -ForegroundColor Gray
    Write-Host "      Best for: Real-time event-driven architecture" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [5] Agent Tools" -ForegroundColor White
    Write-Host "      Shows 6 registered tools agents use for data access:" -ForegroundColor Gray
    Write-Host "      Cosmos DB queries, inventory, pricing, budgets." -ForegroundColor Gray
    Write-Host "      Best for: Tool calling and RAG patterns" -ForegroundColor DarkCyan
    Write-Host ""

    Write-Host "`nTips for Successful Demos:" -ForegroundColor Cyan
    Write-Host "  • Run option [7] or [H] before starting" -ForegroundColor Gray
    Write-Host "  • Have Azure Portal open to show resources" -ForegroundColor Gray
    Write-Host "  • Keep Drasi dashboard visible for graph updates" -ForegroundColor Gray
    Write-Host "  • Use unique child IDs to avoid confusion" -ForegroundColor Gray
    Write-Host "  • Scenarios 1-4 can be run in sequence for full story" -ForegroundColor Gray
    Write-Host ""

    Pause
}

# Main loop
while ($true) {
    Show-Banner
    Show-Menu

    $choice = Read-Host

    switch ($choice.ToUpper()) {
        "1" { Demo-Scenario1 }
        "2" { Demo-Scenario2 }
        "3" { Demo-Scenario3 }
        "4" { Demo-Scenario4 }
        "5" { Demo-Scenario5 }
        "6" { Demo-Scenario6 }
        "7" { Demo-Scenario7 }
        "H" { Test-ApiHealth }
        "D" { Show-Documentation }
        "Q" {
            Write-Host "`n🎄 Thank you for exploring Santa's Workshop! 🎅" -ForegroundColor Green
            exit 0
        }
        default {
            Write-Host "`n⚠️  Invalid option. Please try again." -ForegroundColor Yellow
            Start-Sleep -Seconds 1
        }
    }
}
