namespace Drasicrhsit.Infrastructure;

/// <summary>
/// Centralized configuration resolution with consistent fallback strategy.
/// Eliminates duplication of config["Key"] ?? Environment.GetEnvironmentVariable("KEY") pattern.
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// Get configuration value with fallback priority:
    /// 1. IConfiguration key (appsettings.json, user secrets, etc.)
    /// 2. Environment variable
    /// 3. Default value
    /// </summary>
    /// <param name="config">Configuration provider</param>
    /// <param name="configKey">Configuration key (e.g., "Drasi:ViewServiceBaseUrl")</param>
    /// <param name="envVarName">Environment variable name (e.g., "DRASI_VIEW_SERVICE_BASE_URL")</param>
    /// <param name="defaultValue">Default value if neither config nor env var found</param>
    /// <returns>Configuration value</returns>
    /// <exception cref="InvalidOperationException">Thrown when value not found and no default provided</exception>
    public static string GetValue(
        IConfiguration config,
        string configKey,
        string? envVarName = null,
        string? defaultValue = null)
    {
        // 1. Check IConfiguration (appsettings.json, user secrets, command line args, etc.)
        var configValue = config[configKey];
        if (!string.IsNullOrWhiteSpace(configValue))
            return configValue;

        // 2. Check environment variable (if specified)
        if (!string.IsNullOrWhiteSpace(envVarName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envValue))
                return envValue;
        }

        // 3. Return default or throw
        return defaultValue ?? throw new InvalidOperationException(
            $"Configuration '{configKey}' not found. " +
            $"Checked: IConfiguration['{configKey}']" +
            (envVarName != null ? $", Environment['{envVarName}']" : "") +
            ". Please set one of these values.");
    }

    /// <summary>
    /// Get required configuration value (throws if not found)
    /// </summary>
    public static string GetRequiredValue(
        IConfiguration config,
        string configKey,
        string? envVarName = null) =>
        GetValue(config, configKey, envVarName, defaultValue: null);

    /// <summary>
    /// Get optional configuration value (returns null if not found)
    /// </summary>
    public static string? GetOptionalValue(
        IConfiguration config,
        string configKey,
        string? envVarName = null)
    {
        try
        {
            return GetValue(config, configKey, envVarName, defaultValue: null);
        }
        catch
        {
            return null;
        }
    }
}
