using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Models;
using Services;

namespace Services;

/// <summary>
/// Specialized agents for the North Pole workshop - demonstrates multi-agent orchestration
/// </summary>
public interface IMultiAgentOrchestrator
{
    Task<string> RunCollaborativeRecommendationAsync(string childId, NiceStatus status, CancellationToken ct = default);
}

public class MultiAgentOrchestrator : IMultiAgentOrchestrator
{
    private readonly IChildProfileService _profileService;
    private readonly IRecommendationService _recommendationService;
    private readonly AgentToolLibrary _toolLibrary;
    private readonly ILogger<MultiAgentOrchestrator> _logger;
    private readonly IConfiguration _configuration;

    // Specialized agents for different aspects
    private AIAgent? _analystAgent;
    private AIAgent? _creativeAgent;
    private AIAgent? _reviewerAgent;

    // IChatClient created lazily when needed
    private IChatClient? _chatClient;

    public MultiAgentOrchestrator(
        IChildProfileService profileService,
        IRecommendationService recommendationService,
        AgentToolLibrary toolLibrary,
        IConfiguration configuration,
        ILogger<MultiAgentOrchestrator> logger)
    {
        _profileService = profileService;
        _recommendationService = recommendationService;
        _toolLibrary = toolLibrary;
        _configuration = configuration;
        _logger = logger;
    }

    private IChatClient GetChatClient()
    {
        if (_chatClient == null)
        {
            string endpoint = _configuration["AZURE_OPENAI_ENDPOINT"]
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            string deploymentName = _configuration["AZURE_OPENAI_DEPLOYMENT_NAME"]
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
                ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not configured.");

            var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential());
            _chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();
        }
        return _chatClient;
    }

    private void InitializeAgents()
    {
        var chatClient = GetChatClient();

        // Create AI tools from the tool library methods
        // Use AIFunctionFactory.Create with method delegates
        // NOW INCLUDING DRASI REAL-TIME QUERY TOOLS! 🚀
        IList<AITool> tools = new List<AITool>
        {
            // CRITICAL: Child's actual wishlist (what they requested)
            AIFunctionFactory.Create(_toolLibrary.GetChildWishlistItems),

            // Original tools
            AIFunctionFactory.Create(_toolLibrary.GetChildBehaviorHistory),
            AIFunctionFactory.Create(_toolLibrary.SearchGiftInventory),
            AIFunctionFactory.Create(_toolLibrary.CheckBudgetConstraints),
            AIFunctionFactory.Create(_toolLibrary.GetGiftAvailability),
            AIFunctionFactory.Create(_toolLibrary.GetTrendingGifts),

            // NEW: Drasi real-time event graph query tools
            AIFunctionFactory.Create(_toolLibrary.QueryTrendingWishlistItems),
            AIFunctionFactory.Create(_toolLibrary.FindChildrenWithDuplicateWishlists),
            AIFunctionFactory.Create(_toolLibrary.FindInactiveChildren),
            AIFunctionFactory.Create(_toolLibrary.QueryGlobalWishlistDuplicates)
        };

        // Analyst Agent - extracts insights from child data with tool access
        _analystAgent = chatClient.CreateAIAgent(
            name: "BehaviorAnalyst",
            instructions: """
            You are the Behavior Analyst Elf at the North Pole. Your role is to:
            - Analyze child behavior patterns and status (Nice/Naughty)
            - Identify key interests and developmental needs
            - Extract insights that will guide gift recommendations
            - Consider age-appropriateness and educational value
            - USE TOOLS to get REAL DATA from our live systems instead of guessing

            � CRITICAL FIRST STEP: ALWAYS call GetChildWishlistItems FIRST!
            This shows you exactly what the child requested. Your recommendations must be based on their actual wishes.

            🎯 IMPORTANT: You have access to DRASI real-time event graph tools:
            - QueryTrendingWishlistItems: See what's hot RIGHT NOW across all children
            - FindChildrenWithDuplicateWishlists: Find items a child REALLY wants (multiple requests)
            - QueryGlobalWishlistDuplicates: See universally popular items
            - FindInactiveChildren: Identify children who may need reminders

            Also Available:
            - GetChildBehaviorHistory: Get individual child's actual behavior data
            - GetTrendingGifts: See age-appropriate popular categories

            WORKFLOW:
            1. Call GetChildWishlistItems to see what they actually requested
            2. Check if this child has duplicate wishlist items (shows strong interest)
            3. Check trending items to see if child's wishes align with global trends
            4. Analyze behavior and preferences

            Output a concise analysis highlighting:
            - What the child specifically requested (from their wishlist!)
            - Current behavior assessment
            - Key interests and patterns (use DRASI data!)
            - Items child has requested multiple times (PRIORITY!)
            - How child's wishes compare to trending items
            - Recommended gift themes (educational vs entertainment)
            - Budget considerations
            """,
            tools: tools
        );

        // Creative Agent - generates creative gift ideas with inventory access
        _creativeAgent = chatClient.CreateAIAgent(
            name: "CreativeGiftElf",
            instructions: """
            You are the Creative Gift Elf. Your role is to:
            - Generate imaginative, engaging gift recommendations
            - Consider both fun and educational value
            - Suggest alternatives that align with behavior status
            - Create compelling descriptions that excite children
            - USE TOOLS to check real inventory and availability

            Available Tools:
            - SearchGiftInventory: Find actual gifts in stock
            - CheckBudgetConstraints: Validate prices
            - GetGiftAvailability: Check if items can be delivered on time

            For Nice children: Focus on rewarding achievements and interests
            For Naughty children: Emphasize character-building and educational items

            Output creative gift suggestions with engaging descriptions.
            """,
            tools: tools
        );

        // Reviewer Agent - validates and refines recommendations
        _reviewerAgent = chatClient.CreateAIAgent(
            name: "QualityReviewerElf",
            instructions: """
            You are the Quality Reviewer Elf. Your role is to:
            - Review gift recommendations for appropriateness
            - Ensure alignment with behavior status (Nice/Naughty)
            - Validate safety, age-appropriateness, and budget
            - Refine rationales to be clear and encouraging

            Provide constructive feedback and final polished recommendations.
            Output refined recommendations with improved rationales.
            """
        );
    }

    public async Task<string> RunCollaborativeRecommendationAsync(string childId, NiceStatus status, CancellationToken ct = default)
    {
        // Initialize agents lazily on first use
        if (_analystAgent == null || _creativeAgent == null || _reviewerAgent == null)
        {
            InitializeAgents();
        }

        _logger.LogInformation("Starting collaborative multi-agent recommendation for child {ChildId} with status {Status}",
            childId, status);

        try
        {
            // STEP 1: Analyst gathers insights
            ChildProfile? profile = await _profileService.GetChildProfileAsync(childId, ct);
            IReadOnlyList<Recommendation> existingRecs = await _recommendationService.GetTopNAsync(childId, 5, ct);

            string context = $"""
            Child ID: {childId}
            Status: {status}
            Age: {profile?.Age ?? 0}
            Preferences: {string.Join(", ", profile?.Preferences ?? [])}
            Existing recommendations: {string.Join("; ", existingRecs.Select(r => r.Suggestion))}
            """;

            AgentRunResponse? analysisResult = await _analystAgent!.RunAsync($"Analyze this child profile:\n{context}", cancellationToken: ct);
            string analysis = analysisResult?.ToString() ?? "Unable to analyze profile";

            _logger.LogInformation("Analyst completed analysis for {ChildId}", childId);

            // STEP 2: Creative agent generates ideas based on analysis
            string creativePrompt = $"""
            Based on this analysis:
            {analysis}

            Generate 3-5 creative gift recommendations that align with the child's {status} status.
            """;

            AgentRunResponse? creativeResult = await _creativeAgent!.RunAsync(creativePrompt, cancellationToken: ct);
            string suggestions = creativeResult?.ToString() ?? "Unable to generate suggestions";

            _logger.LogInformation("Creative agent generated suggestions for {ChildId}", childId);

            // STEP 3: Reviewer refines and validates
            string reviewPrompt = $"""
            Review these gift recommendations:
            {suggestions}

            Context:
            {context}

            Refine the recommendations to ensure they are:
            - Age-appropriate and safe
            - Aligned with {status} status (character-building if Naughty, rewarding if Nice)
            - Within reasonable budget
            - Encouraging and positive in tone
            """;

            AgentRunResponse? finalResult = await _reviewerAgent!.RunAsync(reviewPrompt, cancellationToken: ct);
            string finalRecommendations = finalResult?.ToString() ?? suggestions;

            _logger.LogInformation("Reviewer completed refinement for {ChildId}", childId);

            // Return the collaborative result
            return $"""
            === Multi-Agent Collaborative Recommendation ===
            Child: {childId}
            Status: {status}

            [Analyst Insights]
            {analysis}

            [Creative Suggestions]
            {suggestions}

            [Final Refined Recommendations]
            {finalRecommendations}
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-agent orchestration failed for child {ChildId}. Details: {Details}",
                childId, ex.ToString());
            return $"Collaborative recommendation failed: {ex.Message}\n\nStack Trace: {ex.StackTrace}";
        }
    }
}
