using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;

namespace Services;

public sealed partial class CosmosWishlistChangeFeedPublisher : IHostedService
{
    // Query ID constant for wishlist updates - must match the Drasi continuous query name
    private const string WishlistUpdatesQueryId = "wishlist-updates";

    private readonly ILogger<CosmosWishlistChangeFeedPublisher> _logger;
    private readonly IConfiguration _config;
    private readonly ICosmosRepository _cosmos;
    private readonly IEventPublisher _publisher;
    private readonly IHubContext<Realtime.DrasiEventsHub> _hubContext;
    private ChangeFeedProcessor? _processor;

    public CosmosWishlistChangeFeedPublisher(
        ILogger<CosmosWishlistChangeFeedPublisher> logger,
        IConfiguration config,
        ICosmosRepository cosmos,
        IEventPublisher publisher,
        IHubContext<Realtime.DrasiEventsHub> hubContext)
    {
        Console.WriteLine("🏗️ CosmosWishlistChangeFeedPublisher constructor called");
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cosmos);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(hubContext);
        _logger = logger;
        _config = config;
        _cosmos = cosmos;
        _publisher = publisher;
        _hubContext = hubContext;
        Console.WriteLine("✅ CosmosWishlistChangeFeedPublisher constructor completed");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("🚀 CosmosWishlistChangeFeedPublisher StartAsync called");
        string wish = _config["Cosmos:Containers:Wishlists"] ?? "wishlists";
        string leases = _config["Cosmos:Containers:Leases"] ?? "leases";
        Console.WriteLine($"📦 Using containers: wishlists={wish}, leases={leases}");

        Container monitored = _cosmos.GetContainer(wish);
        Container lease = _cosmos.GetContainer(leases);

        _processor = monitored
            .GetChangeFeedProcessorBuilder<WishlistItemEntity>(
                processorName: "wishlist-changefeed",
                onChangesDelegate: async (IReadOnlyCollection<WishlistItemEntity> changes, CancellationToken ct) =>
                {
                    _logger.LogInformation("🔔 Change feed triggered with {Count} wishlist changes", changes.Count);
                    foreach (var doc in changes)
                    {
                        try
                        {
                            _logger.LogInformation("Processing wishlist change for child {ChildId}, item {Id}", doc.ChildId, doc.id);
                            JsonObject wishlist = new()
                            {
                                ["id"] = doc.id,
                                ["text"] = doc.Text,
                                ["category"] = doc.Category is null ? null : JsonValue.Create(doc.Category),
                                ["budgetEstimate"] = doc.BudgetEstimate is null ? null : JsonValue.Create(doc.BudgetEstimate),
                                ["createdAt"] = doc.CreatedAt,
                                ["RequestType"] = doc.RequestType,
                                ["StatusChange"] = doc.StatusChange is null ? null : JsonValue.Create(doc.StatusChange)
                            };
                            await _publisher.PublishWishlistAsync(doc.ChildId, doc.DedupeKey, schemaVersion: "v1", wishlist: wishlist, ct);
                            _logger.LogInformation("✅ Successfully published wishlist for child {ChildId} (type: {RequestType})", doc.ChildId, doc.RequestType);

                            // Also broadcast to in-process SignalR hub for immediate UI updates
                            // This provides a fallback path when Drasi SignalR reaction is not yet processing
                            var drasiPayload = new
                            {
                                id = doc.id,
                                childId = doc.ChildId,
                                text = doc.Text,
                                category = doc.Category,
                                budgetEstimate = doc.BudgetEstimate,
                                createdAt = doc.CreatedAt,
                                requestType = doc.RequestType,
                                statusChange = doc.StatusChange
                            };
                            var payloadElement = System.Text.Json.JsonSerializer.SerializeToElement(drasiPayload);
                            Realtime.DrasiEventsHub.ProcessDrasiEvent(WishlistUpdatesQueryId, "i", payloadElement, _hubContext, _logger);
                            _logger.LogDebug("📡 Broadcast wishlist update to SignalR hub for child {ChildId}", doc.ChildId);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Log_PublishWishlistFailed(_logger, ex, doc.ChildId);
                        }
                    }
                })
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(lease)
            .Build();

        try
        {
            await _processor.StartAsync();
            Log_ProcessorStarted(_logger);
        }
        catch (Exception ex)
        {
            Log_ProcessorStartFailed(_logger, ex);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
            Log_ProcessorStopped(_logger);
        }
    }

    // No explicit disposal required for ChangeFeedProcessor; StopAsync is sufficient.
    [LoggerMessage(Level = LogLevel.Information, Message = "Cosmos change feed processor started for wishlist container.")]
    private static partial void Log_ProcessorStarted(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start Cosmos change feed processor.")]
    private static partial void Log_ProcessorStartFailed(ILogger logger, Exception ex);
    [LoggerMessage(Level = LogLevel.Information, Message = "Cosmos change feed processor stopped.")]
    private static partial void Log_ProcessorStopped(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish wishlist change for child {ChildId}")]
    private static partial void Log_PublishWishlistFailed(ILogger logger, Exception ex, string childId);
}

public sealed partial class CosmosRecommendationChangeFeedPublisher : IHostedService
{
    private readonly ILogger<CosmosRecommendationChangeFeedPublisher> _logger;
    private readonly IConfiguration _config;
    private readonly ICosmosRepository _cosmos;
    private readonly IEventPublisher _publisher;
    private ChangeFeedProcessor? _processor;

    public CosmosRecommendationChangeFeedPublisher(
        ILogger<CosmosRecommendationChangeFeedPublisher> logger,
        IConfiguration config,
        ICosmosRepository cosmos,
        IEventPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cosmos);
        ArgumentNullException.ThrowIfNull(publisher);
        _logger = logger;
        _config = config;
        _cosmos = cosmos;
        _publisher = publisher;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string recommendations = _config["Cosmos:Containers:Recommendations"] ?? "recommendations";
        string leases = _config["Cosmos:Containers:Leases"] ?? "leases";

        Container monitored = _cosmos.GetContainer(recommendations);
        Container lease = _cosmos.GetContainer(leases);

        _processor = monitored
            .GetChangeFeedProcessorBuilder<RecommendationSetEntity>(
                processorName: "recommendation-changefeed",
                onChangesDelegate: async (IReadOnlyCollection<RecommendationSetEntity> changes, CancellationToken ct) =>
                {
                    foreach (var doc in changes)
                    {
                        try
                        {
                            // Build JSON representation similar to direct publish schema
                            JsonObject recSetJson = new()
                            {
                                ["recommendationSetId"] = doc.id,
                                ["items"] = new JsonArray(doc.Items.Select(r =>
                                    new JsonObject
                                    {
                                        ["id"] = r.Id,
                                        ["suggestion"] = r.Suggestion,
                                        ["rationale"] = r.Rationale,
                                        ["budgetFit"] = r.BudgetFit is null ? null : JsonValue.Create(r.BudgetFit),
                                        ["availability"] = r.Availability is null ? null : JsonValue.Create(r.Availability)
                                    }).ToArray())
                            };
                            await _publisher.PublishRecommendationAsync(doc.ChildId, schemaVersion: "v1", recommendationSet: recSetJson, ct: ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Log_PublishRecommendationFailed(_logger, ex, doc.ChildId);
                        }
                    }
                })
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(lease)
            .Build();

        try
        {
            await _processor.StartAsync();
            Log_ProcessorStarted(_logger);
        }
        catch (Exception ex)
        {
            Log_ProcessorStartFailed(_logger, ex);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
            Log_ProcessorStopped(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cosmos change feed processor started for recommendation container.")]
    private static partial void Log_ProcessorStarted(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start Cosmos recommendation change feed processor.")]
    private static partial void Log_ProcessorStartFailed(ILogger logger, Exception ex);
    [LoggerMessage(Level = LogLevel.Information, Message = "Cosmos recommendation change feed processor stopped.")]
    private static partial void Log_ProcessorStopped(ILogger logger);
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish recommendation change for child {ChildId}")]
    private static partial void Log_PublishRecommendationFailed(ILogger logger, Exception ex, string childId);
}
