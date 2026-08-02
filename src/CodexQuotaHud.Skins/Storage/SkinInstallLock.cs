using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexQuotaHud.Skins.Storage;

internal interface ISkinInstallLockProvider
{
    IDisposable Acquire(
        string installedSkinsRoot,
        Guid skinId,
        CancellationToken cancellationToken);
}

internal sealed class NamedSkinInstallLockProvider : ISkinInstallLockProvider
{
    public static NamedSkinInstallLockProvider Instance { get; } = new();

    private NamedSkinInstallLockProvider()
    {
    }

    public IDisposable Acquire(
        string installedSkinsRoot,
        Guid skinId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installedSkinsRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var mutex = new Mutex(
            initiallyOwned: false,
            GetMutexName(installedSkinsRoot, skinId));
        var acquired = false;
        try
        {
            try
            {
                var signaled = cancellationToken.CanBeCanceled
                    ? WaitHandle.WaitAny([mutex, cancellationToken.WaitHandle])
                    : mutex.WaitOne() ? 0 : WaitHandle.WaitTimeout;
                if (signaled == 1)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (signaled != 0)
                {
                    throw new IOException("The skin installation lock could not be acquired.");
                }

                acquired = true;
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            return new MutexLease(mutex);
        }
        catch
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }

            mutex.Dispose();
            throw;
        }
    }

    internal static string GetMutexName(string installedSkinsRoot, Guid skinId)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(installedSkinsRoot))
            .ToUpperInvariant();
        var identity = $"{normalizedRoot}|{skinId:D}";
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        return $"Local\\CodexQuotaHud.SkinInstall.{hash}";
    }

    private sealed class MutexLease : IDisposable
    {
        private Mutex? _mutex;

        public MutexLease(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            var mutex = Interlocked.Exchange(ref _mutex, null);
            if (mutex is null)
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }
}
