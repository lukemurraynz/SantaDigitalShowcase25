using System.Collections.Concurrent;

namespace Services;

public class InMemoryIdempotencyStore
{
    private readonly ConcurrentDictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);
    public bool TryGet(string key, out object value) => _cache.TryGetValue(key, out value!);
    public void Set(string key, object value) => _cache.TryAdd(key, value);
}