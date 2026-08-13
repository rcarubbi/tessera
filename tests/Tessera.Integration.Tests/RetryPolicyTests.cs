using Tessera.Infrastructure.Ai;

namespace Tessera.Integration.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task WithRetryAsync_does_not_retry_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        var task = RetryPolicy.WithRetryAsync<string>(async ct2 =>
        {
            attempts++;
            cts.Cancel();
            ct2.ThrowIfCancellationRequested();
            await Task.Delay(Timeout.Infinite, ct2);
            return "unreachable";
        }, maxRetries: 5, ct: cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task WithRetryAsync_retries_transient_timeout_and_eventually_succeeds()
    {
        var attempts = 0;

        var result = await RetryPolicy.WithRetryAsync(ct2 =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new TimeoutException("transient");
            }
            return Task.FromResult("ok");
        }, maxRetries: 5, baseDelay: TimeSpan.FromMilliseconds(1));

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }
}
