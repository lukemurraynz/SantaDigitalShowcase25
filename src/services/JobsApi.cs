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
        app.MapPost("jobs", async (HttpRequest req, IJobService jobs, Services.IEventPublisher publisher, HttpContext ctx, CancellationToken ct) =>
        {
            var node = await req.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            var childId = (string?)node?["childId"];
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

            await jobs.EnsureJobAsync(childId!, dedupeKey!, schemaVersion!, ct);
            // 202 Accepted per spec; omit body to avoid serialization issues in TestHost
            return Results.Accepted($"/jobs/{childId}/{dedupeKey}");
        })
        .WithTags("Internal", "Jobs");

        return app;
    }
}

public record JobRequest(string childId, string dedupeKey, string schemaVersion);
