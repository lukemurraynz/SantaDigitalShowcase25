using Services;
using Xunit;

namespace Tests.Integration;

public class WishlistStreamTests
{
    [Fact]
    public async Task BroadcastPublishesEvents()
    {
        var broadcaster = new InMemoryStreamBroadcaster();
        var reader = broadcaster.Subscribe("child-1");
        await broadcaster.PublishAsync("child-1", "wishlist-item", new { id = "w1" });
        await broadcaster.PublishAsync("child-1", "recommendation-update", new { id = "r1" });
        int count = 0;
        while (reader.TryRead(out var ev)) { count++; }
        Assert.Equal(2, count);
    }
}
