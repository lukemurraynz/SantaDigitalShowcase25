using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace IntegrationTests;

public class AuditRetrievalTests
{
    [Fact]
    public async Task RationaleAudit_ReturnsStoredRationale()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        builder.Services.AddSingleton<IRecommendationRepository>(
            new MockRecommendationRepository());
        
        var app = builder.Build();
        
        app.MapGet("audit/rationale/{childId}", async (string childId, string? setId, 
            IRecommendationRepository repo, CancellationToken ct) =>
        {
            var entries = await repo.GetRationaleAuditAsync(childId, setId);
            return Results.Ok(new { childId, entries, count = entries.Count });
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        // Act
        var response = await client.GetAsync("/audit/rationale/child-001");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RationaleAuditResponse>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.entries);
        Assert.All(result.entries, e => Assert.NotEmpty(e.Rationale));
    }

    [Fact]
    public async Task AssessmentHistoryAudit_ReturnsTimestampedReasons()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        builder.Services.AddSingleton<ILogisticsAssessmentRepository>(
            new MockLogisticsAssessmentRepositoryForAudit());
        
        var app = builder.Build();
        
        app.MapGet("audit/assessments/{childId}", async (string childId, 
            string? recommendationSetId, ILogisticsAssessmentRepository repo, CancellationToken ct) =>
        {
            var entries = await repo.GetAssessmentHistoryAsync(childId, recommendationSetId);
            return Results.Ok(new { childId, entries, count = entries.Count });
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        // Act
        var response = await client.GetAsync("/audit/assessments/child-001");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AssessmentHistoryResponse>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.entries);
        Assert.All(result.entries, e =>
        {
            Assert.NotEmpty(e.Reason);
            Assert.True(e.CheckedAt <= DateTime.UtcNow);
        });
    }
}

public record RationaleAuditResponse(string childId, List<RationaleAuditEntry> entries, int count);
public record AssessmentHistoryResponse(string childId, List<AssessmentHistoryEntry> entries, int count);

public class MockRecommendationRepository : IRecommendationRepository
{
    public Task StoreAsync(RecommendationSetEntity recommendationSet) => Task.CompletedTask;
    
    public Task<RecommendationSetEntity?> GetAsync(string childId, string setId) => 
        Task.FromResult<RecommendationSetEntity?>(null);
    
    public async IAsyncEnumerable<RecommendationSetEntity> ListAsync(string childId, int take = 10)
    {
        yield return new RecommendationSetEntity();
        await Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<RationaleAuditEntry>> GetRationaleAuditAsync(
        string childId, string? setId = null)
    {
        var entries = new List<RationaleAuditEntry>
        {
            new("set-001", DateTime.UtcNow.AddMinutes(-10), "item-001", 
                "Building blocks", "Encourages creativity and spatial awareness", false),
            new("set-001", DateTime.UtcNow.AddMinutes(-10), "item-002", 
                "Art supplies", "Supports artistic expression", false)
        };
        return Task.FromResult<IReadOnlyList<RationaleAuditEntry>>(entries);
    }
}

public class MockLogisticsAssessmentRepositoryForAudit : ILogisticsAssessmentRepository
{
    public Task StoreAsync(LogisticsAssessmentEntity entity) => Task.CompletedTask;
    
    public async IAsyncEnumerable<LogisticsAssessmentEntity> ListAsync(string childId, int take = 10)
    {
        yield return new LogisticsAssessmentEntity();
        await Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<AssessmentHistoryEntry>> GetAssessmentHistoryAsync(
        string childId, string? recommendationSetId = null)
    {
        var entries = new List<AssessmentHistoryEntry>
        {
            new("assess-001", DateTime.UtcNow.AddMinutes(-5), "set-001", "item-001", 
                true, "In stock at main warehouse", "feasible", false),
            new("assess-001", DateTime.UtcNow.AddMinutes(-5), "set-001", "item-002", 
                false, "Out of stock until December", "partial", false)
        };
        return Task.FromResult<IReadOnlyList<AssessmentHistoryEntry>>(entries);
    }
}
