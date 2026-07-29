using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class CodexRunningCoordinatorTests
{
    [Fact]
    public async Task TrueFalseTrueWhileFirstTransitionBlockedAppliesLatestWithoutReset()
    {
        var firstStarted = NewSignal();
        var releaseFirst = NewSignal();
        var applied = new List<bool>();
        var resets = 0;
        var first = true;
        await using var coordinator = new CodexRunningCoordinator(
            async (running, cancellationToken) =>
            {
                applied.Add(running);
                if (first)
                {
                    first = false;
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
            },
            () =>
            {
                resets++;
                return Task.CompletedTask;
            });

        _ = coordinator.SetDesiredStateAsync(true);
        await firstStarted.Task;
        var falseCompletion = coordinator.SetDesiredStateAsync(false);
        var latestCompletion = coordinator.SetDesiredStateAsync(true);
        releaseFirst.TrySetResult();
        await Task.WhenAll(falseCompletion, latestCompletion);

        Assert.DoesNotContain(false, applied);
        Assert.Equal(0, resets);
        Assert.True(applied[^1]);
    }

    [Fact]
    public async Task FalseResetFinishesBeforeNewTrueSessionStarts()
    {
        var resetStarted = NewSignal();
        var releaseReset = NewSignal();
        var sequence = new List<string>();
        await using var coordinator = new CodexRunningCoordinator(
            (running, _) =>
            {
                sequence.Add($"set:{running}");
                return Task.CompletedTask;
            },
            async () =>
            {
                sequence.Add("reset:start");
                resetStarted.TrySetResult();
                await releaseReset.Task;
                sequence.Add("reset:end");
            });

        var stop = coordinator.SetDesiredStateAsync(false);
        await resetStarted.Task;
        var start = coordinator.SetDesiredStateAsync(true);
        releaseReset.TrySetResult();
        await Task.WhenAll(stop, start);

        Assert.Equal(
            ["set:False", "reset:start", "reset:end", "set:True"],
            sequence);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
