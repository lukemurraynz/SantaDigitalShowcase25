using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Middleware;

public sealed class DrasiLoopPreventionMiddleware
{
    readonly RequestDelegate _next;
    public DrasiLoopPreventionMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx); // CA1062 + CA1510
            // If incoming request has Drasi reaction header, mark context to suppress publishing downstream.
            if (ctx.Request.Headers.ContainsKey("X-Drasi-Reaction"))
            {
                ctx.Items["DrasiSuppressPublish"] = true;
            }
            await _next(ctx);
        }
}
