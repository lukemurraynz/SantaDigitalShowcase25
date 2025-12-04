namespace Drasicrhsit.Infrastructure;

public class Metrics
{
    private long _eventsProcessed;
    private long _errors;
    private readonly object _lock = new();

    public void IncEventsProcessed() { lock (_lock) { _eventsProcessed++; } }
    public void IncErrors() { lock (_lock) { _errors++; } }
    public (long eventsProcessed, long errors) Snapshot()
    {
        lock (_lock) { return (_eventsProcessed, _errors); }
    }
}
