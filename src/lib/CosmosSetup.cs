using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace Drasicrhsit.Infrastructure;

public class CosmosSetup
{
    private readonly IConfiguration _config;
    private readonly ISecretProvider _secrets;

    public CosmosSetup(IConfiguration config, ISecretProvider secrets)
    {
        _config = config;
        _secrets = secrets;
    }

    public async Task<CosmosClient?> TryCreateClientAsync(CancellationToken ct = default)
    {
        // NOTE: CosmosSetup is a fallback path for Key Vault secret-based initialization.
        // In production with proper MSI configuration, this fallback should not be invoked.
        // Infrastructure (Bicep) provisions database and containers; this method exists
        // only for local development scenarios where Key Vault stores full connection strings.
        // Consider deprecating this method once all environments use MSI exclusively.

        var endpoint = await _secrets.GetSecretAsync("cosmos-endpoint", ct);
        var key = await _secrets.GetSecretAsync("cosmos-key", ct);
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            return null;

        var conn = $"AccountEndpoint={endpoint};AccountKey={key};";
        // Use same serialization options as main path to ensure ChildId → childId for partition key
        var options = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        };
        var client = new CosmosClient(conn, options);

        // Infrastructure provisioning should handle database/container creation.
        // This CreateIfNotExists pattern is retained only for local dev convenience.
        var database = await _secrets.GetSecretAsync("cosmos-database", ct) ?? "elves_demo";
        var db = await client.CreateDatabaseIfNotExistsAsync(database, cancellationToken: ct);

        var pk = "/childId";
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("events", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("jobs", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("recommendations", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("reports", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("dlq", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("children", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("wishlists", pk), throughput: 400, cancellationToken: ct);
        await db.Database.CreateContainerIfNotExistsAsync(new ContainerProperties("leases", "/id"), throughput: 400, cancellationToken: ct);

        return client;
    }
}
