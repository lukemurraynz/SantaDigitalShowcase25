namespace Services;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string? state, CancellationToken ct = default);
}

public record NotificationDto(
    string Id,
    string ChildId,
    string Type,
    string Message,
    DateTime CreatedAt,
    string State,
    string? RelatedRecommendationSetId
);

public interface INotificationMutator
{
    Task<NotificationDto> AddAsync(string childId, string type, string message, string state = "new", CancellationToken ct = default);
}

public class NotificationService : INotificationService, INotificationMutator
{
    private static readonly List<NotificationDto> _items = new();
    private static readonly object _lock = new();
    private static bool _seeded;

    private static void EnsureSeed()
    {
        if (_seeded) return;
        lock (_lock)
        {
            if (_seeded) return;
            var now = DateTime.UtcNow;
            _items.Add(new NotificationDto(Guid.NewGuid().ToString(), "child-demo", "info", "Workshop initialized", now, "new", null));
            _items.Add(new NotificationDto(Guid.NewGuid().ToString(), "child-demo", "recommendation", "First recommendations generated", now.AddSeconds(-30), "new", null));
            _items.Add(new NotificationDto(Guid.NewGuid().ToString(), "child-demo", "logistics", "Logistics assessment pending", now.AddSeconds(-60), "new", null));
            _seeded = true;
        }
    }

    public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string? state, CancellationToken ct = default)
    {
        EnsureSeed();
        IReadOnlyList<NotificationDto> result = _items
            .Where(n => state is null || string.Equals(n.State, state, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<NotificationDto> AddAsync(string childId, string type, string message, string state = "new", CancellationToken ct = default)
    {
        var dto = new NotificationDto(Guid.NewGuid().ToString(), childId, type, message, DateTime.UtcNow, state, null);
        lock (_lock) { _items.Add(dto); }
        return Task.FromResult(dto);
    }
}
