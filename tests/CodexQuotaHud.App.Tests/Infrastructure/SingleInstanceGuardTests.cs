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

    private static string UniqueMutexName() =>
        $@"Local\CodexQuotaHud.Tests.{Guid.NewGuid():N}";
}
