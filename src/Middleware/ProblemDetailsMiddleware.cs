using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Middleware;

public partial class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;
    
    // SignalR hub paths that should bypass custom error handling
    private const string ApiHubPath = "/api/hub";
    private const string HubPath = "/hub";
    
    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        
        // Skip custom error handling for SignalR hub - let SignalR handle its own errors
        // This prevents interfering with SignalR's protocol-specific error responses
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith(ApiHubPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(HubPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }
        
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException badRequestEx)
        {
            // Handle malformed requests (e.g., invalid JSON body) as 400 Bad Request
            string traceId = context.TraceIdentifier;
            Log_BadRequest(_logger, badRequestEx, traceId);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";
                var payload = new
                {
                    type = "https://httpstatuses.com/400",
                    title = "Bad Request",
                    status = 400,
                    traceId,
                    detail = "The request could not be processed. Please check the request format and try again."
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        }
        catch (JsonException jsonEx)
        {
            // Handle JSON parsing errors as 400 Bad Request
            string traceId = context.TraceIdentifier;
            Log_JsonParseError(_logger, jsonEx, traceId);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";
                var payload = new
                {
                    type = "https://httpstatuses.com/400",
                    title = "Bad Request",
                    status = 400,
                    traceId,
                    detail = "Invalid JSON format in request body."
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string traceId = context.TraceIdentifier;
            Log_UnhandledException(_logger, ex, traceId);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                var payload = new
                {
                    type = "https://httpstatuses.com/500",
                    title = "Internal Server Error",
                    status = 500,
                    traceId,
                    detail = "An unexpected error occurred. Please try again later."
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bad request {TraceId}")]
    private static partial void Log_BadRequest(ILogger logger, Exception ex, string traceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "JSON parse error {TraceId}")]
    private static partial void Log_JsonParseError(ILogger logger, Exception ex, string traceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception {TraceId}")]
    private static partial void Log_UnhandledException(ILogger logger, Exception ex, string traceId);
}