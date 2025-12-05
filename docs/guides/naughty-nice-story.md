# Letter to the North Pole - Naughty/Nice Tracking

This document describes the festive story-driven architecture where children send letters to the North Pole, and their behavior (naughty or nice) affects gift recommendations.

> ⚠️ **Script Migration Notice**: The `simulate-naughty-nice.ps1` script has been consolidated into `demo-interactive.ps1`. Use Scenario 4 in the interactive demo for Naughty/Nice testing.

## Story Overview

Children write letters to the North Pole requesting gifts. Santa's workshop uses Drasi to detect behavior changes in real-time:
- When a child becomes **Nice**: Recommendations are enhanced, showing they deserve great rewards
- When a child becomes **Naughty**: Agent Framework updates recommendations toward character-building "Goal" items (educational, improving behavior)

## Architecture

### Components

1. **Letter to North Pole** (replaces wishlist)
   - Children submit letters with `requestType`:
     - `"gift"` - Gift requests
     - `"behavior-update"` - Behavior status changes
   
2. **Drasi Continuous Queries**
   - `letter-updates`: Monitors all letter submissions
   - `naughty-nice-changes`: Specifically tracks behavior status changes
   
3. **Agent Framework Integration**
   - `NaughtyNiceEventHandler`: Processes status changes
   - Updates child profiles with new status
   - Adjusts recommendations based on behavior

4. **Child Profile with Status**
   ```csharp
   public enum NiceStatus { Nice, Naughty, Unknown }
   
   public record ChildProfile(
       string Id,
       string? Name,
       int? Age,
       string[]? Preferences,
       Constraints? Constraints,
       PrivacyFlags? PrivacyFlags,
       NiceStatus Status = NiceStatus.Unknown
   );
   ```

### Data Flow

```
Letter to North Pole
    ↓
Drasi Detection (behavior-update)
    ↓
naughty-nice-changes Query
    ↓
NaughtyNiceEventHandler
    ↓
Update ChildProfile.Status
    ↓
Agent Framework Adjusts Recommendations
    ↓
Report Generation with Status
```

## Testing the Scenario

### Using the Interactive Demo (Recommended)

```powershell
# Launch the interactive demo
.\scripts\demo-interactive.ps1

# Select [4] for Naughty/Nice Behavior Detection
# Choose nice or naughty from the submenu
```

### Manual Event Submission (Advanced)

```powershell
# Send a gift request via Event Hub
.\scripts\send-wishlist-event.ps1 -ChildId "child-123" -Items "Cleaned room:1" -Hub "wishlist-events"

# Submit behavior update via API
$behaviorUpdate = @{
    childId = "child-123"
    schemaVersion = "v1"
    requestType = "behavior-update"
    statusChange = "Nice"
    itemName = "Helped with chores"
    dedupeKey = "child-123:behavior:$(Get-Date -Format 'yyyyMMddHHmmss')"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:8080/api/jobs" `
    -Method Post `
    -ContentType "application/json" `
    -Body $behaviorUpdate `
    -Headers @{ "X-Role" = "operator" }
```

## Drasi Configuration

### Continuous Query: naughty-nice-changes

```yaml
kind: ContinuousQuery
apiVersion: v1
name: naughty-nice-changes
spec:
    mode: query
    queryLanguage: Cypher
    sources:
        subscriptions:
            - id: wishlist-eh
    query: >
        MATCH (l:`wishlist-events`)
        WHERE l.requestType = 'behavior-update' AND l.statusChange IS NOT NULL
        RETURN l.childId AS childId, l.statusChange AS newStatus, l.itemName AS behaviorDescription
```

## Agent Framework Recommendation Updates

When a status change is detected:

1. **Naughty → Nice Transition**
   - Recommendations enhanced with encouraging messages
   - Example: "Great behavior deserves great rewards: [Gift Name]"

2. **Nice → Naughty Transition**
   - Recommendations adjusted to "Goal" items (educational/character-building)
   - Example: "Given recent behavior, recommend character-building alternative: [Educational Item]"

## API Endpoints

### Submit Letter (Gift Request)
```http
POST /api/v1/children/{childId}/wishlist-items
Content-Type: application/json

{
  "text": "LEGO Space Shuttle",
  "category": "toys",
  "budgetEstimate": 79.99
}
```

### Submit Letter (Behavior Update)
```http
POST /api/jobs
Content-Type: application/json

{
  "childId": "child-123",
  "schemaVersion": "v1",
  "requestType": "behavior-update",
  "statusChange": "Nice",
  "itemName": "Helped with chores",
  "dedupeKey": "child-123:behavior:20251124"
}
```

## Festive Benefits

This architecture tells a better story:
- **Real-time behavior tracking**: Drasi instantly detects naughty/nice changes
- **Dynamic recommendations**: Agent Framework adapts suggestions based on behavior
- **Encouraging feedback**: System provides festive, educational responses
- **Character building**: Naughty status guides toward improvement, not punishment

## Future Enhancements

- Behavior scoring system (accumulate points)
- Historical behavior tracking
- Parent/guardian notifications on status changes
- Redemption pathways (naughty → nice progression)
- Integration with actual gift inventory for "Goal" items
