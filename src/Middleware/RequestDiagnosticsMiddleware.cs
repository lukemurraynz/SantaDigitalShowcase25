using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Middleware
{
    public sealed partial class RequestDiagnosticsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestDiagnosticsMiddleware> _logger;

        public RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var sw = Stopwatch.StartNew();
            Log_RequestStart(_logger, context.Request.Method, context.Request.Path);
            try
            {
                await _next(context);
                Log_RequestEnd(_logger, context.Response.StatusCode, context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log_RequestException(_logger, ex, context.Request.Method, context.Request.Path);
                context.Response.StatusCode = 500;
                if (!context.Response.HasStarted)
                {
                    await context.Response.WriteAsync("internal error");
                }
            }
        }

        // High-performance logging (CA1848) partial logger methods inside the middleware class
        [LoggerMessage(Level = LogLevel.Information, Message = "RequestStart {Method} {Path}")]
        private static partial void Log_RequestStart(ILogger logger, string method, PathString path);

        [LoggerMessage(Level = LogLevel.Information, Message = "RequestEnd {StatusCode} {Method} {Path} {ElapsedMs}")]
        private static partial void Log_RequestEnd(ILogger logger, int statusCode, string method, PathString path, long elapsedMs);

        [LoggerMessage(Level = LogLevel.Error, Message = "RequestException {Method} {Path}")]
        private static partial void Log_RequestException(ILogger logger, Exception ex, string method, PathString path);
    }

    public static class RequestDiagnosticsMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestDiagnostics(this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequestDiagnosticsMiddleware>();
        }
    }
}