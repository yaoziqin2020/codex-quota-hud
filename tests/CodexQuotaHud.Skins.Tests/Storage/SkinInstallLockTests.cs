using System.Diagnostics;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class SkinInstallLockTests
{
    [Fact]
    public async Task NamedLock_IsObservedAcrossProcessesAndCancelledWaitDoesNotLeak()
    {
        var installedRoot = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.Skins.Tests",
            Guid.NewGuid().ToString("N"));
        var skinId = Guid.NewGuid();
        var mutexName = NamedSkinInstallLockProvider.GetMutexName(
            installedRoot,
            skinId);
        var shellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo(shellPath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "$mutex = [System.Threading.Mutex]::new($false, $env:CODEX_TEST_MUTEX_NAME); " +
            "$null = $mutex.WaitOne(); " +
            "[Console]::Out.WriteLine('locked'); [Console]::Out.Flush(); " +
            "$null = [Console]::In.ReadLine(); " +
            "$mutex.ReleaseMutex(); $mutex.Dispose()");
        startInfo.Environment["CODEX_TEST_MUTEX_NAME"] = mutexName;

        using var process = Assert.IsType<Process>(Process.Start(startInfo));
        try
        {
            var status = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("locked", status);

            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(250));
            Assert.Throws<OperationCanceledException>(() =>
                NamedSkinInstallLockProvider.Instance.Acquire(
                    installedRoot,
                    skinId,
                    cancellation.Token));

            await process.StandardInput.WriteLineAsync("release");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);

            using var acquired = NamedSkinInstallLockProvider.Instance.Acquire(
                installedRoot,
                skinId,
                CancellationToken.None);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public void NamedLock_AcquiresAbandonedMutex()
    {
        var installedRoot = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.Skins.Tests",
            Guid.NewGuid().ToString("N"));
        var skinId = Guid.NewGuid();
        var mutexName = NamedSkinInstallLockProvider.GetMutexName(
            installedRoot,
            skinId);
        using var acquired = new ManualResetEventSlim();
        Mutex? abandonedMutex = null;
        var owner = new Thread(() =>
        {
            abandonedMutex = new Mutex(initiallyOwned: false, mutexName);
            _ = abandonedMutex.WaitOne();
            acquired.Set();
        });
        owner.Start();
        Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));

        using var recovered = NamedSkinInstallLockProvider.Instance.Acquire(
            installedRoot,
            skinId,
            CancellationToken.None);
        abandonedMutex!.Dispose();
    }
}
