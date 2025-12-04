using System.Text.Json;
using Dapr.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Drasicrhsit.Infrastructure;

namespace Services;

// Minimal subscriber that bridges Drasi Reaction pubsub messages to SSE broadcaster
public static class DrasiDaprSubscriber
{
    public static IEndpointRouteBuilder MapDrasiDaprSubscriptions(this IEndpointRouteBuilder app)
    {
        // Dapr subscribes by calling this endpoint at startup to discover topics
        app.MapGet("dapr/subscribe", (IConfiguration config) =>
        {
            // Configure expected topics (packed CloudEvents) from Drasi Reaction
            // Topics follow pattern: <queryId>-results
            var pubsub = ConfigurationHelper.GetValue(
                config,
                "Drasi:DaprPubSubName",
                "DRASI_DAPR_PUBSUB_NAME",
                "rg-pubsub");
            // Allow override via configuration array: Drasi:DaprTopics: [ "queryId1", "queryId2" ]
            var configured = config.GetSection("Drasi:DaprTopics").Get<string[]>() ?? Array.Empty<string>();
            var queryIds = configured.Length > 0
                ? configured
                : new[] { "wishlist-trending-1h", "wishlist-duplicates-by-child", "wishlist-inactive-children-3d" };

            var topics = queryIds.Select(q => new { pubsubName = pubsub, topic = $"{q}-results", route = $"/dapr/drasi/{q}" }).ToArray();

            return Results.Json(topics);
        })
        .WithName("DaprSubscribe");

        // Per-topic handlers (CloudEvents: { id, source, type, data, ... })
        app.MapPost("dapr/drasi/{queryId}", async (
            string queryId,
            HttpRequest req,
            IStreamBroadcaster broadcaster,
            INotificationRepository notifications,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("DrasiDaprSubscriber");
            try
            {
                using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
                var root = doc.RootElement;

                // CloudEvent payload: data contains packed ChangeEvent
                if (!root.TryGetProperty("data", out var data))
                {
                    logger.LogWarning("CloudEvent missing data for query {QueryId}", queryId);
                    return Results.Accepted();
                }

                // Try to extract childId if present in projection
                string childId = data.TryGetProperty("childId", out var childProp) ? (childProp.GetString() ?? "unknown") : "unknown";
                string message = BuildMessage(queryId, data);

                var entity = new NotificationEntity
                {
                    ChildId = childId,
                    Type = MapType(queryId),
                    Message = message,
                    RelatedId = null,
                    State = "unread"
                };

                // Persist for history and initial fetch API
                await notifications.StoreAsync(entity);

                // Broadcast to child stream; UI shows immediately via SSE
                var json = JsonSerializer.Serialize(new { entity.id, entity.Type, entity.Message, entity.RelatedId, entity.State, timestamp = DateTime.UtcNow.ToString("o") });
                await broadcaster.PublishAsync(childId, "notification", json);

                return Results.Accepted();
            }
            catch (Exception ex)
            {
                logger!.LogError(ex, "Failed to process Dapr message for query {QueryId}", queryId);
                return Results.Accepted(); // swallow to avoid redeliver storms
            }
        })
        .WithName("DrasiDaprTopicHandler");

        return app;
    }

    private static string MapType(string queryId) => queryId switch
    {
        "wishlist-trending-1h" => "wishlist",
        "wishlist-duplicates-by-child" => "recommendation",
        "wishlist-inactive-children-3d" => "behavior",
        _ => "info"
    };

    private static string BuildMessage(string queryId, JsonElement data)
    {
        // Create a concise message per query
        try
        {
            return queryId switch
            {
                "wishlist-trending-1h" =>
                    $"Trending update: {(data.TryGetProperty("item", out var item) ? item.GetString() : "Unknown")} ({(data.TryGetProperty("frequency", out var freq) ? freq.GetInt32() : 0)})",
                "wishlist-duplicates-by-child" =>
                    $"Duplicate wishlist item detected: {(data.TryGetProperty("item", out var item2) ? item2.GetString() : "Unknown")}",
                "wishlist-inactive-children-3d" =>
                    $"Inactive child detected: {(data.TryGetProperty("childId", out var c) ? c.GetString() : "Unknown")}",
                _ => $"Drasi update for {queryId}"
            };
        }
        catch { return $"Drasi update for {queryId}"; }
    }
}
