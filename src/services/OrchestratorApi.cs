using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Nodes;
using Drasicrhsit.Infrastructure;

namespace Services;

public static class OrchestratorApi
{
    public static IEndpointRouteBuilder MapOrchestratorApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("orchestrator/ingest", async (HttpRequest req,
            IJobService jobs,
            IElfAgentOrchestrator orchestrator,
            IConfiguration cfg,
            CancellationToken ct) =>
        {
            string? configuredSecret = Environment.GetEnvironmentVariable("AGENT_TRIGGER_SECRET") ?? cfg["Agents:TriggerSecret"];
            string providedSecret = req.Headers["X-Agent-Secret"].ToString();
            if (!string.IsNullOrWhiteSpace(configuredSecret) && providedSecret != configuredSecret)
            {
                return Results.Unauthorized();
            }

            var node = await req.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            if (node is null)
            {
                return Results.BadRequest(new { error = "invalid json" });
            }

            string? schemaVersion = (string?)node["schemaVersion"] ?? "v1";
            string? type = (string?)node["type"];
            string? childId = (string?)node["childId"];
            string? correlationId = (string?)node["correlationId"] ?? req.Headers["X-Drasi-CorrelationId"].ToString();

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(childId))
            {
                return Results.BadRequest(new { error = "type and childId are required" });
            }

            string typeLower = type.ToLowerInvariant();

            // Back-compat: support either envelope payload or legacy wishlist field
            JsonNode? payload = node["payload"] ?? node["wishlist"];
            string? precomputedHash = (string?)node["hash"];

            switch (typeLower)
            {
                case "wishlist":
                {
                    if (payload is null)
                    {
                        return Results.BadRequest(new { error = "payload is required for wishlist" });
                    }
                    string json = payload.ToJsonString();
                    string dedupeKey = precomputedHash ?? DedupeKeyHasher.Compute(childId!, json);
                    await jobs.EnsureJobAsync(childId!, dedupeKey, schemaVersion!, ct);
                    return Results.Accepted($"/jobs/{childId}/{dedupeKey}", new { status = "accepted", correlationId = correlationId });
                }
                case "profile":
                {
                    await orchestrator.RunProfileEnrichmentAsync(childId!, ct);
                    return Results.Accepted($"/children/{childId}/profile", new { status = "accepted", correlationId = correlationId });
                }
                case "recommendation":
                {
                    await orchestrator.RunRecommendationGenerationAsync(childId!, ct);
                    return Results.Accepted($"/children/{childId}/recommendations", new { status = "accepted", correlationId = correlationId });
                }
                case "logistics":
                {
                    await orchestrator.RunLogisticsAssessmentAsync(childId!, ct);
                    return Results.Accepted($"/children/{childId}/logistics", new { status = "accepted", correlationId = correlationId });
                }
                case "notification":
                {
                    await orchestrator.RunNotificationAggregationAsync(childId!, ct);
                    return Results.Accepted($"/notifications", new { status = "accepted", correlationId = correlationId });
                }
                default:
                    return Results.BadRequest(new { error = "unsupported type" });
            }
        })
        // Removed .WithName to avoid duplicate endpoint names across versioned & unversioned route groups
        .WithTags("Internal", "Orchestrator");

        return app;
    }
}
