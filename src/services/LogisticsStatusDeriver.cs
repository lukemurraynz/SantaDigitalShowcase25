using System.Collections.Generic;

namespace Services;

public interface ILogisticsStatusDeriver
{
    string Derive(IReadOnlyList<AssessmentWorkItem> items);
}

public sealed class LogisticsStatusDeriver : ILogisticsStatusDeriver
{
    public string Derive(IReadOnlyList<AssessmentWorkItem> items)
    {
        ArgumentNullException.ThrowIfNull(items); // CA1062/CA1510
        if (items.Count == 0) return "unknown";
        bool allFeasible = items.All(i => i.Feasible == true);
        bool allInfeasible = items.All(i => i.Feasible == false);
        if (allFeasible) return "feasible";
        if (allInfeasible) return "infeasible";
        return "partial";
    }
}

public record AssessmentWorkItem(string RecommendationId, bool? Feasible, string Reason);