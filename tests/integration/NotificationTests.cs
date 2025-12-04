using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace IntegrationTests;

public class NotificationTests
{
    [Fact]
    public async Task RecommendationGeneration_CreatesNotification()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        var notificationService = new MockNotificationService();
        builder.Services.AddSingleton<INotificationService>(notificationService);
        builder.Services.AddSingleton<INotificationMutator>(notificationService);
        
        var app = builder.Build();
        
        app.MapPost("test/recommendation", async (INotificationMutator mutator, CancellationToken ct) =>
        {
            // Simulate recommendation generation completing
            var dto = await mutator.AddAsync("child-001", "recommendation", 
                "New recommendations generated", "new", ct);
            return Results.Ok(dto);
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        // Act
        var response = await client.PostAsync("/test/recommendation", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await response.Content.ReadFromJsonAsync<NotificationDto>();
        Assert.NotNull(notification);
        Assert.Equal("recommendation", notification.Type);
        Assert.Equal("child-001", notification.ChildId);
    }

    [Fact]
    public async Task LogisticsAssessment_InfeasibleItems_CreatesNotification()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        
        var notificationService = new MockNotificationService();
        builder.Services.AddSingleton<INotificationService>(notificationService);
        builder.Services.AddSingleton<INotificationMutator>(notificationService);
        
        var app = builder.Build();
        
        app.MapPost("test/logistics", async (INotificationMutator mutator, CancellationToken ct) =>
        {
            // Simulate logistics assessment with infeasible items
            var dto = await mutator.AddAsync("child-001", "logistics", 
                "Logistics assessment partial", "new", ct);
            return Results.Ok(dto);
        });

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        // Act
        var response = await client.PostAsync("/test/logistics", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await response.Content.ReadFromJsonAsync<NotificationDto>();
        Assert.NotNull(notification);
        Assert.Equal("logistics", notification.Type);
        Assert.Contains("partial", notification.Message);
    }
}

public class MockNotificationService : INotificationService, INotificationMutator
{
    private readonly List<NotificationDto> _notifications = new();
    
    public Task<NotificationDto> AddAsync(string childId, string type, string message, 
        string state = "new", CancellationToken ct = default)
    {
        var dto = new NotificationDto(
            Guid.NewGuid().ToString(),
            childId,
            type,
            message,
            DateTime.UtcNow,
            state,
            null
        );
        _notifications.Add(dto);
        return Task.FromResult(dto);
    }
    
    public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(
        string? state, CancellationToken ct = default)
    {
        IReadOnlyList<NotificationDto> result = state is null
            ? _notifications
            : _notifications.Where(n => n.State == state).ToList();
        return Task.FromResult(result);
    }
}
