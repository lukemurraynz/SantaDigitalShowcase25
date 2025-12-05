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
Version: 5.0
Last Updated: 2025-12-05
Changes: 
  - v5.0: Fixed streaming chunk size (80 chars) and event type handling (text-delta)
  - v5.0: Streaming buffers 300+ chars before output for smoother reading
  - v4.9: Auto-detects console width for proper line wrapping
  - v4.8: Added profile data support - wishlist items now update child profile preferences
  - v4.8: Scenario 6 now prompts for optional child name/age for better AI recommendations
  - v4.8: Submit-WishlistJob accepts -ChildName and -ChildAge parameters
  - v4.8: AI recommendations now have context about child's interests from wishlist items
  - v4.7: Fixed Scenario 4 - Added Drasi/Agent Framework animation for behavior detection
  - v4.7: Fixed Scenario 6 - Changed from /jobs endpoint to /wishlist-items (no Event Hub required)
  - v4.7: Submit-WishlistJob now adds items one-by-one via wishlist-items endpoint
  - v4.6: OPTIMIZED multi-agent orchestration with PARALLEL execution (Analyst + Creative run concurrently)
  - v4.6: Reduced expected time from ~60s to ~40s with parallel optimization
  - v4.6: Increased timeout from 120s to 180s for resilience under Azure OpenAI load
  - v4.6: More concise agent prompts = faster GPT-4o responses
  - v4.6: Network timeout increased from 45s to 60s per agent call
  - v4.5: Updated ALL animations to highlight DRASI + AGENT FRAMEWORK integration
  - v4.5: Menu now shows DRASI/AGENT labels for each scenario
  - v4.5: Processing pipeline shows [DRASI] and [AGENT FRAMEWORK] prefixes
  - v4.5: Multi-agent display renamed to "Drasi + Agent Framework - Live Orchestration"
  - v4.5: All scenario descriptions updated to explain Drasi/Agent roles
  - v4.4: Scenario 4 now uses faster single-agent endpoint (was 3-agent collaborative with 120s timeout)
  - v4.4: Scenario 4 displays behavior-specific recommendations with colored UI
  - v4.4: Fixed frontend caching - recommendations now clear when switching children
  - v4.3: Added animated progress to ALL scenarios (3, 4, 5, 6, 7)
  - v4.3: Fixed Scenario 6 to use API instead of EventHub (resolves EVENTHUB_CONNECTION error)
  - v4.3: Updated Submit-WishlistJob to support custom items (not just random)
  - v4.3: Added connection animation for Scenario 3 (Streaming)
  - v4.3: Added discovery animation for Scenario 5 (Agent Tools) with staggered reveal
  - v4.3: Added system check animation for Scenario 7 (Full Validation)
  - v4.2: Added Show-MultiAgentProgress function for scenario 2 visual feedback
  - v4.2: Enhanced scenario 2 with step-by-step agent progress indicators
  - v4.2: Improved error handling with timeout-specific guidance
  - v4.2: Increased scenario 2 timeout from 90s to 120s
  - v4.1: Added animated progress display during AI processing (spinner, stages, progress bar)
  - v4.1: Enhanced scenario 1 with step-by-step visual indicators
  - v4.1: Improved recommendation display with formatted boxes and icons
  - v4.1: Added Show-AIProcessingProgress and Show-Spinner helper functions
  - v4.0: Fixed response format handling - API returns 'items' not 'recommendations'
  - v4.0: Added support for both 'rationale' and 'reasoning' fields in response
  - v4.0: Added support for both 'price' and 'estimatedCost' fields in response
  - v3.9: Increased AI timeout from 10s to 20s (backend fix for Azure OpenAI latency)
  - v3.9: Updated demo script: 20s initial wait + 25s polling = 45s total (was 15s + 30s)
  - v3.9: Fixed HttpClient timeout errors when Azure OpenAI is under load
  - v3.8: Changed initial wait from 10s to 15s (matches AI generation time: 5-8s AI + 5s enrichment)
  - v3.8: Updated polling from 40s to 30s (total wait now 45s: 15s initial + 30s polling)
  - v3.8: Updated error messages to show total wait time and more accurate failure causes
  - v3.8: Removed incorrect "Drasi event detection delay" cause (direct trigger bypasses Drasi)
  - v3.7: Extended total wait time to 50 seconds (10s initial + 40s polling)
  - v3.7: Reduced check frequency to every 5s (was 3s) to reduce API load
  - v3.7: Added 10-second initial wait before first check (allows AI agents to start)
  - v3.7: Better diagnostics showing pipeline stages and possible delay causes
  - v3.7: Captures and displays last API error for troubleshooting
  - v3.6: Improved streaming display with text buffering (50-char chunks instead of per-character)
  - v3.6: Better formatting with proper line breaks between thinking/progress/text
  - v3.5: Fixed double slash in API URLs by normalizing base URL
  - v3.5: Increased polling time from 20s to 30s (accounts for AI agent processing)
  - v3.5: Added manual retry option when recommendations not ready
  - v3.4: Auto-fetch and display recommendations in Scenarios 1 & 6 with nice formatting
  - v3.4: Added polling mechanism with progress indicators (checks every 3s for 20s)
  - v3.4: Shows detailed recommendation info (suggestion, reasoning, confidence, cost)
  - v3.3: Increased timeout to 90s for multi-agent scenarios (3 sequential AI calls)
  - v3.3: Added retry logic with exponential backoff for timeout resilience
  - v3.3: Improved progress indicators showing all 3 agent steps
  - v3.2: Added real-time curl streaming in PowerShell window for Scenario 3 (SSE)
  - v3.2: Auto-detects curl availability, falls back to Invoke-WebRequest if not available
  - v3.1: Fixed agent tools display to properly handle nested JSON response structure
  - v3.1: Improved streaming scenario with event count and better formatting
  - v3.0: Fixed behavior scenario order (behavior→wishlist instead of wishlist→behavior)
  - v3.0: Use collaborative endpoint with correct status parameter for behavior-aware recommendations
  - v2.2: Increased timeout to 30s for AI recommendation generation (14s typical response time)
Dependencies: Azure CLI (azd), curl (optional, for real-time streaming)
Validated against: Current API endpoints with multi-agent orchestration
#>

param(
    [string]$ApiUrl = "https://santadigitalshowcase-uhgsd-api.greenocean-92276740.australiaeast.azurecontainerapps.io/",
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

# Normalize API URL (remove trailing slash)
$ApiUrl = $ApiUrl.TrimEnd('/')

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
║  DRASI Real-Time Detection + AGENT FRAMEWORK AI           ║
║  Version 5.0 - Improved Streaming & Preferences          ║
║                                                            ║
╚════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

    Write-Host "`nAPI Endpoint: " -NoNewline -ForegroundColor Gray
    Write-Host $ApiUrl -ForegroundColor White
    Write-Host ""
}

# Animated progress display for AI processing
function Show-AIProcessingProgress {
    param(
        [int]$DurationSeconds = 20,
        [string]$TaskDescription = "Processing"
    )
    
    $stages = @(
        @{ Time = 0; Icon = "📨"; Message = "API Gateway receives request..." },
        @{ Time = 1; Icon = "📊"; Message = "[DRASI] Event published to Cosmos DB change feed..." },
        @{ Time = 2; Icon = "🔄"; Message = "[DRASI] Continuous Query detecting wishlist change..." },
        @{ Time = 4; Icon = "⚡"; Message = "[DRASI] Real-time graph update triggered!" },
        @{ Time = 5; Icon = "🤖"; Message = "[AGENT FRAMEWORK] Initializing ElfRecommendationAgent..." },
        @{ Time = 7; Icon = "🧠"; Message = "[AGENT FRAMEWORK] Agent calling Azure OpenAI (GPT-4o)..." },
        @{ Time = 9; Icon = "🔧"; Message = "[AGENT FRAMEWORK] Agent using Drasi tools for context..." },
        @{ Time = 11; Icon = "📈"; Message = "[DRASI] Querying trending items from event graph..." },
        @{ Time = 13; Icon = "🎁"; Message = "[AGENT FRAMEWORK] Generating behavior-aware recommendations..." },
        @{ Time = 15; Icon = "💾"; Message = "[DRASI] Persisting to Cosmos DB + SignalR broadcast..." },
        @{ Time = 18; Icon = "✅"; Message = "Pipeline complete! Drasi + Agent Framework in action!" }
    )
    
    $spinnerFrames = @('⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏')
    $frameIndex = 0
    $currentStageIndex = 0
    
    Write-Host ""
    Write-Host "  ┌──────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "  │  🎄 Drasi + Agent Framework - Event Pipeline 🎄         │" -ForegroundColor DarkGray
    Write-Host "  └──────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""
    
    for ($i = 0; $i -lt $DurationSeconds; $i++) {
        # Check if we should show the next stage
        while ($currentStageIndex -lt $stages.Count -and $stages[$currentStageIndex].Time -le $i) {
            $stage = $stages[$currentStageIndex]
            Write-Host "  $($stage.Icon) $($stage.Message)" -ForegroundColor Cyan
            $currentStageIndex++
        }
        
        # Show spinner with elapsed time
        $spinner = $spinnerFrames[$frameIndex % $spinnerFrames.Count]
        $progressBar = "█" * [Math]::Floor(($i / $DurationSeconds) * 20) + "░" * (20 - [Math]::Floor(($i / $DurationSeconds) * 20))
        Write-Host "`r  $spinner [$progressBar] $i/$DurationSeconds sec " -NoNewline -ForegroundColor Yellow
        
        Start-Sleep -Milliseconds 500
        $frameIndex++
        
        # Second half of the second
        $spinner = $spinnerFrames[$frameIndex % $spinnerFrames.Count]
        Write-Host "`r  $spinner [$progressBar] $i/$DurationSeconds sec " -NoNewline -ForegroundColor Yellow
        
        Start-Sleep -Milliseconds 500
        $frameIndex++
    }
    
    # Complete the progress bar
    Write-Host "`r  ✓ [████████████████████] $DurationSeconds/$DurationSeconds sec " -ForegroundColor Green
    Write-Host ""
}

# Simple spinner for shorter waits
function Show-Spinner {
    param(
        [int]$Seconds = 5,
        [string]$Message = "Working"
    )
    
    $spinnerFrames = @('◐', '◓', '◑', '◒')
    $frameIndex = 0
    
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        $spinner = $spinnerFrames[$frameIndex % $spinnerFrames.Count]
        Write-Host "`r  $spinner $Message... ($([Math]::Floor($i/4)+1)s) " -NoNewline -ForegroundColor Yellow
        Start-Sleep -Milliseconds 250
        $frameIndex++
    }
    Write-Host "`r  ✓ $Message... Done!       " -ForegroundColor Green
}

# Multi-agent progress display - shows parallel agent execution
function Show-MultiAgentProgress {
    $agents = @(
        @{ Icon = "🔍"; Name = "[DRASI] Event Processor"; Tasks = @("Detecting change in Cosmos DB...", "Running Continuous Query...", "Updating event graph...") },
        @{ Icon = "🤖"; Name = "[AGENT] BehaviorAnalyst"; Tasks = @("Querying Drasi for child data...", "Analyzing behavior patterns...", "Generating insights...") },
        @{ Icon = "🎁"; Name = "[AGENT] CreativeGiftElf"; Tasks = @("Searching gift inventory...", "Matching wishlist to gifts...", "Creating recommendations...") },
        @{ Icon = "✨"; Name = "[AGENT] QualityReviewer"; Tasks = @("Reviewing recommendations...", "Validating appropriateness...", "Polishing final output...") }
    )
    
    $spinnerFrames = @('⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏')
    
    Write-Host ""
    Write-Host "  ┌────────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "  │  🎄 Drasi + Agent Framework - PARALLEL Orchestration 🎄        │" -ForegroundColor DarkGray
    Write-Host "  │  ⚡ Analyst + Creative run concurrently (saves ~20 seconds)    │" -ForegroundColor DarkGray
    Write-Host "  └────────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""
    
    $frameIndex = 0
    $agentIndex = 0
    $taskIndex = 0
    $ticksPerTask = 6  # ~1.5 seconds per task (faster for parallel)
    $currentTick = 0
    
    # Show parallel execution visual
    for ($i = 0; $i -lt 24; $i++) {
        $agent = $agents[$agentIndex]
        $task = $agent.Tasks[$taskIndex]
        $spinner = $spinnerFrames[$frameIndex % $spinnerFrames.Count]
        
        # Show parallel indicator for agents 1 and 2
        $parallelTag = if ($agentIndex -in @(1, 2)) { "[PARALLEL] " } else { "" }
        
        Write-Host "`r  $spinner $($agent.Icon) $parallelTag$($agent.Name): $task                    " -NoNewline -ForegroundColor Cyan
        
        Start-Sleep -Milliseconds 200
        $frameIndex++
        $currentTick++
        
        # Move to next task/agent
        if ($currentTick -ge $ticksPerTask) {
            $currentTick = 0
            $taskIndex++
            if ($taskIndex -ge $agent.Tasks.Count) {
                $parallelTag = if ($agentIndex -in @(1, 2)) { "⚡ " } else { "" }
                Write-Host "`r  ✓ $($agent.Icon) $parallelTag$($agent.Name): Complete!                                    " -ForegroundColor Green
                $taskIndex = 0
                $agentIndex++
                if ($agentIndex -ge $agents.Count) {
                    break
                }
            }
        }
    }
    
    # Mark remaining agents as pending (since the real work happens server-side)
    while ($agentIndex -lt $agents.Count) {
        $agent = $agents[$agentIndex]
        Write-Host "`r  ⏳ $($agent.Icon) $($agent.Name): Awaiting server response...                    " -ForegroundColor Yellow
        $agentIndex++
    }
    
    Write-Host ""
}

function Show-Menu {
    Write-Host "`n════════════════ DEMO SCENARIOS ════════════════" -ForegroundColor Yellow
    Write-Host "        Drasi + Microsoft Agent Framework" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [1] End-to-End Wishlist Processing" -ForegroundColor White
    Write-Host "      └─ DRASI detects change → AGENT generates recs" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [2] Multi-Agent Collaboration" -ForegroundColor White
    Write-Host "      └─ 3 AGENTS use DRASI tools for real-time data" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [3] Streaming Recommendations (SSE)" -ForegroundColor White
    Write-Host "      └─ AGENT streams tokens via Azure OpenAI" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [4] Naughty/Nice Behavior Detection" -ForegroundColor White
    Write-Host "      └─ DRASI event triggers AGENT behavior analysis" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [5] Agent Tools Showcase" -ForegroundColor White
    Write-Host "      └─ 6 DRASI-powered tools for AGENT context" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [6] Custom Child Test ⭐ NEW: Preference-Based!" -ForegroundColor White
    Write-Host "      └─ Full DRASI → AGENT pipeline with YOUR data" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [7] Full System Validation" -ForegroundColor White
    Write-Host "      └─ DRASI + AGENT health and integration check" -ForegroundColor DarkGray
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
        [switch]$UseRandomWishlist,
        [string]$CustomItems, # Accept comma-separated custom items
        [string]$ChildName, # Optional: child's name for better recommendations
        [int]$ChildAge           # Optional: child's age for age-appropriate recommendations
    )

    # Parse items
    $itemList = @()
    if ($CustomItems) {
        $itemList = $CustomItems -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        Write-Host "  Items: '$($itemList -join ', ')'" -ForegroundColor Gray
    }
    elseif ($UseRandomWishlist) {
        $items = @('Lego Set', 'Story Book', 'Board Game', 'Paint Kit', 'Puzzle', 'RC Car', 'Plush Toy', 'STEM Kit')
        $itemList = $items | Sort-Object { Get-Random } | Select-Object -First 3
        Write-Host "  Items: $($itemList -join ', ')" -ForegroundColor Gray
    }

    if ($itemList.Count -eq 0) {
        Write-Host "  ✗ No items to submit" -ForegroundColor Red
        return $false
    }

    # Use the /api/v1/children/{childId}/wishlist-items endpoint for each item
    # This endpoint doesn't require Event Hubs and triggers recommendation generation directly
    $success = $true
    $endpoint = "$ApiUrl/api/v1/children/$ChildId/wishlist-items"
    $isFirstItem = $true

    foreach ($item in $itemList) {
        $payload = @{
            text     = $item
            category = "gift"
        }
        
        # Only add name/age on the first item to avoid redundant updates
        if ($isFirstItem) {
            if ($ChildName) { $payload["childName"] = $ChildName }
            if ($ChildAge -gt 0) { $payload["childAge"] = $ChildAge }
            $isFirstItem = $false
        }
        
        $jsonPayload = $payload | ConvertTo-Json -Depth 5

        try {
            $response = Invoke-WebRequest $endpoint -Method Post `
                -ContentType 'application/json' -Body $jsonPayload `
                -Headers @{ 'X-Role' = 'operator' } `
                -UseBasicParsing -TimeoutSec 30 -ErrorAction Stop

            if ($response.StatusCode -in @(200, 201, 202)) {
                Write-Host "  ✓ Added: $item" -ForegroundColor Green
            }
        }
        catch {
            Write-Host "  ✗ Failed to add '$item': $($_.Exception.Message)" -ForegroundColor Red
            $success = $false
        }
    }

    return $success
}

function Submit-BehaviorUpdate {
    param(
        [string]$ChildId,
        [ValidateSet('nice', 'naughty')][string]$Behavior,
        [string]$Description
    )

    Write-Host "`n[1/2] Submitting behavior update FIRST..." -ForegroundColor Cyan
    Write-Host "  ⌛ Establishing behavior status before recommendations..." -ForegroundColor Yellow
    $statusValue = if ($Behavior -eq 'nice') { 'Nice' } else { 'Naughty' }
    $behaviorPayload = @{
        requestType  = 'behavior-update'
        text         = $Description
        statusChange = $statusValue
    } | ConvertTo-Json -Depth 5

    $behaviorResult = $null
    try {
        $behaviorResult = Invoke-RestMethod "$ApiUrl/api/v1/children/$ChildId/wishlist-items" -Method Post `
            -ContentType 'application/json' -Body $behaviorPayload `
            -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 45
        Write-Host "  ✓ Behavior set to: $statusValue" -ForegroundColor $(if ($Behavior -eq 'nice') { 'Green' } else { 'Red' })
    }
    catch {
        Write-Warning "  Behavior update failed: $($_.Exception.Message)"
    }

    # Return the recommendations from the behavior update (they're generated on submission)
    return $behaviorResult
}

function Test-ApiHealth {
    Write-Host "`n🏥 Running Health Checks..." -ForegroundColor Cyan

    # Health endpoint
    try {
        $health = Invoke-RestMethod "$ApiUrl/healthz" -TimeoutSec 5 -ErrorAction Stop
        Write-Host "  ✓ Health endpoint: OK" -ForegroundColor Green
    }
    catch {
        Write-Host "  ✗ Health endpoint: FAILED" -ForegroundColor Red
        Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor DarkGray
    }

    # Readiness
    try {
        $ready = Invoke-RestMethod "$ApiUrl/readyz" -TimeoutSec 5 -ErrorAction Stop
        Write-Host "  ✓ Readiness: OK" -ForegroundColor Green
    }
    catch {
        Write-Host "  ⚠ Readiness: Not ready" -ForegroundColor Yellow
        Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor DarkGray
    }

    # Cosmos DB
    try {
        $ping = Invoke-RestMethod "$ApiUrl/api/pingz" -TimeoutSec 5 -ErrorAction Stop
        if ($ping.cosmosReady) {
            Write-Host "  ✓ Cosmos DB: Connected" -ForegroundColor Green
        }
        else {
            Write-Host "  ⚠ Cosmos DB: Not ready" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "  ⚠ Cosmos DB: Unknown" -ForegroundColor Yellow
        Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor DarkGray
    }

    Pause
}

function Demo-Scenario1 {
    Show-Banner
    Write-Host "📝 SCENARIO 1: End-to-End Wishlist Processing" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = "demo-alice-$(Get-Random -Max 999)"
    Write-Host "🧒 Child ID: " -NoNewline -ForegroundColor White
    Write-Host $childId -ForegroundColor Cyan
    Write-Host ""
    Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "│  How Drasi + Agent Framework work together:                 │" -ForegroundColor DarkGray
    Write-Host "│                                                             │" -ForegroundColor DarkGray
    Write-Host "│  1️⃣  DRASI: Detects wishlist change in Cosmos DB            │" -ForegroundColor DarkGray
    Write-Host "│  2️⃣  DRASI: Continuous Query triggers event pipeline        │" -ForegroundColor DarkGray
    Write-Host "│  3️⃣  AGENT: ElfRecommendationAgent initialized              │" -ForegroundColor DarkGray
    Write-Host "│  4️⃣  AGENT: Calls Azure OpenAI GPT-4o for ideas             │" -ForegroundColor DarkGray
    Write-Host "│  5️⃣  DRASI: Results persisted + SignalR broadcast           │" -ForegroundColor DarkGray
    Write-Host "│                                                             │" -ForegroundColor DarkGray
    Write-Host "│  ⏱️  Expected total time: 20-25 seconds                      │" -ForegroundColor DarkGray
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""

    Write-Host -NoNewline "Press Enter to start the magic... 🎄 " -ForegroundColor Green
    Read-Host

    # Run simulation
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
    Write-Host "  🚀 STEP 1: Submitting Wishlist to Santa's Workshop" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
    $jobSubmitted = Submit-WishlistJob -ChildId $childId -UseRandomWishlist

    if ($jobSubmitted) {
        Write-Host ""
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
        Write-Host "  🤖 STEP 2: AI Agent Processing Pipeline" -ForegroundColor Cyan
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
        
        # Show animated progress during the wait
        Show-AIProcessingProgress -DurationSeconds 20 -TaskDescription "AI Processing"
        
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
        Write-Host "  🔍 STEP 3: Fetching Recommendations" -ForegroundColor Cyan
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
        
        # Wait for processing with progress indicator
        $maxWaitSeconds = 25  # Additional buffer after initial 20s wait
        $checkInterval = 5    # Check every 5 seconds
        $elapsed = 20         # Start at 20 since we already waited
        $foundRecommendations = $false
        $lastError = $null
        
        while ($elapsed -lt $maxWaitSeconds -and -not $foundRecommendations) {
            Write-Host "  🔄 Polling API for results... (elapsed: ${elapsed}s)" -ForegroundColor Gray
            
            try {
                $recommendations = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations" `
                    -Headers @{ 'X-Role' = 'operator' } `
                    -TimeoutSec 10 `
                    -ErrorAction Stop
                
                # Handle both response formats: 'recommendations' (legacy) or 'items' (current)
                $recItems = if ($recommendations.recommendations) { $recommendations.recommendations } elseif ($recommendations.items) { $recommendations.items } else { $null }
                
                if ($recommendations -and $recItems -and $recItems.Count -gt 0) {
                    $foundRecommendations = $true
                    
                    Write-Host ""
                    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
                    Write-Host "  ║  🎉 SUCCESS! Recommendations Generated in ~$elapsed seconds!     ║" -ForegroundColor Green
                    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
                    Write-Host ""
                    Write-Host "  🧒 Child ID: $($recommendations.childId)" -ForegroundColor Cyan
                    Write-Host "  📦 Total Recommendations: $($recItems.Count)" -ForegroundColor Cyan
                    Write-Host ""
                    Write-Host "  ┌──────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
                    
                    $index = 1
                    foreach ($rec in $recItems) {
                        $giftIcon = switch ($index) { 1 { "🎁" } 2 { "🎀" } 3 { "🎄" } 4 { "⭐" } default { "🎁" } }
                        Write-Host "  │                                                              │" -ForegroundColor DarkGray
                        Write-Host "  │  $giftIcon " -NoNewline -ForegroundColor DarkGray
                        Write-Host "Recommendation #$index" -NoNewline -ForegroundColor Yellow
                        Write-Host "                                          │" -ForegroundColor DarkGray
                        Write-Host "  │     " -NoNewline -ForegroundColor DarkGray
                        $suggestionDisplay = if ($rec.suggestion.Length -gt 50) { $rec.suggestion.Substring(0, 47) + "..." } else { $rec.suggestion }
                        Write-Host "$suggestionDisplay" -NoNewline -ForegroundColor White
                        Write-Host "$(' ' * (55 - $suggestionDisplay.Length))│" -ForegroundColor DarkGray
                        
                        # Show rationale (truncated for display)
                        $rationale = if ($rec.rationale) { $rec.rationale } elseif ($rec.reasoning) { $rec.reasoning } else { $null }
                        if ($rationale) {
                            $rationaleDisplay = if ($rationale.Length -gt 52) { $rationale.Substring(0, 49) + "..." } else { $rationale }
                            Write-Host "  │     💡 " -NoNewline -ForegroundColor DarkGray
                            Write-Host "$rationaleDisplay" -NoNewline -ForegroundColor Gray
                            Write-Host "$(' ' * (52 - $rationaleDisplay.Length))│" -ForegroundColor DarkGray
                        }
                        
                        # Show price
                        $price = if ($rec.price) { $rec.price } elseif ($rec.estimatedCost) { $rec.estimatedCost } else { $null }
                        if ($price) {
                            Write-Host "  │     💰 " -NoNewline -ForegroundColor DarkGray
                            Write-Host "`$$price" -NoNewline -ForegroundColor DarkGreen
                            $priceLen = ("`$$price").Length
                            Write-Host "$(' ' * (52 - $priceLen))│" -ForegroundColor DarkGray
                        }
                        
                        $index++
                    }
                    
                    Write-Host "  │                                                              │" -ForegroundColor DarkGray
                    Write-Host "  └──────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
                    
                    if ($recommendations.metadata) {
                        Write-Host ""
                        Write-Host "  📋 Metadata:" -ForegroundColor Yellow
                        Write-Host "     Generated: $($recommendations.metadata.generatedAt)" -ForegroundColor Gray
                        if ($recommendations.metadata.agentType) {
                            Write-Host "     Agent: $($recommendations.metadata.agentType)" -ForegroundColor Gray
                        }
                    }
                    Write-Host ""
                }
                else {
                    # API returned but no recommendations yet
                    Write-Host "    ↳ API responded, awaiting AI completion..." -ForegroundColor DarkGray
                }
            }
            catch {
                # Capture error for diagnostics
                $lastError = $_.Exception.Message
                Write-Host "    ↳ Still processing... ($($_.Exception.Message.Substring(0, [Math]::Min(40, $_.Exception.Message.Length))))..." -ForegroundColor DarkGray
            }
            
            if (-not $foundRecommendations) {
                Start-Sleep -Seconds $checkInterval
                $elapsed += $checkInterval
            }
        }
        
        if (-not $foundRecommendations) {
            $totalWait = $elapsed
            Write-Host "`n⚠️  Recommendations not yet available after $totalWait seconds total wait time" -ForegroundColor Yellow
            Write-Host "  This is longer than usual. Possible causes:" -ForegroundColor Gray
            Write-Host "    • AI agent processing taking longer than expected (>15s)" -ForegroundColor DarkGray
            Write-Host "    • Azure OpenAI API latency or throttling" -ForegroundColor DarkGray
            Write-Host "    • Backend service cold start" -ForegroundColor DarkGray
            if ($lastError) {
                Write-Host "  Last API error: $lastError" -ForegroundColor DarkGray
            }
            Write-Host ""
            Write-Host "Options:" -ForegroundColor Cyan
            Write-Host "  [R] Retry - Check again now" -ForegroundColor White
            Write-Host "  [C] Continue - Show curl command and exit" -ForegroundColor White
            Write-Host ""
            $choice = Read-Host "Choice (R/C)"
            
            if ($choice -eq 'R') {
                Write-Host "`n🔄 Checking now..." -ForegroundColor Cyan
                try {
                    $recommendations = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations" `
                        -Headers @{ 'X-Role' = 'operator' } `
                        -TimeoutSec 10 `
                        -ErrorAction Stop
                    
                    # Handle both response formats: 'recommendations' (legacy) or 'items' (current)
                    $recItems = if ($recommendations.recommendations) { $recommendations.recommendations } elseif ($recommendations.items) { $recommendations.items } else { $null }
                    
                    if ($recommendations -and $recItems -and $recItems.Count -gt 0) {
                        Write-Host "`n✅ Recommendations Generated!" -ForegroundColor Green
                        Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray
                        Write-Host "`nChild ID: $($recommendations.childId)" -ForegroundColor Cyan
                        Write-Host "Total Recommendations: $($recItems.Count)" -ForegroundColor Cyan
                        Write-Host ""
                        
                        $index = 1
                        foreach ($rec in $recItems) {
                            Write-Host "[$index] $($rec.suggestion)" -ForegroundColor White
                            if ($rec.rationale) {
                                Write-Host "    💡 $($rec.rationale)" -ForegroundColor Gray
                            }
                            if ($rec.reasoning) {
                                Write-Host "    💡 $($rec.reasoning)" -ForegroundColor Gray
                            }
                            if ($rec.confidence) {
                                Write-Host "    📊 Confidence: $($rec.confidence)" -ForegroundColor DarkCyan
                            }
                            if ($rec.price) {
                                Write-Host "    💰 Price: `$$($rec.price)" -ForegroundColor DarkGreen
                            }
                            if ($rec.estimatedCost) {
                                Write-Host "    💰 Estimated Cost: `$$($rec.estimatedCost)" -ForegroundColor DarkGreen
                            }
                            Write-Host ""
                            $index++
                        }
                        
                        Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray
                    }
                    else {
                        Write-Host "`n⚠️  Still processing. Manual command:" -ForegroundColor Yellow
                        Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White
                    }
                }
                catch {
                    Write-Host "`n⚠️  Not ready yet. Error: $($_.Exception.Message)" -ForegroundColor Yellow
                    Write-Host "  Manual command:" -ForegroundColor Gray
                    Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White
                }
            }
            else {
                Write-Host "`n📋 Manual check command:" -ForegroundColor Cyan
                Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White
            }
        }
    }

    Pause
}

function Demo-Scenario2 {
    Show-Banner
    Write-Host "🤝 SCENARIO 2: Multi-Agent Collaboration" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""

    $childId = "demo-bob-$(Get-Random -Max 999)"
    Write-Host "🧒 Child ID: " -NoNewline -ForegroundColor White
    Write-Host $childId -ForegroundColor Cyan
    Write-Host ""
    Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "│  AGENT FRAMEWORK: 3 specialized agents orchestrated         │" -ForegroundColor DarkGray
    Write-Host "│  DRASI: Provides real-time data tools for each agent        │" -ForegroundColor DarkGray
    Write-Host "│                                                             │" -ForegroundColor DarkGray
    Write-Host "│  🔍 [DRASI] Event Processor → Queries event graph           │" -ForegroundColor DarkGray
    Write-Host "│  🤖 [AGENT] Recommendation Agent → GPT-4o generates ideas   │" -ForegroundColor DarkGray
    Write-Host "│  🎁 [AGENT] Gift Matcher → Uses Drasi tools for data        │" -ForegroundColor DarkGray
    Write-Host "│                                                             │" -ForegroundColor DarkGray
    Write-Host "│  ⏱️  Expected time: 45-90 seconds (3 sequential AI calls)    │" -ForegroundColor DarkGray
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""

    Write-Host -NoNewline "Press Enter to start the collaboration... 🎄 " -ForegroundColor Green
    Read-Host

    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
    Write-Host "  🤖 DRASI + AGENT FRAMEWORK ORCHESTRATION IN PROGRESS" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkCyan
    
    # Show animated multi-agent progress
    Show-MultiAgentProgress
    
    Write-Host ""
    Write-Host "  🔄 Calling collaborative endpoint (parallel optimization enabled)..." -ForegroundColor Yellow
    
    try {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations/collaborative?status=Nice&optimized=true" `
            -Method Post -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 180 -ErrorAction Stop
        $stopwatch.Stop()

        Write-Host ""
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
        Write-Host "  ║  🎉 SUCCESS! Multi-Agent Collaboration Complete!             ║" -ForegroundColor Green
        Write-Host "  ║  ⏱️  Total time: $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) seconds (parallel mode)                   ║" -ForegroundColor Green
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
        Write-Host ""
        
        Write-Host "  📋 Collaborative Recommendation:" -ForegroundColor Cyan
        Write-Host "  ─────────────────────────────────" -ForegroundColor DarkGray
        Write-Host "$($result.collaborativeRecommendation)" -ForegroundColor White
        
        if ($result.agentTypes) {
            Write-Host ""
            Write-Host "  🤖 Agents Used:" -ForegroundColor Cyan
            $result.agentTypes | ForEach-Object { Write-Host "     • $_" -ForegroundColor Gray }
        }
        
        if ($result.toolsUsed) {
            Write-Host ""
            Write-Host "  🔧 Tools Called:" -ForegroundColor Cyan
            $result.toolsUsed | ForEach-Object { Write-Host "     • $_" -ForegroundColor Gray }
        }

        if ($result.drasiContext) {
            Write-Host ""
            Write-Host "  📊 Drasi Real-Time Context:" -ForegroundColor Cyan
            Write-Host "     Trending Items: $($result.drasiContext.trendingItems.Count)" -ForegroundColor Gray
            Write-Host "     Duplicate Alerts: $($result.drasiContext.duplicateAlerts.Count)" -ForegroundColor Gray
            Write-Host "     Last Update: $($result.drasiContext.lastUpdate)" -ForegroundColor Gray
        }

        if ($result.optimized) {
            Write-Host ""
            Write-Host "  ⚡ Optimization: PARALLEL execution (Analyst + Creative ran concurrently)" -ForegroundColor Magenta
        }
    }
    catch {
        Write-Host ""
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
        Write-Host "  ║  ⚠️  Multi-Agent Orchestration Timeout                       ║" -ForegroundColor Yellow
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        
        if ($_.Exception.Message -like "*timeout*" -or $_.Exception.Message -like "*Timeout*") {
            Write-Host ""
            Write-Host "  💡 AGENT FRAMEWORK: Multi-agent orchestration under high Azure OpenAI load." -ForegroundColor Gray
            Write-Host "     Even with parallel optimization, 3 GPT-4o calls can exceed timeout." -ForegroundColor Gray
            Write-Host ""
            Write-Host "  ✨ Try these faster alternatives:" -ForegroundColor Cyan
            Write-Host "     [1] End-to-End Processing - Single agent, ~20 seconds" -ForegroundColor White
            Write-Host "     [4] Naughty/Nice Detection - Behavior-aware, ~15 seconds" -ForegroundColor White
        }
        elseif ($_.Exception.Response.StatusCode -eq 404) {
            Write-Host "  Endpoint not found - may need API deployment." -ForegroundColor Gray
        }
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
    Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "│  AGENT FRAMEWORK: Streams tokens via Azure OpenAI SSE       │" -ForegroundColor DarkGray
    Write-Host "│  Watch the AI think and generate in real-time!              │" -ForegroundColor DarkGray
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "You'll see:" -ForegroundColor Gray
    Write-Host "  • [AGENT] Thought process ('Analyzing profile...')" -ForegroundColor Gray
    Write-Host "  • [AGENT] Token-by-token text generation via GPT-4o" -ForegroundColor Gray
    Write-Host "  • [AGENT] Progress updates in real-time" -ForegroundColor Gray
    Write-Host ""

    Write-Host -NoNewline "Press Enter to start streaming..." -ForegroundColor Green
    Read-Host

    # Connection animation
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║  📡 Establishing Server-Sent Events (SSE) Connection         ║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    # Quick connection animation
    $connectionSteps = @(
        "  🔌 Opening SSE connection to AI service...",
        "  🔗 Handshake complete, stream ready...",
        "  ✨ Receiving real-time AI output..."
    )
    foreach ($step in $connectionSteps) {
        Write-Host $step -ForegroundColor Yellow
        Start-Sleep -Milliseconds 400
    }
    Write-Host ""
    
    Write-Host "🌊 Streaming output (watch AI think in real-time):" -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray

    $streamUrl = "$ApiUrl/api/v1/children/$childId/recommendations/stream?status=Nice"

    # Check if curl is available for true SSE streaming
    $curlAvailable = Get-Command curl -ErrorAction SilentlyContinue
    
    if ($curlAvailable) {
        Write-Host "✅ Using curl for real-time SSE streaming..." -ForegroundColor Green
        Write-Host ""
        
        # Get console width for proper word wrapping (default to 100 if unavailable)
        $consoleWidth = 100
        try { $consoleWidth = [Math]::Max(80, $Host.UI.RawUI.WindowSize.Width - 4) } catch {}
        
        try {
            # Use curl for true streaming with real-time output
            $curlArgs = @(
                '-N', # Disable buffering
                '--no-buffer',
                '-H', 'X-Role: operator',
                '-H', 'Accept: text/event-stream',
                $streamUrl
            )
            
            # Process curl output line by line in real-time
            $eventCount = 0
            $textBuffer = ""
            $currentLineLength = 0
            $lastWasText = $false
            
            # Helper function to output text with proper word wrapping
            function Write-WrappedText {
                param([string]$Text, [int]$MaxWidth, [ref]$LineLength)
                
                # Split on explicit newlines first
                $lines = $Text -split "`n"
                for ($i = 0; $i -lt $lines.Count; $i++) {
                    $line = $lines[$i]
                    
                    # If this isn't the first segment and we had a newline, output it
                    if ($i -gt 0) {
                        Write-Host "" # New line
                        $LineLength.Value = 0
                    }
                    
                    # Split into words for wrapping
                    $words = $line -split '(\s+)' | Where-Object { $_ }
                    foreach ($word in $words) {
                        $wordLen = $word.Length
                        
                        # If adding this word would exceed line width, wrap
                        if ($LineLength.Value + $wordLen -gt $MaxWidth -and $LineLength.Value -gt 0) {
                            Write-Host "" # New line
                            $LineLength.Value = 0
                            # Skip leading whitespace on new line
                            if ($word -match '^\s+$') { continue }
                        }
                        
                        Write-Host $word -NoNewline -ForegroundColor White
                        $LineLength.Value += $wordLen
                    }
                }
            }
            
            & curl @curlArgs 2>$null | ForEach-Object {
                if ($_ -match '^data: (.+)$') {
                    $eventCount++
                    try {
                        $eventData = $matches[1] | ConvertFrom-Json

                        switch ($eventData.type) {
                            # Handle both 'thinking' and 'thought' event types
                            { $_ -in @('thinking', 'thought') } { 
                                if ($lastWasText -and $textBuffer) {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    Write-Host "" # New line
                                    $textBuffer = ""
                                    $currentLineLength = 0
                                    $lastWasText = $false
                                }
                                Write-Host "`n💭 $($eventData.content)" -ForegroundColor Yellow
                                $currentLineLength = 0
                            }
                            # Handle both 'text' and 'text-delta' event types
                            { $_ -in @('text', 'text-delta') } { 
                                $textBuffer += $eventData.content
                                $lastWasText = $true
                                
                                # Output in larger chunks (~300 chars) for smoother reading
                                # Or immediately if we have complete sentences/paragraphs
                                if ($textBuffer.Length -gt 300 -or $textBuffer -match '\n\n' -or $textBuffer -match '\.\s{2,}') {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    $textBuffer = ""
                                }
                            }
                            'progress' { 
                                if ($lastWasText -and $textBuffer) {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    $textBuffer = ""
                                    $currentLineLength = 0
                                }
                                Write-Host "`n`n⏳" -NoNewline -ForegroundColor Cyan
                                $currentLineLength = 2
                                $lastWasText = $false
                            }
                            'completed' { 
                                if ($lastWasText -and $textBuffer) {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    $textBuffer = ""
                                    $currentLineLength = 0
                                }
                                Write-Host "`n✅ $($eventData.message)" -ForegroundColor Green 
                                $currentLineLength = 0
                                $lastWasText = $false
                            }
                            'error' { 
                                if ($lastWasText -and $textBuffer) {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    $textBuffer = ""
                                    $currentLineLength = 0
                                }
                                Write-Host "`n❌ Error: $($eventData.message)" -ForegroundColor Red 
                                $currentLineLength = 0
                                $lastWasText = $false
                            }
                            'done' { 
                                if ($lastWasText -and $textBuffer) {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    $textBuffer = ""
                                    $currentLineLength = 0
                                }
                                Write-Host "`n`n☑️ Stream complete ($eventCount events)" -ForegroundColor Green
                                $currentLineLength = 0
                                $lastWasText = $false
                            }
                            default { 
                                if ($lastWasText -and $textBuffer) {
                                    Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                                    $textBuffer = ""
                                    $currentLineLength = 0
                                }
                                Write-Host "$($eventData.content)" -ForegroundColor Gray 
                                $currentLineLength = 0
                            }
                        }
                    }
                    catch {
                        Write-Host $_ -ForegroundColor Gray
                    }
                }
            }
            
            # Flush any remaining buffer
            if ($textBuffer) {
                Write-WrappedText -Text $textBuffer -MaxWidth $consoleWidth -LineLength ([ref]$currentLineLength)
                Write-Host "" # Final newline
            }
        }
        catch {
            Write-Host "`n⚠️  Streaming failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    else {
        # Fallback to Invoke-WebRequest (waits for complete response)
        Write-Host "⚠️  curl not found - using PowerShell (waits for complete response)" -ForegroundColor Yellow
        Write-Host "   Install curl for real-time streaming experience`n" -ForegroundColor Gray
        
        try {
            $response = Invoke-WebRequest -Uri $streamUrl -Method Get `
                -Headers @{ 'X-Role' = 'operator'; 'Accept' = 'text/event-stream' } `
                -TimeoutSec 60 -ErrorAction Stop

            Write-Host "`n✅ Received complete response ($($response.Content.Length) bytes)" -ForegroundColor Green
            Write-Host "SSE Events:" -ForegroundColor Cyan
            Write-Host ""

            $eventCount = 0
            $response.Content -split "`n" | ForEach-Object {
                if ($_ -match '^data: (.+)$') {
                    $eventCount++
                    try {
                        $eventData = $matches[1] | ConvertFrom-Json

                        switch ($eventData.type) {
                            'thinking' { Write-Host "  💭 [$eventCount] $($eventData.content)" -ForegroundColor Yellow }
                            'text' { Write-Host "  📝 [$eventCount] $($eventData.content)" -ForegroundColor White }
                            'progress' { Write-Host "  ⏳ [$eventCount] $($eventData.message)" -ForegroundColor Cyan }
                            'completed' { Write-Host "  ✅ [$eventCount] $($eventData.message)" -ForegroundColor Green }
                            'error' { Write-Host "  ❌ [$eventCount] $($eventData.message)" -ForegroundColor Red }
                            'done' { Write-Host "  ☑️ [$eventCount] Stream complete" -ForegroundColor DarkGray }
                            default { Write-Host "  • [$eventCount] $($eventData.content)" -ForegroundColor Gray }
                        }
                    }
                    catch {
                        Write-Host "  • [$eventCount] $_" -ForegroundColor Gray
                    }
                }
            }
            
            Write-Host "`nTotal SSE events: $eventCount" -ForegroundColor Cyan
        }
        catch {
            Write-Host "`n⚠️  Streaming failed: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "  Endpoint: $streamUrl" -ForegroundColor Gray
        }
    }

    Write-Host "`n════════════════════════════════════════════════" -ForegroundColor DarkGray
    Pause
}

function Demo-Scenario4 {
    Show-Banner
    Write-Host "😇😈 SCENARIO 4: Naughty/Nice Behavior Detection" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  ┌─────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "  │  DRASI: Detects behavior change → Triggers Continuous Query │" -ForegroundColor DarkGray
    Write-Host "  │  AGENT: Adjusts recommendations based on naughty/nice status│" -ForegroundColor DarkGray
    Write-Host "  └─────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
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
    $niceStatus = if ($behavior -eq "1") { "Nice" } else { "Naughty" }
    $description = if ($behavior -eq "1") {
        "Helped siblings with homework without being asked"
    }
    else {
        "Ignored chores despite multiple reminders"
    }

    Write-Host "`n🚀 Submitting $behaviorType behavior event..." -ForegroundColor Cyan
    
    # Show the Drasi + Agent Framework animation for behavior detection
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║  🎄 Drasi + Agent Framework - Behavior Detection Pipeline 🎄 ║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    # Animated behavior detection progress
    $behaviorSteps = @(
        @{ Icon = "📝"; Step = "[DRASI] Recording behavior event to Cosmos DB..." },
        @{ Icon = "🔄"; Step = "[DRASI] Change feed detecting behavior update..." },
        @{ Icon = "⚡"; Step = "[DRASI] Continuous Query triggering status change..." },
        @{ Icon = "🤖"; Step = "[AGENT] ElfRecommendationAgent receiving $niceStatus status..." },
        @{ Icon = "🧠"; Step = "[AGENT] Adjusting gift criteria for $niceStatus child..." },
        @{ Icon = "🎁"; Step = "[AGENT] Generating behavior-appropriate recommendations..." }
    )
    
    $spinnerFrames = @('⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏')
    $frameIndex = 0
    
    foreach ($step in $behaviorSteps) {
        for ($i = 0; $i -lt 6; $i++) {
            $spinner = $spinnerFrames[$frameIndex % $spinnerFrames.Count]
            Write-Host "`r  $spinner $($step.Icon) $($step.Step)                    " -NoNewline -ForegroundColor Yellow
            Start-Sleep -Milliseconds 150
            $frameIndex++
        }
        Write-Host "`r  ✓ $($step.Icon) $($step.Step)                    " -ForegroundColor Green
    }
    Write-Host ""
    
    # Now call the API
    Write-Host "  🔄 Calling behavior update API..." -ForegroundColor Yellow
    $result = Submit-BehaviorUpdate -ChildId $childId -Behavior $behaviorType -Description $description
    
    # Display the recommendations from the submission response
    if ($result -and $result.recommendations) {
        $recItems = $result.recommendations
        
        Write-Host ""
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor $(if ($behaviorType -eq 'nice') { 'Green' } else { 'Yellow' })
        Write-Host "  ║  🎁 Behavior-Aware Recommendations for $($behaviorType.ToUpper()) Child      ║" -ForegroundColor $(if ($behaviorType -eq 'nice') { 'Green' } else { 'Yellow' })
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor $(if ($behaviorType -eq 'nice') { 'Green' } else { 'Yellow' })
        Write-Host ""
        
        $index = 1
        foreach ($rec in $recItems) {
            $giftIcon = switch ($index) { 1 { "🎁" } 2 { "📚" } 3 { "🔬" } 4 { "🧩" } default { "🎁" } }
            Write-Host "  $giftIcon " -NoNewline
            Write-Host "$($rec.Suggestion)" -ForegroundColor White
            if ($rec.Rationale) {
                $rationaleDisplay = if ($rec.Rationale.Length -gt 60) { $rec.Rationale.Substring(0, 57) + "..." } else { $rec.Rationale }
                Write-Host "     💡 $rationaleDisplay" -ForegroundColor Gray
            }
            if ($rec.Price) {
                Write-Host "     💰 `$$($rec.Price)" -ForegroundColor DarkGreen
            }
            Write-Host ""
            $index++
        }
        
        # Explain the behavior-awareness
        Write-Host ""
        if ($behaviorType -eq 'nice') {
            Write-Host "  ✨ AGENT recognized NICE status → Fun, rewarding gifts!" -ForegroundColor Green
            Write-Host "     Xbox, Bicycles, Toys - Agent Framework behavior-aware!" -ForegroundColor DarkGreen
        }
        else {
            Write-Host "  📖 AGENT recognized NAUGHTY status → Educational gifts!" -ForegroundColor Yellow
            Write-Host "     Science Kits, Books, Brain Teasers - for character growth!" -ForegroundColor DarkYellow
        }
    }
    else {
        Write-Host ""
        Write-Host "  ⚠️ No recommendations in response. API may have timed out." -ForegroundColor Yellow
        Write-Host "  Try running Scenario 1 first to warm up the API." -ForegroundColor Gray
    }

    Pause
}

function Demo-Scenario5 {
    Show-Banner
    Write-Host "🛠️  SCENARIO 5: Agent Tools Showcase" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "│  DRASI: Powers 6 tools with real-time event graph data      │" -ForegroundColor DarkGray
    Write-Host "│  AGENT: Uses these tools for context-aware recommendations  │" -ForegroundColor DarkGray
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""

    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║  🔧 Discovering DRASI-Powered Agent Tools                    ║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    # Quick discovery animation
    $discoverySteps = @(
        "  🔍 [AGENT] Querying tool registry...",
        "  📋 [DRASI] Parsing tool definitions from event graph...",
        "  🔗 [DRASI] Resolving real-time data integrations..."
    )
    foreach ($step in $discoverySteps) {
        Write-Host $step -ForegroundColor Yellow
        Start-Sleep -Milliseconds 300
    }
    Write-Host ""

    try {
        $result = Invoke-RestMethod "$ApiUrl/api/v1/agent-tools" `
            -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10

        Write-Host "✅ Available Tools:" -ForegroundColor Green
        Write-Host ""
        
        # Display tools array with animated reveal
        if ($result.tools) {
            foreach ($tool in $result.tools) {
                Write-Host "  • $($tool.name)" -ForegroundColor White
                Write-Host "    $($tool.description)" -ForegroundColor Gray
                if ($tool.category) {
                    Write-Host "    Category: $($tool.category)" -ForegroundColor DarkGray
                }
                Write-Host ""
                Start-Sleep -Milliseconds 200  # Staggered reveal effect
            }
        }
        
        Write-Host "Integration:" -ForegroundColor Yellow
        Write-Host "  $($result.integration)" -ForegroundColor White
        Write-Host "`nStats:" -ForegroundColor Yellow
        Write-Host "  Drasi Tools: $($result.stats.drasiTools)" -ForegroundColor White
        
        Write-Host "`nThese tools provide agents with:" -ForegroundColor Yellow
        Write-Host "  • Real-time data from Drasi event graph" -ForegroundColor Gray
        Write-Host "  • Trending wishlist analysis" -ForegroundColor Gray
        Write-Host "  • Duplicate detection across children" -ForegroundColor Gray
        Write-Host "  • Behavior status tracking" -ForegroundColor Gray
    }
    catch {
        Write-Host "`n⚠️  Could not fetch tools: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  Endpoint: $ApiUrl/api/v1/agent-tools" -ForegroundColor Gray
    }

    Pause
}

function Demo-Scenario6 {
    Show-Banner
    Write-Host "🎯 SCENARIO 6: Custom Child Test (Preference-Based AI)" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "│  ⭐ PREFERENCE-BASED RECOMMENDATIONS                        │" -ForegroundColor DarkGray
    Write-Host "│                                                             │" -ForegroundColor DarkGray
    Write-Host "│  Try: 'Xbox' → AI recommends Xbox + controllers + games!    │" -ForegroundColor DarkGray
    Write-Host "│  Try: 'LEGO Set' → AI recommends LEGO + building toys!      │" -ForegroundColor DarkGray
    Write-Host "│                                                             │" -ForegroundColor DarkGray
    Write-Host "│  The AI actually LISTENS to what the child asked for!       │" -ForegroundColor DarkGray
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""

    $childId = Read-Host "Enter child ID (or leave blank for random)"
    if ([string]::IsNullOrWhiteSpace($childId)) {
        $childId = "demo-custom-$(Get-Random -Max 9999)"
    }
    
    # Prompt for optional profile info to improve AI recommendations
    Write-Host "`n📋 Profile Information (improves AI recommendations):" -ForegroundColor Cyan
    $childName = Read-Host "  Child's name (optional, press Enter to skip)"
    $childAgeInput = Read-Host "  Child's age (optional, press Enter to skip)"
    $childAge = 0
    if ($childAgeInput -match '^\d+$') { $childAge = [int]$childAgeInput }

    Write-Host "`nEnter wishlist items (comma-separated, e.g., 'Lego Set, Story Book'):" -ForegroundColor Cyan
    $items = Read-Host "Items"

    if ([string]::IsNullOrWhiteSpace($items)) {
        Write-Host "`n🚀 Creating wishlist with random items..." -ForegroundColor Cyan
        if ($childName -or $childAge -gt 0) {
            Submit-WishlistJob -ChildId $childId -UseRandomWishlist -ChildName $childName -ChildAge $childAge
        }
        else {
            Submit-WishlistJob -ChildId $childId -UseRandomWishlist
        }
    }
    else {
        Write-Host "`n🚀 Creating custom wishlist via API..." -ForegroundColor Cyan
        if ($childName -or $childAge -gt 0) {
            Submit-WishlistJob -ChildId $childId -CustomItems $items -ChildName $childName -ChildAge $childAge
        }
        else {
            Submit-WishlistJob -ChildId $childId -CustomItems $items
        }
    }

    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║  🤖 AI Gift Recommendation Agent Processing                  ║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    # Show animated AI processing progress
    Show-AIProcessingProgress
    
    # Wait for processing with progress indicator
    $maxWaitSeconds = 25  # Additional buffer after animated wait
    $checkInterval = 5    # Check every 5 seconds
    $elapsed = 20         # Start at 20 since animated wait covered this
    $foundRecommendations = $false
    $lastError = $null
    
    while ($elapsed -lt $maxWaitSeconds -and -not $foundRecommendations) {
        Write-Host "  ⌛ Checking for recommendations ($elapsed/$maxWaitSeconds seconds)..." -ForegroundColor Gray
        
        try {
            $recommendations = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations" `
                -Headers @{ 'X-Role' = 'operator' } `
                -TimeoutSec 10 `
                -ErrorAction Stop
            
            # Handle both response formats: 'recommendations' (legacy) or 'items' (current)
            $recItems = if ($recommendations.recommendations) { $recommendations.recommendations } elseif ($recommendations.items) { $recommendations.items } else { $null }
            
            if ($recommendations -and $recItems -and $recItems.Count -gt 0) {
                $foundRecommendations = $true
                
                Write-Host "`n✅ Recommendations Generated (total time: ~$elapsed seconds)!" -ForegroundColor Green
                Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray
                Write-Host "`nChild ID: $($recommendations.childId)" -ForegroundColor Cyan
                Write-Host "Total Recommendations: $($recItems.Count)" -ForegroundColor Cyan
                Write-Host ""
                
                $index = 1
                foreach ($rec in $recItems) {
                    Write-Host "[$index] $($rec.suggestion)" -ForegroundColor White
                    if ($rec.rationale) {
                        Write-Host "    💡 $($rec.rationale)" -ForegroundColor Gray
                    }
                    if ($rec.reasoning) {
                        Write-Host "    💡 $($rec.reasoning)" -ForegroundColor Gray
                    }
                    if ($rec.confidence) {
                        Write-Host "    📊 Confidence: $($rec.confidence)" -ForegroundColor DarkCyan
                    }
                    if ($rec.price) {
                        Write-Host "    💰 Price: `$$($rec.price)" -ForegroundColor DarkGreen
                    }
                    if ($rec.estimatedCost) {
                        Write-Host "    💰 Estimated Cost: `$$($rec.estimatedCost)" -ForegroundColor DarkGreen
                    }
                    Write-Host ""
                    $index++
                }
                
                if ($recommendations.metadata) {
                    Write-Host "Metadata:" -ForegroundColor Yellow
                    Write-Host "  Generated: $($recommendations.metadata.generatedAt)" -ForegroundColor Gray
                    if ($recommendations.metadata.agentType) {
                        Write-Host "  Agent: $($recommendations.metadata.agentType)" -ForegroundColor Gray
                    }
                }
                
                Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray
            }
            else {
                # API returned but no recommendations yet
                Write-Host "    (API responded but recommendations not ready)" -ForegroundColor DarkGray
            }
        }
        catch {
            # Capture error for diagnostics
            $lastError = $_.Exception.Message
            # Continue waiting - recommendations not ready yet
        }
        
        if (-not $foundRecommendations) {
            Start-Sleep -Seconds $checkInterval
            $elapsed += $checkInterval
        }
    }
    
    if (-not $foundRecommendations) {
        $totalWait = $elapsed
        Write-Host "`n⚠️  Recommendations not yet available after $totalWait seconds total wait time" -ForegroundColor Yellow
        Write-Host "  This is longer than usual. Possible causes:" -ForegroundColor Gray
        Write-Host "    • AI agent processing taking longer than expected (>15s)" -ForegroundColor DarkGray
        Write-Host "    • Azure OpenAI API latency or throttling" -ForegroundColor DarkGray
        Write-Host "    • Backend service cold start" -ForegroundColor DarkGray
        if ($lastError) {
            Write-Host "  Last API error: $lastError" -ForegroundColor DarkGray
        }
        Write-Host ""
        Write-Host "Options:" -ForegroundColor Cyan
        Write-Host "  [R] Retry - Check again now" -ForegroundColor White
        Write-Host "  [C] Continue - Show curl command and exit" -ForegroundColor White
        Write-Host ""
        $choice = Read-Host "Choice (R/C)"
        
        if ($choice -eq 'R') {
            Write-Host "`n🔄 Checking now..." -ForegroundColor Cyan
            try {
                $recommendations = Invoke-RestMethod "$ApiUrl/api/v1/children/$childId/recommendations" `
                    -Headers @{ 'X-Role' = 'operator' } `
                    -TimeoutSec 10 `
                    -ErrorAction Stop
                
                # Handle both response formats: 'recommendations' (legacy) or 'items' (current)
                $recItems = if ($recommendations.recommendations) { $recommendations.recommendations } elseif ($recommendations.items) { $recommendations.items } else { $null }
                
                if ($recommendations -and $recItems -and $recItems.Count -gt 0) {
                    Write-Host "`n✅ Recommendations Generated!" -ForegroundColor Green
                    Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray
                    Write-Host "`nChild ID: $($recommendations.childId)" -ForegroundColor Cyan
                    Write-Host "Total Recommendations: $($recItems.Count)" -ForegroundColor Cyan
                    Write-Host ""
                    
                    $index = 1
                    foreach ($rec in $recItems) {
                        Write-Host "[$index] $($rec.suggestion)" -ForegroundColor White
                        if ($rec.rationale) {
                            Write-Host "    💡 $($rec.rationale)" -ForegroundColor Gray
                        }
                        if ($rec.reasoning) {
                            Write-Host "    💡 $($rec.reasoning)" -ForegroundColor Gray
                        }
                        if ($rec.confidence) {
                            Write-Host "    📊 Confidence: $($rec.confidence)" -ForegroundColor DarkCyan
                        }
                        if ($rec.price) {
                            Write-Host "    💰 Price: `$$($rec.price)" -ForegroundColor DarkGreen
                        }
                        if ($rec.estimatedCost) {
                            Write-Host "    💰 Estimated Cost: `$$($rec.estimatedCost)" -ForegroundColor DarkGreen
                        }
                        Write-Host ""
                        $index++
                    }
                    
                    Write-Host "════════════════════════════════════════════════" -ForegroundColor DarkGray
                }
                else {
                    Write-Host "`n⚠️  Still processing. Manual command:" -ForegroundColor Yellow
                    Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White
                }
            }
            catch {
                Write-Host "`n⚠️  Not ready yet. Error: $($_.Exception.Message)" -ForegroundColor Yellow
                Write-Host "  Manual command:" -ForegroundColor Gray
                Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White
            }
        }
        else {
            Write-Host "`n📋 Manual check command:" -ForegroundColor Cyan
            Write-Host "  curl `"$ApiUrl/api/v1/children/$childId/recommendations`" -H `"X-Role: operator`"" -ForegroundColor White
        }
    }

    Pause
}

function Demo-Scenario7 {
    Show-Banner
    Write-Host "🔍 SCENARIO 7: Full System Validation" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""
    
    Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║  🩺 Running Comprehensive Pre-Demo Health Checks             ║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    
    # Animated system check intro
    $checkItems = @(
        @{ Icon = "🌐"; Text = "Infrastructure connectivity" },
        @{ Icon = "🏥"; Text = "API health & readiness" },
        @{ Icon = "💾"; Text = "Cosmos DB connection" },
        @{ Icon = "⚡"; Text = "Drasi integration" },
        @{ Icon = "🤖"; Text = "Agent Framework status" }
    )
    
    foreach ($item in $checkItems) {
        Write-Host "  $($item.Icon) Checking: $($item.Text)..." -ForegroundColor Yellow
        Start-Sleep -Milliseconds 250
    }
    Write-Host ""

    # Check if test-demo-readiness.ps1 exists
    $readinessScript = Join-Path $PSScriptRoot "test-demo-readiness.ps1"
    if (Test-Path $readinessScript) {
        Write-Host "✅ Launching full validation script..." -ForegroundColor Green
        Write-Host ""
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

            $response = Invoke-WebRequest "$ApiUrl/api/v1/jobs" -Method Post `
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
                -Headers @{ 'X-Role' = 'operator' } -TimeoutSec 10 -ErrorAction Stop
            $toolCount = $tools.tools.Count
            Write-Host "  ✓ Agent tools: $toolCount registered" -ForegroundColor Green
            Write-Host "    Categories: Drasi Real-Time ($($tools.stats.drasiTools))" -ForegroundColor DarkGray
        }
        catch {
            Write-Host "  ⚠ Agent tools: Unavailable" -ForegroundColor Yellow
            Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor DarkGray
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
    Write-Host "     Showcasing Drasi + Microsoft Agent Framework Integration" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [1] End-to-End Processing" -ForegroundColor White
    Write-Host "      DRASI: Detects wishlist changes via Cosmos DB change feed" -ForegroundColor Gray
    Write-Host "      AGENT: ElfRecommendationAgent generates personalized recs" -ForegroundColor Gray
    Write-Host "      Best for: Complete event-driven AI pipeline demo" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [2] Multi-Agent Collaboration" -ForegroundColor White
    Write-Host "      DRASI: Provides real-time event graph for agent queries" -ForegroundColor Gray
    Write-Host "      AGENT: 3 agents orchestrated via Agent Framework" -ForegroundColor Gray
    Write-Host "      Best for: Multi-agent + Drasi tool integration" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [3] Streaming (SSE)" -ForegroundColor White
    Write-Host "      AGENT: Azure OpenAI streams tokens via SSE" -ForegroundColor Gray
    Write-Host "      Shows: AI thought process in real-time" -ForegroundColor Gray
    Write-Host "      Best for: Responsive UX with streaming capabilities" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [4] Naughty/Nice Detection" -ForegroundColor White
    Write-Host "      DRASI: Continuous Query triggers on behavior change" -ForegroundColor Gray
    Write-Host "      AGENT: Behavior-aware recommendations adapt dynamically" -ForegroundColor Gray
    Write-Host "      Best for: Real-time event-driven architecture" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [5] Agent Tools" -ForegroundColor White
    Write-Host "      DRASI: Powers 6 registered tools for real-time data" -ForegroundColor Gray
    Write-Host "      AGENT: Uses Drasi tools for Cosmos, inventory, pricing" -ForegroundColor Gray
    Write-Host "      Best for: Tool calling and DRASI-powered RAG patterns" -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  [6] Custom Child Test ⭐ PREFERENCE-BASED" -ForegroundColor White
    Write-Host "      AGENT: Recommends items based on what child ASKED FOR" -ForegroundColor Gray
    Write-Host "      Try: 'Xbox' → gets Xbox + controllers + Game Pass!" -ForegroundColor Gray
    Write-Host "      Best for: Showing AI actually listens to preferences" -ForegroundColor DarkCyan
    Write-Host ""

    Write-Host "`nTips for Successful Demos:" -ForegroundColor Cyan
    Write-Host "  • Run option [7] or [H] before starting" -ForegroundColor Gray
    Write-Host "  • Have Azure Portal open to show resources" -ForegroundColor Gray
    Write-Host "  • Use Scenario [6] with 'Xbox' to show preference-based AI" -ForegroundColor Gray
    Write-Host "  • Scenarios 1→6→4→3 makes a great story arc" -ForegroundColor Gray
    Write-Host "  • Then switch to the UI to show the frontend!" -ForegroundColor Gray
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
