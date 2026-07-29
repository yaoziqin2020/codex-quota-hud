using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;
using CodexQuotaHud.Core.Refresh;

namespace CodexQuotaHud.Core.Tests.Refresh;

public sealed class QuotaRefreshServiceTests
{
    private static readonly DateTimeOffset StartTime =
        DateTimeOffset.Parse("2026-07-29T00:00:00Z");

    [Fact]
    public async Task CodexStart_TriggersImmediateRefresh()
    {
        var snapshot = Snapshot(StartTime, remainingPercent: 73);
        var client = new FakeQuotaClient(_ => Task.FromResult(snapshot));
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        var states = new List<QuotaRefreshState>();
        service.StateChanged += states.Add;

        await service.SetCodexRunningAsync(true, CancellationToken.None);

        Assert.Equal(1, client.CallCount);
        var state = states[^1];
        Assert.True(state.IsCodexRunning);
        Assert.False(state.IsRefreshing);
        Assert.Equal(QuotaDisplayMode.Single, state.Display.Mode);
        Assert.Equal(73, state.Display.Primary!.RemainingPercent);
        Assert.False(state.Display.IsStale);
        Assert.Null(state.LastError);

        await service.SetCodexRunningAsync(false, CancellationToken.None);
    }

    [Fact]
    public async Task CodexStop_HidesAndStopsPolling()
    {
        var client = new FakeQuotaClient(
            _ => Task.FromResult(Snapshot(StartTime, remainingPercent: 73)));
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        var states = new List<QuotaRefreshState>();
        service.StateChanged += states.Add;
        await service.SetCodexRunningAsync(true, CancellationToken.None);
        await WaitUntilAsync(() => clock.PendingDelayCount == 1);

        await service.SetCodexRunningAsync(false, CancellationToken.None);

        var stopped = states[^1];
        Assert.False(stopped.IsCodexRunning);
        Assert.False(stopped.IsRefreshing);
        Assert.Equal(QuotaDisplayMode.Hidden, stopped.Display.Mode);
        Assert.Equal(0, clock.PendingDelayCount);

        clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Yield();
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task CodexStop_DuringRefreshDoesNotRepublishData()
    {
        var attempt = 0;
        var initial = Snapshot(StartTime, remainingPercent: 73);
        var completedAfterStop = Snapshot(StartTime, remainingPercent: 42);
        var blockedRead = new TaskCompletionSource<QuotaSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(_ =>
            Interlocked.Increment(ref attempt) == 1
                ? Task.FromResult(initial)
                : blockedRead.Task);
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        var states = new List<QuotaRefreshState>();
        service.StateChanged += states.Add;
        await service.SetCodexRunningAsync(true, CancellationToken.None);

        var refresh = service.RefreshNowAsync(
            onlyIfStale: false,
            CancellationToken.None);
        await client.WaitForCallCountAsync(2);
        await service.SetCodexRunningAsync(false, CancellationToken.None);
        blockedRead.SetResult(completedAfterStop);
        await refresh;

        var final = states[^1];
        Assert.False(final.IsCodexRunning);
        Assert.Equal(QuotaDisplayMode.Hidden, final.Display.Mode);
    }

    [Fact]
    public async Task CodexStop_DuringInitialRefreshDoesNotRestartPolling()
    {
        var snapshot = Snapshot(StartTime, remainingPercent: 73);
        var blockedRead = new TaskCompletionSource<QuotaSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(_ => blockedRead.Task);
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        var states = new List<QuotaRefreshState>();
        service.StateChanged += states.Add;

        var start = service.SetCodexRunningAsync(true, CancellationToken.None);
        await client.WaitForCallCountAsync(1);
        await service.SetCodexRunningAsync(false, CancellationToken.None);
        blockedRead.SetResult(snapshot);
        await start;

        var final = states[^1];
        Assert.False(final.IsCodexRunning);
        Assert.Equal(QuotaDisplayMode.Hidden, final.Display.Mode);
        Assert.Equal(0, clock.PendingDelayCount);
    }

    [Fact]
    public async Task PeriodicRefresh_RunsEverySixtySeconds()
    {
        var client = new FakeQuotaClient(
            _ => Task.FromResult(Snapshot(StartTime, remainingPercent: 73)));
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);

        await service.SetCodexRunningAsync(true, CancellationToken.None);
        await WaitUntilAsync(() => clock.PendingDelayCount == 1);

        clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Yield();
        Assert.Equal(1, client.CallCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        await client.WaitForCallCountAsync(2);
        await WaitUntilAsync(() => clock.PendingDelayCount == 1);

        clock.Advance(TimeSpan.FromSeconds(60));
        await client.WaitForCallCountAsync(3);

        Assert.Equal(3, client.CallCount);
        await service.SetCodexRunningAsync(false, CancellationToken.None);
    }

    [Fact]
    public async Task HoverRefresh_SkipsFreshData()
    {
        var client = new FakeQuotaClient(
            _ => Task.FromResult(Snapshot(StartTime, remainingPercent: 73)));
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        await service.SetCodexRunningAsync(true, CancellationToken.None);

        await service.RefreshNowAsync(
            onlyIfStale: true,
            CancellationToken.None);

        Assert.Equal(1, client.CallCount);
        await service.SetCodexRunningAsync(false, CancellationToken.None);
    }

    [Fact]
    public async Task FailedRefresh_KeepsSuccessForFiveMinutesAsStale()
    {
        var attempt = 0;
        var successfulSnapshot = Snapshot(
            StartTime - TimeSpan.FromMinutes(5),
            remainingPercent: 73);
        var client = new FakeQuotaClient(_ =>
            ++attempt == 1
                ? Task.FromResult(successfulSnapshot)
                : Task.FromException<QuotaSnapshot>(
                    new InvalidOperationException("quota unavailable")));
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        var states = new List<QuotaRefreshState>();
        service.StateChanged += states.Add;
        await service.SetCodexRunningAsync(true, CancellationToken.None);
        var successfulDisplay = states[^1].Display;

        await service.RefreshNowAsync(
            onlyIfStale: false,
            CancellationToken.None);

        var refreshing = states[^2];
        Assert.True(refreshing.IsRefreshing);
        Assert.Equal(successfulDisplay, refreshing.Display);

        var failed = states[^1];
        Assert.False(failed.IsRefreshing);
        Assert.Equal(QuotaDisplayMode.Single, failed.Display.Mode);
        Assert.True(failed.Display.IsStale);
        Assert.Equal(73, failed.Display.Primary!.RemainingPercent);
        Assert.Contains("quota unavailable", failed.LastError);

        await service.SetCodexRunningAsync(false, CancellationToken.None);
    }

    [Fact]
    public async Task FailedRefresh_HidesDataAfterFiveMinutes()
    {
        var attempt = 0;
        var expiredSnapshot = Snapshot(
            StartTime - TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1),
            remainingPercent: 73);
        var client = new FakeQuotaClient(_ =>
            ++attempt == 1
                ? Task.FromResult(expiredSnapshot)
                : Task.FromException<QuotaSnapshot>(
                    new InvalidOperationException("quota unavailable")));
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        var states = new List<QuotaRefreshState>();
        service.StateChanged += states.Add;
        await service.SetCodexRunningAsync(true, CancellationToken.None);

        await service.RefreshNowAsync(
            onlyIfStale: false,
            CancellationToken.None);

        var failed = states[^1];
        Assert.False(failed.IsRefreshing);
        Assert.Equal(QuotaDisplayMode.Hidden, failed.Display.Mode);
        Assert.Contains("quota unavailable", failed.LastError);

        await service.SetCodexRunningAsync(false, CancellationToken.None);
    }

    [Fact]
    public async Task ConcurrentRefreshes_CollapseIntoOneRequest()
    {
        var attempt = 0;
        var snapshot = Snapshot(StartTime, remainingPercent: 73);
        var blockedRead = new TaskCompletionSource<QuotaSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeQuotaClient(_ =>
            Interlocked.Increment(ref attempt) == 1
                ? Task.FromResult(snapshot)
                : blockedRead.Task);
        var clock = new FakeClock(StartTime);
        await using var service = new QuotaRefreshService(client, clock);
        await service.SetCodexRunningAsync(true, CancellationToken.None);

        var first = service.RefreshNowAsync(
            onlyIfStale: false,
            CancellationToken.None);
        await client.WaitForCallCountAsync(2);
        var concurrent = service.RefreshNowAsync(
            onlyIfStale: false,
            CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        blockedRead.SetResult(snapshot);
        await Task.WhenAll(first, concurrent);

        Assert.Equal(2, client.CallCount);
        await service.SetCodexRunningAsync(false, CancellationToken.None);
    }

    private static QuotaSnapshot Snapshot(
        DateTimeOffset fetchedAt,
        double remainingPercent) =>
        new(
            new QuotaWindow(
                QuotaWindowKind.FiveHour,
                remainingPercent,
                fetchedAt.AddHours(5)),
            null,
            fetchedAt);

    private sealed class FakeQuotaClient(
        Func<CancellationToken, Task<QuotaSnapshot>> readAsync) : IQuotaClient
    {
        private readonly object _sync = new();
        private readonly List<(int Expected, TaskCompletionSource Completion)> _waiters = [];
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var callCount = Interlocked.Increment(ref _callCount);
            lock (_sync)
            {
                foreach (var waiter in _waiters.Where(waiter => waiter.Expected <= callCount))
                {
                    waiter.Completion.TrySetResult();
                }

                _waiters.RemoveAll(waiter => waiter.Expected <= callCount);
            }

            return readAsync(cancellationToken);
        }

        public Task WaitForCallCountAsync(int expected)
        {
            lock (_sync)
            {
                if (CallCount >= expected)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((expected, completion));
                return completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    private sealed class FakeClock : IClock
    {
        private readonly object _sync = new();
        private readonly List<DelayRequest> _delays = [];
        private DateTimeOffset _utcNow;

        public FakeClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_sync)
                {
                    return _utcNow;
                }
            }
        }

        public int PendingDelayCount
        {
            get
            {
                lock (_sync)
                {
                    return _delays.Count(delay => !delay.Completion.Task.IsCompleted);
                }
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync)
            {
                _delays.Add(new DelayRequest(_utcNow + delay, completion));
            }

            return AwaitDelayAsync(
                completion.Task,
                cancellationToken.Register(
                    () => completion.TrySetCanceled(cancellationToken)));
        }

        public void Advance(TimeSpan elapsed)
        {
            List<TaskCompletionSource> due;
            lock (_sync)
            {
                _utcNow += elapsed;
                due = _delays
                    .Where(delay =>
                        !delay.Completion.Task.IsCompleted &&
                        delay.Due <= _utcNow)
                    .Select(delay => delay.Completion)
                    .ToList();
                _delays.RemoveAll(delay =>
                    delay.Completion.Task.IsCompleted ||
                    delay.Due <= _utcNow);
            }

            foreach (var completion in due)
            {
                completion.TrySetResult();
            }
        }

        private static async Task AwaitDelayAsync(
            Task delay,
            CancellationTokenRegistration registration)
        {
            using (registration)
            {
                await delay.ConfigureAwait(false);
            }
        }

        private sealed record DelayRequest(
            DateTimeOffset Due,
            TaskCompletionSource Completion);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        Assert.True(condition(), "The asynchronous condition was not reached.");
    }
}
