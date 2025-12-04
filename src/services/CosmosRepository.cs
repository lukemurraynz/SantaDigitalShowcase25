using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Services
{
    public interface ICosmosRepository
    {
        Container GetContainer(string name);
    }

    public sealed class CosmosRepository : ICosmosRepository
    {
        readonly CosmosClient _client;
        readonly string _databaseName;

        public CosmosRepository(CosmosClient client, string databaseName)
        {
            _client = client;
            _databaseName = databaseName;
        }

        public Container GetContainer(string name) => _client.GetContainer(_databaseName, name);
    }
}