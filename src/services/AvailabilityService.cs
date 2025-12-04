using Models;

namespace Services;

public interface IAvailabilityService
{
    Task<Availability> GetAvailabilityAsync(string suggestion, CancellationToken ct = default);
}

public class AvailabilityService : IAvailabilityService
{
    public Task<Availability> GetAvailabilityAsync(string suggestion, CancellationToken ct = default)
    {
        // Stub: unknown availability
        return Task.FromResult(new Availability(null, null));
    }
}
