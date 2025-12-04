using System.Text.Json;
using System.Threading.Channels;
// Required for IClientProxy SendAsync extension methods
using Microsoft.AspNetCore.SignalR;

namespace Services;

public sealed class SseEvent
{
    public required string Type { get; init; }
    public required string Json { get; init; }
}

public interface IStreamBroadcaster
{
    ChannelReader<SseEvent> Subscribe(string childId);
    Task PublishAsync(string childId, string eventType, object payload, CancellationToken ct = default);
    Task PublishDrasiEventAsync(string queryId, string operation, object payload, CancellationToken ct = default);
}

public sealed class InMemoryStreamBroadcaster : IStreamBroadcaster
{
    readonly Dictionary<string, Channel<SseEvent>> _channels = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    public ChannelReader<SseEvent> Subscribe(string childId)
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(childId, out var ch))
            {
                ch = Channel.CreateUnbounded<SseEvent>();
                _channels[childId] = ch;
            }
            return ch.Reader;
        }
    }

    public Task PublishAsync(string childId, string eventType, object payload, CancellationToken ct = default)
    {
        Channel<SseEvent> channel;
        lock (_lock)
        {
            if (!_channels.TryGetValue(childId, out channel!))
            {
                channel = Channel.CreateUnbounded<SseEvent>();
                _channels[childId] = channel;
            }
        }
        string json = JsonSerializer.Serialize(payload);
        channel.Writer.TryWrite(new SseEvent { Type = eventType, Json = json });
        return Task.CompletedTask;
    }

    public Task PublishDrasiEventAsync(string queryId, string operation, object payload, CancellationToken ct = default)
    {
        // InMemory broadcaster doesn't handle Drasi events directly
        return Task.CompletedTask;
    }
}

// Decorator that also pushes events to SignalR hub groups named by childId
public sealed class HubStreamBroadcaster : IStreamBroadcaster
{
    private readonly InMemoryStreamBroadcaster _inner;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Realtime.DrasiEventsHub> _hub;
    private readonly ILogger<HubStreamBroadcaster> _logger;

    public HubStreamBroadcaster(InMemoryStreamBroadcaster inner,
        Microsoft.AspNetCore.SignalR.IHubContext<Realtime.DrasiEventsHub> hub,
        ILogger<HubStreamBroadcaster> logger)
    {
        _inner = inner;
        _hub = hub;
        _logger = logger;
    }

    public ChannelReader<SseEvent> Subscribe(string childId) => _inner.Subscribe(childId);

    public async Task PublishAsync(string childId, string eventType, object payload, CancellationToken ct = default)
    {
        await _inner.PublishAsync(childId, eventType, payload, ct);
        try
        {
            // Clients must have called hub method Subscribe(childId) to join this group
            await _hub.Clients.Group(childId).SendAsync("stream", new
            {
                type = eventType,
                childId,
                payload
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR broadcast failed for childId={ChildId} eventType={EventType}", childId, eventType);
        }
    }

    /// <summary>
    /// Publish a Drasi event to all connected SignalR clients.
    /// This bridges internal events to the Drasi SignalR protocol expected by @drasi/signalr-react.
    /// Note: ProcessDrasiEvent uses fire-and-forget semantics for broadcasting.
    /// </summary>
    public Task PublishDrasiEventAsync(string queryId, string operation, object payload, CancellationToken ct = default)
    {
        try
        {
            var jsonPayload = JsonSerializer.SerializeToElement(payload);
            // ProcessDrasiEvent is intentionally fire-and-forget for performance
            Realtime.DrasiEventsHub.ProcessDrasiEvent(queryId, operation, jsonPayload, _hub, _logger);
            _logger.LogDebug("[Broadcaster] Published Drasi event for query: {QueryId}, op: {Op}", queryId, operation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish Drasi event for query={QueryId}", queryId);
        }
        return Task.CompletedTask;
    }
}
