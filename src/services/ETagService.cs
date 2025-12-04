using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Services;

/// <summary>
/// Service for generating ETags for resources to support conditional requests.
/// Implements Azure API Guidelines recommendations for optimistic concurrency control.
/// </summary>
public interface IETagService
{
    /// <summary>
    /// Generate a strong ETag from a resource object by hashing its JSON representation.
    /// </summary>
    string GenerateETag<T>(T resource) where T : notnull;
    
    /// <summary>
    /// Validate if the provided If-Match header value matches the current ETag.
    /// </summary>
    bool ValidateIfMatch(string? ifMatchHeader, string currentETag);
    
    /// <summary>
    /// Validate if the provided If-None-Match header value matches the current ETag.
    /// </summary>
    bool ValidateIfNoneMatch(string? ifNoneMatchHeader, string currentETag);
}

public class ETagService : IETagService
{
    public string GenerateETag<T>(T resource) where T : notnull
    {
        // Serialize the resource to JSON for consistent hashing
        var json = JsonSerializer.Serialize(resource);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        // Use SHA256 for strong ETag generation
        var hash = SHA256.HashData(bytes);
        var base64Hash = Convert.ToBase64String(hash);
        
        // Return as quoted string per HTTP specification
        return $"\"{base64Hash}\"";
    }
    
    public bool ValidateIfMatch(string? ifMatchHeader, string currentETag)
    {
        if (string.IsNullOrWhiteSpace(ifMatchHeader))
            return true; // No precondition, always valid
            
        // Handle wildcard
        if (ifMatchHeader.Trim() == "*")
            return true; // Resource exists, wildcard matches
            
        // Compare ETags (case-sensitive per HTTP spec)
        return ifMatchHeader.Trim() == currentETag;
    }
    
    public bool ValidateIfNoneMatch(string? ifNoneMatchHeader, string currentETag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatchHeader))
            return true; // No precondition, always valid
            
        // Handle wildcard
        if (ifNoneMatchHeader.Trim() == "*")
            return false; // Resource exists, wildcard means "none" failed
            
        // Compare ETags - if they match, precondition fails
        return ifNoneMatchHeader.Trim() != currentETag;
    }
}
