using System.Net.Http.Json;
using Drasicrhsit.Infrastructure;
using Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;
using Xunit;
using System.Text.Json.Nodes;

namespace IntegrationTests;

public class JobsApiTests
{
    private sealed class CapturingPublisher : IEventPublisher
    {
        public int PublishCalls { get; private set; }
        public Task PublishWishlistAsync(string childId, string dedupeKey, string schemaVersion, JsonNode? wishlist, CancellationToken ct = default)
        {
            PublishCalls++;
            return Task.CompletedTask;
        }
        public Task PublishRecommendationAsync(string childId, string schemaVersion, JsonNode recommendationSet, CancellationToken ct = default)
        {
            PublishCalls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PostJob_PublishesEvent()
    {
        var publisher = new CapturingPublisher();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddSingleton<IEventPublisher>(publisher);
        var app = builder.Build();

        app.MapPost("/jobs", async (HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<JsonNode>();
            var childId = (string?)body?["childId"] ?? "";
            var dedupeKey = (string?)body?["dedupeKey"] ?? "";
            await publisher.PublishWishlistAsync(childId, dedupeKey, "v1", null, ctx.RequestAborted);
            return Results.Ok();
        });
        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        var response = await client.PostAsJsonAsync("/jobs", new { childId = "c1", dedupeKey = "d1" });
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, publisher.PublishCalls);
    }
}