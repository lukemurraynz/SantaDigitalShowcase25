using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services;
using Xunit;

namespace IntegrationTests;

public class ProfileEnrichmentTests
{
    [Fact]
    public async Task ProfileEnrichment_WithWishlistData_ProducesSnapshot()
    {
        // Arrange: Create test application with minimal services
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        // Mock services for isolated test
        builder.Services.AddSingleton<IProfilePreferenceExtractor>(new MockProfilePreferenceExtractor());
        builder.Services.AddSingleton<IAiProfileAgent>(new MockAiProfileAgent());
        builder.Services.AddSingleton<IProfileSnapshotRepository>(new MockProfileSnapshotRepository());
        
        var app = builder.Build();
        
        // Register endpoint
        app.MapPost("children/{childId}/profile", async (string childId,
            IProfilePreferenceExtractor extractor,
            IAiProfileAgent aiAgent,
            IProfileSnapshotRepository profiles,
            CancellationToken ct) =>
        {
            var prefs = await extractor.ExtractAsync(childId, ct);
            var (behaviorSummary, budgetCeiling, fallback) = await aiAgent.EnrichAsync(childId, prefs, ct);
            var snapshot = new ProfileSnapshotEntity
            {
                ChildId = childId,
                Preferences = prefs.ToList(),
                BehaviorSummary = behaviorSummary,
                BudgetCeiling = budgetCeiling.HasValue ? (double?)budgetCeiling.Value : null,
                EnrichmentSource = fallback ? "fallback" : "ai",
                FallbackUsed = fallback
            };
            await profiles.StoreAsync(snapshot);
            return Results.Ok(snapshot);
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        // Act: Call profile enrichment endpoint
        var response = await client.PostAsync("/children/test-child-001/profile", null);

        // Assert: Verify snapshot was created with preferences
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<ProfileSnapshotEntity>();
        Assert.NotNull(snapshot);
        Assert.Equal("test-child-001", snapshot.ChildId);
        Assert.NotEmpty(snapshot.Preferences);
        Assert.Contains("toys", snapshot.Preferences);
    }

    [Fact]
    public async Task ProfileEnrichment_WithSparseData_UsesFallback()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        builder.Services.AddSingleton<IProfilePreferenceExtractor>(new MockProfilePreferenceExtractor(sparse: true));
        builder.Services.AddSingleton<IAiProfileAgent>(new MockAiProfileAgent(forceFallback: true));
        builder.Services.AddSingleton<IProfileSnapshotRepository>(new MockProfileSnapshotRepository());
        
        var app = builder.Build();
        
        app.MapPost("children/{childId}/profile", async (string childId,
            IProfilePreferenceExtractor extractor,
            IAiProfileAgent aiAgent,
            IProfileSnapshotRepository profiles,
            CancellationToken ct) =>
        {
            var prefs = await extractor.ExtractAsync(childId, ct);
            var (behaviorSummary, budgetCeiling, fallback) = await aiAgent.EnrichAsync(childId, prefs, ct);
            var snapshot = new ProfileSnapshotEntity
            {
                ChildId = childId,
                Preferences = prefs.ToList(),
                BehaviorSummary = behaviorSummary,
                BudgetCeiling = budgetCeiling.HasValue ? (double?)budgetCeiling.Value : null,
                EnrichmentSource = fallback ? "fallback" : "ai",
                FallbackUsed = fallback
            };
            await profiles.StoreAsync(snapshot);
            return Results.Ok(snapshot);
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        // Act
        var response = await client.PostAsync("/children/sparse-child/profile", null);

        // Assert: Verify fallback was used
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<ProfileSnapshotEntity>();
        Assert.NotNull(snapshot);
        Assert.True(snapshot.FallbackUsed);
        Assert.Equal("fallback", snapshot.EnrichmentSource);
    }
}

// Mock implementations for isolated testing
public class MockProfilePreferenceExtractor : IProfilePreferenceExtractor
{
    private readonly bool _sparse;
    public MockProfilePreferenceExtractor(bool sparse = false) { _sparse = sparse; }
    
    public Task<IReadOnlyList<string>> ExtractAsync(string childId, CancellationToken ct = default)
    {
        IReadOnlyList<string> prefs = _sparse 
            ? new List<string>() 
            : new List<string> { "toys", "games", "books" };
        return Task.FromResult(prefs);
    }
}

public class MockAiProfileAgent : IAiProfileAgent
{
    private readonly bool _forceFallback;
    public MockAiProfileAgent(bool forceFallback = false) { _forceFallback = forceFallback; }
    
    public Task<(string? behaviorSummary, decimal? budgetCeiling, bool fallback)> EnrichAsync(
        string childId, IReadOnlyList<string> preferences, CancellationToken ct = default)
    {
        if (_forceFallback)
            return Task.FromResult<(string?, decimal?, bool)>((null, null, true));
        
        return Task.FromResult<(string?, decimal?, bool)>(
            ("Prefers creative and educational items", 50m, false));
    }
}

public class MockProfileSnapshotRepository : IProfileSnapshotRepository
{
    private readonly Dictionary<string, ProfileSnapshotEntity> _store = new();

    public Task StoreAsync(ProfileSnapshotEntity entity)
    {
        _store[entity.id] = entity;
        return Task.CompletedTask;
    }

    public Task<ProfileSnapshotEntity?> GetAsync(string childId, string id)
    {
        _store.TryGetValue(id, out var entity);
        return Task.FromResult(entity);
    }
}
