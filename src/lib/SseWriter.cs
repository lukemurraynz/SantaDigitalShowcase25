using System.Text;
using Microsoft.AspNetCore.Http;

namespace Drasicrhsit.Infrastructure;

public static class SseWriter
{
    public static async Task WriteEventAsync(HttpResponse response, string eventType, string data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(data);

        await response.WriteAsync($"event: {eventType}\n", ct);
        foreach (var line in data.Split('\n'))
        {
            await response.WriteAsync($"data: {line}\n", ct);
        }
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static void Prepare(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers.Connection = "keep-alive";
        // Help proxies avoid buffering SSE
        response.Headers["X-Accel-Buffering"] = "no";
    }
}
