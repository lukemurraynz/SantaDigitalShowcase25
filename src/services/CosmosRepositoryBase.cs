using Microsoft.Azure.Cosmos;
using System.Net;

namespace Services;

/// <summary>
/// Interface for Cosmos DB entities that have an id and partition key.
/// </summary>
public interface ICosmosEntity
{
    /// <summary>
    /// The document id (Cosmos DB requires lowercase 'id').
    /// </summary>
    string id { get; set; }

    /// <summary>
    /// The partition key value (typically ChildId).
    /// </summary>
    string PartitionKeyValue { get; }
}

/// <summary>
/// Abstract base class for Cosmos DB repositories providing common CRUD operations.
/// Reduces duplication across repository implementations.
/// </summary>
/// <typeparam name="T">The entity type that implements ICosmosEntity.</typeparam>
public abstract class CosmosRepositoryBase<T> where T : class, ICosmosEntity
{
    /// <summary>
    /// The underlying Cosmos DB container.
    /// </summary>
    protected readonly Container Container;

    /// <summary>
    /// Initializes the repository with the specified container.
    /// </summary>
    /// <param name="cosmos">The Cosmos repository for container access.</param>
    /// <param name="config">Configuration containing container name.</param>
    /// <param name="containerConfigKey">Configuration key for the container name.</param>
    protected CosmosRepositoryBase(ICosmosRepository cosmos, IConfiguration config, string containerConfigKey)
    {
        ArgumentNullException.ThrowIfNull(cosmos);
        ArgumentNullException.ThrowIfNull(config);
        Container = cosmos.GetContainer(config[containerConfigKey]!);
    }

    /// <summary>
    /// Creates a new item in the container.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    protected async Task CreateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Container.CreateItemAsync(entity, new PartitionKey(entity.PartitionKeyValue));
    }

    /// <summary>
    /// Upserts an item in the container (creates or updates).
    /// </summary>
    /// <param name="entity">The entity to upsert.</param>
    protected async Task UpsertAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Container.UpsertItemAsync(entity, new PartitionKey(entity.PartitionKeyValue));
    }

    /// <summary>
    /// Retrieves an item by its id and partition key.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="partitionKey">The partition key value.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    protected async Task<T?> GetByIdAsync(string id, string partitionKey)
    {
        try
        {
            ItemResponse<T> response = await Container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists items by partition key with optional ordering and limit.
    /// </summary>
    /// <param name="partitionKeyName">The name of the partition key property in the query (e.g., "childId").</param>
    /// <param name="partitionKeyValue">The partition key value to filter by.</param>
    /// <param name="orderByField">Optional field name to order by.</param>
    /// <param name="orderDescending">Whether to order descending (default true).</param>
    /// <param name="take">Maximum number of items to return (0 for unlimited).</param>
    /// <returns>An async enumerable of matching entities.</returns>
    protected async IAsyncEnumerable<T> ListByPartitionKeyAsync(
        string partitionKeyName,
        string partitionKeyValue,
        string? orderByField = null,
        bool orderDescending = true,
        int take = 0)
    {
        string query = $"SELECT * FROM c WHERE c.{partitionKeyName} = @partitionKey";
        if (!string.IsNullOrEmpty(orderByField))
        {
            query += $" ORDER BY c.{orderByField} {(orderDescending ? "DESC" : "ASC")}";
        }

        QueryDefinition queryDef = new QueryDefinition(query)
            .WithParameter("@partitionKey", partitionKeyValue);

        using FeedIterator<T> iterator = Container.GetItemQueryIterator<T>(queryDef);
        int count = 0;

        while (iterator.HasMoreResults && (take == 0 || count < take))
        {
            foreach (T doc in await iterator.ReadNextAsync())
            {
                yield return doc;
                count++;
                if (take > 0 && count >= take)
                {
                    yield break;
                }
            }
        }
    }

    /// <summary>
    /// Executes a custom query and returns results as an async enumerable.
    /// </summary>
    /// <param name="queryDefinition">The query to execute.</param>
    /// <returns>An async enumerable of matching entities.</returns>
    protected async IAsyncEnumerable<T> QueryAsync(QueryDefinition queryDefinition)
    {
        using FeedIterator<T> iterator = Container.GetItemQueryIterator<T>(queryDefinition);
        while (iterator.HasMoreResults)
        {
            foreach (T doc in await iterator.ReadNextAsync())
            {
                yield return doc;
            }
        }
    }
}
