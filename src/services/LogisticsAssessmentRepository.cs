using Microsoft.Azure.Cosmos;
using System.Text.Json.Serialization;

namespace Services;

public interface ILogisticsAssessmentRepository
{
    Task StoreAsync(LogisticsAssessmentEntity entity);
    IAsyncEnumerable<LogisticsAssessmentEntity> ListAsync(string childId, int take = 10);
    Task<IReadOnlyList<AssessmentHistoryEntry>> GetAssessmentHistoryAsync(string childId, string? recommendationSetId = null);
}

public sealed class LogisticsAssessmentRepository : CosmosRepositoryBase<LogisticsAssessmentEntity>, ILogisticsAssessmentRepository
{
    public LogisticsAssessmentRepository(ICosmosRepository cosmos, IConfiguration config)
        : base(cosmos, config, "Cosmos:Containers:Assessments")
    {
    }

    public Task StoreAsync(LogisticsAssessmentEntity entity)
    {
        return CreateAsync(entity);
    }

    public IAsyncEnumerable<LogisticsAssessmentEntity> ListAsync(string childId, int take = 10)
    {
        return ListByPartitionKeyAsync("childId", childId, orderByField: "checkedAt", orderDescending: true, take: take);
    }

    public async Task<IReadOnlyList<AssessmentHistoryEntry>> GetAssessmentHistoryAsync(string childId, string? recommendationSetId = null)
    {
        var results = new List<AssessmentHistoryEntry>();
        QueryDefinition queryDef = recommendationSetId is null
            ? new QueryDefinition("SELECT * FROM c WHERE c.childId = @childId ORDER BY c.checkedAt DESC")
                .WithParameter("@childId", childId)
            : new QueryDefinition("SELECT * FROM c WHERE c.childId = @childId AND c.recommendationSetId = @setId")
                .WithParameter("@childId", childId)
                .WithParameter("@setId", recommendationSetId);

        await foreach (var assessment in QueryAsync(queryDef))
        {
            foreach (var item in assessment.Items)
            {
                results.Add(new AssessmentHistoryEntry(
                    assessment.id,
                    assessment.CheckedAt,
                    assessment.RecommendationSetId,
                    item.RecommendationItemId,
                    item.Feasible,
                    item.Reason,
                    assessment.OverallStatus,
                    assessment.FallbackUsed
                ));
            }
        }
        return results;
    }
}

public sealed record AssessmentHistoryEntry(
    string AssessmentId,
    DateTime CheckedAt,
    string RecommendationSetId,
    string RecommendationItemId,
    bool? Feasible,
    string Reason,
    string OverallStatus,
    bool FallbackUsed
);

public sealed class LogisticsAssessmentEntity : ICosmosEntity
{
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string ChildId { get; set; } = string.Empty;
    public string RecommendationSetId { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public string OverallStatus { get; set; } = "unknown";
    public bool FallbackUsed { get; set; }
    public List<LogisticsAssessmentItemEntity> Items { get; set; } = new();

    [JsonIgnore]
    public string PartitionKeyValue => ChildId;
}

public sealed class LogisticsAssessmentItemEntity
{
    public string RecommendationItemId { get; set; } = string.Empty;
    public bool? Feasible { get; set; }
    public string Reason { get; set; } = string.Empty;
}