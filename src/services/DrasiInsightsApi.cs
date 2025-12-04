using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Cosmos;
using Drasicrhsit.Infrastructure;

namespace Services;

public static class DrasiInsightsApi
{
    public static IEndpointRouteBuilder MapDrasiInsightsApi(this IEndpointRouteBuilder app)
    {
        // Get real-time Santa's Workshop Intelligence from Drasi continuous queries
        app.MapGet("drasi/insights", async (IDrasiViewClient drasiClient, IConfiguration config, ILogger<Program> logger, CancellationToken ct) =>
        {
            try
            {
                var queryContainer = ConfigurationHelper.GetValue(config, "Drasi:QueryContainer", "DRASI_QUERY_CONTAINER", "default");

                // Query Drasi for trending items (wishlist-trending-1h)
                var trendingResults = await drasiClient.GetCurrentResultAsync(queryContainer, "wishlist-trending-1h", ct);
                var trending = trendingResults
                    .Take(10)
                    .Select(node => new
                    {
                        item = node?["item"]?.ToString() ?? "🎁 Mystery Gift",
                        frequency = node?["frequency"]?.GetValue<int>() ?? 0
                    })
                    .OrderByDescending(x => x.frequency)
                    .ToList();

                // Query Drasi for duplicate requests (wishlist-duplicates-by-child)
                var duplicatesResults = await drasiClient.GetCurrentResultAsync(queryContainer, "wishlist-duplicates-by-child", ct);
                var duplicates = duplicatesResults
                    .Select(node => new
                    {
                        childId = node?["childId"]?.ToString() ?? "elf-unknown",
                        item = node?["item"]?.ToString() ?? "Unknown",
                        count = node?["duplicateCount"]?.GetValue<int>() ?? 2
                    })
                    .OrderByDescending(x => x.count)
                    .Take(10)
                    .ToList();

                // Query Drasi for inactive children (wishlist-inactive-children-3d)
                var inactiveResults = await drasiClient.GetCurrentResultAsync(queryContainer, "wishlist-inactive-children-3d", ct);
                var inactiveChildren = inactiveResults
                    .Take(5)
                    .Select(node =>
                    {
                        var lastEventStr = node?["lastEvent"]?.ToString();
                        var lastEvent = string.IsNullOrEmpty(lastEventStr) ? DateTime.UtcNow.AddDays(-3) : DateTime.Parse(lastEventStr);
                        var daysSince = (int)(DateTime.UtcNow - lastEvent).TotalDays;
                        return new
                        {
                            childId = node?["childId"]?.ToString() ?? "elf-unknown",
                            lastEventDays = daysSince
                        };
                    })
                    .ToList();

                // Query Drasi for updates to get total event count
                var updatesResults = await drasiClient.GetCurrentResultAsync(queryContainer, "wishlist-updates", ct);
                var totalEvents = updatesResults.Count;

                // Query Drasi for behavior status changes (naughty/nice)
                var behaviorResults = await drasiClient.GetCurrentResultAsync(queryContainer, "behavior-status-changes", ct);
                var behaviorChanges = behaviorResults
                    .Take(10)
                    .Select(node => new
                    {
                        childId = node?["childId"]?.ToString() ?? "elf-unknown",
                        oldStatus = node?["oldStatus"]?.ToString() ?? "Unknown",
                        newStatus = node?["newStatus"]?.ToString() ?? "Unknown",
                        reason = node?["reason"]?.ToString()
                    })
                    .ToList();

                var insights = new
                {
                    trending = trending.ToArray(),
                    duplicates = duplicates.ToArray(),
                    inactiveChildren = inactiveChildren.ToArray(),
                    behaviorChanges = behaviorChanges.ToArray(),
                    stats = new
                    {
                        totalEvents,
                        activeQueries = 7, // All 7 Drasi queries
                        lastUpdateSeconds = 0
                    }
                };

                logger.LogInformation("Drasi insights: {TrendingCount} trending, {DuplicatesCount} duplicates, {InactiveCount} inactive, {BehaviorCount} behavior changes, {TotalEvents} total events",
                    trending.Count, duplicates.Count, inactiveChildren.Count, behaviorChanges.Count, totalEvents);

                return Results.Ok(insights);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching Santa's Workshop insights from Drasi");

                // Return empty results on error to keep UI functional
                return Results.Ok(new
                {
                    trending = Array.Empty<object>(),
                    duplicates = Array.Empty<object>(),
                    inactiveChildren = Array.Empty<object>(),
                    behaviorChanges = Array.Empty<object>(),
                    stats = new { totalEvents = 0, activeQueries = 0, lastUpdateSeconds = 0 }
                });
            }
        })
        .WithName("GetDrasiInsights")
        .WithTags("Drasi");

        // Get detailed query results for a specific continuous query
        app.MapGet("drasi/queries/{queryName}", async (string queryName, IDrasiViewClient drasiClient, IConfiguration config, ILogger<IDrasiViewClient> logger, CancellationToken ct) =>
        {
            try
            {
                var queryContainerId = ConfigurationHelper.GetValue(
                    config,
                    "Drasi:QueryContainer",
                    "DRASI_QUERY_CONTAINER",
                    "default");
                var results = await drasiClient.GetCurrentResultAsync(queryContainerId, queryName, ct);

                return Results.Ok(new { queryName, results, count = results.Count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching Drasi query {QueryName}", queryName);
                return Results.Problem($"Error fetching query results: {ex.Message}");
            }
        })
        .WithName("GetDrasiQueryResults")
        .WithTags("Drasi");

        // Stream real-time Santa's Workshop insights (SSE) from Cosmos DB change feed
        app.MapGet("drasi/insights/stream", async (HttpContext ctx, ICosmosRepository cosmos, IConfiguration config, ILogger<Program> logger, CancellationToken ct) =>
        {
            // Proxy-friendly SSE headers (avoid buffering and caching)
            ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache, no-transform";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers["X-Accel-Buffering"] = "no"; // Nginx/Envoy hint

            try
            {
                // Send initial Santa's Workshop connection event
                await ctx.Response.WriteAsync(": 🎄 Connected to Santa's Workshop Intelligence\n\n", ct);
                await ctx.Response.WriteAsync("event: ready\n", ct);
                await ctx.Response.WriteAsync("data: {\"type\":\"workshop_ready\",\"message\":\"Live insights from the North Pole\"}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);

                var wishlistContainer = cosmos.GetContainer(config["Cosmos:Containers:Wishlists"]!);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Poll for trending items every 10 seconds
                        var query = new QueryDefinition(
                            "SELECT TOP 1 r.item, r.frequency " +
                            "FROM ( " +
                            "  SELECT c.Text AS item, COUNT(1) AS frequency " +
                            "  FROM c " +
                            "  WHERE c.CreatedAt >= DateTimeAdd('hh', -1, GetCurrentDateTime()) " +
                            "  GROUP BY c.Text " +
                            ") r " +
                            "ORDER BY r.frequency DESC"
                        );

                        using (var iterator = wishlistContainer.GetItemQueryIterator<dynamic>(query))
                        {
                            if (iterator.HasMoreResults)
                            {
                                var response = await iterator.ReadNextAsync(ct);
                                var topItem = response.FirstOrDefault();
                                if (topItem != null)
                                {
                                    var insight = new
                                    {
                                        type = "trending_update",
                                        timestamp = DateTime.UtcNow,
                                        item = (string)topItem.item ?? "🎁 Mystery Gift",
                                        frequency = (int)topItem.frequency
                                    };

                                    var json = JsonSerializer.Serialize(insight);
                                    await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                                    await ctx.Response.Body.FlushAsync(ct);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error in workshop insights stream");
                        var err = new { type = "stream_error", message = "temporary elves issue" };
                        await ctx.Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(err)}\n\n", CancellationToken.None);
                        await ctx.Response.Body.FlushAsync(CancellationToken.None);
                    }

                    // Heartbeat every 10 seconds
                    await ctx.Response.WriteAsync(": ❄️ workshop heartbeat\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal disconnection
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workshop insights stream error");
            }
        })
        .WithName("StreamDrasiInsights")
        .WithTags("Drasi");

        // Debug endpoint: raw View Service response for troubleshooting
        app.MapGet("drasi/debug/{queryName}", async (string queryName, IDrasiViewClient drasiClient, IConfiguration config, ILogger<IDrasiViewClient> logger, CancellationToken ct) =>
        {
            try
            {
                var queryContainerId = ConfigurationHelper.GetValue(
                    config,
                    "Drasi:QueryContainer",
                    "DRASI_QUERY_CONTAINER",
                    "default");
                var baseUrl = ConfigurationHelper.GetOptionalValue(
                    config,
                    "Drasi:ViewServiceBaseUrl",
                    "DRASI_VIEW_SERVICE_BASE_URL") ?? string.Empty;
                var resolvedUrl = !string.IsNullOrEmpty(baseUrl) ? $"{baseUrl.TrimEnd('/')}/{queryName}" : $"http://{queryContainerId}-view-svc/{queryName}";

                logger.LogInformation("[Drasi Debug] QueryContainer={Container}, BaseUrl={BaseUrl}, ResolvedUrl={Url}", queryContainerId, baseUrl, resolvedUrl);

                var results = await drasiClient.GetCurrentResultAsync(queryContainerId, queryName, ct);

                return Results.Ok(new
                {
                    queryName,
                    queryContainerId,
                    baseUrl,
                    resolvedUrl,
                    resultCount = results.Count,
                    results = results.Take(5), // first 5 for inspection
                    message = results.Count == 0 ? "No results - check if query is deployed and has data" : "OK"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Drasi debug endpoint for {QueryName}", queryName);
                return Results.Problem($"Error: {ex.Message}");
            }
        })
        .WithName("DrasiDebug")
        .WithTags("Drasi", "Debug");

        return app;
    }
}
