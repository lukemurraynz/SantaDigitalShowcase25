using Drasicrhsit.Infrastructure;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnitTests;

public class SecretProviderTests
{
    private sealed class FakeSecretProvider : ISecretProvider
    {
        public int Calls { get; private set; }
        private readonly string _value;
        public FakeSecretProvider(string value) => _value = value;
        public Task<string?> GetSecretAsync(string name, CancellationToken ct = default)
        {
            Calls++; return Task.FromResult<string?>(_value);
        }
    }

    [Fact]
    public async Task KeyVaultSecretProvider_NullUri_ReturnsNull()
    {
        var inMemory = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>()).Build();
        var provider = new KeyVaultSecretProvider(inMemory, NullLogger<KeyVaultSecretProvider>.Instance);
        var result = await provider.GetSecretAsync("anything");
        Assert.Null(result);
    }

    [Fact]
    public async Task NegativeCache_ReturnsNullTwice()
    {
        var inMemory = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { {"KeyVault:Uri", ""} }).Build();
        var provider = new KeyVaultSecretProvider(inMemory, NullLogger<KeyVaultSecretProvider>.Instance);
        var first = await provider.GetSecretAsync("missing");
        var second = await provider.GetSecretAsync("missing");
        Assert.Null(first);
        Assert.Null(second);
    }
}