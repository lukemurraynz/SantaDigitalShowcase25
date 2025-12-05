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
    private readonly IChildProfileService _profileService;
    private readonly ILogger<AgentRationaleService> _logger;

    public AgentRationaleService(AIAgent agent, IChildProfileService profileService, ILogger<AgentRationaleService> logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Recommendation> AddRationaleAsync(Recommendation rec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rec);

        // Fetch profile to include in rationale prompt for context
        var profile = await _profileService.GetChildProfileAsync(rec.ChildId, ct);
        string preferences = profile?.Preferences is { Length: > 0 }
            ? string.Join(", ", profile.Preferences)
            : "not specified";
        string age = profile?.Age?.ToString() ?? "not specified";
        string name = profile?.Name ?? "not specified";

        string prompt = $"""
ChildId: {rec.ChildId}
Child Name: {name}
Age: {age}
Stated Preferences/Wishlist: {preferences}
Suggestion: {rec.Suggestion}

You are the Elf Recommendation Agent. Produce a short, enthusiastic rationale (1-2 sentences) explaining why this suggestion is a PERFECT match for this child!

If the suggestion matches something from their wishlist/preferences, mention that directly (e.g., "You asked for an Xbox, and this Xbox Series X is exactly what you wanted!").
If it's a related accessory or complementary item, explain the connection (e.g., "Since you love gaming, this Xbox Game Pass will give you access to hundreds of games!").
Keep it fun, festive, and personalized. This is Santa's Workshop!
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
                text = GetFallback(rec.ChildId, rec.Suggestion, preferences);
            }
            return rec with { Rationale = text };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested == false)
        {
            // Timeout occurred (not user cancellation) - use fallback
            _logger.LogWarning("Rationale generation timed out for {ChildId}, using fallback", rec.ChildId);
            return rec with { Rationale = GetFallback(rec.ChildId, rec.Suggestion, preferences) };
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
            return rec with { Rationale = GetFallback(rec.ChildId, rec.Suggestion, preferences) };
        }
    }

    public Task<string> GetLabelExplanationAsync(string childId, CancellationToken ct = default)
    {
        // Agent-based behavior assessment - for now return static text
        // In production, this would query the agent with child behavior data
        return Task.FromResult("Behavior assessed from events: status being monitored. Keep up the festive spirit!");
    }

    private static string GetFallback(string childId, string suggestion, string preferences)
    {
        if (preferences != "not specified" && preferences.Length > 0)
        {
            return $"🎁 '{suggestion}' is a great choice based on your interests in {preferences}! Santa's elves picked this just for you.";
        }
        return $"🎁 Santa's elves recommend '{suggestion}' - it's perfect for your wishlist!";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rationale generation failed; using fallback")]
    private static partial void Log_RationaleGenerationFailed(ILogger logger, Exception ex);
}
