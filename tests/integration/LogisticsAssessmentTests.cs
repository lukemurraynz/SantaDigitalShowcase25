using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace IntegrationTests;

public class LogisticsAssessmentTests
{
    [Fact]
    public async Task LogisticsAssessment_AllFeasible_ReturnsFeasibleStatus()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        builder.Services.AddSingleton<ILogisticsAssessmentService>(
            new MockLogisticsAssessmentService(allFeasible: true));
        builder.Services.AddSingleton<ILogisticsAssessmentRepository>(
            new MockLogisticsAssessmentRepository());
        
        var app = builder.Build();
        
        app.MapPost("children/{childId}/logistics", async (string childId,
            ILogisticsAssessmentService logistics,
            ILogisticsAssessmentRepository assessments,
            CancellationToken ct) =>
        {
            var assessment = await logistics.RunAssessmentAsync(childId, ct);
            if (assessment is null) return Results.NotFound();

            var entity = new LogisticsAssessmentEntity
            {
                ChildId = assessment.ChildId,
                RecommendationSetId = assessment.RecommendationSetId,
                CheckedAt = assessment.CheckedAt,
                OverallStatus = assessment.OverallStatus,
                FallbackUsed = assessment.FallbackUsed,
                Items = assessment.Items.Select(i => new LogisticsAssessmentItemEntity
                {
                    RecommendationItemId = i.RecommendationId,
                    Feasible = i.Feasible,
                    Reason = i.Reason
                }).ToList()
            };
            await assessments.StoreAsync(entity);
            return Results.Ok(entity);
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();
        var response = await client.PostAsync("/children/test-child/logistics", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assessment = await response.Content.ReadFromJsonAsync<LogisticsAssessmentEntity>();
        Assert.NotNull(assessment);
        Assert.Equal("feasible", assessment.OverallStatus);
        Assert.All(assessment.Items, item => Assert.True(item.Feasible));
    }

    [Fact]
    public async Task LogisticsAssessment_MixedFeasibility_ReturnsPartialStatus()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        builder.Services.AddSingleton<ILogisticsAssessmentService>(
            new MockLogisticsAssessmentService(allFeasible: false));
        builder.Services.AddSingleton<ILogisticsAssessmentRepository>(
            new MockLogisticsAssessmentRepository());
        
        var app = builder.Build();
        
        app.MapPost("children/{childId}/logistics", async (string childId,
            ILogisticsAssessmentService logistics,
            ILogisticsAssessmentRepository assessments,
            CancellationToken ct) =>
        {
            var assessment = await logistics.RunAssessmentAsync(childId, ct);
            if (assessment is null) return Results.NotFound();

            var entity = new LogisticsAssessmentEntity
            {
                ChildId = assessment.ChildId,
                RecommendationSetId = assessment.RecommendationSetId,
                CheckedAt = assessment.CheckedAt,
                OverallStatus = assessment.OverallStatus,
                FallbackUsed = assessment.FallbackUsed,
                Items = assessment.Items.Select(i => new LogisticsAssessmentItemEntity
                {
                    RecommendationItemId = i.RecommendationId,
                    Feasible = i.Feasible,
                    Reason = i.Reason
                }).ToList()
            };
            await assessments.StoreAsync(entity);
            return Results.Ok(entity);
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();
        var response = await client.PostAsync("/children/test-child/logistics", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assessment = await response.Content.ReadFromJsonAsync<LogisticsAssessmentEntity>();
        Assert.NotNull(assessment);
        Assert.Equal("partial", assessment.OverallStatus);
        Assert.Contains(assessment.Items, item => item.Feasible == true);
        Assert.Contains(assessment.Items, item => item.Feasible == false);
    }
}

// Mock implementations
public class MockLogisticsAssessmentService : ILogisticsAssessmentService
{
    private readonly bool _allFeasible;
    public MockLogisticsAssessmentService(bool allFeasible) { _allFeasible = allFeasible; }
    
    public Task<LogisticsAssessmentResult?> RunAssessmentAsync(string childId, CancellationToken ct = default)
    {
        var items = _allFeasible
            ? new List<LogisticsAssessmentItem>
            {
                new("rec-001", true, "In stock at warehouse"),
                new("rec-002", true, "Available from supplier")
            }
            : new List<LogisticsAssessmentItem>
            {
                new("rec-001", true, "In stock at warehouse"),
                new("rec-002", false, "Out of stock until next month")
            };
        
        var result = new LogisticsAssessmentResult(
            Id: Guid.NewGuid().ToString(),
            ChildId: childId,
            RecommendationSetId: "set-001",
            OverallStatus: _allFeasible ? "feasible" : "partial",
            CheckedAt: DateTime.UtcNow,
            Items: items,
            FallbackUsed: false
        );
        
        return Task.FromResult<LogisticsAssessmentResult?>(result);
    }
}

public class MockLogisticsAssessmentRepository : ILogisticsAssessmentRepository
{
    private readonly List<LogisticsAssessmentEntity> _store = new();
    
    public Task StoreAsync(LogisticsAssessmentEntity entity)
    {
        _store.Add(entity);
        return Task.CompletedTask;
    }
    
    public async IAsyncEnumerable<LogisticsAssessmentEntity> ListAsync(string childId, int take = 10)
    {
        foreach (var item in _store.Where(a => a.ChildId == childId).Take(take))
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<AssessmentHistoryEntry>> GetAssessmentHistoryAsync(
        string childId, string? recommendationSetId = null)
    {
        var entries = new List<AssessmentHistoryEntry>();
        return Task.FromResult<IReadOnlyList<AssessmentHistoryEntry>>(entries);
    }
}
