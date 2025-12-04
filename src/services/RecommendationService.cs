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
            You are the Elf Recommendation Agent for Santa's Workshop, powered by real-time data from Drasi Event Graph.
            Your job is to generate personalized gift recommendations based on:
            1. The child's behavior status (Nice, Naughty, or Unknown)
            2. Real-time trending data from other children's wishlists
            3. The child's preferences and age

            IMPORTANT BEHAVIOR RULES:
            - For NICE children: Recommend fun, rewarding gifts they'll love. They've earned it!
            - For NAUGHTY children: Focus on character-building items. ALWAYS include a "Lump of Coal" as the first recommendation -
              it's a traditional reminder that good behavior is rewarded. Follow with educational and growth-oriented items.
            - For UNKNOWN status: Provide balanced recommendations mixing fun and educational items.

            OUTPUT FORMAT: Return a JSON array of exactly 3-4 recommendations with this structure:
            [
              {
                "suggestion": "Gift Name",
                "rationale": "Why this gift is perfect for this child given their behavior status",
                "price": 29.99,
                "budgetFit": "within_budget",
                "inStock": true,
                "leadTimeDays": 3
              }
            ]

            Keep rationales encouraging and positive, even for naughty children - focus on growth potential!
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

            // Add timeout protection for Azure OpenAI calls (5 second timeout)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
            _logger.LogWarning("AI recommendation generation timed out for child {ChildId} after 5 seconds, using status-aware fallback ({Status})", childId, status);
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
        try
        {
            var trending = await _drasiClient.GetCurrentResultAsync("default", "wishlist-trending-1h", ct);
            if (trending.Count > 0)
            {
                var topItems = trending.Take(5).Select(t => t["item"]?.GetValue<string>() ?? "").Where(s => !string.IsNullOrEmpty(s));
                return $"Currently trending gifts: {string.Join(", ", topItems)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch trending data from Drasi");
        }
        return "No trending data available";
    }

    private static string BuildPrompt(string childId, NiceStatus status, ChildProfile? profile, string trendingContext, int topN)
    {
        string statusDescription = status switch
        {
            NiceStatus.Nice => "NICE - This child has been wonderful and deserves rewarding gifts!",
            NiceStatus.Naughty => "NAUGHTY - This child needs character-building items. Start with a Lump of Coal as tradition, then helpful growth items.",
            _ => "UNKNOWN - Provide a balanced mix of fun and educational items."
        };

        string preferences = profile?.Preferences is { Length: > 0 }
            ? string.Join(", ", profile.Preferences)
            : "not specified";

        string age = profile?.Age?.ToString() ?? "unknown";

        return $"""
            Generate {topN} personalized gift recommendations for:

            Child ID: {childId}
            Behavior Status: {statusDescription}
            Age: {age}
            Known Preferences: {preferences}

            Real-time context from Santa's Workshop:
            {trendingContext}

            Remember:
            - For NAUGHTY children, the FIRST recommendation MUST be "🪨 Lump of Coal" with an encouraging message about improvement
            - All recommendations should have positive, encouraging rationales
            - Consider age-appropriateness
            - Include price estimates and availability

            Return ONLY a valid JSON array of recommendations.
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
                new(Guid.NewGuid().ToString(), childId, "🪨 Lump of Coal",
                    "A traditional reminder that Santa notices behavior. But don't worry - every day is a chance to start fresh and earn wonderful gifts next year!",
                    1.99m, "within_budget", new Availability(true, 1)),
                new(Guid.NewGuid().ToString(), childId, "📚 Character Building Story Collection",
                    "Stories about kindness, sharing, and making good choices. Perfect for inspiring positive change!",
                    19.99m, "within_budget", new Availability(true, 3)),
                new(Guid.NewGuid().ToString(), childId, "🧹 Helpful Helper Chore Chart",
                    "Learning responsibility through helping at home. Each task completed is a step toward the Nice list!",
                    12.99m, "within_budget", new Availability(true, 2)),
                new(Guid.NewGuid().ToString(), childId, "🎯 Goal Setting Journal",
                    "Set behavior goals and track your awesome progress. Santa loves to see improvement!",
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
