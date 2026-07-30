using CodexQuotaHud.App.Infrastructure;

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

    private static string UniqueEventName() =>
        $@"Local\CodexQuotaHud.Tests.Shutdown.{Guid.NewGuid():N}";
}
