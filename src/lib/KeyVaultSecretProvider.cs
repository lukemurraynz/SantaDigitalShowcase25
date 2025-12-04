using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System.Collections.Concurrent;
using Drasicrhsit.Infrastructure;

namespace Drasicrhsit.Infrastructure;

public class KeyVaultSecretProvider : ISecretProvider
{
    private static readonly Action<ILogger, string, Exception?> _logInitializationWarning =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(KeyVaultSecretProvider)),
            "Key Vault URI is not configured. Secrets will not be loaded from Key Vault. Expected configuration at {ConfigKey}.");

    private static readonly Action<ILogger, string, Exception?> _logInitializationInfo =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, nameof(KeyVaultSecretProvider)),
            "Key Vault secret provider initialized for vault {VaultUri}.");

    private static readonly Action<ILogger, string, Exception?> _logInitializationError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(KeyVaultSecretProvider)),
            "Failed to initialize Key Vault secret provider for vault {VaultUri}. Secrets will not be available.");

    private readonly SecretClient? _client;
    private readonly ConcurrentDictionary<string, string?> _cache = new();

    public KeyVaultSecretProvider(IConfiguration config, ILogger<KeyVaultSecretProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        var uri = ConfigurationHelper.GetOptionalValue(config, "KeyVault:Uri", "KEYVAULT_URI");
        if (string.IsNullOrWhiteSpace(uri))
        {
            _logInitializationWarning(logger, "KeyVault:Uri", null);
            return;
        }

        try
        {
            _client = new SecretClient(new Uri(uri), new DefaultAzureCredential());
            _logInitializationInfo(logger, uri, null);
        }
        catch (Exception ex)
        {
            _logInitializationError(logger, uri, ex);
        }
    }

    public async Task<string?> GetSecretAsync(string name, CancellationToken ct = default)
    {
        if (_client is null)
            return null;
        if (_cache.TryGetValue(name, out var cached))
            return cached;
        try
        {
            var secret = await _client.GetSecretAsync(name, cancellationToken: ct);
            var value = secret.Value.Value;
            _cache[name] = value;
            return value;
        }
        catch
        {
            _cache[name] = null; // negative cache
            return null;
        }
    }
}