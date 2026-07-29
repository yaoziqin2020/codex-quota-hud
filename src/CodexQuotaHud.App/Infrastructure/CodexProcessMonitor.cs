using System.ComponentModel;
using System.Diagnostics;

namespace CodexQuotaHud.App.Infrastructure;

internal interface IProcessSnapshot : IDisposable
{
    int Id { get; }
    string ProcessName { get; }
    nint MainWindowHandle { get; }
    string? ExecutablePath { get; }
}

public sealed class CodexProcessMonitor : ICodexProcessMonitor, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Func<IReadOnlyList<IProcessSnapshot>> _getProcessSnapshots;
    private readonly int _currentProcessId;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _pollTask;
    private int _isRunning;
    private bool _disposed;

    public CodexProcessMonitor()
        : this(ProcessSnapshotFactory.Capture, Environment.ProcessId, startPolling: true)
    {
    }

    internal CodexProcessMonitor(
        Func<IReadOnlyList<IProcessSnapshot>> getProcessSnapshots,
        int currentProcessId,
        bool startPolling)
    {
        ArgumentNullException.ThrowIfNull(getProcessSnapshots);

        _getProcessSnapshots = getProcessSnapshots;
        _currentProcessId = currentProcessId;
        _isRunning = DetectRunningCodex() ? 1 : 0;
        _pollTask = startPolling ? PollPeriodicallyAsync(_stopping.Token) : Task.CompletedTask;
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public event Action<bool>? RunningChanged;

    internal void Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var isRunning = DetectRunningCodex();
        var previous = Interlocked.Exchange(ref _isRunning, isRunning ? 1 : 0) != 0;
        if (previous != isRunning)
        {
            RunningChanged?.Invoke(isRunning);
        }
    }

    internal static IReadOnlyList<string> FindRunningCodexExecutablePaths()
    {
        return FindRunningCodexExecutablePaths(
            ProcessSnapshotFactory.Capture,
            Environment.ProcessId);
    }

    internal static IReadOnlyList<string> FindRunningCodexExecutablePaths(
        Func<IReadOnlyList<IProcessSnapshot>> getProcessSnapshots,
        int currentProcessId)
    {
        var snapshots = getProcessSnapshots();
        var paths = new List<string>();

        try
        {
            foreach (var snapshot in snapshots)
            {
                try
                {
                    if (IsCodexDesktop(snapshot, currentProcessId) &&
                        !string.IsNullOrWhiteSpace(snapshot.ExecutablePath))
                    {
                        paths.Add(snapshot.ExecutablePath);
                    }
                }
                catch (Exception exception) when (IsPerProcessInspectionFailure(exception))
                {
                }
            }
        }
        finally
        {
            DisposeSnapshots(snapshots);
        }

        return paths;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool DetectRunningCodex()
    {
        var snapshots = _getProcessSnapshots();

        try
        {
            foreach (var snapshot in snapshots)
            {
                try
                {
                    if (IsCodexDesktop(snapshot, _currentProcessId))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (IsPerProcessInspectionFailure(exception))
                {
                }
            }

            return false;
        }
        finally
        {
            DisposeSnapshots(snapshots);
        }
    }

    private async Task PollPeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Poll();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsCodexDesktop(IProcessSnapshot snapshot, int currentProcessId)
    {
        if (snapshot.Id == currentProcessId ||
            !string.Equals(snapshot.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (snapshot.MainWindowHandle != nint.Zero)
            {
                return true;
            }
        }
        catch (Exception exception) when (IsPerProcessInspectionFailure(exception))
        {
        }

        try
        {
            return snapshot.ExecutablePath?.Contains(
                "OpenAI.Codex_",
                StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception exception) when (IsPerProcessInspectionFailure(exception))
        {
            return false;
        }
    }

    private static bool IsPerProcessInspectionFailure(Exception exception)
    {
        return exception is UnauthorizedAccessException or
            Win32Exception or
            InvalidOperationException or
            NotSupportedException;
    }

    private static void DisposeSnapshots(IReadOnlyList<IProcessSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            snapshot.Dispose();
        }
    }

    private static class ProcessSnapshotFactory
    {
        public static IReadOnlyList<IProcessSnapshot> Capture()
        {
            return Process.GetProcesses()
                .Select(static process => (IProcessSnapshot)new ProcessSnapshot(process))
                .ToArray();
        }
    }

    private sealed class ProcessSnapshot(Process process) : IProcessSnapshot
    {
        public int Id => process.Id;
        public string ProcessName => process.ProcessName;
        public nint MainWindowHandle => process.MainWindowHandle;
        public string? ExecutablePath => process.MainModule?.FileName;

        public void Dispose() => process.Dispose();
    }
}
