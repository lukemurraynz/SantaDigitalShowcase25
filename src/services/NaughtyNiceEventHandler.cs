using Models;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Handles naughty/nice status change events from Drasi and updates recommendations using Agent Framework
/// </summary>
public interface INaughtyNiceEventHandler
{
    Task HandleStatusChangeAsync(string childId, NiceStatus newStatus, string behaviorDescription, CancellationToken ct = default);
}

public class NaughtyNiceEventHandler : INaughtyNiceEventHandler
{
    private readonly IChildProfileService _profileService;
    private readonly IRecommendationService _recommendationService;
    private readonly IAgentRationaleService _rationaleService;
    private readonly ILogger<NaughtyNiceEventHandler> _logger;

    public NaughtyNiceEventHandler(
        IChildProfileService profileService,
        IRecommendationService recommendationService,
        IAgentRationaleService rationaleService,
        ILogger<NaughtyNiceEventHandler> logger)
    {
        _profileService = profileService;
        _recommendationService = recommendationService;
        _rationaleService = rationaleService;
        _logger = logger;
    }

    public async Task HandleStatusChangeAsync(string childId, NiceStatus newStatus, string behaviorDescription, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing naughty/nice status change for child {ChildId}: {NewStatus} - {Description}", 
            childId, newStatus, behaviorDescription);

        try
        {
            // Update the child's profile status
            await _profileService.UpdateStatusAsync(childId, newStatus, ct);
            _logger.LogInformation("Updated profile status for child {ChildId} to {Status}", childId, newStatus);

            // Get current recommendations
            var currentRecs = await _recommendationService.GetTopNAsync(childId, topN: 10, ct);
            
            if (currentRecs.Count == 0)
            {
                _logger.LogInformation("No existing recommendations found for child {ChildId}", childId);
                return;
            }

            // Update recommendations based on new status
            // If child becomes Naughty: adjust recommendations toward "Goal" items (educational, character-building)
            // If child becomes Nice: keep current recommendations or enhance them
            foreach (var rec in currentRecs)
            {
                var updatedRec = newStatus switch
                {
                    NiceStatus.Naughty => rec with 
                    { 
                        Rationale = $"Given recent behavior, recommend character-building alternative: {rec.Suggestion}. {await _rationaleService.GetLabelExplanationAsync(childId, ct)}" 
                    },
                    NiceStatus.Nice => rec with 
                    { 
                        Rationale = $"Great behavior deserves great rewards: {rec.Suggestion}. {await _rationaleService.GetLabelExplanationAsync(childId, ct)}" 
                    },
                    _ => rec
                };

                // In production, would persist updated recommendation to Cosmos
                _logger.LogInformation("Updated recommendation {RecId} for child {ChildId} based on {Status} status", 
                    rec.Id, childId, newStatus);
            }

            _logger.LogInformation("Successfully processed naughty/nice status change for child {ChildId}", childId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle status change for child {ChildId}", childId);
            throw;
        }
    }
}
