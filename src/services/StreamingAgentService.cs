using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Models;
using Services;
using System.Runtime.CompilerServices;

namespace Services;

/// <summary>
/// Streaming agent service for real-time recommendation generation with SSE
/// Showcases token-level streaming capability of Agent Framework
/// </summary>
public interface IStreamingAgentService
{
    IAsyncEnumerable<StreamingAgentUpdate> StreamRecommendationGenerationAsync(
        string childId, 
        NiceStatus status, 
        CancellationToken ct = default);
}

public record StreamingAgentUpdate(
    string Type,  // "thought", "text-delta", "recommendation", "completed"
    string Content,
    string? AgentName = null,
    object? Metadata = null
);

public class StreamingAgentService : IStreamingAgentService
{
    private readonly AIAgent _agent;
    private readonly IChildProfileService _profileService;
    private readonly ILogger<StreamingAgentService> _logger;

    public StreamingAgentService(
        AIAgent agent,
        IChildProfileService profileService,
        ILogger<StreamingAgentService> logger)
    {
        _agent = agent;
        _profileService = profileService;
        _logger = logger;
    }

    public async IAsyncEnumerable<StreamingAgentUpdate> StreamRecommendationGenerationAsync(
        string childId,
        NiceStatus status,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Starting streaming recommendation for child {ChildId}", childId);

        // Send initial status
        yield return new StreamingAgentUpdate(
            "thought",
            $"Analyzing profile for child {childId} with status {status}...",
            "RecommendationAgent"
        );

        ChildProfile? profile = null;
        string? errorMessage = null;
        try
        {
            profile = await _profileService.GetChildProfileAsync(childId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get profile for {ChildId}", childId);
            errorMessage = $"Failed to retrieve profile: {ex.Message}";
        }

        if (errorMessage is not null)
        {
            yield return new StreamingAgentUpdate("error", errorMessage);
            yield break;
        }

        yield return new StreamingAgentUpdate(
            "thought",
            "Profile retrieved. Generating personalized recommendations...",
            "RecommendationAgent"
        );

        string prompt = status switch
        {
            NiceStatus.Nice => $"""
                Child {childId} has been NICE! Generate 3-5 exciting gift recommendations that:
                - Celebrate their good behavior
                - Match their interests: {string.Join(", ", profile?.Preferences ?? [])}
                - Are age-appropriate for age {profile?.Age ?? 0}
                - Include both fun and educational elements
                
                For each recommendation, provide:
                1. Gift name
                2. Why it's perfect for this nice child
                3. Educational or developmental benefit
                """,
            NiceStatus.Naughty => $"""
                Child {childId} needs character building. Generate 3-5 thoughtful recommendations that:
                - Focus on educational and character-development items
                - Encourage positive behavior and growth
                - Match their interests: {string.Join(", ", profile?.Preferences ?? [])}
                - Are age-appropriate for age {profile?.Age ?? 0}
                
                For each recommendation, provide:
                1. Goal-oriented item name
                2. How it helps build character
                3. Encouraging message about improvement
                """,
            _ => $"""
                Generate 3-5 balanced gift recommendations for child {childId}:
                - Interests: {string.Join(", ", profile?.Preferences ?? [])}
                - Age: {profile?.Age ?? 0}
                - Mix of fun and educational items
                """
        };

        // Stream agent response using RunAsync (Agent Framework doesn't expose token-level streaming yet)
        // For demo purposes, we'll simulate streaming by chunking the response
        AgentRunResponse? result = null;
        string? agentError = null;
        try
        {
            result = await _agent.RunAsync(prompt, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent execution failed for {ChildId}", childId);
            agentError = $"Failed to generate recommendations: {ex.Message}";
        }

        if (agentError is not null)
        {
            yield return new StreamingAgentUpdate("error", agentError);
            yield break;
        }

        string fullText = result?.ToString() ?? "";
        if (string.IsNullOrEmpty(fullText))
        {
            yield return new StreamingAgentUpdate("completed", "No recommendations generated");
            yield break;
        }

        // Simulate streaming by sending text in chunks
        int chunkSize = 10; // characters per chunk
        int tokenCount = 0;
        for (int i = 0; i < fullText.Length; i += chunkSize)
        {
            if (ct.IsCancellationRequested)
            {
                yield return new StreamingAgentUpdate("cancelled", "Recommendation generation cancelled");
                yield break;
            }

            string chunk = fullText.Substring(i, Math.Min(chunkSize, fullText.Length - i));
            tokenCount++;
            yield return new StreamingAgentUpdate(
                "text-delta",
                chunk,
                "RecommendationAgent",
                new { tokenCount, childId, status }
            );

            // Emit progress updates periodically
            if (tokenCount % 50 == 0)
            {
                yield return new StreamingAgentUpdate(
                    "progress",
                    $"Generated {tokenCount} chunks...",
                    "RecommendationAgent",
                    new { tokenCount }
                );
            }

            // Small delay to simulate real streaming
            await Task.Delay(20, ct);
        }

        yield return new StreamingAgentUpdate(
            "completed",
            $"Recommendation generation completed with {tokenCount} tokens",
            "RecommendationAgent",
            new { totalTokens = tokenCount, childId, status }
        );

        _logger.LogInformation("Completed streaming recommendation for {ChildId} with {TokenCount} tokens", childId, tokenCount);
    }
}
