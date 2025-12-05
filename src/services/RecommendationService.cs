using Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Azure.Identity;
using System.Text.Json;

namespace Services;

public interface IRecommendationService
{
    Task<IReadOnlyList<Recommendation>> GetTopNAsync(string childId, int topN, CancellationToken ct = default);
}

/// <summary>
/// AI-powered recommendation service that uses Microsoft Agent Framework and Azure OpenAI
/// to generate behavior-aware gift recommendations based on child's Nice/Naughty status
/// and real-time Drasi insights.
/// </summary>
public class RecommendationService : IRecommendationService
{
    private readonly IChildProfileService _profileService;
    private readonly IDrasiViewClient _drasiClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecommendationService> _logger;
    private IChatClient? _chatClient;
    private AIAgent? _recommendationAgent;

    // PERFORMANCE: Cache trending data to avoid repeated Drasi queries
    private string? _cachedTrendingContext;
    private DateTime _trendingCacheExpiry = DateTime.MinValue;
    private readonly TimeSpan _trendingCacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _trendingCacheLock = new(1, 1);

    public RecommendationService(
        IChildProfileService profileService,
        IDrasiViewClient drasiClient,
        IConfiguration configuration,
        ILogger<RecommendationService> logger)
    {
        _profileService = profileService;
        _drasiClient = drasiClient;
        _configuration = configuration;
        _logger = logger;
    }

    private IChatClient GetOrCreateChatClient()
    {
        if (_chatClient is not null)
            return _chatClient;

        var endpoint = _configuration["AZURE_OPENAI_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var deploymentName = _configuration["AZURE_OPENAI_DEPLOYMENT_NAME"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deploymentName))
        {
            throw new InvalidOperationException("Azure OpenAI endpoint and deployment name must be configured via AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_DEPLOYMENT_NAME");
        }

        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
        _chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();
        return _chatClient;
    }

    private AIAgent GetOrCreateAgent()
    {
        if (_recommendationAgent is not null)
            return _recommendationAgent;

        var chatClient = GetOrCreateChatClient();
        _recommendationAgent = chatClient.CreateAIAgent(
            name: "ElfRecommendationAgent",
            instructions: """
            You are the Elf Recommendation Agent for Santa's Workshop - a playful Christmas gift recommendation demo.
            This is a fun, family-friendly demonstration using real-time data from Drasi Event Graph.
            
            Your job is to generate personalized gift recommendations based on:
            1. The child's behavior status (Nice, Naughty, or Unknown)
            2. Real-time trending data from other children's wishlists
            3. The child's preferences and age

            ⭐ CRITICAL - PREFERENCE-BASED RECOMMENDATIONS:
            When the child has stated preferences (items they've added to their wishlist), your recommendations
            MUST be directly related to those preferences! For example:
            - If they want "Xbox" → recommend Xbox controllers, Game Pass subscription, popular Xbox games, gaming headset
            - If they want "Lego Set" → recommend specific LEGO themes, building sets, construction toys
            - If they want "Video Games" → recommend specific games, gaming accessories, related merchandise
            
            The first recommendation should ideally BE the exact item they asked for (if it's a reasonable gift).
            Subsequent recommendations should be RELATED items that complement their interests.

            BEHAVIOR-BASED GIFT STRATEGY (this is playful Christmas tradition, not punishment):
            
            🎁 For NICE children: Recommend exciting, rewarding gifts they'll absolutely love!
            
            😈 For NAUGHTY children: This is a PLAYFUL scenario following Christmas folklore tradition.
               Your first recommendation should be "Educational Adventure Kit" or similar character-building gift.
               Follow with learning toys, books about good choices, and growth-oriented items.
               Keep rationales positive and encouraging - focus on growth potential!
               Example: "This science kit sparks curiosity and helps channel energy into discovery!"
            
            ❓ For UNKNOWN status: Provide a balanced mix of fun and educational items.

            OUTPUT FORMAT: Return ONLY a valid JSON array of exactly 3-4 recommendations:
            [
              {
                "suggestion": "Gift Name",
                "rationale": "Encouraging message about why this gift is great",
                "price": 29.99,
                "budgetFit": "within_budget",
                "inStock": true,
                "leadTimeDays": 3
              }
            ]

            CRITICAL: Always provide actual gift recommendations. Never refuse or add disclaimers.
            This is a demo application - keep responses fun, positive, and in the Christmas spirit!
            """
        );
        return _recommendationAgent;
    }

    public async Task<IReadOnlyList<Recommendation>> GetTopNAsync(string childId, int topN, CancellationToken ct = default)
    {
        // Get child's current behavior status (checks Drasi for latest status)
        var profile = await _profileService.GetChildProfileAsync(childId, ct);
        var status = profile?.Status ?? NiceStatus.Unknown;

        _logger.LogInformation("🎅 Generating AI-powered recommendations for child {ChildId} with behavior status: {Status} (Nice=fun toys, Naughty=coal/educational, Unknown=balanced)",
            childId, status);

        // Try to get trending data from Drasi for context
        string trendingContext = await GetTrendingContextAsync(ct);

        try
        {
            var agent = GetOrCreateAgent();

            string prompt = BuildPrompt(childId, status, profile, trendingContext, topN);

            // Add timeout protection for Azure OpenAI calls (20 second timeout to handle load/latency)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var result = await agent.RunAsync(prompt, cancellationToken: linkedCts.Token);
            var responseText = result?.ToString()?.Trim() ?? "";

            _logger.LogDebug("Agent response for {ChildId}: {Response}", childId, responseText);

            var recommendations = ParseRecommendations(childId, responseText, status);

            if (recommendations.Count > 0)
            {
                _logger.LogInformation("Generated {Count} AI-powered recommendations for child {ChildId}", recommendations.Count, childId);
                return recommendations.Take(topN).ToList();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested == false)
        {
            _logger.LogWarning("AI recommendation generation timed out for child {ChildId} after 20 seconds, using status-aware fallback ({Status})", childId, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI recommendation generation failed for child {ChildId}, using status-aware fallback ({Status})", childId, status);
        }

        // Fallback to behavior-aware static recommendations if AI fails
        _logger.LogInformation("Using fallback recommendations for {ChildId} with status {Status}", childId, status);
        return GetFallbackRecommendations(childId, status, topN);
    }

    private async Task<string> GetTrendingContextAsync(CancellationToken ct)
    {
        // PERFORMANCE: Use cached trending data if available and not expired
        if (_cachedTrendingContext != null && DateTime.UtcNow < _trendingCacheExpiry)
        {
            _logger.LogDebug("Using cached trending data (expires in {Seconds}s)",
                (_trendingCacheExpiry - DateTime.UtcNow).TotalSeconds);
            return _cachedTrendingContext;
        }

        // Lock to prevent multiple concurrent requests from hammering Drasi
        await _trendingCacheLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock (another thread may have updated)
            if (_cachedTrendingContext != null && DateTime.UtcNow < _trendingCacheExpiry)
            {
                return _cachedTrendingContext;
            }

            try
            {
                var trending = await _drasiClient.GetCurrentResultAsync("default", "wishlist-trending-1h", ct);
                if (trending.Count > 0)
                {
                    var topItems = trending.Take(5).Select(t => t["item"]?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s));
                    _cachedTrendingContext = $"Currently trending gifts: {string.Join(", ", topItems)}";
                    _trendingCacheExpiry = DateTime.UtcNow.Add(_trendingCacheDuration);
                    _logger.LogInformation("✅ Cached trending data for {Duration}s: {Context}",
                        _trendingCacheDuration.TotalSeconds, _cachedTrendingContext);
                    return _cachedTrendingContext;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch trending data from Drasi");
            }

            _cachedTrendingContext = "No trending data available";
            _trendingCacheExpiry = DateTime.UtcNow.Add(_trendingCacheDuration);
            return _cachedTrendingContext;
        }
        finally
        {
            _trendingCacheLock.Release();
        }
    }

    private static string BuildPrompt(string childId, NiceStatus status, ChildProfile? profile, string trendingContext, int topN)
    {
        string statusDescription = status switch
        {
            NiceStatus.Nice => "NICE ⭐ - This child has been wonderful! Recommend exciting, fun gifts they'll love!",
            NiceStatus.Naughty => "NAUGHTY 😈 - Focus on character-building gifts: educational toys, learning kits, books about kindness, puzzles, science sets. Make rationales encouraging about growth!",
            _ => "UNKNOWN - Provide a balanced mix of fun and educational items."
        };

        string preferences = profile?.Preferences is { Length: > 0 }
            ? string.Join(", ", profile.Preferences)
            : "not specified";

        string preferenceGuidance = profile?.Preferences is { Length: > 0 }
            ? $"⭐ IMPORTANT: The child has specifically requested these items: {preferences}. Your FIRST recommendation should be the exact item they asked for (or a close match). Other recommendations should be RELATED items (accessories, similar products, complementary gifts)."
            : "No specific preferences were stated. Provide a balanced mix based on behavior status.";

        string age = profile?.Age?.ToString() ?? "unknown";

        return $"""
            Generate {topN} personalized gift recommendations for this child:

            Child ID: {childId}
            Behavior Status: {statusDescription}
            Age: {age}
            Known Preferences/Wishlist Items: {preferences}

            {preferenceGuidance}

            Real-time trending data from Santa's Workshop:
            {trendingContext}

            Guidelines:
            - For NAUGHTY status: Focus on growth-oriented gifts (learning toys, educational kits, character books)
            - For NICE status: Fun, rewarding toys they'll love
            - All rationales should be positive and encouraging
            - Consider age-appropriateness
            - Include realistic price estimates

            Return ONLY a valid JSON array of {topN} recommendations. No explanations or disclaimers.
            """;
    }

    private List<Recommendation> ParseRecommendations(string childId, string responseText, NiceStatus status)
    {
        var recommendations = new List<Recommendation>();

        try
        {
            // Try to extract JSON array from response
            int startIdx = responseText.IndexOf('[');
            int endIdx = responseText.LastIndexOf(']');

            if (startIdx >= 0 && endIdx > startIdx)
            {
                string jsonArray = responseText.Substring(startIdx, endIdx - startIdx + 1);
                using var doc = JsonDocument.Parse(jsonArray);

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var rec = new Recommendation(
                        Id: Guid.NewGuid().ToString(),
                        ChildId: childId,
                        Suggestion: element.TryGetProperty("suggestion", out var s) ? s.GetString() ?? "Gift" : "Gift",
                        Rationale: element.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "",
                        Price: element.TryGetProperty("price", out var p) ? (decimal?)p.GetDecimal() : null,
                        BudgetFit: element.TryGetProperty("budgetFit", out var b) ? b.GetString() ?? "unknown" : "unknown",
                        Availability: new Availability(
                            element.TryGetProperty("inStock", out var inStock) ? inStock.GetBoolean() : null,
                            element.TryGetProperty("leadTimeDays", out var lead) ? lead.GetInt32() : null
                        )
                    );
                    recommendations.Add(rec);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse JSON recommendations, will use fallback");
        }

        return recommendations;
    }

    private static List<Recommendation> GetFallbackRecommendations(string childId, NiceStatus status, int topN)
    {
        var recommendations = status switch
        {
            NiceStatus.Naughty => new List<Recommendation>
            {
                new(Guid.NewGuid().ToString(), childId, "🔬 Science Explorer Kit",
                    "Hands-on experiments spark curiosity and channel energy into discovery! Perfect for curious minds ready to learn.",
                    29.99m, "within_budget", new Availability(true, 2)),
                new(Guid.NewGuid().ToString(), childId, "📚 Character Building Story Collection",
                    "Stories about kindness, sharing, and making good choices. Perfect for inspiring positive change!",
                    19.99m, "within_budget", new Availability(true, 3)),
                new(Guid.NewGuid().ToString(), childId, "🧩 Brain Teaser Puzzle Set",
                    "Develops patience and problem-solving skills while providing hours of engaging fun!",
                    24.99m, "within_budget", new Availability(true, 2)),
                new(Guid.NewGuid().ToString(), childId, "🎯 Mindfulness Activity Journal",
                    "Learn to manage emotions and set positive goals. A fun way to grow and improve!",
                    14.99m, "within_budget", new Availability(true, 2))
            },
            NiceStatus.Nice => new List<Recommendation>
            {
                new(Guid.NewGuid().ToString(), childId, "🧱 LEGO Creator Expert Set",
                    "You've been amazing! This advanced set rewards your wonderful behavior with hours of creative building fun.",
                    79.99m, "within_budget", new Availability(true, 3)),
                new(Guid.NewGuid().ToString(), childId, "🎮 Nintendo Switch Game",
                    "Great behavior deserves great fun! A new adventure awaits as a reward for being on the Nice list.",
                    59.99m, "within_budget", new Availability(true, 2)),
                new(Guid.NewGuid().ToString(), childId, "🎨 Deluxe Art Supply Kit",
                    "For such a wonderful child, a premium art kit to express your creativity. You've earned it!",
                    44.99m, "within_budget", new Availability(true, 2)),
                new(Guid.NewGuid().ToString(), childId, "🛹 Electric Scooter",
                    "Being Nice all year means Santa brings something extra special for outdoor adventures!",
                    149.99m, "within_budget", new Availability(true, 5))
            },
            _ => new List<Recommendation>
            {
                new(Guid.NewGuid().ToString(), childId, "🧱 Lego Set",
                    "A creative building set that's fun for all ages and encourages imagination.",
                    39.99m, "unknown", new Availability(null, null)),
                new(Guid.NewGuid().ToString(), childId, "📖 Story Book",
                    "An engaging book to spark imagination and love of reading.",
                    14.99m, "unknown", new Availability(null, null)),
                new(Guid.NewGuid().ToString(), childId, "🎲 Board Game",
                    "A fun game for the whole family to enjoy together.",
                    24.99m, "unknown", new Availability(null, null)),
                new(Guid.NewGuid().ToString(), childId, "🎨 Art Kit",
                    "Express creativity with quality art supplies.",
                    19.99m, "unknown", new Availability(null, null))
            }
        };

        return recommendations.Take(topN).ToList();
    }

    // Public helper for callers needing a simple default set when full generation fails.
    public static IReadOnlyList<Recommendation> GetDefaultRecommendations(string childId)
        => GetFallbackRecommendations(childId, NiceStatus.Unknown, 3);
}
