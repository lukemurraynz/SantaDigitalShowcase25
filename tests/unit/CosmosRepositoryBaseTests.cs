using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Moq;
using Services;
using Xunit;

namespace UnitTests;

public class CosmosRepositoryBaseTests
{
    private readonly Mock<ICosmosRepository> _cosmosMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<IConfiguration> _configMock;

    public CosmosRepositoryBaseTests()
    {
        _cosmosMock = new Mock<ICosmosRepository>();
        _containerMock = new Mock<Container>();
        _configMock = new Mock<IConfiguration>();

        _configMock.Setup(c => c["TestContainer"]).Returns("test-container");
        _cosmosMock.Setup(c => c.GetContainer("test-container")).Returns(_containerMock.Object);
    }

    [Fact]
    public void Constructor_ValidParameters_InitializesCorrectly()
    {
        // Act
        var repository = new TestRepository(_cosmosMock.Object, _configMock.Object);

        // Assert
        Assert.NotNull(repository);
        _cosmosMock.Verify(c => c.GetContainer("test-container"), Times.Once);
    }

    [Fact]
    public void Constructor_NullCosmos_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestRepository(null!, _configMock.Object));
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestRepository(_cosmosMock.Object, null!));
    }

    // Test entity that implements ICosmosEntity
    private sealed class TestEntity : ICosmosEntity
    {
        public string id { get; set; } = Guid.NewGuid().ToString();
        public string ChildId { get; set; } = string.Empty;
        public string PartitionKeyValue => ChildId;
    }

    // Concrete implementation for testing
    private sealed class TestRepository : CosmosRepositoryBase<TestEntity>
    {
        public TestRepository(ICosmosRepository cosmos, IConfiguration config)
            : base(cosmos, config, "TestContainer")
        {
        }

        // Expose protected methods for testing
        public new Task CreateAsync(TestEntity entity) => base.CreateAsync(entity);
        public new Task UpsertAsync(TestEntity entity) => base.UpsertAsync(entity);
        public new Task<TestEntity?> GetByIdAsync(string id, string partitionKey) => base.GetByIdAsync(id, partitionKey);
        public new IAsyncEnumerable<TestEntity> ListByPartitionKeyAsync(
            string partitionKeyName,
            string partitionKeyValue,
            string? orderByField = null,
            bool orderDescending = true,
            int take = 0) => base.ListByPartitionKeyAsync(partitionKeyName, partitionKeyValue, orderByField, orderDescending, take);
    }
}
