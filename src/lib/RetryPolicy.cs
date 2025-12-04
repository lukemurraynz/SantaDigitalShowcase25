using Polly;
using Polly.Contrib.WaitAndRetry;

namespace Drasicrhsit.Infrastructure;

public static class RetryPolicy
{
    // Creates an async retry policy with jitter (3 attempts)
    public static IAsyncPolicy CreateAsyncPolicy(TimeSpan? baseDelay = null, int retries = 3)
    {
        baseDelay ??= TimeSpan.FromMilliseconds(200);
        var delays = Backoff.DecorrelatedJitterBackoffV2(baseDelay.Value, retries);
        return Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(delays);
    }
}
