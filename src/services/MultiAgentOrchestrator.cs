using Azure;
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

    /// <summary>
    /// Optimized parallel execution - Analyst and Creative run concurrently, then Reviewer synthesizes.
    /// This reduces total time from ~60s (3 sequential) to ~40s (2 parallel + 1 sequential).
    /// </summary>
    Task<string> RunCollaborativeRecommendationOptimizedAsync(string childId, NiceStatus status, CancellationToken ct = default);
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

            // Prefer API Key if provided (for emergency workaround), otherwise use Managed Identity
            string? apiKey = _configuration["AZURE_OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

            // Configure client options with extended network timeout for AI operations
            // Each agent call may take 20-40s with tool calling, so we need 60s per call
            var clientOptions = new Azure.AI.OpenAI.AzureOpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(60)
            };

            Azure.AI.OpenAI.AzureOpenAIClient azureClient;
            if (!string.IsNullOrEmpty(apiKey))
            {
                azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey), clientOptions);
            }
            else
            {
                azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential(), clientOptions);
            }
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
            You are the Behavior Analyst Elf. Be CONCISE - output max 200 words.

            WORKFLOW (call tools in this order):
            1. GetChildWishlistItems - see what they requested
            2. GetChildBehaviorHistory - check their behavior
            3. QueryTrendingWishlistItems - see global trends

            OUTPUT (brief bullet points):
            • Child's wishlist items
            • Behavior assessment (Nice/Naughty reasoning)
            • Top interests
            • Budget considerations
            """,
            tools: tools
        );

        // Creative Agent - generates creative gift ideas with inventory access
        _creativeAgent = chatClient.CreateAIAgent(
            name: "CreativeGiftElf",
            instructions: """
            You are the Creative Gift Elf. Be CONCISE - output max 200 words.

            WORKFLOW:
            1. SearchGiftInventory - find matching gifts
            2. CheckBudgetConstraints - validate prices

            OUTPUT 3 gift ideas with:
            • Gift name and brief description (1 line each)
            • Why it fits the child's interests
            • Price range

            Nice children: rewarding gifts. Naughty children: educational/character-building.
            """,
            tools: tools
        );

        // Reviewer Agent - validates and refines recommendations
        _reviewerAgent = chatClient.CreateAIAgent(
            name: "QualityReviewerElf",
            instructions: """
            You are the Quality Reviewer Elf. Be CONCISE - output max 300 words.

            Review and synthesize the analysis and suggestions into FINAL recommendations.

            OUTPUT:
            🎁 TOP 3 GIFT RECOMMENDATIONS
            For each gift:
            1. [Gift Name] - [One-line description]
               ✓ Why it's perfect: [1 sentence]
               💰 Budget: [price]

            End with a brief encouraging message for the child.
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

        var startTime = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting collaborative multi-agent recommendation for child {ChildId} with status {Status}",
            childId, status);

        try
        {
            // STEP 1: Gather data in parallel (Profile + Existing Recommendations)
            var profileTask = _profileService.GetChildProfileAsync(childId, ct);
            var existingRecsTask = _recommendationService.GetTopNAsync(childId, 5, ct);
            await Task.WhenAll(profileTask, existingRecsTask);

            ChildProfile? profile = await profileTask;
            IReadOnlyList<Recommendation> existingRecs = await existingRecsTask;

            var dataFetchTime = startTime.ElapsedMilliseconds;
            _logger.LogInformation("Data fetching completed in {ElapsedMs}ms for {ChildId}", dataFetchTime, childId);

            string context = $"""
            Child ID: {childId}
            Status: {status}
            Age: {profile?.Age ?? 0}
            Preferences: {string.Join(", ", profile?.Preferences ?? [])}
            Existing recommendations: {string.Join("; ", existingRecs.Select(r => r.Suggestion))}
            """;

            var analystStartTime = startTime.ElapsedMilliseconds;
            AgentRunResponse? analysisResult = await _analystAgent!.RunAsync($"Analyze this child profile:\n{context}", cancellationToken: ct);
            string analysis = analysisResult?.ToString() ?? "Unable to analyze profile";

            var analystTime = startTime.ElapsedMilliseconds - analystStartTime;
            _logger.LogInformation("Analyst completed analysis for {ChildId} in {ElapsedMs}ms", childId, analystTime);

            // STEP 2: Creative agent generates ideas based on analysis
            string creativePrompt = $"""
            Based on this analysis:
            {analysis}

            Generate 3-5 creative gift recommendations that align with the child's {status} status.
            """;

            var creativeStartTime = startTime.ElapsedMilliseconds;
            AgentRunResponse? creativeResult = await _creativeAgent!.RunAsync(creativePrompt, cancellationToken: ct);
            string suggestions = creativeResult?.ToString() ?? "Unable to generate suggestions";

            var creativeTime = startTime.ElapsedMilliseconds - creativeStartTime;
            _logger.LogInformation("Creative agent generated suggestions for {ChildId} in {ElapsedMs}ms", childId, creativeTime);

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

            var reviewerStartTime = startTime.ElapsedMilliseconds;
            AgentRunResponse? finalResult = await _reviewerAgent!.RunAsync(reviewPrompt, cancellationToken: ct);
            string finalRecommendations = finalResult?.ToString() ?? suggestions;

            var reviewerTime = startTime.ElapsedMilliseconds - reviewerStartTime;
            var totalTime = startTime.ElapsedMilliseconds;
            _logger.LogInformation("Reviewer completed refinement for {ChildId} in {ElapsedMs}ms. Total time: {TotalMs}ms",
                childId, reviewerTime, totalTime);

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

    /// <summary>
    /// Optimized version that runs Analyst and Creative agents in parallel,
    /// reducing total time from ~60s to ~40s.
    /// </summary>
    public async Task<string> RunCollaborativeRecommendationOptimizedAsync(string childId, NiceStatus status, CancellationToken ct = default)
    {
        // Initialize agents lazily on first use
        if (_analystAgent == null || _creativeAgent == null || _reviewerAgent == null)
        {
            InitializeAgents();
        }

        var startTime = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("[OPTIMIZED] Starting parallel multi-agent recommendation for child {ChildId} with status {Status}",
            childId, status);

        try
        {
            // STEP 1: Gather data in parallel (Profile + Existing Recommendations)
            var profileTask = _profileService.GetChildProfileAsync(childId, ct);
            var existingRecsTask = _recommendationService.GetTopNAsync(childId, 5, ct);
            await Task.WhenAll(profileTask, existingRecsTask);

            ChildProfile? profile = await profileTask;
            IReadOnlyList<Recommendation> existingRecs = await existingRecsTask;

            var dataFetchTime = startTime.ElapsedMilliseconds;
            _logger.LogInformation("[OPTIMIZED] Data fetching completed in {ElapsedMs}ms", dataFetchTime);

            string context = $"""
            Child: {childId} | Status: {status} | Age: {profile?.Age ?? 0}
            Preferences: {string.Join(", ", profile?.Preferences ?? [])}
            Prior recommendations: {string.Join("; ", existingRecs.Take(3).Select(r => r.Suggestion))}
            """;

            // STEP 2: Run Analyst and Creative agents IN PARALLEL
            // They both use tools independently, so no need to wait for one to finish
            var parallelStart = startTime.ElapsedMilliseconds;
            _logger.LogInformation("[OPTIMIZED] Starting PARALLEL agent execution (Analyst + Creative)");

            var analystTask = _analystAgent!.RunAsync(
                $"Analyze child profile:\n{context}",
                cancellationToken: ct);

            var creativeTask = _creativeAgent!.RunAsync(
                $"Generate 3 gift ideas for {status} child.\nContext:\n{context}",
                cancellationToken: ct);

            // Wait for both to complete
            await Task.WhenAll(analystTask, creativeTask);

            var parallelTime = startTime.ElapsedMilliseconds - parallelStart;
            _logger.LogInformation("[OPTIMIZED] Parallel agents completed in {ElapsedMs}ms (saved ~20s vs sequential)", parallelTime);

            string analysis = (await analystTask)?.ToString() ?? "Unable to analyze profile";
            string suggestions = (await creativeTask)?.ToString() ?? "Unable to generate suggestions";

            // STEP 3: Reviewer synthesizes both outputs (must be sequential)
            string reviewPrompt = $"""
            Synthesize these into FINAL gift recommendations for {childId} ({status}):

            ANALYSIS:
            {analysis}

            SUGGESTIONS:
            {suggestions}

            Output 3 polished recommendations with reasons.
            """;

            var reviewerStart = startTime.ElapsedMilliseconds;
            AgentRunResponse? finalResult = await _reviewerAgent!.RunAsync(reviewPrompt, cancellationToken: ct);
            string finalRecommendations = finalResult?.ToString() ?? suggestions;

            var reviewerTime = startTime.ElapsedMilliseconds - reviewerStart;
            var totalTime = startTime.ElapsedMilliseconds;
            _logger.LogInformation("[OPTIMIZED] Reviewer completed in {ReviewerMs}ms. TOTAL: {TotalMs}ms",
                reviewerTime, totalTime);

            return $"""
            === Multi-Agent Collaborative Recommendation (Optimized) ===
            Child: {childId} | Status: {status}
            ⚡ Execution: {totalTime}ms (parallel optimization enabled)

            📊 Analyst Insights:
            {analysis}

            💡 Creative Suggestions:
            {suggestions}

            🎁 Final Recommendations:
            {finalRecommendations}
            """;
        }
        catch (OperationCanceledException)
        {
            var elapsed = startTime.ElapsedMilliseconds;
            _logger.LogWarning("[OPTIMIZED] Request cancelled after {ElapsedMs}ms for child {ChildId}", elapsed, childId);
            return $"Request cancelled after {elapsed}ms. The multi-agent orchestration was interrupted.";
        }
        catch (Exception ex)
        {
            var elapsed = startTime.ElapsedMilliseconds;
            _logger.LogError(ex, "[OPTIMIZED] Multi-agent orchestration failed after {ElapsedMs}ms for child {ChildId}",
                elapsed, childId);
            return $"Collaborative recommendation failed after {elapsed}ms: {ex.Message}";
        }
    }
}
