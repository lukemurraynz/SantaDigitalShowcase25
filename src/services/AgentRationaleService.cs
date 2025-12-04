using Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Services;

public interface IAgentRationaleService
{
    Task<Recommendation> AddRationaleAsync(Recommendation rec, CancellationToken ct = default);
    Task<string> GetLabelExplanationAsync(string childId, CancellationToken ct = default);
}

// Agent rationale service that delegates to the Elf agent orchestrator.
public partial class AgentRationaleService : IAgentRationaleService
{
    private readonly AIAgent _agent;
    private readonly ILogger<AgentRationaleService> _logger;

    public AgentRationaleService(AIAgent agent, ILogger<AgentRationaleService> logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Recommendation> AddRationaleAsync(Recommendation rec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rec);
        string prompt = $"""
ChildId: {rec.ChildId}
Suggestion: {rec.Suggestion}

You are the Elf Recommendation Agent. Produce a short, honest rationale (1-3 sentences) explaining why this suggestion is a good match for the child, referencing age, interests, and constraints when known. Do not invent personal details that were not provided.
""";
        try
        {
            // Check for cancellation first to avoid unnecessary work
            ct.ThrowIfCancellationRequested();

            // Add timeout protection for Azure OpenAI calls (3 second timeout for rationale)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var run = await _agent.RunAsync(prompt, cancellationToken: linkedCts.Token).ConfigureAwait(false);
            var text = run?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = GetFallback(rec.ChildId, rec.Suggestion);
            }
            return rec with { Rationale = text };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested == false)
        {
            // Timeout occurred (not user cancellation) - use fallback
            _logger.LogWarning("Rationale generation timed out for {ChildId}, using fallback", rec.ChildId);
            return rec with { Rationale = GetFallback(rec.ChildId, rec.Suggestion) };
        }
        catch (OperationCanceledException)
        {
            // User cancellation - propagate
            throw;
        }
        catch (Exception ex)
        {
            // Catch ALL other exceptions and use fallback to ensure the API doesn't fail
            Log_RationaleGenerationFailed(_logger, ex);
            return rec with { Rationale = GetFallback(rec.ChildId, rec.Suggestion) };
        }
    }

    public Task<string> GetLabelExplanationAsync(string childId, CancellationToken ct = default)
    {
        // Agent-based behavior assessment - for now return static text
        // In production, this would query the agent with child behavior data
        return Task.FromResult("Behavior assessed from events: status being monitored. Keep up the festive spirit!");
    }

    private static string GetFallback(string childId, string suggestion) => $"Recommended '{suggestion}' for child '{childId}' based on their profile and letter to the North Pole.";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rationale generation failed; using fallback")]
    private static partial void Log_RationaleGenerationFailed(ILogger logger, Exception ex);
}
