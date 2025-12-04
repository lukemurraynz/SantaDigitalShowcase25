using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;
using Moq;
using Services;
using Xunit;

namespace UnitTests;

public class ElfRecommendationServiceTests
{
    [Fact]
    public async Task GetRecommendationsForChildAsync_NoBaseRecommendations_ReturnsEmpty()
    {
        var recsMock = new Mock<IRecommendationService>();
        recsMock
            .Setup(r => r.GetTopNAsync("child-1", 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Recommendation>());

        var rationaleMock = new Mock<IAgentRationaleService>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<ElfRecommendationService>>();

        var service = new ElfRecommendationService(recsMock.Object, rationaleMock.Object, loggerMock.Object);

        var result = await service.GetRecommendationsForChildAsync("child-1", CancellationToken.None);

        Assert.Empty(result);
        rationaleMock.Verify(r => r.AddRationaleAsync(It.IsAny<Recommendation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRecommendationsForChildAsync_WithBaseRecommendations_EnrichesWithRationale()
    {
        var baseRecs = new List<Recommendation>
        {
            new(
                Id: "rec-1",
                ChildId: "child-1",
                Suggestion: "toy-1",
                Rationale: "",
                Price: null,
                BudgetFit: "unknown",
                Availability: null),
            new(
                Id: "rec-2",
                ChildId: "child-1",
                Suggestion: "toy-2",
                Rationale: "",
                Price: null,
                BudgetFit: "unknown",
                Availability: null)
        };

        var recsMock = new Mock<IRecommendationService>();
        recsMock
            .Setup(r => r.GetTopNAsync("child-1", 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseRecs);

        var rationaleMock = new Mock<IAgentRationaleService>();
        rationaleMock
            .Setup(r => r.AddRationaleAsync(It.IsAny<Recommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Recommendation rec, CancellationToken _) => rec with { Rationale = "because magic" });

        var loggerMock = new Mock<ILogger<ElfRecommendationService>>();

        var service = new ElfRecommendationService(recsMock.Object, rationaleMock.Object, loggerMock.Object);

        var result = await service.GetRecommendationsForChildAsync("child-1", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("because magic", r.Rationale));
        rationaleMock.Verify(r => r.AddRationaleAsync(It.IsAny<Recommendation>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
