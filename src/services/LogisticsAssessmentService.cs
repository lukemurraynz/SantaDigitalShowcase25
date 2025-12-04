using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Models;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface ILogisticsAssessmentService
{
    Task<LogisticsAssessmentResult?> RunAssessmentAsync(string childId, CancellationToken ct = default);
}

public record LogisticsAssessmentItem(string RecommendationId, bool? Feasible, string Reason);

public record LogisticsAssessmentResult(
    string Id,
    string ChildId,
    string RecommendationSetId,
    string OverallStatus,
    DateTime CheckedAt,
    IReadOnlyList<LogisticsAssessmentItem> Items,
    bool FallbackUsed
);

public class LogisticsAssessmentService : ILogisticsAssessmentService
{
    private readonly IElfRecommendationService _recommendations;
    private readonly IAvailabilityService _availability;
    private readonly IRecommendationRepository _recRepo;
    private readonly ILogisticsStatusDeriver _statusDeriver;
    private readonly IAiLogisticsAgent _ai;
    private readonly ILogisticsAssessmentValidator _validator;
    private readonly IFallbackUtils _fallback;

    public LogisticsAssessmentService(
        IElfRecommendationService recommendations,
        IAvailabilityService availability,
        IRecommendationRepository recRepo,
        ILogisticsStatusDeriver statusDeriver,
        IAiLogisticsAgent ai,
        ILogisticsAssessmentValidator validator,
        IFallbackUtils fallback)
    {
        _recommendations = recommendations;
        _availability = availability;
        _recRepo = recRepo;
        _statusDeriver = statusDeriver;
        _ai = ai;
        _validator = validator;
        _fallback = fallback;
    }

    public async Task<LogisticsAssessmentResult?> RunAssessmentAsync(string childId, CancellationToken ct = default)
    {
        var recs = await _recommendations.GetRecommendationsForChildAsync(childId, ct);
        if (recs.Count == 0) return null;

        var work = new List<AssessmentWorkItem>();
        bool anyFallback = false;
        foreach (var rec in recs)
        {
            var availability = await _availability.GetAvailabilityAsync(rec.Suggestion, ct);
            bool? feasible = availability?.InStock;
            var (reason, fb) = await _ai.ExplainAsync(rec.Suggestion, ct);
            if (fb) anyFallback = true;
            work.Add(new AssessmentWorkItem(rec.Id, feasible, reason));
        }

        string status = _statusDeriver.Derive(work);
        if (!_validator.Validate(work))
        {
            // Force fallback status & reasons if validation fails
            work = work.Select(w => w with { Reason = _fallback.Rationale("invalid-assessment") }).ToList();
            status = "unknown";
            anyFallback = true;
        }

        // Attempt to get latest recommendation set id (first list item)
        string recommendationSetId = string.Empty;
        await foreach (var set in _recRepo.ListAsync(childId, take: 1).WithCancellation(ct))
        {
            recommendationSetId = set.id;
        }

        var resultItems = work.Select(w => new LogisticsAssessmentItem(w.RecommendationId, w.Feasible, w.Reason)).ToList();
        return new LogisticsAssessmentResult(
            Id: Guid.NewGuid().ToString(),
            ChildId: childId,
            RecommendationSetId: recommendationSetId,
            OverallStatus: status,
            CheckedAt: DateTime.UtcNow,
            Items: resultItems,
            FallbackUsed: anyFallback
        );
    }
}
