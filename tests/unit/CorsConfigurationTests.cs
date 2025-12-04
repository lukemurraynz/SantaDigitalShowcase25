using Microsoft.Extensions.Configuration;
using Xunit;

namespace UnitTests;

public class CorsConfigurationTests
{
    [Fact]
    public void CorsAllowedOrigins_FromJsonConfig_ReturnsConfiguredOrigins()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddJsonFile("testappsettings.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Cors:AllowedOrigins:1"] = "http://localhost:3000",
                ["Cors:AllowedOrigins:2"] = "http://localhost:8080"
            })
            .Build();

        // Act
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>();

        // Assert
        Assert.NotNull(origins);
        Assert.Equal(3, origins.Length);
        Assert.Contains("http://localhost:5173", origins);
        Assert.Contains("http://localhost:3000", origins);
        Assert.Contains("http://localhost:8080", origins);
    }

    [Fact]
    public void CorsAllowedOrigins_EmptyConfig_ReturnsEmptyArray()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        // Assert
        Assert.Empty(origins);
    }

    [Fact]
    public void CorsAllowedOrigins_EnvironmentVariableOverride_AddsOrigin()
    {
        // Arrange - Environment variables use __ for section delimiters
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:1"] = "https://custom.azurestaticapps.net"
            })
            .Build();

        // Act
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>();

        // Assert
        Assert.NotNull(origins);
        Assert.Equal(2, origins.Length);
        Assert.Contains("http://localhost:5173", origins);
        Assert.Contains("https://custom.azurestaticapps.net", origins);
    }
}
