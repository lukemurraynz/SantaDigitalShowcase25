using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Identity;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IEventPublisher
{
    Task PublishWishlistAsync(string childId, string dedupeKey, string schemaVersion, JsonNode? wishlist, CancellationToken ct = default);
    Task PublishRecommendationAsync(string childId, string schemaVersion, JsonNode recommendationSet, CancellationToken ct = default);
}

public class EventHubPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly ILogger<EventHubPublisher> _logger;
    private EventHubProducerClient? _producer;
    private readonly string? _eventHubName;
    private readonly string? _fullyQualifiedNamespace; // e.g. namespace.servicebus.windows.net
    private readonly IConfiguration _config;

    public EventHubPublisher(ILogger<EventHubPublisher> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _eventHubName = Environment.GetEnvironmentVariable("EVENTHUB_NAME") ?? _config["EventHubs:HubName"];
        // Support managed identity: allow FQDN via env EVENTHUB_FQDN or config EventHubs:FullyQualifiedNamespace/Namespace
        _fullyQualifiedNamespace = Environment.GetEnvironmentVariable("EVENTHUB_FQDN")
            ?? _config["EventHubs:FullyQualifiedNamespace"]
            ?? _config["EventHubs:Namespace"];
    }

    public async Task PublishWishlistAsync(string childId, string dedupeKey, string schemaVersion, JsonNode? wishlist, CancellationToken ct = default)
    {
        _logger.LogInformation("📤 Publishing wishlist event for child {ChildId}", childId);
        EnsureProducer(ct);
        if (_producer is null)
        {
            _logger.LogWarning("⚠️ EventHub producer is null, cannot publish for child {ChildId}", childId);
            return;
        }

        // Flatten the payload structure for Drasi EventHub source
        // Drasi creates graph nodes from top-level properties, so we need to flatten nested wishlist data
        var payload = new
        {
            childId,
            dedupeKey,
            schemaVersion,
            occurredAt = DateTime.UtcNow,
            type = "wishlist-update",
            // Flatten wishlist properties to top level
            id = wishlist?["id"]?.ToString(),
            text = wishlist?["text"]?.ToString(),
            category = wishlist?["category"]?.ToString(),
            budgetEstimate = wishlist?["budgetEstimate"]?.GetValue<double?>(), // JSON numbers are double
            createdAt = wishlist?["createdAt"]?.ToString(),
            // Include RequestType and StatusChange for behavior updates
            RequestType = wishlist?["RequestType"]?.ToString() ?? wishlist?["requestType"]?.ToString() ?? "gift",
            StatusChange = wishlist?["StatusChange"]?.ToString() ?? wishlist?["statusChange"]?.ToString()
        };
        var json = JsonSerializer.Serialize(payload);
        _logger.LogInformation("📋 EventHub payload: {Json}", json);
        using var batch = await _producer.CreateBatchAsync(ct);
        if (!batch.TryAdd(new EventData(Encoding.UTF8.GetBytes(json))))
        {
            // Fallback: send single event
            _logger.LogInformation("📨 Sending single wishlist event for child {ChildId}", childId);
            await _producer.SendAsync(new[] { new EventData(Encoding.UTF8.GetBytes(json)) }, ct);
            return;
        }
        _logger.LogInformation("📦 Sending batched wishlist event for child {ChildId}", childId);
        await _producer.SendAsync(batch, ct);
    }

    public async Task PublishRecommendationAsync(string childId, string schemaVersion, JsonNode recommendationSet, CancellationToken ct = default)
    {
        EnsureProducer(ct);
        if (_producer is null)
            return; // still unavailable
        var payload = new
        {
            childId,
            schemaVersion,
            occurredAt = DateTime.UtcNow,
            recommendationSet,
            type = "recommendation-update"
        };
        var json = JsonSerializer.Serialize(payload);
        using var batch = await _producer.CreateBatchAsync(ct);
        if (!batch.TryAdd(new EventData(Encoding.UTF8.GetBytes(json))))
        {
            await _producer.SendAsync(new[] { new EventData(Encoding.UTF8.GetBytes(json)) }, ct);
            return;
        }
        await _producer.SendAsync(batch, ct);
    }

    void EnsureProducer(CancellationToken ct)
    {
        if (_producer is not null)
            return;
        // Prefer managed identity when we have namespace + hub
        if (_producer is null && !string.IsNullOrWhiteSpace(_fullyQualifiedNamespace) && !string.IsNullOrWhiteSpace(_eventHubName))
        {
            try
            {
                _producer = new EventHubProducerClient(_fullyQualifiedNamespace, _eventHubName, new DefaultAzureCredential());
                return; // success with MI
            }
            catch
            {
                // fall back to connection string path
            }
        }

        var conn = Environment.GetEnvironmentVariable("EVENTHUBS_CONNECTION_STRING")
            ?? _config["EventHubs:ConnectionString"];
        if (string.IsNullOrWhiteSpace(conn))
            return; // no credential available (no MI, no connection string)
        _producer = string.IsNullOrWhiteSpace(_eventHubName)
            ? new EventHubProducerClient(conn)
            : new EventHubProducerClient(conn, _eventHubName!);
    }

    public async ValueTask DisposeAsync()
    {
        if (_producer is not null)
        {
            await _producer.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
