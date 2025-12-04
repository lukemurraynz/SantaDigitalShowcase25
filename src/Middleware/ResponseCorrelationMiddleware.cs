using Microsoft.AspNetCore.Http;

namespace Middleware;

public class ResponseCorrelationMiddleware
{
    private readonly RequestDelegate _next;
    public ResponseCorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        
        // Generate unique request ID if not provided
        string requestId = Guid.NewGuid().ToString();
        
        await _next(context);
        
        if (!context.Response.HasStarted)
        {
            // Azure API Guidelines: Required x-ms-request-id for correlation
            context.Response.Headers.TryAdd("x-ms-request-id", requestId);
            
            // Optional: Also include x-ms-client-request-id if provided by caller
            if (context.Request.Headers.TryGetValue("x-ms-client-request-id", out var clientRequestId))
            {
                context.Response.Headers.TryAdd("x-ms-client-request-id", clientRequestId.ToString());
            }
            
            // Keep existing correlation ID for internal use
            string correlationId = context.TraceIdentifier;
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                context.Response.Headers.TryAdd("X-Correlation-Id", correlationId);
            }
        }
    }
}