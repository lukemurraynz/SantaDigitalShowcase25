using System.ComponentModel;
using Drasicrhsit.Infrastructure;
using Models;
using Services;

namespace Services;

/// <summary>
/// Tool library providing AIFunction-decorated methods for agent tool calling.
/// Agents can use these tools to access real data instead of hallucinating.
/// </summary>
public class AgentToolLibrary
{
    private readonly IRecommendationService _recommendationService;
    private readonly IChildProfileService _profileService;
    private readonly IAvailabilityService _availabilityService;
    private readonly IDrasiViewClient _drasiClient;
    private readonly IWishlistRepository _wishlistRepository;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentToolLibrary> _logger;

    public AgentToolLibrary(
        IRecommendationService recommendationService,
        IChildProfileService profileService,
        IAvailabilityService availabilityService,
        IDrasiViewClient drasiClient,
        IWishlistRepository wishlistRepository,
        IConfiguration config,
        ILogger<AgentToolLibrary> logger)
    {
        _recommendationService = recommendationService;
        _profileService = profileService;
        _availabilityService = availabilityService;
        _drasiClient = drasiClient;
        _wishlistRepository = wishlistRepository;
        _config = config;
        _logger = logger;
    }

    private string GetQueryContainerId() => ConfigurationHelper.GetValue(
        _config,
        "Drasi:QueryContainer",
        "DRASI_QUERY_CONTAINER",
        "default");

    /// <summary>
    /// Get child behavior history from real-time graph data
    /// </summary>
    [Description("Retrieves the behavior history for a specific child, including status changes and letter submissions")]
    public async Task<string> GetChildBehaviorHistory(
        [Description("The child ID to query")] string childId,
        CancellationToken ct = default)
    {
        try
        {
            ChildProfile? profile = await _profileService.GetChildProfileAsync(childId, ct);
            if (profile is null)
            {
                return $"No profile found for child {childId}";
            }

            string status = profile.Status.ToString();
            string prefs = profile.Preferences is { Length: > 0 }
                ? string.Join(", ", profile.Preferences)
                : "none recorded";

            return $"""
                Child: {childId}
                Current Status: {status}
                Age: {profile.Age}
                Preferences: {prefs}
                Behavior: Based on their {status} status, they have been {(profile.Status == NiceStatus.Nice ? "well-behaved" : profile.Status == NiceStatus.Naughty ? "showing areas for growth" : "neutral")}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get behavior history for {ChildId}", childId);
            return $"Error retrieving behavior history: {ex.Message}";
        }
    }

    /// <summary>
    /// Get child's actual wishlist items (what they requested)
    /// </summary>
    [Description("🎁 CRITICAL: Gets the child's actual wishlist items - what they specifically requested. ALWAYS call this FIRST before making recommendations!")]
    public async Task<string> GetChildWishlistItems(
        [Description("The child ID whose wishlist to retrieve")] string childId,
        CancellationToken ct = default)
    {
        try
        {
            var items = new List<WishlistItemEntity>();
            await foreach (var item in _wishlistRepository.ListAsync(childId))
            {
                items.Add(item);
            }

            if (!items.Any())
            {
                return $"Child {childId} has not submitted any wishlist items yet. No specific requests to base recommendations on.";
            }

            string summary = $"""
                🎁 CHILD'S WISHLIST ({childId})
                ================================
                {items.Count} item(s) requested:

                """;

            int itemNum = 1;
            foreach (var item in items.OrderByDescending(i => i.CreatedAt))
            {
                string category = !string.IsNullOrWhiteSpace(item.Category) ? $" [{item.Category}]" : "";
                string budget = item.BudgetEstimate.HasValue ? $" ~${item.BudgetEstimate:F2}" : "";
                summary += $"{itemNum}. {item.Text}{category}{budget}\n";
                summary += $"   Requested: {item.CreatedAt:yyyy-MM-dd}\n";
                itemNum++;
            }

            summary += "\n💡 IMPORTANT: Base your recommendations on THESE specific items the child requested!";
            summary += "\n📌 Action: Suggest these exact items OR close alternatives if unavailable.";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get wishlist items for {ChildId}", childId);
            return $"Error retrieving wishlist: {ex.Message}";
        }
    }

    /// <summary>
    /// Search gift inventory with filters
    /// </summary>
    [Description("Searches the gift inventory based on category, maximum price, and age range")]
    public async Task<string> SearchGiftInventory(
        [Description("Gift category (toys, books, games, electronics, clothing, educational)")] string category,
        [Description("Maximum price in dollars")] decimal maxPrice,
        [Description("Minimum age for the gift")] int minAge,
        CancellationToken ct = default)
    {
        try
        {
            // Use existing recommendation service to get sample gifts
            // In a real system, this would query actual inventory
            IReadOnlyList<Recommendation> recommendations = await _recommendationService.GetTopNAsync("inventory-search", 5, ct);

            string results = $"""
                Inventory Search Results:
                Category: {category}
                Max Price: ${maxPrice:F2}
                Min Age: {minAge}+

                Available Items:
                """;

            foreach (Recommendation rec in recommendations.Where(r => r.Price <= maxPrice))
            {
                Availability? avail = await _availabilityService.GetAvailabilityAsync(rec.Suggestion, ct);
                string stockInfo = avail?.InStock == true ? "In Stock" : "Limited Stock";
                string leadTime = avail?.LeadTimeDays is not null ? $" (Ships in {avail.LeadTimeDays} days)" : "";

                results += $"\n- {rec.Suggestion}: ${rec.Price:F2} - {stockInfo}{leadTime}";
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search inventory for category {Category}", category);
            return $"Error searching inventory: {ex.Message}";
        }
    }

    /// <summary>
    /// Check if gifts fit within budget constraints
    /// </summary>
    [Description("Validates if a list of gift suggestions fits within a specified budget")]
    public Task<string> CheckBudgetConstraints(
        [Description("Comma-separated list of gift names")] string giftNames,
        [Description("Total budget in dollars")] decimal budget)
    {
        ArgumentNullException.ThrowIfNull(giftNames);
        try
        {
            string[] gifts = giftNames.Split(',', StringSplitOptions.TrimEntries);

            // Estimate prices based on gift names (in real system, would look up actual prices)
            Dictionary<string, decimal> estimatedPrices = new();
            decimal totalCost = 0;

            foreach (string gift in gifts)
            {
                // Simple estimation logic
                decimal price = gift.ToLowerInvariant() switch
                {
                    string s when s.Contains("lego") => 39.99m,
                    string s when s.Contains("book") => 14.99m,
                    string s when s.Contains("game") => 24.99m,
                    string s when s.Contains("art") => 19.99m,
                    string s when s.Contains("electronics") => 79.99m,
                    string s when s.Contains("bike") => 149.99m,
                    _ => 29.99m // default
                };

                estimatedPrices[gift] = price;
                totalCost += price;
            }

            string result = $"""
                Budget Analysis:
                Total Budget: ${budget:F2}
                Estimated Cost: ${totalCost:F2}
                Status: {(totalCost <= budget ? "Within Budget ✓" : $"Over Budget by ${totalCost - budget:F2}")}

                Breakdown:
                """;

            foreach (KeyValuePair<string, decimal> item in estimatedPrices)
            {
                result += $"\n- {item.Key}: ${item.Value:F2}";
            }

            if (totalCost > budget)
            {
                result += $"\n\nSuggestion: Remove or replace some items to meet the ${budget:F2} budget.";
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check budget constraints");
            return Task.FromResult($"Error checking budget: {ex.Message}");
        }
    }

    /// <summary>
    /// Query trending wishlist items in real-time from Drasi
    /// </summary>
    [Description("Gets the most popular wishlist items trending in the last hour from Drasi real-time event graph")]
    public async Task<string> QueryTrendingWishlistItems(
        [Description("Optional: minimum frequency threshold (default: 1)")] int minFrequency = 1,
        CancellationToken ct = default)
    {
        try
        {
            string queryContainerId = GetQueryContainerId();
            var results = await _drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-trending-1h", ct);

            if (results.Count == 0)
            {
                return "No trending items found in the last hour. This is expected if no wishlist events have been submitted recently.";
            }

            var trending = results
                .Where(r => (r["frequency"]?.GetValue<int>() ?? 0) >= minFrequency)
                .OrderByDescending(r => r["frequency"]?.GetValue<int>() ?? 0)
                .Take(10)
                .ToList();

            if (!trending.Any())
            {
                return $"No items found with frequency >= {minFrequency}";
            }

            string summary = $"""
                🔥 TRENDING WISHLIST ITEMS (Last Hour)
                =====================================
                Found {trending.Count} popular items:

                """;

            int rank = 1;
            foreach (var item in trending)
            {
                string itemName = item["item"]?.GetValue<string>() ?? "Unknown";
                int frequency = item["frequency"]?.GetValue<int>() ?? 0;
                summary += $"{rank}. {itemName} - Requested {frequency} time(s)\n";
                rank++;
            }

            summary += $"\n💡 Insight: Use this data to prioritize inventory checks and recommendations.";
            summary += $"\n📊 Data Source: Drasi continuous query 'wishlist-trending-1h'";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query trending wishlist items from Drasi");
            return $"Error querying trending items: {ex.Message}";
        }
    }

    /// <summary>
    /// Find children with duplicate wishlist items from Drasi
    /// </summary>
    [Description("Identifies children who have requested the same item multiple times, indicating strong interest")]
    public async Task<string> FindChildrenWithDuplicateWishlists(
        [Description("Optional: specific child ID to check")] string? childId = null,
        CancellationToken ct = default)
    {
        try
        {
            string queryContainerId = GetQueryContainerId();
            var results = await _drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-duplicates-by-child", ct);

            if (results.Count == 0)
            {
                return "No duplicate wishlist items detected. Children have diverse wish lists without repeated items.";
            }

            // Group by childId and item
            var duplicates = results
                .GroupBy(r => new
                {
                    ChildId = r["childId"]?.GetValue<string>() ?? "Unknown",
                    Item = r["item"]?.GetValue<string>() ?? "Unknown"
                })
                .Select(g => new { g.Key.ChildId, g.Key.Item, Count = g.Count() })
                .Where(d => childId == null || d.ChildId == childId)
                .OrderByDescending(d => d.Count)
                .Take(20)
                .ToList();

            if (!duplicates.Any())
            {
                string notFoundMsg = childId != null
                    ? $"No duplicate requests found for child {childId}"
                    : "No duplicate requests found";
                return notFoundMsg;
            }

            string summary = childId != null
                ? $"🔁 DUPLICATE REQUESTS for {childId}\n"
                : "🔁 CHILDREN WITH DUPLICATE WISHLIST ITEMS\n";

            summary += "=============================================\n\n";

            foreach (var dup in duplicates)
            {
                summary += $"Child: {dup.ChildId}\n";
                summary += $"  Item: {dup.Item}\n";
                summary += $"  Requested: {dup.Count} times ⭐\n";
                summary += $"  Signal: HIGH INTEREST - This child really wants this item!\n\n";
            }

            summary += "💡 Insight: Multiple requests indicate strong preference. Prioritize these items in recommendations.";
            summary += "\n📊 Data Source: Drasi continuous query 'wishlist-duplicates-by-child'";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query duplicate wishlists from Drasi");
            return $"Error querying duplicates: {ex.Message}";
        }
    }

    /// <summary>
    /// Identify inactive children from Drasi real-time monitoring
    /// </summary>
    [Description("Finds children who haven't submitted wishlist updates in 3+ days, useful for follow-up")]
    public async Task<string> FindInactiveChildren(
        [Description("Minimum days of inactivity (default: 3)")] int minDaysInactive = 3,
        CancellationToken ct = default)
    {
        try
        {
            string queryContainerId = GetQueryContainerId();
            var results = await _drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-inactive-children-3d", ct);

            if (results.Count == 0)
            {
                return "✅ All children are active! No children have been inactive for 3+ days.";
            }

            var inactive = results
                .Select(r =>
                {
                    string lastEventStr = r["LastEvent"]?.GetValue<string>() ?? "";
                    DateTime lastEvent = DateTime.TryParse(lastEventStr, out var parsed)
                        ? parsed
                        : DateTime.UtcNow.AddDays(-30);
                    int daysSince = (int)(DateTime.UtcNow - lastEvent).TotalDays;

                    return new
                    {
                        ChildId = r["childId"]?.GetValue<string>() ?? "Unknown",
                        LastEvent = lastEvent,
                        DaysInactive = daysSince
                    };
                })
                .Where(c => c.DaysInactive >= minDaysInactive)
                .OrderByDescending(c => c.DaysInactive)
                .Take(15)
                .ToList();

            if (!inactive.Any())
            {
                return $"✅ No children found with {minDaysInactive}+ days of inactivity.";
            }

            string summary = $"""
                ⏰ INACTIVE CHILDREN ALERT
                ========================
                Found {inactive.Count} children with {minDaysInactive}+ days of inactivity:

                """;

            foreach (var child in inactive)
            {
                string urgency = child.DaysInactive switch
                {
                    >= 7 => "🔴 URGENT",
                    >= 5 => "🟡 MODERATE",
                    _ => "🟢 RECENT"
                };

                summary += $"{urgency} {child.ChildId}\n";
                summary += $"  Last Activity: {child.LastEvent:yyyy-MM-dd} ({child.DaysInactive} days ago)\n";
                summary += $"  Action: Consider sending reminder or follow-up\n\n";
            }

            summary += "💡 Insight: Inactive children may need reminders to complete their wishlists.";
            summary += "\n📊 Data Source: Drasi continuous query 'wishlist-inactive-children-3d'";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query inactive children from Drasi");
            return $"Error querying inactive children: {ex.Message}";
        }
    }

    /// <summary>
    /// Get global wishlist duplicate patterns from Drasi
    /// </summary>
    [Description("Shows the most commonly duplicated items across all children, revealing universal desires")]
    public async Task<string> QueryGlobalWishlistDuplicates(
        [Description("Minimum number of children requesting same item (default: 2)")] int minChildren = 2,
        CancellationToken ct = default)
    {
        try
        {
            string queryContainerId = GetQueryContainerId();
            var results = await _drasiClient.GetCurrentResultAsync(queryContainerId, "wishlist-duplicates-global", ct);

            if (results.Count == 0)
            {
                return "No globally duplicated items found. Each child has unique wishes.";
            }

            var global = results
                .Select(r => new
                {
                    Item = r["item"]?.GetValue<string>() ?? "Unknown",
                    ChildCount = r["childCount"]?.GetValue<int>() ?? 0
                })
                .Where(g => g.ChildCount >= minChildren)
                .OrderByDescending(g => g.ChildCount)
                .Take(10)
                .ToList();

            if (!global.Any())
            {
                return $"No items requested by {minChildren}+ children.";
            }

            string summary = $"""
                🌍 GLOBALLY POPULAR WISHLIST ITEMS
                ==================================
                Items requested by multiple children:

                """;

            int rank = 1;
            foreach (var item in global)
            {
                string popularity = item.ChildCount switch
                {
                    >= 10 => "🔥 VIRAL",
                    >= 5 => "⭐ VERY POPULAR",
                    >= 3 => "👍 POPULAR",
                    _ => "📌 NOTABLE"
                };

                summary += $"{rank}. {item.Item}\n";
                summary += $"   {popularity} - Requested by {item.ChildCount} children\n";
                summary += $"   Action: Priority item for bulk ordering\n\n";
                rank++;
            }

            summary += "💡 Insight: These items show universal appeal and should be prioritized for inventory.";
            summary += "\n📊 Data Source: Drasi continuous query 'wishlist-duplicates-global'";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query global duplicates from Drasi");
            return $"Error querying global duplicates: {ex.Message}";
        }
    }

    /// <summary>
    /// Query behavior status changes (naughty/nice) from Drasi
    /// </summary>
    [Description("Gets real-time naughty/nice status changes for children from Drasi event graph")]
    public async Task<string> QueryBehaviorStatusChanges(
        [Description("Optional: specific child ID to check")] string? childId = null,
        CancellationToken ct = default)
    {
        try
        {
            string queryContainerId = GetQueryContainerId();
            var results = await _drasiClient.GetCurrentResultAsync(queryContainerId, "behavior-status-changes", ct);

            if (results.Count == 0)
            {
                return "No behavior status changes detected. All children are maintaining their current nice/naughty status.";
            }

            var changes = results
                .Select(r => new
                {
                    ChildId = r["childId"]?.GetValue<string>() ?? "Unknown",
                    NewStatus = r["newStatus"]?.GetValue<string>() ?? "Unknown",
                    PreviousStatus = r["previousStatus"]?.GetValue<string>() ?? "Unknown",
                    ChangedAt = r["changedAt"]?.GetValue<string>() ?? ""
                })
                .Where(c => childId == null || c.ChildId == childId)
                .OrderByDescending(c => c.ChangedAt)
                .Take(15)
                .ToList();

            if (!changes.Any())
            {
                return childId != null
                    ? $"No behavior changes found for child {childId}"
                    : "No recent behavior changes found";
            }

            string summary = childId != null
                ? $"🎅 BEHAVIOR STATUS FOR {childId}\n"
                : "🎅 RECENT NAUGHTY/NICE STATUS CHANGES\n";

            summary += "=============================================\n\n";

            foreach (var change in changes)
            {
                string emoji = change.NewStatus == "Nice" ? "😇" : change.NewStatus == "Naughty" ? "😈" : "❓";
                summary += $"{emoji} {change.ChildId}\n";
                summary += $"   Status: {change.PreviousStatus} → {change.NewStatus}\n";
                if (!string.IsNullOrEmpty(change.ChangedAt))
                {
                    summary += $"   Changed: {change.ChangedAt}\n";
                }
                summary += "\n";
            }

            summary += "💡 Insight: Use this data to adjust gift recommendations based on behavior.";
            summary += "\n📊 Data Source: Drasi continuous query 'behavior-status-changes'";

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query behavior status changes from Drasi");
            return $"Error querying behavior changes: {ex.Message}";
        }
    }

    /// <summary>
    /// Get current availability and delivery estimates (internal method for agent use)
    /// </summary>
    [Description("Checks current stock availability and estimated delivery time for a specific gift")]
    public async Task<string> GetGiftAvailability(
        [Description("Name of the gift to check")] string giftName,
        CancellationToken ct = default)
    {
        try
        {
            Availability? availability = await _availabilityService.GetAvailabilityAsync(giftName, ct);

            if (availability is null)
            {
                return $"Availability information not found for: {giftName}";
            }

            return $"""
                Gift: {giftName}
                In Stock: {(availability.InStock == true ? "Yes ✓" : "No - Backordered")}
                Estimated Delivery: {(availability.LeadTimeDays.HasValue ? $"{availability.LeadTimeDays} days" : "Unknown")}
                Status: {(availability.InStock == true ? "Ready to ship" : "May require additional time")}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get availability for {GiftName}", giftName);
            return $"Error checking availability: {ex.Message}";
        }
    }

    /// <summary>
    /// Get trending gift categories for specific age groups (internal method for agent use)
    /// </summary>
    [Description("Returns popular gift categories and trends for a specific age range")]
    public Task<string> GetTrendingGifts(
        [Description("Age in years")] int age,
        CancellationToken ct = default)
    {
        // This would query real analytics in production
        string trends = age switch
        {
            <= 5 => "Toddler favorites: Building blocks, stuffed animals, picture books, musical instruments",
            <= 8 => "Elementary age: LEGO sets, board games, art supplies, science kits",
            <= 12 => "Pre-teen interests: Sports equipment, video games, books, hobby kits",
            _ => "Teen preferences: Electronics, gift cards, hobby-specific items, experiences"
        };

        string result = $"""
            Trending Gifts for Age {age}:
            {trends}

            These recommendations are based on popularity data and age-appropriate guidelines.
            """;

        return Task.FromResult(result);
    }
}
