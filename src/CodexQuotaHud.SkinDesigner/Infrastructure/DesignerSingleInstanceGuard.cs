using System.Threading;

namespace CodexQuotaHud.SkinDesigner.Infrastructure;

public sealed class DesignerSingleInstanceGuard : IDisposable
{
    public const string MutexName =
        @"Local\CodexQuotaHud.SkinDesigner.Singleton";

    private static readonly object OwnershipSync = new();
    private static readonly HashSet<string> OwnedNames =
        new(StringComparer.Ordinal);

    private readonly object _disposeSync = new();
    private readonly string _mutexName;
    private readonly int _ownerThreadId;
    private Mutex? _mutex;

    private DesignerSingleInstanceGuard(string mutexName, Mutex mutex)
    {
        _mutexName = mutexName;
        _mutex = mutex;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public static DesignerSingleInstanceGuard? TryAcquire() =>
        TryAcquire(MutexName);

    internal static DesignerSingleInstanceGuard? TryAcquire(string mutexName)
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
            return new DesignerSingleInstanceGuard(mutexName, mutex);
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
                    "DesignerSingleInstanceGuard must be disposed on the thread that acquired it.");
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
