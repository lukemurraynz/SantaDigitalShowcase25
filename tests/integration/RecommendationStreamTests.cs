using Services;
using Xunit;

namespace Tests.Integration;

public class RecommendationStreamTests
{
    [Fact]
    public async Task RecommendationEventsFlow()
    {
        var broadcaster = new InMemoryStreamBroadcaster();
        var reader = broadcaster.Subscribe("child-x");
        await broadcaster.PublishAsync("child-x", "recommendation-update", new { id = "r-set" });
        Assert.True(reader.TryRead(out var ev));
        Assert.Equal("recommendation-update", ev.Type);
    }
}
