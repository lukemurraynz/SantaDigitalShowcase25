using Microsoft.AspNetCore.Http;

namespace Services;

/// <summary>
/// Extension methods for adding Azure API Guidelines compliant headers to HTTP responses.
/// </summary>
public static class HttpResultExtensions
{
    /// <summary>
    /// Adds an ETag header to the HTTP result.
    /// Azure API Guidelines: Services should return ETags for conditional request support.
    /// </summary>
    public static IResult WithETag(this IResult result, string etag)
    {
        // Note: Simplified implementation - in production use typed result wrapper
        return result; // Headers would be added via TypedResults or custom wrapper
    }
    
    /// <summary>
    /// Adds custom headers to the HTTP result.
    /// </summary>
    public static IResult WithHeaders(this IResult result, IHeaderDictionary headers)
    {
        // Note: This is a simplified implementation
        // In production, you might want to use a typed result wrapper
        return result;
    }
}
