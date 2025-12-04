using System.Collections.Concurrent;

namespace Services;

/// <summary>
/// Simple generic in-memory repository used for demo scenarios only.
/// Backed by a static ConcurrentDictionary so data is shared per process.
/// </summary>
public class InMemoryRepository<TKey, TValue>
    where TKey : notnull
{
    private static readonly ConcurrentDictionary<TKey, TValue> Store = new();

    public Task<TValue?> GetAsync(TKey key, CancellationToken ct = default)
    {
        Store.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task UpsertAsync(TKey key, TValue value, CancellationToken ct = default)
    {
        Store[key] = value;
        return Task.CompletedTask;
    }
}
