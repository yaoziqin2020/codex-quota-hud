using System.Diagnostics;

namespace CodexQuotaHud.App.Infrastructure;

internal interface IInstalledAppShutdownPlatform
{
    long Timestamp { get; }
    long TimestampFrequency { get; }
    bool TrySignalShutdown();
    IReadOnlyList<IInstalledAppProcess> CaptureProcesses();
    void Wait(TimeSpan duration);
}

internal interface IInstalledAppProcess : IDisposable
{
    string? ExecutablePath { get; }
    bool HasExited { get; }
    void Kill();
    bool WaitForExit(TimeSpan timeout);
}

internal sealed class InstalledAppShutdownPlatform :
    IInstalledAppShutdownPlatform
{
    public long Timestamp => Stopwatch.GetTimestamp();
    public long TimestampFrequency => Stopwatch.Frequency;

    public bool TrySignalShutdown()
    {
        try
        {
            return InstalledAppShutdownListener.TrySignal();
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(
                "Failed to signal installed-HUD shutdown: {0}",
                exception);
            return false;
        }
    }

    public IReadOnlyList<IInstalledAppProcess> CaptureProcesses()
    {
        try
        {
            return Process.GetProcesses()
                .Select(process =>
                    (IInstalledAppProcess)new InstalledAppProcess(process))
                .ToArray();
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(
                "Failed to enumerate processes while replacing the installed HUD: {0}",
                exception);
            return [];
        }
    }

    public void Wait(TimeSpan duration)
    {
        Thread.Sleep(duration);
    }

    private sealed class InstalledAppProcess(Process process) :
        IInstalledAppProcess
    {
        public string? ExecutablePath => process.MainModule?.FileName;
        public bool HasExited => process.HasExited;

        public void Kill()
        {
            process.Kill(entireProcessTree: true);
        }

        public bool WaitForExit(TimeSpan timeout)
        {
            var totalMilliseconds = timeout.TotalMilliseconds;
            var milliseconds = totalMilliseconds <= 0
                ? 0
                : totalMilliseconds >= int.MaxValue
                    ? int.MaxValue
                    : (int)Math.Ceiling(totalMilliseconds);
            return process.WaitForExit(milliseconds);
        }

        public void Dispose()
        {
            process.Dispose();
        }
    }
}
