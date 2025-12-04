using Microsoft.Azure.Cosmos;
using System.Text.Json.Nodes;
using Drasicrhsit.Infrastructure;

namespace Services;

public static class HistoricalTrendsApi
{
    public static IEndpointRouteBuilder MapHistoricalTrendsApi(this IEndpointRouteBuilder app)
    {
        // Year-over-Year trending comparison endpoint
        // Showcases Drasi real-time data vs historical Cosmos DB data
        app.MapGet("trends/year-over-year", async (
            ICosmosRepository cosmos,
            IDrasiViewClient drasiClient,
            IConfiguration config,
            ILogger<ICosmosRepository> logger,
            CancellationToken ct) =>
        {
            try
            {
                // Get current trending from Drasi real-time event graph
                var queryContainerId = ConfigurationHelper.GetValue(
                    config,
                    "Drasi:QueryContainer",
                    "DRASI_QUERY_CONTAINER",
                    "default");
                var currentTrending = await drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-trending-1h", ct);

                // Calculate date ranges
                var now = DateTime.UtcNow;
                var lastYearStart = new DateTime(now.Year - 1, 12, 1); // Dec 1 last year
                var lastYearEnd = new DateTime(now.Year - 1, 12, 31);  // Dec 31 last year

                // Query Cosmos for historical wishlist data from last Christmas
                var historicalItems = await GetHistoricalTrendingFromCosmosAsync(cosmos, config, lastYearStart, lastYearEnd, logger, ct);

                // Format current trending from Drasi
                var current = currentTrending
                    .Take(10)
                    .Select(r => new
                    {
                        item = r["item"]?.GetValue<string>() ?? "Unknown",
                        frequency = r["frequency"]?.GetValue<int>() ?? 0,
                        period = "current"
                    })
                    .ToArray();

                // Format historical trending from Cosmos
                var historical = historicalItems
                    .OrderByDescending(x => x.Value)
                    .Take(10)
                    .Select(h => new
                    {
                        item = h.Key,
                        frequency = h.Value,
                        period = "last_year"
                    })
                    .ToArray();

                // Calculate comparison insights
                var comparison = new
                {
                    current,
                    historical,
                    insights = GenerateComparisonInsights(current, historical),
                    metadata = new
                    {
                        currentPeriod = "Live from Drasi",
                        historicalPeriod = $"Dec 1-31, {now.Year - 1}",
                        currentYear = now.Year,
                        comparisonYear = now.Year - 1,
                        dataSource = new
                        {
                            current = "Drasi Real-Time Event Graph (wishlist-trending-1h)",
                            historical = "Cosmos DB Historical Data"
                        }
                    }
                };

                return Results.Ok(comparison);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating year-over-year trends comparison");
                return Results.Problem($"Error: {ex.Message}");
            }
        })
        .WithName("GetYearOverYearTrends")
        .WithTags("Frontend", "Trends", "Drasi");

        return app;
    }

    private static async Task<Dictionary<string, int>> GetHistoricalTrendingFromCosmosAsync(
        ICosmosRepository cosmos,
        IConfiguration config,
        DateTime startDate,
        DateTime endDate,
        ILogger logger,
        CancellationToken ct)
    {
        var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Validate date parameters
        if (startDate > endDate)
        {
            logger.LogWarning("Invalid date range: startDate {StartDate} is after endDate {EndDate}", startDate, endDate);
            return GetDemoHistoricalData();
        }

        // Ensure reasonable date range (max 1 year historical)
        var maxHistoricalDays = 365;
        if ((DateTime.UtcNow - startDate).TotalDays > maxHistoricalDays * 2)
        {
            logger.LogWarning("Date range too old: startDate {StartDate} is more than {MaxDays} days ago", startDate, maxHistoricalDays * 2);
            return GetDemoHistoricalData();
        }

        try
        {
            var containerName = config["Cosmos:Containers:Wishlists"];
            if (string.IsNullOrEmpty(containerName))
            {
                logger.LogWarning("Cosmos:Containers:Wishlists not configured, using demo historical data");
                return GetDemoHistoricalData();
            }

            var container = cosmos.GetContainer(containerName);

            // Query for historical wishlist items with aggregation
            var query = new QueryDefinition(
                "SELECT c.Text AS item, COUNT(1) AS frequency " +
                "FROM c " +
                "WHERE c.CreatedAt >= @startDate AND c.CreatedAt <= @endDate " +
                "GROUP BY c.Text")
                .WithParameter("@startDate", startDate.ToString("o"))
                .WithParameter("@endDate", endDate.ToString("o"));

            using var iterator = container.GetItemQueryIterator<dynamic>(query);
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                foreach (var item in response)
                {
                    string text = (string?)item.item ?? "Unknown";
                    int freq = (int?)item.frequency ?? 0;
                    itemCounts[text] = freq;
                }
            }

            // If no historical data found in Cosmos, return demo data for showcase
            if (itemCounts.Count == 0)
            {
                logger.LogInformation("No historical data found in Cosmos DB for {StartDate} to {EndDate}, using demo data",
                    startDate, endDate);
                return GetDemoHistoricalData();
            }

            logger.LogInformation("Retrieved {Count} historical items from Cosmos DB", itemCounts.Count);
            return itemCounts;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Wishlists container not found, using demo historical data");
            return GetDemoHistoricalData();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query historical data from Cosmos, using demo data");
            return GetDemoHistoricalData();
        }
    }

    /// <summary>
    /// Demo historical data representing last year's Christmas trends
    /// Used when no actual historical data is available in Cosmos DB
    /// </summary>
    private static Dictionary<string, int> GetDemoHistoricalData()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "LEGO Star Wars Set", 45 },
            { "Nintendo Switch", 38 },
            { "Harry Potter Book Collection", 32 },
            { "Remote Control Car", 28 },
            { "Art Supply Kit", 25 },
            { "Board Game Collection", 22 },
            { "Bicycle", 20 },
            { "Stuffed Animal", 18 },
            { "Science Experiment Kit", 15 },
            { "Puzzle Set", 12 }
        };
    }

    private static object GenerateComparisonInsights(dynamic[] current, dynamic[] historical)
    {
        var currentItems = current.Select(c => (string)c.item).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var historicalItems = historical.Select(h => (string)h.item).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var returning = currentItems.Intersect(historicalItems, StringComparer.OrdinalIgnoreCase).ToArray();
        var newThisYear = currentItems.Except(historicalItems, StringComparer.OrdinalIgnoreCase).ToArray();
        var goneThisYear = historicalItems.Except(currentItems, StringComparer.OrdinalIgnoreCase).ToArray();

        var currentTotal = current.Sum(c => (int)c.frequency);
        var historicalTotal = historical.Sum(h => (int)h.frequency);
        var percentChange = historicalTotal > 0
            ? Math.Round(((double)(currentTotal - historicalTotal) / historicalTotal) * 100, 1)
            : 0;

        return new
        {
            returningFavorites = returning,
            newTrends = newThisYear,
            noLongerTrending = goneThisYear,
            volumeChange = new
            {
                current = currentTotal,
                historical = historicalTotal,
                percentChange,
                trend = percentChange > 0 ? "up" : percentChange < 0 ? "down" : "stable"
            }
        };
    }
}
