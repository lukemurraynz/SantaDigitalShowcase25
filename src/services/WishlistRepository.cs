using System.Text.Json.Serialization;

namespace Services;

public interface IWishlistRepository
{
    Task UpsertAsync(WishlistItemEntity entity);
    IAsyncEnumerable<WishlistItemEntity> ListAsync(string childId);
}

public sealed class WishlistRepository : CosmosRepositoryBase<WishlistItemEntity>, IWishlistRepository
{
    public WishlistRepository(ICosmosRepository cosmos, IConfiguration config)
        : base(cosmos, config, "Cosmos:Containers:Wishlists")
    {
    }

    public new Task UpsertAsync(WishlistItemEntity entity)
    {
        return base.UpsertAsync(entity);
    }

    public IAsyncEnumerable<WishlistItemEntity> ListAsync(string childId)
    {
        return ListByPartitionKeyAsync("childId", childId);
    }
}

public sealed class WishlistItemEntity : ICosmosEntity
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string ChildId { get; set; } = string.Empty;  // C# naming, will match /childId PK after serialization
    public string DedupeKey { get; set; } = string.Empty;
    public string RequestType { get; set; } = "gift";  // "gift" or "behavior-update"
    public string Text { get; set; } = string.Empty;
    public string? Category { get; set; }
    public double? BudgetEstimate { get; set; }
    public string? StatusChange { get; set; }  // "Nice", "Naughty", or null
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string PartitionKeyValue => ChildId;
}
