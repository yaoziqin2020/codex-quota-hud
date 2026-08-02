using CodexQuotaHud.App.Infrastructure.LocalControl;
using System.Windows.Threading;

namespace CodexQuotaHud.App.Tests.Infrastructure.LocalControl;

public sealed class LocalControlActivationHandlerTests
{
    private const string SelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task HealthyCanonicalSkin_ActivatesExactlyOnceAndSucceedsAfterCompletion()
    {
        var activationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var handler = new LocalControlActivationHandler(
            key => key == SelectionKey,
            async (key, cancellationToken) =>
            {
                Assert.Equal(SelectionKey, key);
                Assert.True(Interlocked.Increment(ref calls) == 1);
                activationStarted.SetResult();
                return await allowCompletion.Task.WaitAsync(cancellationToken);
            });

        var handling = handler.HandleAsync(Request(SelectionKey), CancellationToken.None);
        await activationStarted.Task;
        Assert.False(handling.IsCompleted);

        allowCompletion.SetResult(true);
        var response = await handling;

        Assert.True(response.Succeeded);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task MissingOrCorruptSkin_IsRejectedWithoutActivation()
    {
        var calls = 0;
        var handler = new LocalControlActivationHandler(
            _ => false,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(true);
            });

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("skin.selection.missing", response.ErrorCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task FailedActivation_ReturnsStableRejection()
    {
        var handler = new LocalControlActivationHandler(
            _ => true,
            (_, _) => Task.FromResult(false));

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("skin.activation.failed", response.ErrorCode);
    }

    [Fact]
    public async Task CommittedActivation_WinsCancellationBeforeHandlerConfirmation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new LocalControlActivationHandler(
            _ => true,
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(true);
            });

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            cancellation.Token);

        Assert.True(response.Succeeded);
    }

    [Fact]
    public async Task HandlerException_DoesNotLeakDetails()
    {
        const string packageControlled = "package-controlled-exception-detail";
        var handler = new LocalControlActivationHandler(
            _ => true,
            (_, _) => throw new InvalidOperationException(packageControlled));

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("control.handler.failed", response.ErrorCode);
        Assert.DoesNotContain(
            packageControlled,
            response.Message ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builtin:HudDial")]
    [InlineData("custom:AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("custom:11111111111111111111111111111111")]
    public async Task BuiltinOrMalformedSelection_IsRejectedBeforeCatalogLookup(
        string selectionKey)
    {
        var lookups = 0;
        var handler = new LocalControlActivationHandler(
            _ =>
            {
                lookups++;
                return true;
            },
            (_, _) => Task.FromResult(true));

        var response = await handler.HandleAsync(
            Request(selectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("control.request.invalid", response.ErrorCode);
        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task ProductionDispatcherBoundary_CancelledWhileQueuedNeverExecutes()
    {
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseBlocker = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var dispatcher = await ready.Task;

        try
        {
            _ = dispatcher.BeginInvoke(
                () =>
                {
                    blockerStarted.SetResult();
                    releaseBlocker.Wait();
                },
                DispatcherPriority.Send);
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var cancellation = new CancellationTokenSource();
            var activations = 0;

            var queued = LocalControlActivationHandler.InvokeOnDispatcherAsync(
                dispatcher,
                _ =>
                {
                    Interlocked.Increment(ref activations);
                    return true;
                },
                cancellation.Token);
            cancellation.Cancel();
            releaseBlocker.Set();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(0, Volatile.Read(ref activations));
        }
        finally
        {
            releaseBlocker.Set();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        }
    }

    private static LocalControlRequest Request(string selectionKey) => new(
        LocalControlProtocol.ProtocolVersion,
        LocalControlCommandKind.ActivateSkin,
        selectionKey);
}
