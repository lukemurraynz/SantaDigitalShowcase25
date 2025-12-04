using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Middleware
{
    public sealed class RateLimitingMiddleware
    {
        readonly RequestDelegate _next;
        // Simple in-memory counters: childId -> window timestamps
        static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _minute = new();
        static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _burst = new();
        const int MinuteLimit = 20;
        const int BurstLimit = 10;
        static readonly TimeSpan MinuteWindow = TimeSpan.FromMinutes(1);
        static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(5);

        public RateLimitingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx); // CA1062 + CA1510
            if (ctx.Request.Path.StartsWithSegments("/api/children") && ctx.Request.Method == HttpMethods.Post && ctx.Request.Path.Value?.Contains("wishlist") == true)
            {
                string childId = ctx.Request.Path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipWhile(s => s != "children").Skip(1).FirstOrDefault() ?? "unknown";
                if (!Allow(childId))
                {
                    ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    ctx.Response.Headers.RetryAfter = "5"; // ASP0015 use property
                    await ctx.Response.WriteAsync("Rate limit exceeded");
                    return;
                }
            }
            await _next(ctx);
        }

        static bool Allow(string childId)
        {
            DateTime now = DateTime.UtcNow;
            var m = _minute.GetOrAdd(childId, _ => new());
            var b = _burst.GetOrAdd(childId, _ => new());
            Trim(m, now - MinuteWindow);
            Trim(b, now - BurstWindow);
            if (m.Count >= MinuteLimit || b.Count >= BurstLimit)
            {
                return false;
            }
            m.Enqueue(now);
            b.Enqueue(now);
            return true;
        }

        static void Trim(ConcurrentQueue<DateTime> q, DateTime threshold)
        {
            while (q.TryPeek(out DateTime ts) && ts < threshold)
            {
                q.TryDequeue(out _);
            }
        }
    }
}