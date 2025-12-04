using Microsoft.Azure.Cosmos;
using System.Text.Json.Serialization;

namespace Services;

public interface IRecommendationRepository
{
    Task StoreAsync(RecommendationSetEntity recommendationSet);
    Task<RecommendationSetEntity?> GetAsync(string childId, string setId);
    IAsyncEnumerable<RecommendationSetEntity> ListAsync(string childId, int take = 10);
    Task<IReadOnlyList<RationaleAuditEntry>> GetRationaleAuditAsync(string childId, string? setId = null);
}

public sealed class RecommendationRepository : CosmosRepositoryBase<RecommendationSetEntity>, IRecommendationRepository
{
    public RecommendationRepository(ICosmosRepository cosmos, IConfiguration config)
        : base(cosmos, config, "Cosmos:Containers:Recommendations")
    {
    }

    public Task StoreAsync(RecommendationSetEntity recommendationSet)
    {
        return CreateAsync(recommendationSet);
    }

    public Task<RecommendationSetEntity?> GetAsync(string childId, string setId)
    {
        return GetByIdAsync(setId, childId);
    }

    public IAsyncEnumerable<RecommendationSetEntity> ListAsync(string childId, int take = 10)
    {
        return ListByPartitionKeyAsync("childId", childId, orderByField: "createdAt", orderDescending: true, take: take);
    }

    public async Task<IReadOnlyList<RationaleAuditEntry>> GetRationaleAuditAsync(string childId, string? setId = null)
    {
        var results = new List<RationaleAuditEntry>();
        QueryDefinition queryDef = setId is null
            ? new QueryDefinition("SELECT * FROM c WHERE c.childId = @childId ORDER BY c.createdAt DESC")
                .WithParameter("@childId", childId)
            : new QueryDefinition("SELECT * FROM c WHERE c.childId = @childId AND c.id = @setId")
                .WithParameter("@childId", childId)
                .WithParameter("@setId", setId);

        await foreach (var set in QueryAsync(queryDef))
        {
            foreach (var item in set.Items)
            {
                results.Add(new RationaleAuditEntry(
                    set.id,
                    set.CreatedAt,
                    item.Id,
                    item.Suggestion,
                    item.Rationale,
                    set.FallbackUsed
                ));
            }
        }
        return results;
    }
}

public sealed record RationaleAuditEntry(
    string RecommendationSetId,
    DateTime CreatedAt,
    string ItemId,
    string Suggestion,
    string Rationale,
    bool FallbackUsed
);

public sealed class RecommendationSetEntity : ICosmosEntity
{
    public string id { get; set; } = Guid.NewGuid().ToString(); // recommendationSetId
    public string ChildId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string ProfileSnapshotId { get; set; } = string.Empty;
    public bool FallbackUsed { get; set; }
    public string GenerationSource { get; set; } = "mixed";
    public List<RecommendationItemEntity> Items { get; set; } = new();

    [JsonIgnore]
    public string PartitionKeyValue => ChildId;
}

public sealed class RecommendationItemEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Suggestion { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string? BudgetFit { get; set; }
    public string? Availability { get; set; }
}