using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Threading.Channels;
using Drasicrhsit.Infrastructure;
using Services;

namespace Services;

public static class DrasiStreamApi
{
    public static IEndpointRouteBuilder MapDrasiStreamApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("stream/children/{childId}", async (string childId, HttpContext ctx, IStreamEventService streamService, CancellationToken ct) =>
        {
            await streamService.StreamChildAsync(childId, ctx, ct);
        })
        .WithTags("Frontend", "Streaming");

        // Notifications SSE stream expected by frontend at /api/v1/notifications/stream/{childId}
        app.MapGet("notifications/stream/{childId}", async (string childId, HttpContext ctx, ISseStreamService sse, CancellationToken ct) =>
        {
            await sse.StreamNotificationsAsync(childId, ctx, ct);
        })
        .WithTags("Frontend", "Streaming");
        return app;
    }
}
