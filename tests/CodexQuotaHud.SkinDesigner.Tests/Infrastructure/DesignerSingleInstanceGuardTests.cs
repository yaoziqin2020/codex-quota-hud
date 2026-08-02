using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.SkinDesigner.Infrastructure;
using System.Reflection;

namespace CodexQuotaHud.SkinDesigner.Tests.Infrastructure;

[Collection("Designer singleton")]
public sealed class DesignerSingleInstanceGuardTests
{
    [Fact]
    public void MutexIdentity_IsDistinctFromTheNormalHudAndBothCanBeOwned()
    {
        Assert.Equal(
            @"Local\CodexQuotaHud.SkinDesigner.Singleton",
            DesignerSingleInstanceGuard.MutexName);
        Assert.NotEqual(
            SingleInstanceGuard.MutexName,
            DesignerSingleInstanceGuard.MutexName);

        var suffix = Guid.NewGuid().ToString("N");
        using var normal = AcquireNormal(
            $@"Local\CodexQuotaHud.Tests.Normal.{suffix}");
        using var designer = DesignerSingleInstanceGuard.TryAcquire(
            $@"Local\CodexQuotaHud.Tests.Designer.{suffix}");

        Assert.NotNull(normal);
        Assert.NotNull(designer);
    }

    [Fact]
    public void TryAcquire_RejectsASecondDesignerUntilTheOwnerDisposes()
    {
        var first = DesignerSingleInstanceGuard.TryAcquire();
        Assert.NotNull(first);

        using var rejected = DesignerSingleInstanceGuard.TryAcquire();
        Assert.Null(rejected);

        first.Dispose();
        using var reacquired = DesignerSingleInstanceGuard.TryAcquire();
        Assert.NotNull(reacquired);
    }

    [Fact]
    public void TryAcquire_TreatsAnAbandonedDesignerMutexAsAcquired()
    {
        using var ownerReady = new ManualResetEventSlim();
        Mutex? abandonedOwner = null;
        var owner = new Thread(() =>
        {
            abandonedOwner = new Mutex(
                initiallyOwned: false,
                DesignerSingleInstanceGuard.MutexName);
            abandonedOwner.WaitOne();
            ownerReady.Set();
        });

        owner.Start();
        Assert.True(ownerReady.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(owner.Join(TimeSpan.FromSeconds(2)));

        try
        {
            DesignerSingleInstanceGuard? guard = null;
            Assert.True(SpinWait.SpinUntil(
                () => (guard = DesignerSingleInstanceGuard.TryAcquire()) is not null,
                TimeSpan.FromSeconds(2)));
            using (guard)
            {
                Assert.NotNull(guard);
            }
        }
        finally
        {
            abandonedOwner?.Dispose();
        }
    }

    [Fact]
    public void Dispose_FromAnotherThreadFailsWithoutLosingOwnership()
    {
        var guard = DesignerSingleInstanceGuard.TryAcquire();
        Assert.NotNull(guard);
        Exception? wrongThreadException = null;
        var wrongThread = new Thread(
            () => wrongThreadException = Record.Exception(guard.Dispose));

        wrongThread.Start();
        Assert.True(wrongThread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsType<InvalidOperationException>(wrongThreadException);
        using var rejected = DesignerSingleInstanceGuard.TryAcquire();
        Assert.Null(rejected);

        guard.Dispose();
        using var reacquired = DesignerSingleInstanceGuard.TryAcquire();
        Assert.NotNull(reacquired);
    }

    private static IDisposable? AcquireNormal(string mutexName)
    {
        var overload = typeof(SingleInstanceGuard).GetMethod(
            nameof(SingleInstanceGuard.TryAcquire),
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null);
        Assert.NotNull(overload);
        return Assert.IsAssignableFrom<IDisposable>(
            overload.Invoke(null, [mutexName]));
    }
}

[CollectionDefinition("Designer singleton", DisableParallelization = true)]
public sealed class DesignerSingletonCollection;
