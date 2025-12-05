# Quick Start: Naughty/Nice Letter System

> ⚠️ **Script Migration Notice**: The `simulate-naughty-nice.ps1` script has been consolidated into `demo-interactive.ps1`. Use `.\scripts\demo-interactive.ps1` and select Scenario 4 for Naughty/Nice testing.

## Overview
This guide shows how to use the new letter to North Pole system with real-time naughty/nice behavior tracking.

## Prerequisites
- Backend service running (`dotnet run --project src`)
- Drasi configured with updated continuous queries
- Azure OpenAI endpoint configured

## Scenario 1: Child Sends Gift Request

### Submit a Letter for Gifts
```powershell
$giftLetter = @{
    text = "LEGO Space Shuttle"
    category = "toys"
    budgetEstimate = 79.99
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:8080/api/v1/children/child-123/wishlist-items" `
    -Method Post `
    -ContentType "application/json" `
    -Body $giftLetter
```

**Response:**
```json
{
  "wishlistItem": {
    "id": "...",
    "childId": "child-123",
    "itemName": "LEGO Space Shuttle",
    "category": "toys"
  },
  "recommendationSetId": "...",
  "recommendations": [...]
}
```

## Scenario 2: Child's Behavior Changes

### Using the Interactive Demo (Recommended)

```powershell
# Launch the interactive demo
.\scripts\demo-interactive.ps1

# Select [4] for Naughty/Nice Behavior Detection
# Follow the prompts to test nice or naughty behavior
```

### A. Child Becomes Nice ✨

**What Happens:**
1. Letter sent to North Pole with behavior update
2. Drasi detects `behavior-update` event via `naughty-nice-changes` query
3. `NaughtyNiceEventHandler` processes the change
4. Child profile updated to `NiceStatus.Nice`
5. Agent Framework enhances recommendations: "Great behavior deserves great rewards: [Gift]"
6. Report generated with encouraging messages

### B. Child Becomes Naughty 😞

**What Happens:**
1. Letter sent to North Pole with behavior update
2. Drasi detects `behavior-update` event
3. `NaughtyNiceEventHandler` processes the change
4. Child profile updated to `NiceStatus.Naughty`
5. Agent Framework adjusts recommendations to character-building items: "Given recent behavior, recommend character-building alternative: [Educational Item]"
6. Report generated with constructive guidance

## Scenario 3: Check Child's Current Status

### Via API Endpoint (Future)
```powershell
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/children/child-123/profile"
```

**Response:**
```json
{
  "id": "child-123",
  "name": null,
  "age": null,
  "status": "Nice",
  "preferences": [],
  "constraints": { "budget": null },
  "privacyFlags": { "optOut": false }
}
```

## Scenario 4: View Generated Report

Reports are generated automatically after behavior changes:

```powershell
# Check reports directory
Get-ChildItem reports/*.md

# View a specific child's report
Get-Content reports/child-alice-drasi.md
```

**Report Contents:**
- Child summary
- Current behavior status (Nice/Naughty/Unknown)
- Gift recommendations (adjusted based on status)
- Rationale from Agent Framework
- Festive disclaimer

## Manual Event Submission (Advanced)

### Submit via Event Hub

```powershell
# Gift request
.\scripts\send-wishlist-event.ps1 `
    -ChildId "child-123" `
    -Items "Train:1,Drone:1" `
    -Hub "wishlist-events"

# Behavior update (requires modified sender)
# Coming soon: --RequestType "behavior-update" --Status "Nice"
```

### Direct Job API

```powershell
$behaviorUpdate = @{
    childId = "child-charlie"
    schemaVersion = "v1"
    requestType = "behavior-update"
    statusChange = "Nice"
    itemName = "Helped with holiday decorations"
    dedupeKey = "child-charlie:behavior:$(Get-Date -Format 'yyyyMMddHHmmss')"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:8080/api/jobs" `
    -Method Post `
    -ContentType "application/json" `
    -Body $behaviorUpdate `
    -Headers @{ "X-Role" = "operator" }
```

## Monitoring

### Check Drasi Queries

```powershell
# List all continuous queries
drasi list continuousqueries

# Should see:
# - letter-updates
# - naughty-nice-changes
```

### View Drasi Results

```powershell
# Watch for behavior changes
drasi query get naughty-nice-changes --watch
```

### Application Logs

```powershell
# Backend logs show:
# - "Processing naughty/nice status change for child {ChildId}"
# - "Updated profile status for child {ChildId} to {Status}"
# - "Updated recommendation {RecId} for child {ChildId} based on {Status} status"
```

## Troubleshooting

### Behavior Update Not Detected
1. Verify Drasi is running: `kubectl get pods -n drasi-system`
2. Check continuous query: `drasi query get naughty-nice-changes`
3. Verify Event Hub connection
4. Check application logs for handler errors

### Recommendations Not Updated
1. Ensure `NaughtyNiceEventHandler` is registered in DI container
2. Verify Agent Framework endpoint (AZURE_OPENAI_ENDPOINT)
3. Check recommendation service is accessible
4. Review handler logs for exceptions

### Report Not Generated
1. Wait full 90 seconds (simulator waits automatically)
2. Check `reports/` directory exists
3. Verify job processing succeeded
4. Check Cosmos DB for job status

## Testing Checklist

- [ ] Submit gift request letter
- [ ] Verify recommendations generated
- [ ] Submit "nice" behavior update
- [ ] Confirm status changed to Nice
- [ ] Verify recommendations enhanced with positive messages
- [ ] Submit "naughty" behavior update
- [ ] Confirm status changed to Naughty
- [ ] Verify recommendations adjusted to "Goal" items
- [ ] Check generated report reflects status
- [ ] Verify Drasi queries are running

## Next Steps

1. **Test the Full Journey**: Run both nice and naughty scenarios
2. **Review Reports**: Check how recommendations differ by status
3. **Monitor Drasi**: Watch real-time event detection
4. **Extend Scenarios**: Add more behavior descriptions
5. **Integrate with UI**: Display status in frontend

## Festive Tips 🎄

- Use descriptive behavior descriptions for better Agent Framework responses
- Test both transitions (nice→naughty and naughty→nice) to see recommendation changes
- Review generated reports to see how Agent Framework adapts language
- Consider seasonal behaviors: "Helped wrap presents", "Shared holiday cookies"
- Remember: System encourages improvement, not punishment!

## Example Complete Workflow

```powershell
# RECOMMENDED: Use the interactive demo
.\scripts\demo-interactive.ps1
# Select [4] for Naughty/Nice Behavior Detection
# Follow the menu prompts for testing nice and naughty behaviors

# OR use direct API calls:

# 1. Child starts with gift request
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/children/child-demo/wishlist-items" `
    -Method Post -ContentType "application/json" `
    -Body '{"text":"Remote Control Car","category":"toys","budgetEstimate":45.00}'

# 2. Submit behavior update via API
$behaviorUpdate = @{
    childId = "child-demo"
    schemaVersion = "v1"
    requestType = "behavior-update"
    statusChange = "Nice"
    itemName = "Completed all homework on time"
    dedupeKey = "child-demo:behavior:$(Get-Date -Format 'yyyyMMddHHmmss')"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:8080/api/jobs" `
    -Method Post -ContentType "application/json" `
    -Body $behaviorUpdate -Headers @{ "X-Role" = "operator" }

# 3. View recommendations
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/children/child-demo/recommendations" `
    -Headers @{ "X-Role" = "operator" }
```

## Support

For issues or questions:
1. Check `IMPLEMENTATION-NAUGHTY-NICE.md` for architecture details
2. Review `NAUGHTY-NICE-STORY.md` for story overview
3. Check application logs for errors
4. Verify Drasi configuration in `drasi/resources/continuous-queries.yaml`
