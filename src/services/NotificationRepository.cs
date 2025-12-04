using System.Text.Json.Serialization;

namespace Services;

public interface INotificationRepository
{
    Task StoreAsync(NotificationEntity entity);
    IAsyncEnumerable<NotificationEntity> ListAsync(string childId, int take = 50);
}

public sealed class NotificationRepository : CosmosRepositoryBase<NotificationEntity>, INotificationRepository
{
    public NotificationRepository(ICosmosRepository cosmos, IConfiguration config)
        : base(cosmos, config, "Cosmos:Containers:Notifications")
    {
    }

    public Task StoreAsync(NotificationEntity entity)
    {
        return CreateAsync(entity);
    }

    public IAsyncEnumerable<NotificationEntity> ListAsync(string childId, int take = 50)
    {
        return ListByPartitionKeyAsync("childId", childId, orderByField: "createdAt", orderDescending: true, take: take);
    }
}

public sealed class NotificationEntity : ICosmosEntity
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string ChildId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // recommendation | logistics
    public string Message { get; set; } = string.Empty;
    public string? RelatedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string State { get; set; } = "unread";

    [JsonIgnore]
    public string PartitionKeyValue => ChildId;
}