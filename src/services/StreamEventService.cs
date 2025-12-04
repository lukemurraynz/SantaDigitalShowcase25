using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IStreamEventService
{
    Task StreamChildAsync(string childId, HttpContext ctx, CancellationToken ct);
}

public sealed class StreamEventService : IStreamEventService
{
    readonly IElfRecommendationService _recs;
    readonly IStreamResumeStore _resume;
    readonly IStreamMetrics _metrics;
    readonly IStreamBroadcaster _broadcaster;
    public StreamEventService(IElfRecommendationService recs, IStreamResumeStore resume, IStreamMetrics metrics, IStreamBroadcaster broadcaster)
    {
        _recs = recs;
        _resume = resume;
        _metrics = metrics;
        _broadcaster = broadcaster;
    }

    public async Task StreamChildAsync(string childId, HttpContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx); // CA1062 + CA1510
        SseWriter.Prepare(ctx.Response);
        string streamKey = "child:" + childId;
        _metrics.IncrementConnection(streamKey);

        // Resume logic (placeholder: send missed summary if Last-Event-ID present)
        string? last = ctx.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (!string.IsNullOrEmpty(last))
        {
            var resumePayload = JsonSerializer.Serialize(new { type = "resume", lastEventId = last });
            await SseWriter.WriteEventAsync(ctx.Response, "resume", resumePayload, ct);
            _metrics.IncrementEvent(streamKey, "resume");
        }

        // Initial recommendations snapshot
        var recs = await _recs.GetRecommendationsForChildAsync(childId, ct);
        var payload = JsonSerializer.Serialize(new { type = "recommendation-update", childId, recommendations = recs });
        string eventId = Guid.NewGuid().ToString();
        ctx.Response.Headers["X-Last-Event-ID"] = eventId;
        _resume.Record(streamKey, eventId);
        await SseWriter.WriteEventAsync(ctx.Response, "message", payload, ct);
        _metrics.IncrementEvent(streamKey, "message");

        // Live dynamic events subscription
        var reader = _broadcaster.Subscribe(childId);
        while (!ct.IsCancellationRequested && await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out SseEvent? ev))
            {
                string dynId = Guid.NewGuid().ToString();
                _resume.Record(streamKey, dynId);
                await SseWriter.WriteEventAsync(ctx.Response, ev.Type, ev.Json, ct);
                _metrics.IncrementEvent(streamKey, ev.Type);
            }
        }
    }
}
