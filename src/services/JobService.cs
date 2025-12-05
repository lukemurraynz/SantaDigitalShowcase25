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

        // Create job in queued state
        // PERFORMANCE: Processing now happens asynchronously via direct trigger in JobsApi
        // This allows 202 response to return immediately without blocking
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

        // Note: Actual recommendation generation happens via:
        // 1. Direct trigger (JobsApi.cs Task.Run) - immediate, bypasses Drasi delay
        // 2. Drasi reaction (OrchestratorApi.cs) - triggered by event graph query
        // Both paths are deduplicated by ElfAgentOrchestrator to prevent duplicate work

        return true;
    }
}
