using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_RejectsASecondGuardInTheSameProcess()
    {
        var name = UniqueMutexName();

        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Dispose_ReleasesOwnershipSoTheNameCanBeAcquiredAgain()
    {
        var name = UniqueMutexName();
        using var observer = new Mutex(initiallyOwned: false, name);
        var first = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(first);

        first.Dispose();
        using var next = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(next);
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        var guard = SingleInstanceGuard.TryAcquire(UniqueMutexName());
        Assert.NotNull(guard);

        guard.Dispose();
        guard.Dispose();
    }

    [Fact]
    public void TryAcquire_AcquiresAnExistingButUnownedNamedMutex()
    {
        var name = UniqueMutexName();
        using var observer = new Mutex(initiallyOwned: false, name);

        using var guard = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(guard);
    }

    [Fact]
    public void TryAcquire_TreatsAnAbandonedNamedMutexAsAcquired()
    {
        var name = UniqueMutexName();
        using var observer = new Mutex(initiallyOwned: false, name);
        using var ownerReady = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, name);
            mutex.WaitOne();
            ownerReady.Set();
        });

        owner.Start();
        Assert.True(ownerReady.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(owner.Join(TimeSpan.FromSeconds(2)));

        using var guard = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(guard);
    }

    [Fact]
    public void Dispose_FromAnotherThreadFailsWithoutLosingOwnerCleanup()
    {
        var name = UniqueMutexName();
        var guard = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(guard);
        Exception? wrongThreadException = null;
        var wrongThread = new Thread(
            () => wrongThreadException = Record.Exception(guard.Dispose));

        wrongThread.Start();
        Assert.True(wrongThread.Join(TimeSpan.FromSeconds(2)));
        Assert.IsType<InvalidOperationException>(wrongThreadException);
        using var rejectedWhileStillOwned = SingleInstanceGuard.TryAcquire(name);
        Assert.Null(rejectedWhileStillOwned);

        guard.Dispose();
        using var reacquired = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(reacquired);
    }

    private static string UniqueMutexName() =>
        $@"Local\CodexQuotaHud.Tests.{Guid.NewGuid():N}";
}
