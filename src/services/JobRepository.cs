using Models;

namespace Services;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(string childId, string id, CancellationToken ct = default);
    Task<Job> UpsertAsync(Job job, CancellationToken ct = default);
}

public class JobRepository : IJobRepository
{
    // In-memory store fallback keyed by childId:id
    private static readonly InMemoryRepository<string, Job> Store = new();

    public Task<Job?> GetByIdAsync(string childId, string id, CancellationToken ct = default)
    {
        return Store.GetAsync(Key(childId, id), ct);
    }

    public Task<Job> UpsertAsync(Job job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        Store.UpsertAsync(Key(job.ChildId, job.Id), job, ct);
        return Task.FromResult(job);
    }

    private static string Key(string childId, string id) => $"{childId}:{id}";
}
