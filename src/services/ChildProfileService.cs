using Models;
using Microsoft.Extensions.Logging;

namespace Services;

public interface IChildProfileService
{
    Task<ChildProfile?> GetChildProfileAsync(string childId, CancellationToken ct = default);
    Task UpdateStatusAsync(string childId, NiceStatus newStatus, CancellationToken ct = default);
}

public class ChildProfileService : IChildProfileService
{
    // In-memory status tracking (in production this would be persisted to Cosmos)
    private static readonly Dictionary<string, NiceStatus> _statusCache = new();
    private static readonly object _lock = new();
    private readonly IDrasiViewClient _drasiClient;
    private readonly ILogger<ChildProfileService> _logger;

    public ChildProfileService(IDrasiViewClient drasiClient, ILogger<ChildProfileService> logger)
    {
        _drasiClient = drasiClient;
        _logger = logger;
    }

    public async Task<ChildProfile?> GetChildProfileAsync(string childId, CancellationToken ct = default)
    {
        NiceStatus status = NiceStatus.Unknown;

        // First check the in-memory cache
        lock (_lock)
        {
            if (_statusCache.TryGetValue(childId, out var cachedStatus))
            {
                status = cachedStatus;
                _logger.LogDebug("Retrieved cached status for {ChildId}: {Status}", childId, status);
            }
        }

        // If not in cache, query Drasi for the latest behavior status
        if (status == NiceStatus.Unknown)
        {
            try
            {
                var behaviorChanges = await _drasiClient.GetCurrentResultAsync("default", "behavior-status-changes", ct);

                // Find the most recent status change for this child
                var latestChange = behaviorChanges
                    .Where(item => item["childId"]?.GetValue<string>() == childId)
                    .OrderByDescending(item => item["changedAt"]?.GetValue<string>() ?? "")
                    .FirstOrDefault();

                if (latestChange != null)
                {
                    var statusString = latestChange["newStatus"]?.GetValue<string>();
                    status = statusString?.ToLowerInvariant() switch
                    {
                        "nice" => NiceStatus.Nice,
                        "naughty" => NiceStatus.Naughty,
                        _ => NiceStatus.Unknown
                    };

                    if (status != NiceStatus.Unknown)
                    {
                        _logger.LogInformation("Retrieved status from Drasi for {ChildId}: {Status} (from behavior-status-changes query)",
                            childId, status);

                        // Update cache with the Drasi result
                        lock (_lock)
                        {
                            _statusCache[childId] = status;
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("No behavior status changes found in Drasi for {ChildId}, using Unknown", childId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Drasi for status of {ChildId}, using Unknown", childId);
            }
        }

        var profile = new ChildProfile(
            Id: childId,
            Name: null,
            Age: null,
            Preferences: Array.Empty<string>(),
            Constraints: new Constraints(Budget: null),
            PrivacyFlags: new PrivacyFlags(OptOut: false),
            Status: status
        );

        return profile;
    }

    public Task UpdateStatusAsync(string childId, NiceStatus newStatus, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _statusCache[childId] = newStatus;
        }
        return Task.CompletedTask;
    }
}
