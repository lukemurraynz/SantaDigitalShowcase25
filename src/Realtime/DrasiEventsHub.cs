using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;

namespace Realtime
{
    /// <summary>
    /// SignalR hub for Drasi real-time events.
    /// Implements the Drasi SignalR protocol for compatibility with @drasi/signalr-react library.
    /// Supports both direct client connections and forwarding from Drasi SignalR reaction.
    /// </summary>
    public class DrasiEventsHub : Hub
    {
        private readonly ILogger<DrasiEventsHub> _logger;

        // Maximum number of items to cache per query for reload requests.
        // In production, consider using a distributed cache like Redis.
        private const int MaxCachedItemsPerQuery = 100;

        // Instance ID for uniqueness in multi-instance deployments
        private static readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

        // In-memory store for query results using a circular buffer pattern.
        // Note: This is not distributed and should be replaced with Redis or 
        // IMemoryCache with proper key management in multi-instance deployments.
        // Using ConcurrentDictionary for thread-safety, with Queue<T> for O(1) enqueue/dequeue operations.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Queue<JsonElement>> _queryResults = new();
        
        // Lock object for thread-safe cache modification operations
        private static readonly object _cacheLock = new();
        
        private static int _sequenceNumber = 0;

        public DrasiEventsHub(ILogger<DrasiEventsHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            _logger.LogInformation("[DrasiHub] Client connected: {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("hub.info", new { message = "connected", connectionId = Context.ConnectionId });
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "[DrasiHub] Client disconnected with error: {ConnectionId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("[DrasiHub] Client disconnected: {ConnectionId}", Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Subscribe to a topic (e.g., childId) for receiving stream events.
        /// </summary>
        public async Task Subscribe(string topic)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, topic);
            _logger.LogInformation("[DrasiHub] Client {ConnectionId} subscribed to topic: {Topic}", Context.ConnectionId, topic);
            await Clients.Caller.SendAsync("hub.subscribed", new { topic });
        }

        /// <summary>
        /// Unsubscribe from a topic.
        /// </summary>
        public async Task Unsubscribe(string topic)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, topic);
            _logger.LogInformation("[DrasiHub] Client {ConnectionId} unsubscribed from topic: {Topic}", Context.ConnectionId, topic);
            await Clients.Caller.SendAsync("hub.unsubscribed", new { topic });
        }

        /// <summary>
        /// Broadcast an event to all clients in a topic group.
        /// </summary>
        public async Task Broadcast(string topic, object payload)
        {
            await Clients.Group(topic).SendAsync("event", new { topic, payload });
        }

        /// <summary>
        /// Get the next sequence number with instance ID prefix for multi-instance uniqueness.
        /// </summary>
        private static string GetNextSequenceId()
        {
            var seq = Interlocked.Increment(ref _sequenceNumber);
            return $"{_instanceId}-{seq}";
        }

        /// <summary>
        /// Implements Drasi's reload protocol for the @drasi/signalr-react library.
        /// Returns a stream of reload header followed by current query results as reload items.
        /// </summary>
        public async IAsyncEnumerable<object> Reload(string queryId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _logger.LogInformation("[DrasiHub] Reload requested for query: {QueryId}", queryId);
            
            var seq = GetNextSequenceId();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Emit reload header (op: "h")
            yield return new
            {
                op = "h",
                seq = seq,
                ts_ms = now
            };

            // Get current results from cache (thread-safe read with lock)
            JsonElement[]? results = null;
            lock (_cacheLock)
            {
                if (_queryResults.TryGetValue(queryId, out var cached))
                {
                    results = cached.ToArray();
                }
            }

            if (results != null)
            {
                foreach (var item in results)
                {
                    // Emit reload items (op: "r")
                    yield return new
                    {
                        op = "r",
                        payload = new
                        {
                            after = item,
                            source = new { queryId, ts_ms = now }
                        },
                        ts_ms = now
                    };

                    if (cancellationToken.IsCancellationRequested)
                        yield break;
                }
            }

            _logger.LogInformation("[DrasiHub] Reload completed for query: {QueryId}, items: {Count}", queryId, results?.Length ?? 0);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Process a change notification from Drasi and broadcast to subscribed clients.
        /// Called by backend services when receiving events from Drasi reaction or change feed.
        /// </summary>
        public static void ProcessDrasiEvent(string queryId, string operation, JsonElement data, IHubContext<DrasiEventsHub> hubContext, ILogger logger)
        {
            var seq = GetNextSequenceId();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Store in cache for reload - use global lock to prevent race conditions
            // between GetOrAdd and queue modification
            if (operation == "i" || operation == "u")
            {
                lock (_cacheLock)
                {
                    var queue = _queryResults.GetOrAdd(queryId, _ => new Queue<JsonElement>());
                    queue.Enqueue(data);
                    // Keep only the most recent items per query to limit memory usage (O(1) removal)
                    while (queue.Count > MaxCachedItemsPerQuery)
                    {
                        queue.Dequeue();
                    }
                }
            }

            // Build Drasi change notification
            var notification = new
            {
                op = operation,
                seq = seq,
                ts_ms = now,
                payload = new
                {
                    after = data,
                    source = new { queryId, ts_ms = now }
                }
            };

            // Broadcast to all connected clients subscribed to this query
            // Using fire-and-forget with explicit discard and TaskScheduler.Default
            _ = hubContext.Clients.All.SendAsync(queryId, notification)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        logger.LogWarning(t.Exception, "[DrasiHub] Failed to broadcast event for query: {QueryId}", queryId);
                    }
                    else
                    {
                        logger.LogDebug("[DrasiHub] Broadcast event for query: {QueryId}, op: {Op}", queryId, operation);
                    }
                }, TaskScheduler.Default);
        }

        /// <summary>
        /// Clear the cache for a specific query ID. Used before re-seeding with fresh data.
        /// </summary>
        public static void ClearQueryCache(string queryId, ILogger logger)
        {
            lock (_cacheLock)
            {
                if (_queryResults.TryRemove(queryId, out _))
                {
                    logger.LogDebug("[DrasiHub] Cleared cache for query: {QueryId}", queryId);
                }
                else
                {
                    logger.LogDebug("[DrasiHub] No cache to clear for query: {QueryId}", queryId);
                }
            }
        }

        /// <summary>
        /// Ping endpoint for connection health checks.
        /// </summary>
        public Task Ping() => Clients.Caller.SendAsync("hub.pong", new { time = DateTime.UtcNow });
    }
}
