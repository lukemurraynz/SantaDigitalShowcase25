using System.Collections.Concurrent;

namespace Services;

public interface IStreamMetrics
{
    void IncrementConnection(string streamKey);
    void IncrementEvent(string streamKey, string eventType);
    StreamMetricsSnapshot Snapshot();
}

public sealed class StreamMetricsSnapshot
{
    public required Dictionary<string,int> ActiveConnections { get; init; }
    public required Dictionary<string,int> EventCounts { get; init; }
}

public sealed class InMemoryStreamMetrics : IStreamMetrics
{
    readonly ConcurrentDictionary<string,int> _connections = new();
    readonly ConcurrentDictionary<string,int> _eventCounts = new();

    public void IncrementConnection(string streamKey)
    {
        _connections.AddOrUpdate(streamKey, 1, (_, v) => v + 1);
    }
    public void IncrementEvent(string streamKey, string eventType)
    {
        string key = streamKey + ":" + eventType;
        _eventCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
    }
    public StreamMetricsSnapshot Snapshot() => new()
    {
        ActiveConnections = _connections.ToDictionary(k => k.Key, v => v.Value),
        EventCounts = _eventCounts.ToDictionary(k => k.Key, v => v.Value)
    };
}
