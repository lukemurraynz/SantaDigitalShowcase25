using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos;
using System.Text.Json;

namespace Services;

/// <summary>
/// Background service that seeds the SignalR hub cache with current trending data from Cosmos DB.
/// This ensures the frontend can display data even when Drasi SignalR reactions aren't connected.
/// </summary>
public sealed partial class DrasiHubCacheSeeder : BackgroundService
{
    private readonly ILogger<DrasiHubCacheSeeder> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<Realtime.DrasiEventsHub> _hubContext;
    private readonly IConfiguration _config;

    // Query IDs that match the frontend's ResultSet queryId props
    private const string TrendingQueryId = "wishlist-trending-1h";
    private const string DuplicatesQueryId = "wishlist-duplicates-by-child";
    private const string InactiveQueryId = "wishlist-inactive-children-3d";
    private const string BehaviorChangesQueryId = "behavior-status-changes";

    // Drasi operation type for insert events
    private const string InsertOperation = "i";

    // Maximum number of items to return for each query type
    private const int MaxTrendingItems = 10;
    private const int MaxDuplicateItems = 5;
    private const int MaxInactiveItems = 5;
    private const int MaxBehaviorChanges = 10;

    public DrasiHubCacheSeeder(
        ILogger<DrasiHubCacheSeeder> logger,
        IServiceScopeFactory scopeFactory,
        IHubContext<Realtime.DrasiEventsHub> hubContext,
        IConfiguration config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to allow Cosmos DB connection to be established
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        _logger.LogInformation("Starting Drasi hub cache seeder");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SeedTrendingDataAsync(stoppingToken);
                await SeedDuplicatesDataAsync(stoppingToken);
                await SeedInactiveChildrenDataAsync(stoppingToken);
                await SeedBehaviorChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error seeding Drasi hub cache, will retry");
            }

            // Refresh cache every 30 seconds
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task SeedTrendingDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cosmos = scope.ServiceProvider.GetRequiredService<ICosmosRepository>();
        var wishlistContainer = cosmos.GetContainer(_config["Cosmos:Containers:Wishlists"] ?? "wishlists");

        var query = new QueryDefinition(
            "SELECT c.text AS item, COUNT(1) AS frequency " +
            "FROM c " +
            "WHERE c.createdAt >= DateTimeAdd('hh', -1, GetCurrentDateTime()) " +
            "GROUP BY c.text"
        );

        var results = new List<dynamic>();
        using var iterator = wishlistContainer.GetItemQueryIterator<dynamic>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        var trending = results
            .OrderByDescending(x => (int)x.frequency)
            .Take(MaxTrendingItems)
            .Select(x => new
            {
                item = (string?)x.item ?? "🎁 Mystery Gift",
                frequency = (int)x.frequency
            })
            .ToList();

        // Clear existing cache and repopulate with fresh data
        Realtime.DrasiEventsHub.ClearQueryCache(TrendingQueryId, _logger);

        // Broadcast each item to the hub cache
        foreach (var item in trending)
        {
            var payload = JsonSerializer.SerializeToElement(item);
            Realtime.DrasiEventsHub.ProcessDrasiEvent(TrendingQueryId, InsertOperation, payload, _hubContext, _logger);
        }

        _logger.LogDebug("Seeded {Count} trending items to hub cache", trending.Count);
    }

    private async Task SeedDuplicatesDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cosmos = scope.ServiceProvider.GetRequiredService<ICosmosRepository>();
        var wishlistContainer = cosmos.GetContainer(_config["Cosmos:Containers:Wishlists"] ?? "wishlists");

        var query = new QueryDefinition(
            "SELECT c.childId AS childId, c.text AS item, COUNT(1) AS duplicateCount " +
            "FROM c " +
            "WHERE c.createdAt >= DateTimeAdd('dd', -7, GetCurrentDateTime()) " +
            "GROUP BY c.childId, c.text"
        );

        var results = new List<dynamic>();
        using var iterator = wishlistContainer.GetItemQueryIterator<dynamic>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        var duplicates = results
            .Where(x => (int)x.duplicateCount > 1)
            .OrderByDescending(x => (int)x.duplicateCount)
            .Take(MaxDuplicateItems)
            .Select(x => new
            {
                childId = (string?)x.childId ?? "elf-unknown",
                item = (string?)x.item ?? "Unknown",
                duplicateCount = (int)x.duplicateCount
            })
            .ToList();

        // Clear existing cache and repopulate with fresh data
        Realtime.DrasiEventsHub.ClearQueryCache(DuplicatesQueryId, _logger);

        foreach (var item in duplicates)
        {
            var payload = JsonSerializer.SerializeToElement(item);
            Realtime.DrasiEventsHub.ProcessDrasiEvent(DuplicatesQueryId, InsertOperation, payload, _hubContext, _logger);
        }

        _logger.LogDebug("Seeded {Count} duplicate items to hub cache", duplicates.Count);
    }

    private async Task SeedInactiveChildrenDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cosmos = scope.ServiceProvider.GetRequiredService<ICosmosRepository>();
        var wishlistContainer = cosmos.GetContainer(_config["Cosmos:Containers:Wishlists"] ?? "wishlists");

        var query = new QueryDefinition(
            "SELECT r.childId, r.lastActivity " +
            "FROM ( " +
            "  SELECT c.childId AS childId, MAX(c.createdAt) AS lastActivity " +
            "  FROM c " +
            "  GROUP BY c.childId " +
            ") r " +
            "WHERE r.lastActivity < DateTimeAdd('dd', -3, GetCurrentDateTime())"
        );

        var results = new List<object>();
        using var iterator = wishlistContainer.GetItemQueryIterator<dynamic>(query);
        while (iterator.HasMoreResults && results.Count < MaxInactiveItems)
        {
            var response = await iterator.ReadNextAsync(ct);
            foreach (var child in response)
            {
                // Parse ISO 8601 date format from Cosmos DB with invariant culture
                if (DateTime.TryParse((string)child.lastActivity, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var lastActivity))
                {
                    var daysSince = (int)(DateTime.UtcNow - lastActivity).TotalDays;
                    results.Add(new
                    {
                        childId = (string?)child.childId ?? "elf-unknown",
                        lastEvent = (string)child.lastActivity,
                        daysSinceLastEvent = daysSince
                    });
                }
            }
        }

        // Clear existing cache and repopulate with fresh data
        Realtime.DrasiEventsHub.ClearQueryCache(InactiveQueryId, _logger);

        foreach (var item in results)
        {
            var payload = JsonSerializer.SerializeToElement(item);
            Realtime.DrasiEventsHub.ProcessDrasiEvent(InactiveQueryId, InsertOperation, payload, _hubContext, _logger);
        }

        _logger.LogDebug("Seeded {Count} inactive children to hub cache", results.Count);
    }

    private async Task SeedBehaviorChangesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cosmos = scope.ServiceProvider.GetRequiredService<ICosmosRepository>();
        var wishlistContainer = cosmos.GetContainer(_config["Cosmos:Containers:Wishlists"] ?? "wishlists");

        // Query for behavior-update type entries (Naughty/Nice status changes)
        // Note: previousStatus is not tracked in the current schema, so it defaults to "Unknown"
        var query = new QueryDefinition(
            "SELECT c.childId AS childId, c.statusChange AS newStatus, c.createdAt AS changedAt " +
            "FROM c " +
            "WHERE c.requestType = 'behavior-update' AND c.statusChange != null " +
            "ORDER BY c.createdAt DESC"
        );

        var results = new List<object>();
        using var iterator = wishlistContainer.GetItemQueryIterator<dynamic>(query);
        while (iterator.HasMoreResults && results.Count < MaxBehaviorChanges)
        {
            var response = await iterator.ReadNextAsync(ct);
            foreach (var item in response)
            {
                if (results.Count >= MaxBehaviorChanges)
                    break;

                results.Add(new
                {
                    childId = (string?)item.childId ?? "unknown",
                    newStatus = (string?)item.newStatus ?? "Unknown",
                    previousStatus = "Unknown",
                    changedAt = (string?)item.changedAt ?? DateTime.UtcNow.ToString("o")
                });
            }
        }

        // Clear existing cache and repopulate with fresh data
        Realtime.DrasiEventsHub.ClearQueryCache(BehaviorChangesQueryId, _logger);

        foreach (var item in results)
        {
            var payload = JsonSerializer.SerializeToElement(item);
            Realtime.DrasiEventsHub.ProcessDrasiEvent(BehaviorChangesQueryId, InsertOperation, payload, _hubContext, _logger);
        }

        _logger.LogDebug("Seeded {Count} behavior changes to hub cache", results.Count);
    }
}
