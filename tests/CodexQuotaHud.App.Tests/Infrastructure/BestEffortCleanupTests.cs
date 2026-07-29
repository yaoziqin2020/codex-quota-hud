using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class BestEffortCleanupTests
{
    [Fact]
    public async Task RunAsync_AttemptsEveryResourceAndAggregatesFailures()
    {
        var calls = new List<int>();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => BestEffortCleanup.RunAsync(
                () => ThrowingCleanup(1, calls),
                () =>
                {
                    calls.Add(2);
                    return ValueTask.CompletedTask;
                },
                () => ThrowingCleanup(3, calls)));

        Assert.Equal([1, 2, 3], calls);
        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    private static ValueTask ThrowingCleanup(int value, List<int> calls)
    {
        calls.Add(value);
        throw new IOException($"failure {value}");
    }
}
