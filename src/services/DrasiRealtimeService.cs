using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Drasicrhsit.Services;

public interface IDrasiRealtimeService
{
    Task<DrasiRealtimeContext> GetLatestContextAsync(CancellationToken ct = default);
    IAsyncEnumerable<DrasiEvent> StreamEventsAsync(string queryName, CancellationToken ct = default);
}

public record DrasiRealtimeContext(
    List<TrendingItem> TrendingItems,
    List<DuplicateAlert> Duplicates,
    List<InactiveChild> InactiveChildren,
    DateTime LastUpdate
);

public record DrasiEvent(string QueryName, JsonElement Data, DateTime Timestamp);
public record TrendingItem(string Item, int Frequency);
public record DuplicateAlert(string ChildId, string Item, int Count);
public record InactiveChild(string ChildId, int DaysSinceLastEvent);

public class DrasiRealtimeService : IDrasiRealtimeService, IAsyncDisposable
{
    private readonly ILogger<DrasiRealtimeService> _logger;
    private readonly string? _signalRUrl;
    private HubConnection? _connection;
    private DrasiRealtimeContext _cachedContext = new([], [], [], DateTime.UtcNow);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DrasiRealtimeService(ILogger<DrasiRealtimeService> logger, IConfiguration config)
    {
        _logger = logger;
        _signalRUrl = config["DRASI_SIGNALR_BASE_URL"];
    }

    private async Task<HubConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is not null && _connection.State == HubConnectionState.Connected)
                return _connection;

            if (string.IsNullOrWhiteSpace(_signalRUrl))
                throw new InvalidOperationException("DRASI_SIGNALR_BASE_URL not configured");

            // Normalize provided base URL to scheme://host[:port] only (strip any accidental path/query)
            // This makes the service resilient if DRASI_SIGNALR_BASE_URL was set to a full endpoint path by mistake.
            var baseUri = new Uri(_signalRUrl, UriKind.Absolute);
            var normalizedBase = $"{baseUri.Scheme}://{baseUri.Host}{(baseUri.IsDefaultPort ? string.Empty : ":" + baseUri.Port)}";

            _connection = new HubConnectionBuilder()
                .WithUrl($"{normalizedBase}/hub")
                .WithAutomaticReconnect()
                .Build();

            // Subscribe to all Drasi queries
            _connection.On<string, JsonElement>("wishlist-trending-1h", (_, data) => UpdateTrending(data));
            _connection.On<string, JsonElement>("wishlist-duplicates-by-child", (_, data) => UpdateDuplicates(data));
            _connection.On<string, JsonElement>("wishlist-inactive-children-3d", (_, data) => UpdateInactive(data));

            await _connection.StartAsync(ct);
            _logger.LogInformation("Connected to Drasi SignalR at {Url}", normalizedBase);

            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void UpdateTrending(JsonElement data)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<TrendingItem>>(data.GetRawText()) ?? [];
            _cachedContext = _cachedContext with { TrendingItems = items, LastUpdate = DateTime.UtcNow };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse trending data");
        }
    }

    private void UpdateDuplicates(JsonElement data)
    {
        try
        {
            var duplicates = JsonSerializer.Deserialize<List<DuplicateAlert>>(data.GetRawText()) ?? [];
            _cachedContext = _cachedContext with { Duplicates = duplicates, LastUpdate = DateTime.UtcNow };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse duplicates data");
        }
    }

    private void UpdateInactive(JsonElement data)
    {
        try
        {
            var inactive = JsonSerializer.Deserialize<List<InactiveChild>>(data.GetRawText()) ?? [];
            _cachedContext = _cachedContext with { InactiveChildren = inactive, LastUpdate = DateTime.UtcNow };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse inactive data");
        }
    }

    public async Task<DrasiRealtimeContext> GetLatestContextAsync(CancellationToken ct = default)
    {
        // Ensure connected
        await GetConnectionAsync(ct);
        return _cachedContext;
    }

    public async IAsyncEnumerable<DrasiEvent> StreamEventsAsync(string queryName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct);
        var channel = System.Threading.Channels.Channel.CreateUnbounded<DrasiEvent>();

        connection.On<string, JsonElement>(queryName, (_, data) =>
        {
            channel.Writer.TryWrite(new DrasiEvent(queryName, data, DateTime.UtcNow));
        });

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
