using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;
using Drasicrhsit.Services;
using System.Text.Json.Serialization;

namespace Services;

/// <summary>
/// API endpoints for enhanced agent capabilities: multi-agent orchestration and streaming
/// </summary>
public static class EnhancedAgentApi
{
    public static IEndpointRouteBuilder MapEnhancedAgentApi(this IEndpointRouteBuilder app)
    {
        // Multi-agent collaborative recommendation with real-time Drasi context
        app.MapPost("children/{childId}/recommendations/collaborative", async (
            string childId,
            IMultiAgentOrchestrator orchestrator,
            IDrasiRealtimeService drasiRealtime,
            NiceStatus? status,
            CancellationToken ct) =>
        {
            NiceStatus effectiveStatus = status ?? NiceStatus.Unknown;

            // Get real-time Drasi context for agent enrichment
            var drasiContext = await drasiRealtime.GetLatestContextAsync(ct);

            string result = await orchestrator.RunCollaborativeRecommendationAsync(childId, effectiveStatus, ct);

            return Results.Ok(new
            {
                childId,
                status = effectiveStatus.ToString(),
                collaborativeRecommendation = result,
                agentTypes = new[] { "BehaviorAnalyst", "CreativeGiftElf", "QualityReviewerElf" },
                toolsUsed = new[] { "GetChildBehaviorHistory", "SearchGiftInventory", "CheckBudgetConstraints", "QueryDrasiGraph" },
                drasiContext = new
                {
                    trendingItems = drasiContext.TrendingItems.Take(5),
                    duplicateAlerts = drasiContext.Duplicates.Where(d => d.ChildId == childId),
                    lastUpdate = drasiContext.LastUpdate
                }
            });
        })
        .WithName("CollaborativeRecommendation")
        .WithTags("Frontend", "EnhancedAgents")
        .WithDescription("Generate recommendations using multi-agent collaboration (Analyst → Creative → Reviewer)")
        .Produces<object>(StatusCodes.Status200OK);

        // Streaming agent response (SSE)
        app.MapGet("children/{childId}/recommendations/stream", async (
            string childId,
            HttpContext httpContext,
            IStreamingAgentService streamingAgent,
            NiceStatus? status,
            CancellationToken ct) =>
        {
            // Set up SSE headers
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            NiceStatus effectiveStatus = status ?? NiceStatus.Unknown;

            try
            {
                await foreach (StreamingAgentUpdate update in streamingAgent.StreamRecommendationGenerationAsync(
                    childId,
                    effectiveStatus,
                    ct))
                {
                    // Write SSE event
                    string eventData = System.Text.Json.JsonSerializer.Serialize(update);
                    await httpContext.Response.WriteAsync($"data: {eventData}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);

                    // Break if completed
                    if (update.Type == "completed" || update.Type == "error" || update.Type == "cancelled")
                    {
                        break;
                    }
                }

                // Send final completion event
                await httpContext.Response.WriteAsync("data: {\"type\":\"done\"}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                string errorData = System.Text.Json.JsonSerializer.Serialize(new { type = "error", message = ex.Message });
                await httpContext.Response.WriteAsync($"data: {errorData}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            return Results.Empty;
        })
        .WithName("StreamingRecommendation")
        .WithTags("Frontend", "EnhancedAgents")
        .WithDescription("Stream recommendation generation in real-time (Server-Sent Events)")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        // Tool demo endpoint - shows Drasi-powered tools only
        app.MapGet("agent-tools", (AgentToolLibrary toolLibrary) =>
        {
            return Results.Ok(new
            {
                tools = new object[]
                {
                    // Drasi Real-Time Event Graph Tools 🚀
                    new { name = "QueryTrendingWishlistItems", description = "🔥 Gets most popular wishlist items trending RIGHT NOW from Drasi", parameters = new[] { "minFrequency?" }, category = "Drasi Real-Time", source = "wishlist-trending-1h" },
                    new { name = "FindChildrenWithDuplicateWishlists", description = "🔁 Finds children requesting same item multiple times", parameters = new[] { "childId?" }, category = "Drasi Real-Time", source = "wishlist-duplicates-by-child" },
                    new { name = "FindInactiveChildren", description = "⏰ Identifies children with 3+ days of inactivity", parameters = new[] { "minDaysInactive?" }, category = "Drasi Real-Time", source = "wishlist-inactive-children-3d" },
                    new { name = "QueryGlobalWishlistDuplicates", description = "🌍 Shows most commonly requested items across ALL children", parameters = new[] { "minChildren?" }, category = "Drasi Real-Time", source = "wishlist-duplicates-global" },
                    new { name = "QueryBehaviorStatusChanges", description = "🎅 Tracks naughty/nice status changes in real-time", parameters = new string[] { }, category = "Drasi Real-Time", source = "behavior-status-changes" }
                },
                stats = new
                {
                    drasiTools = 5
                },
                description = "🎯 Drasi-powered real-time event graph tools for Microsoft Agent Framework! These tools enable access to live event patterns, trending data, and behavioral insights.",
                integration = "Microsoft Agent Framework + Drasi Event Processing"
            });
        })
        .WithName("AgentTools")
        .WithTags("Frontend", "EnhancedAgents")
        .WithDescription("List Drasi real-time event graph query tools for Microsoft Agent Framework")
        .Produces<object>(StatusCodes.Status200OK);

        // NEW: Drasi-Powered Agent Demo Endpoint 🚀
        app.MapGet("drasi-agent-demo/{childId}", async (
            string childId,
            AgentToolLibrary toolLibrary,
            ILogger<AgentToolLibrary> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation("Running Drasi-powered agent tool demo for child {ChildId}", childId);

            try
            {
                // Demonstrate all 4 Drasi tools in parallel
                var trendingTask = toolLibrary.QueryTrendingWishlistItems(minFrequency: 1, ct);
                var duplicatesTask = toolLibrary.FindChildrenWithDuplicateWishlists(childId, ct);
                var globalTask = toolLibrary.QueryGlobalWishlistDuplicates(minChildren: 2, ct);
                var inactiveTask = toolLibrary.FindInactiveChildren(minDaysInactive: 3, ct);

                await Task.WhenAll(trendingTask, duplicatesTask, globalTask, inactiveTask);

                return Results.Ok(new
                {
                    childId,
                    timestamp = DateTime.UtcNow,
                    demonstration = "Drasi + Microsoft Agent Framework Integration",
                    description = "Agents can directly query Drasi's real-time event graph during reasoning",

                    drasiInsights = new
                    {
                        trending = new { tool = "QueryTrendingWishlistItems", result = await trendingTask },
                        duplicates = new { tool = "FindChildrenWithDuplicateWishlists", result = await duplicatesTask },
                        global = new { tool = "QueryGlobalWishlistDuplicates", result = await globalTask },
                        inactive = new { tool = "FindInactiveChildren", result = await inactiveTask }
                    },

                    capabilities = new[]
                    {
                        "✅ Real-time data from Drasi continuous queries",
                        "✅ Sub-5-second latency from event graph",
                        "✅ Grounded recommendations (no hallucination)",
                        "✅ Pattern detection across event streams"
                    },

                    nextSteps = new[]
                    {
                        "Call /api/v1/children/{childId}/recommendations/collaborative to see multi-agent orchestration with Drasi tools",
                        "Call /api/v1/children/{childId}/recommendations/stream for streaming responses",
                        "Check /api/v1/drasi/insights for raw dashboard data"
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running Drasi agent demo");
                return Results.Problem($"Error: {ex.Message}");
            }
        })
        .WithName("DrasiAgentDemo")
        .WithTags("Debug", "EnhancedAgents", "Drasi")
        .WithDescription("🚀 DEMO: Shows agents querying Drasi real-time event graph with all 4 new tools")
        .Produces<object>(StatusCodes.Status200OK);

        // Behavior update endpoint (for naughty/nice system)
        // This ensures error message format consistency with other endpoints, rather than replacing ASP.NET's default 400 response.
        app.MapPost("children/{childId}/letters/behavior", async (
            string childId,
            [FromBody] BehaviorUpdateRequest? request,
            IWishlistService wishlistService,
            INaughtyNiceEventHandler eventHandler,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            
            // Validate request body - returns early if null, guaranteeing non-null for rest of handler
            if (request is null)
            {
                logger.LogWarning("[BehaviorUpdate] Null request body for child {ChildId}", childId);
                return Results.BadRequest(new
                {
                    type = "https://httpstatuses.com/400",
                    title = "Bad Request",
                    status = 400,
                    detail = "Request body is required. Expected JSON with 'newStatus' and optional 'message' properties."
                });
            }

            // Create letter with behavior update
            LetterToNorthPole letter = new(
                Id: Guid.NewGuid().ToString(),
                ChildId: childId,
                RequestType: "behavior-update",
                ItemName: "behavior-report",
                Category: "behavior",
                Tags: null,
                Priority: null,
                Notes: request.Message,
                StatusChange: request.NewStatus
            );

            // Primary operation: Store the behavior update letter - this MUST succeed
            try
            {
                await wishlistService.AddLetterAsync(
                    childId,
                    "behavior-update",
                    request.Message ?? "Status update",
                    "behavior",
                    null,
                    request.NewStatus?.ToString(),
                    ct);
                
                logger.LogInformation("[BehaviorUpdate] Stored behavior update letter {LetterId} for child {ChildId}", letter.Id, childId);
            }
            catch (OperationCanceledException)
            {
                throw; // Let cancellation propagate
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[BehaviorUpdate] Failed to store behavior update for child {ChildId}", childId);
                return Results.Problem(
                    detail: "Failed to store behavior update. Please try again.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error");
            }

            // Secondary operation: Trigger event handler to update recommendations
            // This is best-effort and should not fail the request
            bool recommendationUpdateSucceeded = false;
            if (request.NewStatus.HasValue)
            {
                try
                {
                    await eventHandler.HandleStatusChangeAsync(childId, request.NewStatus.Value, request.Message ?? "Status update", ct);
                    recommendationUpdateSucceeded = true;
                }
                catch (OperationCanceledException)
                {
                    throw; // Let cancellation propagate
                }
                catch (Exception ex)
                {
                    // Log but don't fail - the behavior update was already stored
                    logger.LogWarning(ex, "[BehaviorUpdate] Failed to trigger recommendation update for child {ChildId}, but behavior update was stored", childId);
                }
            }

            return Results.Ok(new
            {
                childId,
                letterId = letter.Id,
                requestType = letter.RequestType,
                statusChange = request.NewStatus?.ToString(),
                message = recommendationUpdateSucceeded 
                    ? "Behavior update processed and recommendations adjusted"
                    : "Behavior update stored (recommendation update pending)",
                triggeredAgent = recommendationUpdateSucceeded
            });
        })
        .WithName("UpdateBehavior")
        .WithTags("Frontend", "NaughtyNice")
        .WithDescription("Update child behavior status (Nice/Naughty) and trigger recommendation adjustments")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    public record BehaviorUpdateRequest(
        [property: JsonPropertyName("newStatus")] NiceStatus? NewStatus,
        [property: JsonPropertyName("message")] string? Message
    );
}
