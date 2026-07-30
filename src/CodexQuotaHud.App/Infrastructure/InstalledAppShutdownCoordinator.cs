using System.Diagnostics;
using System.IO;

namespace CodexQuotaHud.App.Infrastructure;

internal sealed class InstalledAppShutdownCoordinator
{
    private static readonly TimeSpan GracefulTimeout =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForceExitTimeout =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryInterval =
        TimeSpan.FromMilliseconds(50);

    private readonly Func<IDisposable?> _tryAcquire;
    private readonly string _installedExecutablePath;
    private readonly IInstalledAppShutdownPlatform _platform;

    public InstalledAppShutdownCoordinator(
        Func<IDisposable?> tryAcquire,
        string installedExecutablePath,
        IInstalledAppShutdownPlatform platform)
    {
        _tryAcquire = tryAcquire ?? throw new ArgumentNullException(
            nameof(tryAcquire));
        ArgumentException.ThrowIfNullOrWhiteSpace(installedExecutablePath);
        _installedExecutablePath = Path.GetFullPath(installedExecutablePath);
        _platform = platform ?? throw new ArgumentNullException(
            nameof(platform));
    }

    public bool TryAcquireForPreview(
        out IDisposable? lease,
        out string? error)
    {
        lease = _tryAcquire();
        if (lease is not null)
        {
            error = null;
            return true;
        }

        _ = TrySignalShutdown();
        lease = TryAcquireUntil(GracefulTimeout);
        if (lease is not null)
        {
            error = null;
            return true;
        }

        var processes = _platform.CaptureProcesses();
        try
        {
            var matches = FindInstalledProcesses(processes);
            if (matches.Count == 0)
            {
                error = "未找到正在运行的已安装正式版，预览无法取得单实例锁。";
                return false;
            }

            if (matches.Count > 1)
            {
                error = "检测到多个正式版进程，为避免误关，预览未启动。";
                return false;
            }

            var process = matches[0];
            try
            {
                process.Kill();
                if (!process.WaitForExit(ForceExitTimeout))
                {
                    error = "无法关闭正在运行的正式版：等待正式版退出超时。";
                    return false;
                }
            }
            catch (Exception exception)
            {
                var detail = string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.GetType().Name
                    : exception.Message;
                error = $"无法关闭正在运行的正式版：{detail}";
                return false;
            }

            lease = TryAcquireUntil(ForceExitTimeout);
            if (lease is not null)
            {
                error = null;
                return true;
            }

            error = "正式版已关闭，但单实例锁仍未释放。";
            return false;
        }
        finally
        {
            DisposeProcesses(processes);
        }
    }

    private bool TrySignalShutdown()
    {
        try
        {
            return _platform.TrySignalShutdown();
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(
                "Failed to signal installed-HUD shutdown: {0}",
                exception);
            return false;
        }
    }

    private List<IInstalledAppProcess> FindInstalledProcesses(
        IReadOnlyList<IInstalledAppProcess> processes)
    {
        var matches = new List<IInstalledAppProcess>();

        foreach (var process in processes)
        {
            try
            {
                var candidate = process.ExecutablePath;
                if (candidate is not null &&
                    string.Equals(
                        Path.GetFullPath(candidate),
                        _installedExecutablePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(process);
                }
            }
            catch
            {
            }
        }

        return matches;
    }

    private IDisposable? TryAcquireUntil(TimeSpan timeout)
    {
        var start = _platform.Timestamp;
        var frequency = _platform.TimestampFrequency;
        var timeoutTicks = timeout.TotalSeconds * frequency;

        while (_platform.Timestamp - start < timeoutTicks)
        {
            _platform.Wait(RetryInterval);
            var lease = _tryAcquire();
            if (lease is not null)
            {
                return lease;
            }
        }

        return null;
    }

    private static void DisposeProcesses(
        IReadOnlyList<IInstalledAppProcess> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                process.Dispose();
            }
            catch (Exception exception)
            {
                Trace.TraceWarning(
                    "Failed to dispose a captured installed-app process: {0}",
                    exception);
            }
        }
    }
}
