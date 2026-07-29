using System.Threading;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\CodexQuotaHud.Singleton";

    private static readonly object OwnershipSync = new();
    private static readonly HashSet<string> OwnedNames = new(StringComparer.Ordinal);

    private readonly object _disposeSync = new();
    private readonly string _mutexName;
    private readonly int _ownerThreadId;
    private Mutex? _mutex;

    private SingleInstanceGuard(string mutexName, Mutex mutex)
    {
        _mutexName = mutexName;
        _mutex = mutex;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public static SingleInstanceGuard? TryAcquire() => TryAcquire(MutexName);

    internal static SingleInstanceGuard? TryAcquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        lock (OwnershipSync)
        {
            if (OwnedNames.Contains(mutexName))
            {
                return null;
            }

            var mutex = new Mutex(initiallyOwned: false, mutexName);
            var ownsMutex = false;
            try
            {
                ownsMutex = mutex.WaitOne(millisecondsTimeout: 0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
            catch
            {
                mutex.Dispose();
                throw;
            }

            if (!ownsMutex)
            {
                mutex.Dispose();
                return null;
            }

            OwnedNames.Add(mutexName);
            return new SingleInstanceGuard(mutexName, mutex);
        }
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            var mutex = _mutex;
            if (mutex is null)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "SingleInstanceGuard must be disposed on the thread that acquired it.");
            }

            lock (OwnershipSync)
            {
                mutex.ReleaseMutex();
                OwnedNames.Remove(_mutexName);
                _mutex = null;
            }

            mutex.Dispose();
        }
    }
}
