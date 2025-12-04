using System.Text.Json.Serialization;

namespace Services;

public interface IProfileSnapshotRepository
{
    Task StoreAsync(ProfileSnapshotEntity entity);
    Task<ProfileSnapshotEntity?> GetAsync(string childId, string id);
}

public sealed class ProfileSnapshotRepository : CosmosRepositoryBase<ProfileSnapshotEntity>, IProfileSnapshotRepository
{
    public ProfileSnapshotRepository(ICosmosRepository cosmos, IConfiguration config)
        : base(cosmos, config, "Cosmos:Containers:Profiles")
    {
    }

    public Task StoreAsync(ProfileSnapshotEntity entity)
    {
        return CreateAsync(entity);
    }

    public Task<ProfileSnapshotEntity?> GetAsync(string childId, string id)
    {
        return GetByIdAsync(id, childId);
    }
}

public sealed class ProfileSnapshotEntity : ICosmosEntity
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string ChildId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> Preferences { get; set; } = new();
    public double? BudgetCeiling { get; set; }
    public string? BehaviorSummary { get; set; }
    public string EnrichmentSource { get; set; } = "mixed";
    public bool FallbackUsed { get; set; }

    [JsonIgnore]
    public string PartitionKeyValue => ChildId;
}