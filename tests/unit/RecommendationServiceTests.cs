using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Moq;
using Services;
using Xunit;

namespace UnitTests;

public class RecommendationServiceTests
{
    private readonly Mock<IChildProfileService> _profileMock;
    private readonly Mock<IDrasiViewClient> _drasiMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<RecommendationService>> _loggerMock;

    public RecommendationServiceTests()
    {
        _profileMock = new Mock<IChildProfileService>();
        _drasiMock = new Mock<IDrasiViewClient>();
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<RecommendationService>>();

        // Setup default Drasi mock to return empty (AI not available in test)
        _drasiMock
            .Setup(d => d.GetCurrentResultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JsonNode>());
    }

    [Fact]
    public async Task GetTopNAsync_NaughtyChild_FallbackReturnsCoalAndCharacterBuildingItems()
    {
        // Arrange - AI will fail without config, so fallback should kick in
        _profileMock
            .Setup(p => p.GetChildProfileAsync("naughty-child", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile(
                Id: "naughty-child",
                Name: null,
                Age: null,
                Preferences: null,
                Constraints: null,
                PrivacyFlags: null,
                Status: NiceStatus.Naughty));

        var service = new RecommendationService(
            _profileMock.Object, 
            _drasiMock.Object, 
            _configMock.Object, 
            _loggerMock.Object);
        var result = await service.GetTopNAsync("naughty-child", 3, CancellationToken.None);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Suggestion.Contains("Coal"));
        Assert.All(result, r => Assert.Equal("naughty-child", r.ChildId));
    }

    [Fact]
    public async Task GetTopNAsync_NiceChild_FallbackReturnsFunRewardingItems()
    {
        _profileMock
            .Setup(p => p.GetChildProfileAsync("nice-child", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile(
                Id: "nice-child",
                Name: null,
                Age: null,
                Preferences: null,
                Constraints: null,
                PrivacyFlags: null,
                Status: NiceStatus.Nice));

        var service = new RecommendationService(
            _profileMock.Object, 
            _drasiMock.Object, 
            _configMock.Object, 
            _loggerMock.Object);
        var result = await service.GetTopNAsync("nice-child", 3, CancellationToken.None);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Suggestion.Contains("LEGO") || r.Suggestion.Contains("Nintendo"));
        Assert.All(result, r => Assert.Equal("nice-child", r.ChildId));
        // Nice children shouldn't get coal
        Assert.DoesNotContain(result, r => r.Suggestion.Contains("Coal"));
    }

    [Fact]
    public async Task GetTopNAsync_UnknownStatus_FallbackReturnsDefaultItems()
    {
        _profileMock
            .Setup(p => p.GetChildProfileAsync("new-child", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile(
                Id: "new-child",
                Name: null,
                Age: null,
                Preferences: null,
                Constraints: null,
                PrivacyFlags: null,
                Status: NiceStatus.Unknown));

        var service = new RecommendationService(
            _profileMock.Object, 
            _drasiMock.Object, 
            _configMock.Object, 
            _loggerMock.Object);
        var result = await service.GetTopNAsync("new-child", 3, CancellationToken.None);
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("new-child", r.ChildId));
    }

    [Fact]
    public async Task GetTopNAsync_NoProfile_FallbackReturnsDefaultItems()
    {
        _profileMock
            .Setup(p => p.GetChildProfileAsync("missing-child", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildProfile?)null);

        var service = new RecommendationService(
            _profileMock.Object, 
            _drasiMock.Object, 
            _configMock.Object, 
            _loggerMock.Object);
        var result = await service.GetTopNAsync("missing-child", 3, CancellationToken.None);
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("missing-child", r.ChildId));
    }

    [Fact]
    public async Task GetTopNAsync_NaughtyChild_AllRecommendationsHavePositiveRationales()
    {
        _profileMock
            .Setup(p => p.GetChildProfileAsync("naughty-child", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile(
                Id: "naughty-child",
                Name: null,
                Age: null,
                Preferences: null,
                Constraints: null,
                PrivacyFlags: null,
                Status: NiceStatus.Naughty));

        var service = new RecommendationService(
            _profileMock.Object, 
            _drasiMock.Object, 
            _configMock.Object, 
            _loggerMock.Object);
        var result = await service.GetTopNAsync("naughty-child", 4, CancellationToken.None);

        // Assert - even naughty children get encouraging rationales
        Assert.All(result, r => Assert.False(string.IsNullOrEmpty(r.Rationale)));
    }
}
