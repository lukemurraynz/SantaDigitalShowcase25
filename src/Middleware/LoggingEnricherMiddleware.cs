using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Middleware;

public sealed class LoggingEnricherMiddleware
{
    readonly RequestDelegate _next;
    readonly ILogger<LoggingEnricherMiddleware> _logger;
    public LoggingEnricherMiddleware(RequestDelegate next, ILogger<LoggingEnricherMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

        public async Task InvokeAsync(HttpContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx); // CA1062 + CA1510
            string role = ctx.User.FindFirst("role")?.Value ?? "unknown";
            using (_logger.BeginScope(new Dictionary<string,object>
            {
                ["role"] = role,
                ["path"] = ctx.Request.Path.Value ?? string.Empty
            }))
            {
                await _next(ctx);
            }
        }
}
