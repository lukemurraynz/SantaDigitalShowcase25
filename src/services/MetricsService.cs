using System.Collections.Concurrent;

namespace Services;

public interface IMetricsService
{
    void ObserveLatency(string name, TimeSpan duration);
    void Increment(string name);
    MetricsSnapshot Snapshot();
}

public sealed class MetricsSnapshot
{
    public required Dictionary<string,double> LatenciesMs { get; init; }
    public required Dictionary<string,int> Counters { get; init; }
}

public sealed class InMemoryMetricsService : IMetricsService
{
    readonly ConcurrentDictionary<string,List<double>> _latencies = new();
    readonly ConcurrentDictionary<string,int> _counters = new();

    public void ObserveLatency(string name, TimeSpan duration)
    {
        var list = _latencies.GetOrAdd(name, _ => new());
        lock (list) { list.Add(duration.TotalMilliseconds); }
    }
    public void Increment(string name) => _counters.AddOrUpdate(name, 1, (_, v) => v + 1);
    public MetricsSnapshot Snapshot()
    {
        var latencyAgg = _latencies.ToDictionary(k => k.Key, v => {
            lock (v.Value) { return v.Value.Count == 0 ? 0 : v.Value.Average(); }
        });
        return new MetricsSnapshot { LatenciesMs = latencyAgg, Counters = _counters.ToDictionary(k => k.Key, v => v.Value) };
    }
}
