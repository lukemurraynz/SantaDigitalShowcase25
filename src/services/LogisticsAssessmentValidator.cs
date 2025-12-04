using System.Collections.Generic;

namespace Services;

public interface ILogisticsAssessmentValidator
{
    bool Validate(IReadOnlyList<AssessmentWorkItem> items);
}

public sealed class LogisticsAssessmentValidator : ILogisticsAssessmentValidator
{
    public bool Validate(IReadOnlyList<AssessmentWorkItem> items)
    {
        // Ensure any explicitly infeasible item has a non-empty reason.
        return items.All(i => i.Feasible != false || !string.IsNullOrWhiteSpace(i.Reason));
    }
}