# Drasi + Agent Framework Integration - Tool Calling Demo

## 🚀 What's New

We've added **4 powerful Drasi query tools** to the Agent Framework, enabling agents to directly query real-time event patterns during their reasoning process.

## 🎯 New Drasi Tools for Agents

### 1. QueryTrendingWishlistItems

- **Purpose**: Gets the most popular wishlist items trending RIGHT NOW
- **Source**: Drasi continuous query wishlist-trending-1h
- **Use Case**: Agents can recommend items based on what's popular globally

### 2. FindChildrenWithDuplicateWishlists

- **Purpose**: Finds children who have requested the same item multiple times
- **Source**: Drasi continuous query wishlist-duplicates-by-child
- **Use Case**: Strong interest signal - prioritize these items in recommendations

### 3. FindInactiveChildren

- **Purpose**: Identifies children with 3+ days of no activity
- **Source**: Drasi continuous query wishlist-inactive-children-3d
- **Use Case**: Proactive follow-up and engagement

### 4. QueryGlobalWishlistDuplicates

- **Purpose**: Shows items requested by multiple children globally
- **Source**: Drasi continuous query wishlist-duplicates-global
- **Use Case**: Inventory planning and bulk ordering insights

## 📊 Demo Endpoints

### GET /api/v1/drasi-agent-demo/{childId}

**NEW endpoint** that demonstrates all 4 Drasi tools in action. Shows:

- Real-time trending items
- Child-specific duplicate requests
- Global popularity patterns
- Inactive children alerts

Example:

```powershell
# Replace localhost:8080 with your API URL if deployed
curl http://localhost:8080/api/v1/drasi-agent-demo/child-123
```

### GET /api/v1/agent-tools

Lists all 9 available agent tools (5 original + 4 new Drasi tools)

### POST /api/v1/children/{childId}/recommendations/collaborative

Multi-agent orchestration **now uses Drasi tools** during analysis

## 🎭 How Agents Use Drasi Tools

Agents automatically call these tools during reasoning:

1. **Behavior Analyst Agent** checks:

   - `FindChildrenWithDuplicateWishlists(childId)` → Strong preferences
   - `QueryTrendingWishlistItems()` → Popular items
   - `GetChildBehaviorHistory(childId)` → Profile data

2. **Creative Gift Agent** validates:

   - `SearchGiftInventory()` → Real inventory
   - `CheckBudgetConstraints()` → Price validation
   - `GetGiftAvailability()` → Stock levels

3. **Quality Reviewer Agent** synthesizes all data

## 🔧 Technical Implementation

### AgentToolLibrary.cs

Added 4 new `[AIFunction]` decorated methods that query Drasi:

```csharp
[Description("Gets trending wishlist items from Drasi")]
public async Task<string> QueryTrendingWishlistItems(
    int minFrequency = 1,
    CancellationToken ct = default)
{
    var results = await _drasiClient.GetCurrentResultAsync(
        "default",
        "wishlist-trending-1h",
        ct);
    // ... format and return insights
}
```

### MultiAgentOrchestrator.cs

Updated agent initialization to include Drasi tools:

```csharp
IList<AITool> tools = new List<AITool>
{
    // ... original tools
    AIFunctionFactory.Create(_toolLibrary.QueryTrendingWishlistItems),
    AIFunctionFactory.Create(_toolLibrary.FindChildrenWithDuplicateWishlists),
    AIFunctionFactory.Create(_toolLibrary.FindInactiveChildren),
    AIFunctionFactory.Create(_toolLibrary.QueryGlobalWishlistDuplicates)
};
```

## 📈 Benefits

1. **Grounded Recommendations**: Agents use REAL event data, not hallucinations
2. **Real-Time Insights**: Sub-5-second latency from Drasi event graph
3. **Pattern Detection**: Agents can identify trends across event streams
4. **Enterprise Architecture**: Proper tool calling + event-driven processing
5. **Compelling Demo**: Shows Drasi + Agent Framework working together

## 🧪 Testing

1. **Start the API**: `dotnet run --project src` (or deploy with `azd up`)
2. **Send wishlist events** to generate Drasi data (use `scripts/demo-interactive.ps1`)
3. **Call demo endpoint**: `GET /api/v1/drasi-agent-demo/child-123`
4. **Run collaborative recommendation**: `POST /api/v1/children/child-123/recommendations/collaborative`
5. **Check agent tools**: `GET /api/v1/agent-tools`

**Tip**: Use `$API_URL` environment variable or replace `localhost:8080` with your deployed Container App URL.

## 🎯 Demo Script

1. **Show Event**: Submit wishlist via frontend or API
2. **Drasi Detection**: Query detects trend in <5s
3. **Agent Tool Calling**: Agent calls `QueryTrendingWishlistItems()`
4. **Grounded Response**: Agent generates recommendation based on REAL data
5. **Visual Pipeline**: Show event → detection → tool call → reasoning → result

This demonstrates the **complete integration** of event-driven detection (Drasi) with AI reasoning (Agent Framework).

---

**Status**: ✅ Fully Implemented and Operational
**Files Modified**:

- `src/services/AgentToolLibrary.cs`
- `src/services/MultiAgentOrchestrator.cs`
- `src/services/EnhancedAgentApi.cs`
