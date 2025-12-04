using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IChildRepository
{
    Task<bool> AddAsync(string childId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string childId, CancellationToken ct = default);
}

/// <summary>
/// In-memory child registry used by the demo APIs.
/// </summary>
public class ChildRepository : IChildRepository
{
    private readonly HashSet<string> _children = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> AddAsync(string childId, CancellationToken ct = default)
    {
        var added = _children.Add(childId);
        return Task.FromResult(added);
    }

    public Task<bool> ExistsAsync(string childId, CancellationToken ct = default)
        => Task.FromResult(_children.Contains(childId));
}

public static class ChildrenApi
{
    public static IEndpointRouteBuilder MapChildrenApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("children", async (HttpRequest req, IChildRepository repo, CancellationToken ct) =>
        {
            var node = await req.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(cancellationToken: ct);
            var childId = (string?)node?["childId"];
            if (string.IsNullOrWhiteSpace(childId))
                return Results.BadRequest(new { error = "childId required" });
            await repo.AddAsync(childId!, ct);
            // Attempt to persist child minimal doc in Cosmos if available
            try
            {
                var cosmosSetup = req.HttpContext.RequestServices.GetRequiredService<Drasicrhsit.Infrastructure.CosmosSetup>();
                var client = await cosmosSetup.TryCreateClientAsync(ct);
                if (client is not null)
                {
                    var dbName = await req.HttpContext.RequestServices.GetRequiredService<ISecretProvider>().GetSecretAsync("cosmos-database", ct) ?? "elves_demo";
                    var container = client.GetContainer(dbName, "children");
                    var payload = new { id = Guid.NewGuid().ToString("n"), childId, type = "child", createdAt = DateTime.UtcNow }; // id distinct from pk
                    await container.UpsertItemAsync(payload, new Microsoft.Azure.Cosmos.PartitionKey(childId!), cancellationToken: ct);
                }
            }
            catch { /* swallow for demo */ }
            return Results.Created($"/children/{childId}", new { childId });
        })
        .WithTags("Frontend", "Children");

        app.MapGet("children/{childId}", async (string childId, IChildRepository repo, CancellationToken ct) =>
        {
            var exists = await repo.ExistsAsync(childId, ct);
            return exists ? Results.Ok(new { childId }) : Results.NotFound();
        })
        .WithTags("Frontend", "Children");

        // Wishlist (toy idea) submission -> Cosmos + EventHub publish
        app.MapPost("children/{childId}/wishlist", async (string childId, HttpRequest req, IEventPublisher publisher, ISecretProvider secrets, CancellationToken ct) =>
        {
            var body = await req.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(cancellationToken: ct);
            if (body is null)
                return Results.BadRequest(new { error = "invalid json" });
            string? toyName = (string?)body["toyName"]; // required
            if (string.IsNullOrWhiteSpace(toyName))
                return Results.BadRequest(new { error = "toyName required" });
            string? category = (string?)body["category"];
            string? notes = (string?)body["notes"];
            decimal? budgetLimit = (decimal?)body["budgetLimit"];
            var dedupeKey = Guid.NewGuid().ToString("n");
            var schemaVersion = "1.0";

            // Persist to Cosmos wishlists container if available
            try
            {
                var cosmosSetup = req.HttpContext.RequestServices.GetRequiredService<Drasicrhsit.Infrastructure.CosmosSetup>();
                var client = await cosmosSetup.TryCreateClientAsync(ct);
                if (client is not null)
                {
                    var dbName = await secrets.GetSecretAsync("cosmos-database", ct) ?? "elves_demo";
                    var container = client.GetContainer(dbName, "wishlists");
                    var doc = new
                    {
                        id = dedupeKey,
                        childId,
                        toyName,
                        category,
                        notes,
                        budgetLimit,
                        createdAt = DateTime.UtcNow,
                        type = "wishlist-item"
                    };
                    await container.UpsertItemAsync(doc, new Microsoft.Azure.Cosmos.PartitionKey(childId), cancellationToken: ct);
                }
            }
            catch { }

            // Publish to Event Hub for Drasi graph ingestion
            System.Text.Json.Nodes.JsonNode wishlistNode = System.Text.Json.Nodes.JsonNode.Parse($"{System.Text.Json.JsonSerializer.Serialize(new { toyName, category, notes, budgetLimit })}")!;
            await publisher.PublishWishlistAsync(childId, dedupeKey, schemaVersion, wishlistNode, ct);
            return Results.Accepted($"/api/v1/children/{childId}/wishlist-items/{dedupeKey}", new { childId, dedupeKey, status = "accepted" });
        })
        .WithTags("Frontend", "Wishlist");

        return app;
    }
}
