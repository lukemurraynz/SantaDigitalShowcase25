using Microsoft.Extensions.Logging;
using Models;

namespace Services;

public interface IElfRecommendationService
{
    Task<IReadOnlyList<Recommendation>> GetRecommendationsForChildAsync(string childId, CancellationToken ct = default);
}

public class ElfRecommendationService : IElfRecommendationService
{
    private readonly IRecommendationService _recommendations;
    private readonly IAgentRationaleService _rationale;
    private readonly ILogger<ElfRecommendationService> _logger;

    public ElfRecommendationService(
        IRecommendationService recommendations,
        IAgentRationaleService rationale,
        ILogger<ElfRecommendationService> logger)
    {
        _recommendations = recommendations;
        _rationale = rationale;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Recommendation>> GetRecommendationsForChildAsync(string childId, CancellationToken ct = default)
    {
        try
        {
            var baseRecs = await _recommendations.GetTopNAsync(childId, topN: 4, ct);
            if (baseRecs.Count == 0)
            {
                return Array.Empty<Recommendation>();
            }

            var withRationale = new List<Recommendation>(baseRecs.Count);
            // Run rationale enrichment in parallel to avoid sequential timeouts stacking
            var enrichmentTasks = baseRecs.Select(rec => _rationale.AddRationaleAsync(rec, ct));
            var enrichedResults = await Task.WhenAll(enrichmentTasks);
            withRationale.AddRange(enrichedResults);
            return withRationale;
        }
        catch (Exception ex)
        {
            // If underlying data access (e.g., Cosmos) fails, degrade gracefully.
            _logger.LogError(ex, "Failed to get recommendations for child {ChildId}. Returning empty set.", childId);
            return Array.Empty<Recommendation>();
        }
    }
}
