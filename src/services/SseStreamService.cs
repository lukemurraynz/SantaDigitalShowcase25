using Microsoft.AspNetCore.Http;
using Drasicrhsit.Infrastructure;
using System.Text.Json;

namespace Services;

public interface ISseStreamService
{
    Task StreamWishlistAsync(string childId, HttpContext ctx, CancellationToken ct);
    Task StreamRecommendationsAsync(string childId, HttpContext ctx, CancellationToken ct);
    Task StreamNotificationsAsync(string childId, HttpContext ctx, CancellationToken ct);
}

public sealed class SseStreamService : ISseStreamService
{
    readonly IWishlistRepository _wishlists;
    readonly IRecommendationRepository _recs;
    readonly INotificationRepository _notifications;
    readonly IStreamResumeStore _resume;
    readonly IStreamMetrics _metrics;
    readonly IStreamBroadcaster _broadcaster;
    public SseStreamService(IWishlistRepository wishlists, IRecommendationRepository recs, INotificationRepository notifications, IStreamResumeStore resume, IStreamMetrics metrics, IStreamBroadcaster broadcaster)
    {
        _wishlists = wishlists;
        _recs = recs;
        _notifications = notifications;
        _resume = resume;
        _metrics = metrics;
        _broadcaster = broadcaster;
    }

    public async Task StreamWishlistAsync(string childId, HttpContext ctx, CancellationToken ct)
    {
        await PrepareAsync(childId, ctx, streamType: "wishlist", ct);
        await foreach (var item in _wishlists.ListAsync(childId).WithCancellation(ct))
        {
            // Use camelCase property names expected by frontend
            await EmitAsync(ctx, childId, "wishlist-item", new { id = item.id, text = item.Text, category = item.Category, budgetEstimate = item.BudgetEstimate }, ct);
        }
        // Live updates
        var reader = _broadcaster.Subscribe(childId);
        while (!ct.IsCancellationRequested && await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out SseEvent? ev))
            {
                if (ev.Type == "wishlist-item")
                {
                    await SseWriter.WriteEventAsync(ctx.Response, ev.Type, ev.Json, ct);
                    _metrics.IncrementEvent(childId, ev.Type);
                }
            }
        }
    }

    public async Task StreamRecommendationsAsync(string childId, HttpContext ctx, CancellationToken ct)
    {
        await PrepareAsync(childId, ctx, streamType: "recommendations", ct);
        await foreach (var set in _recs.ListAsync(childId, take: 5).WithCancellation(ct))
        {
            // Normalize casing for consistency
            await EmitAsync(ctx, childId, "recommendation-update", new { id = set.id, items = set.Items }, ct);
        }
        // Live updates
        var reader = _broadcaster.Subscribe(childId);
        while (!ct.IsCancellationRequested && await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out SseEvent? ev))
            {
                if (ev.Type == "recommendation-update")
                {
                    await SseWriter.WriteEventAsync(ctx.Response, ev.Type, ev.Json, ct);
                    _metrics.IncrementEvent(childId, ev.Type);
                }
            }
        }
    }

    public async Task StreamNotificationsAsync(string childId, HttpContext ctx, CancellationToken ct)
    {
        await PrepareAsync(childId, ctx, streamType: "notifications", ct);
        try
        {
            await foreach (var n in _notifications.ListAsync(childId).WithCancellation(ct))
            {
                // Emit camelCase payload to match UI model
                await EmitAsync(ctx, childId, "notification", new { id = n.id, type = n.Type, message = n.Message, relatedId = n.RelatedId, state = n.State }, ct);
            }
        }
        catch
        {
            // If historical listing fails (e.g., container missing), continue with live stream only
        }
        // Live updates
        var reader = _broadcaster.Subscribe(childId);
        while (!ct.IsCancellationRequested && await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out SseEvent? ev))
            {
                if (ev.Type == "notification")
                {
                    await SseWriter.WriteEventAsync(ctx.Response, ev.Type, ev.Json, ct);
                    _metrics.IncrementEvent(childId, ev.Type);
                }
            }
        }
    }

    async Task PrepareAsync(string childId, HttpContext ctx, string streamType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx); // CA1062 + CA1510
        SseWriter.Prepare(ctx.Response);
        string streamKey = streamType + ":" + childId;
        _metrics.IncrementConnection(streamKey);
        string? last = ctx.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (!string.IsNullOrEmpty(last))
        {
            await SseWriter.WriteEventAsync(ctx.Response, "resume", JsonSerializer.Serialize(new { lastEventId = last }), ct);
            _metrics.IncrementEvent(streamKey, "resume");
        }
    }

    async Task EmitAsync(HttpContext ctx, string childId, string type, object payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx); // CA1062 + CA1510
        string eventId = Guid.NewGuid().ToString();
        _resume.Record(childId, eventId);
        string json = JsonSerializer.Serialize(payload);
        await SseWriter.WriteEventAsync(ctx.Response, type, json, ct);
        _metrics.IncrementEvent(childId, type);
    }
}
