using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Nodes;
using Drasicrhsit.Infrastructure;

namespace Services;

public static class JobsApi
{
    public static IEndpointRouteBuilder MapJobsApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("jobs", async (HttpRequest req, IJobService jobs, Services.IEventPublisher publisher,
            IElfAgentOrchestrator orchestrator, ILogger<JobRequest> logger, HttpContext ctx, CancellationToken ct) =>
        {
            logger.LogInformation("🔵 JobsApi POST /jobs endpoint called - Request received");
            var node = await req.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            var childId = (string?)node?["childId"];
            logger.LogInformation("🔵 Parsed childId: {ChildId}", childId ?? "(null)");
            var schemaVersion = (string?)node?["schemaVersion"] ?? "v1";
            var dedupeKey = (string?)node?["dedupeKey"];
            var wishlistNode = node?["wishlist"];

            if (string.IsNullOrWhiteSpace(childId))
                return Results.BadRequest(new { error = "childId is required" });

            if (string.IsNullOrWhiteSpace(dedupeKey))
            {
                if (wishlistNode is null)
                {
                    return Results.BadRequest(new { error = "dedupeKey or wishlist required" });
                }
                var wishlistJson = wishlistNode.ToJsonString();
                dedupeKey = DedupeKeyHasher.Compute(childId!, wishlistJson);
            }

            // Dual-write: publish wishlist change event to Event Hubs unless request originated from Drasi Reaction
            // Drasi Reaction sets header 'X-Drasi-Origin: 1' to prevent feedback loops.
            if (!req.Headers.ContainsKey("X-Drasi-Origin"))
            {
                await publisher.PublishWishlistAsync(childId!, dedupeKey!, schemaVersion!, wishlistNode, ct);
            }

            // PERFORMANCE OPTIMIZATION: Trigger recommendation generation immediately (fire-and-forget)
            // This bypasses the Drasi pipeline delay (5-10s) and starts AI processing in parallel
            // The Drasi reaction will still trigger but will be deduplicated by the orchestrator
            if (!req.Headers.ContainsKey("X-Drasi-Origin"))
            {
                logger.LogInformation("🔵 Direct trigger condition met for {ChildId} - Starting Task.Run", childId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        logger.LogInformation("🚀 Direct trigger: Starting recommendation generation for {ChildId}", childId);
                        await orchestrator.RunRecommendationGenerationAsync(childId!, CancellationToken.None);
                        logger.LogInformation("✅ Direct trigger: Completed recommendation generation for {ChildId}", childId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "⚠️ Direct trigger: Background recommendation generation failed for {ChildId}", childId);
                    }
                });
                logger.LogInformation("🔵 Direct trigger Task.Run dispatched for {ChildId}", childId);
            }
            else
            {
                logger.LogInformation("🔵 Skipping direct trigger for {ChildId} - X-Drasi-Origin header present", childId);
            }

            await jobs.EnsureJobAsync(childId!, dedupeKey!, schemaVersion!, ct);
            // 202 Accepted per spec; omit body to avoid serialization issues in TestHost
            return Results.Accepted($"/jobs/{childId}/{dedupeKey}");
        })
        .WithTags("Internal", "Jobs");

        return app;
    }
}

public record JobRequest(string childId, string dedupeKey, string schemaVersion);
