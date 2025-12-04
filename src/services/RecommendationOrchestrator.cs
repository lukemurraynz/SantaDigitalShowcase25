using Models;
using Microsoft.Extensions.Logging;

namespace Services;

public interface IRecommendationOrchestrator
{
    Task<IReadOnlyList<Recommendation>> GenerateAsync(string childId, int topN, CancellationToken ct = default);
}

/// <summary>
/// Orchestrates recommendation generation and rationale enrichment.
///
/// This class is marked as <c>partial</c> to support source generation of high-performance logging methods
/// via <see cref="LoggerMessageAttribute"/>. The <c>LoggerMessage</c> source generator creates implementations
/// for the partial logging methods defined in this class, enabling efficient structured logging.
/// </summary>
public sealed partial class RecommendationOrchestrator : IRecommendationOrchestrator
{
    private readonly IRecommendationService _recs;
    private readonly IAgentRationaleService _rationale;
    private readonly ILogger<RecommendationOrchestrator> _logger;

    public RecommendationOrchestrator(IRecommendationService recs, IAgentRationaleService rationale, ILogger<RecommendationOrchestrator> logger)
    {
        _recs = recs;
        _rationale = rationale;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Recommendation>> GenerateAsync(string childId, int topN, CancellationToken ct = default)
    {
        IReadOnlyList<Recommendation> items;
        try
        {
            items = await _recs.GetTopNAsync(childId, topN, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log_RecommendationFetchFailed(_logger, ex, childId);
            return Array.Empty<Recommendation>();
        }

        var list = new List<Recommendation>(items.Count);
        foreach (var r in items)
        {
            try
            {
                var withRationale = await _rationale.AddRationaleAsync(r, ct);
                list.Add(withRationale);
            }
            catch (OperationCanceledException)
            {
                // On cancellation, return what we have so far
                return list;
            }
            catch (Exception ex)
            {
                // If rationale generation fails for one item, add the item without rationale
                Log_RationaleEnrichmentFailed(_logger, ex, childId, r.Id);
                list.Add(r with { Rationale = $"Recommended '{r.Suggestion}' for child '{childId}' based on their profile and letter to the North Pole." });
            }
        }
        return list;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch recommendations for child {ChildId}")]
    private static partial void Log_RecommendationFetchFailed(ILogger logger, Exception ex, string childId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to enrich rationale for child {ChildId}, recommendation {RecommendationId}")]
    private static partial void Log_RationaleEnrichmentFailed(ILogger logger, Exception ex, string childId, string recommendationId);
}
