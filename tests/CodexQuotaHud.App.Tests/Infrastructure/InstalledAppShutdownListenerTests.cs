using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.Infrastructure.LocalControl;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class InstalledAppShutdownListenerTests
{
    [Fact]
    public void Signal_InvokesExitCallback()
    {
        var name = UniqueEventName();
        using var called = new ManualResetEventSlim();
        using var listener = new InstalledAppShutdownListener(name, called.Set);

        Assert.True(InstalledAppShutdownListener.TrySignal(name));
        Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void MissingListener_ReturnsFalse()
    {
        Assert.False(InstalledAppShutdownListener.TrySignal(UniqueEventName()));
    }

    [Fact]
    public void Dispose_StopsFutureCallbacksAndIsIdempotent()
    {
        var name = UniqueEventName();
        var calls = 0;
        var listener = new InstalledAppShutdownListener(
            name,
            () => Interlocked.Increment(ref calls));

        listener.Dispose();
        listener.Dispose();

        Assert.False(InstalledAppShutdownListener.TrySignal(name));
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void Signal_CanInvokeExitCallbackTwice()
    {
        var name = UniqueEventName();
        var calls = 0;
        using var called = new ManualResetEventSlim();
        using var listener = new InstalledAppShutdownListener(
            name,
            () =>
            {
                Interlocked.Increment(ref calls);
                called.Set();
            });

        Assert.True(InstalledAppShutdownListener.TrySignal(name));
        Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
        called.Reset();

        Assert.True(InstalledAppShutdownListener.TrySignal(name));
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref calls) == 2,
            TimeSpan.FromSeconds(2)));
        Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ShutdownEvent_RemainsOperationalWhileTypedPipeIsListening()
    {
        var pipeName = $"CodexQuotaHud.Tests.Coexist.{Guid.NewGuid():N}";
        var eventName = UniqueEventName();
        using var exitRequested = new ManualResetEventSlim();
        using var listener = new InstalledAppShutdownListener(
            eventName,
            exitRequested.Set);
        await using var server = new LocalControlServer(
            pipeName,
            (_, _) => Task.FromResult(new LocalControlResponse(true, null, null)));
        server.Start();

        var activation = await new LocalControlClient(pipeName).SendAsync(
            new LocalControlRequest(
                LocalControlProtocol.ProtocolVersion,
                LocalControlCommandKind.ActivateSkin,
                "custom:11111111-1111-1111-1111-111111111111"));
        var signaled = InstalledAppShutdownListener.TrySignal(eventName);

        Assert.True(activation.Succeeded);
        Assert.True(signaled);
        Assert.True(exitRequested.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(
            @"Local\CodexQuotaHud.ShutdownRequested",
            InstalledAppShutdownListener.EventName);
    }

    private static string UniqueEventName() =>
        $@"Local\CodexQuotaHud.Tests.Shutdown.{Guid.NewGuid():N}";
}
