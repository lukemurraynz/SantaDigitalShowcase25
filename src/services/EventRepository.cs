using Models;

namespace Services;

public interface IEventRepository
{
    Task AddAsync(WorkshopEvent evt, CancellationToken ct = default);
    Task<WorkshopEvent?> GetByDedupeKeyAsync(string childId, string dedupeKey, CancellationToken ct = default);
}

public class EventRepository : IEventRepository
{
    private static readonly InMemoryRepository<string, WorkshopEvent> Store = new();

    public Task AddAsync(WorkshopEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var key = Key(evt.ChildId, evt.DedupeKey);
        return Store.UpsertAsync(key, evt, ct);
    }

    public Task<WorkshopEvent?> GetByDedupeKeyAsync(string childId, string dedupeKey, CancellationToken ct = default)
    {
        var key = Key(childId, dedupeKey);
        return Store.GetAsync(key, ct);
    }

    private static string Key(string childId, string dedupeKey) => $"{childId}:{dedupeKey}";
}
