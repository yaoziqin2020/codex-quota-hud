using System.Threading;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\CodexQuotaHud.Singleton";

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire() => TryAcquire(MutexName);

    internal static SingleInstanceGuard? TryAcquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
