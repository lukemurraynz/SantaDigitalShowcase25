using Models;
using Microsoft.Extensions.Logging;

namespace Services;

public interface IChildProfileService
{
    Task<ChildProfile?> GetChildProfileAsync(string childId, CancellationToken ct = default);
    Task UpdateStatusAsync(string childId, NiceStatus newStatus, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a child profile with the given details.
    /// If the profile exists, preferences are merged (not replaced).
    /// </summary>
    Task<ChildProfile> UpsertProfileAsync(string childId, string? name, int? age, IEnumerable<string>? preferences, decimal? budget = null, CancellationToken ct = default);

    /// <summary>
    /// Adds preferences to an existing profile (or creates a new one).
    /// Used when adding wishlist items to build up the child's interests.
    /// </summary>
    Task AddPreferencesAsync(string childId, IEnumerable<string> preferences, CancellationToken ct = default);
}

public class ChildProfileService : IChildProfileService
{
    // In-memory profile storage (in production this would be persisted to Cosmos)
    private static readonly Dictionary<string, ChildProfile> _profileCache = new();
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
        // First check the in-memory cache for a full profile
        lock (_lock)
        {
            if (_profileCache.TryGetValue(childId, out var cachedProfile))
            {
                _logger.LogDebug("Retrieved cached profile for {ChildId}: Name={Name}, Age={Age}, Preferences={Preferences}, Status={Status}",
                    childId, cachedProfile.Name, cachedProfile.Age,
                    cachedProfile.Preferences?.Length ?? 0, cachedProfile.Status);
                return cachedProfile;
            }
        }

        // No cached profile, try to get status from Drasi
        NiceStatus status = NiceStatus.Unknown;
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

        // Return a minimal profile (no preferences since we don't have any stored)
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
            if (_profileCache.TryGetValue(childId, out var existing))
            {
                // Update existing profile with new status
                _profileCache[childId] = existing with { Status = newStatus };
            }
            else
            {
                // Create minimal profile with just the status
                _profileCache[childId] = new ChildProfile(
                    Id: childId,
                    Name: null,
                    Age: null,
                    Preferences: Array.Empty<string>(),
                    Constraints: new Constraints(Budget: null),
                    PrivacyFlags: new PrivacyFlags(OptOut: false),
                    Status: newStatus
                );
            }
        }
        _logger.LogInformation("Updated status for {ChildId} to {Status}", childId, newStatus);
        return Task.CompletedTask;
    }

    public Task<ChildProfile> UpsertProfileAsync(string childId, string? name, int? age, IEnumerable<string>? preferences, decimal? budget = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var existingPrefs = Array.Empty<string>();
            var existingStatus = NiceStatus.Unknown;

            if (_profileCache.TryGetValue(childId, out var existing))
            {
                existingPrefs = existing.Preferences ?? Array.Empty<string>();
                existingStatus = existing.Status;
            }

            // Merge new preferences with existing ones (deduplicated)
            var mergedPrefs = existingPrefs
                .Concat(preferences ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var profile = new ChildProfile(
                Id: childId,
                Name: name,
                Age: age,
                Preferences: mergedPrefs,
                Constraints: new Constraints(Budget: budget),
                PrivacyFlags: new PrivacyFlags(OptOut: false),
                Status: existingStatus
            );

            _profileCache[childId] = profile;
            _logger.LogInformation("Upserted profile for {ChildId}: Name={Name}, Age={Age}, Preferences=[{Preferences}]",
                childId, name, age, string.Join(", ", mergedPrefs));

            return Task.FromResult(profile);
        }
    }

    public Task AddPreferencesAsync(string childId, IEnumerable<string> preferences, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var newPrefs = preferences.ToArray();

            if (_profileCache.TryGetValue(childId, out var existing))
            {
                // Merge with existing preferences
                var mergedPrefs = (existing.Preferences ?? Array.Empty<string>())
                    .Concat(newPrefs)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                _profileCache[childId] = existing with { Preferences = mergedPrefs };
                _logger.LogInformation("Added preferences for {ChildId}: [{NewPrefs}] -> Total: [{AllPrefs}]",
                    childId, string.Join(", ", newPrefs), string.Join(", ", mergedPrefs));
            }
            else
            {
                // Create new profile with just preferences
                _profileCache[childId] = new ChildProfile(
                    Id: childId,
                    Name: null,
                    Age: null,
                    Preferences: newPrefs,
                    Constraints: new Constraints(Budget: null),
                    PrivacyFlags: new PrivacyFlags(OptOut: false),
                    Status: NiceStatus.Unknown
                );
                _logger.LogInformation("Created profile for {ChildId} with preferences: [{Prefs}]",
                    childId, string.Join(", ", newPrefs));
            }
        }
        return Task.CompletedTask;
    }
}
