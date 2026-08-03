using CodexQuotaHud.SkinDesigner.UI;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Cancel_WaitsForCancellationPublicationBeforeReturning()
    {
        var factoryEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new ManualResetEventSlim();
        var executeEntered = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = new AsyncRelayCommand(
            async token =>
            {
                executeEntered.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            canExecute: null,
            context: null,
            createCancellation: () =>
            {
                factoryEntered.SetResult();
                releaseFactory.Wait(TimeSpan.FromSeconds(5));
                return new CancellationTokenSource();
            });

        var execution = Task.Run(sut.ExecuteAsync);
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancel = Task.Run(sut.Cancel);
        var cancelReturnedBeforePublication = ReferenceEquals(
            await Task.WhenAny(cancel, Task.Delay(200)),
            cancel);
        releaseFactory.Set();
        await cancel.WaitAsync(TimeSpan.FromSeconds(5));
        var token = await executeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!token.IsCancellationRequested)
        {
            sut.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(cancelReturnedBeforePublication);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposeDuringExecution_CancelsAndSuppressesLaterPublication()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new RecordingSynchronizationContext();
        var notifications = 0;
        using var sut = new AsyncRelayCommand(
            async token =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            },
            canExecute: null,
            context: context,
            createCancellation: static () => new CancellationTokenSource());
        sut.CanExecuteChanged += (_, _) => notifications++;

        Task execution = null!;
        RunWithContext(context, () => execution = sut.ExecuteAsync());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RunWithContext(context, sut.Dispose);
        var notificationsAtDispose = notifications;
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(notificationsAtDispose, notifications);
        Assert.Equal(0, context.PostCount);
    }

    [Fact]
    public async Task PostFailure_DoesNotMaskOriginalExecutionFailure()
    {
        var original = new InvalidOperationException("execution failed");
        var context = new FailingSecondPostSynchronizationContext();
        var sut = new AsyncRelayCommand(
            async _ =>
            {
                await Task.Delay(1).ConfigureAwait(false);
                throw original;
            },
            canExecute: null,
            context: context,
            createCancellation: static () => new CancellationTokenSource());

        Task execution = null!;
        execution = sut.ExecuteAsync();

        try
        {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(
                () => execution);
            Assert.Same(original, observed);
        }
        finally
        {
            RunWithContext(context, sut.Dispose);
        }
    }

    [Fact]
    public async Task CanExecuteChangedHandlerFailure_DoesNotMaskOriginalExecutionFailure()
    {
        var original = new InvalidOperationException("execution failed");
        var notifications = 0;
        var sut = new AsyncRelayCommand(
            async _ =>
            {
                await Task.Delay(1).ConfigureAwait(false);
                throw original;
            },
            canExecute: null,
            context: null,
            createCancellation: static () => new CancellationTokenSource());
        EventHandler handler = (_, _) =>
        {
            notifications++;
            if (notifications > 1)
            {
                throw new ApplicationException("handler failed");
            }
        };
        sut.CanExecuteChanged += handler;

        try
        {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(
                sut.ExecuteAsync);

            Assert.Same(original, observed);
            Assert.Equal(2, notifications);
        }
        finally
        {
            sut.CanExecuteChanged -= handler;
            sut.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsReentryAndRestoresAvailabilityOnCompletion()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var sut = new AsyncRelayCommand(async _ =>
        {
            calls++;
            entered.SetResult();
            await release.Task;
        });

        var first = sut.ExecuteAsync();
        await entered.Task;
        var reentry = sut.ExecuteAsync();

        Assert.True(sut.IsRunning);
        Assert.False(sut.CanExecute(null));
        await reentry;
        Assert.Equal(1, calls);
        release.SetResult();
        await first;
        Assert.False(sut.IsRunning);
        Assert.True(sut.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_PropagatesTokenWithoutStartingAnotherExecution()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;
        using var sut = new AsyncRelayCommand(async token =>
        {
            observedToken = token;
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        var execution = sut.ExecuteAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sut.Cancel();
        Assert.True(observedToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(sut.IsRunning);
    }

    private static void RunWithContext(
        SynchronizationContext context,
        Action action)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            d(state);
        }
    }

    private sealed class FailingSecondPostSynchronizationContext :
        SynchronizationContext
    {
        private int _posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (Interlocked.Increment(ref _posts) == 2)
            {
                throw new ApplicationException("dispatcher is shutting down");
            }

            d(state);
        }
    }
}
