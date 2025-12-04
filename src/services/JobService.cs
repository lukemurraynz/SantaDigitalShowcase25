using Models;

namespace Services;

public interface IJobService
{
    Task<bool> EnsureJobAsync(string childId, string dedupeKey, string schemaVersion, CancellationToken ct = default);
}

public class JobService : IJobService
{
    private readonly IEventRepository _events;
    private readonly IJobRepository _jobs;
    private readonly IRecommendationService _recs;
    private readonly IAgentRationaleService _agent;
    private readonly IReportGenerator _generator;
    private readonly IReportRepository _reports;

    public JobService(IEventRepository events, IJobRepository jobs,
        IRecommendationService recs, IAgentRationaleService agent, IReportGenerator generator, IReportRepository reports)
    {
        _events = events;
        _jobs = jobs;
        _recs = recs;
        _agent = agent;
        _generator = generator;
        _reports = reports;
    }

    public async Task<bool> EnsureJobAsync(string childId, string dedupeKey, string schemaVersion, CancellationToken ct = default)
    {
        // Use dedupeKey as the Job.Id for idempotency
        var existing = await _jobs.GetByIdAsync(childId, dedupeKey, ct);
        if (existing is not null)
        {
            return false; // already exists
        }

        // Persist event record
        var evt = new WorkshopEvent(
            Id: Guid.NewGuid().ToString(),
            ChildId: childId,
            Type: "update",
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid().ToString(),
            SchemaVersion: schemaVersion,
            DedupeKey: dedupeKey
        );
        await _events.AddAsync(evt, ct);

        // Create job
        var job = new Job(
            Id: dedupeKey,
            ChildId: childId,
            Status: "queued",
            Attempts: 0,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Error: null
        );
        await _jobs.UpsertAsync(job, ct);

        // Process pipeline (synchronously for demo)
        try
        {
            var top = await _recs.GetTopNAsync(childId, topN: 3, ct);
            var withRationale = new List<Recommendation>();
            foreach (var r in top)
            {
                withRationale.Add(await _agent.AddRationaleAsync(r, ct));
            }

            var summary = await _agent.GetLabelExplanationAsync(childId, ct);
            var label = "Nice"; // demo default
            var path = await _generator.GenerateMarkdownAsync(childId, withRationale, label, summary, ct);

            var report = new Report(
                Id: Guid.NewGuid().ToString(),
                ChildId: childId,
                CreatedAt: DateTime.UtcNow,
                Recommendations: withRationale,
                Summary: summary,
                Label: label,
                Disclaimer: "Demo-only; synthetic data.",
                Format: "markdown",
                Path: path.Replace(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..")) + Path.DirectorySeparatorChar, string.Empty)
            );
            await _reports.UpsertAsync(report, ct);

            var succeeded = job with { Status = "succeeded", UpdatedAt = DateTime.UtcNow };
            await _jobs.UpsertAsync(succeeded, ct);
        }
        catch (Exception ex)
        {
            var failed = job with { Status = "failed", UpdatedAt = DateTime.UtcNow, Error = ex.Message };
            await _jobs.UpsertAsync(failed, ct);
        }

        return true;
    }
}
