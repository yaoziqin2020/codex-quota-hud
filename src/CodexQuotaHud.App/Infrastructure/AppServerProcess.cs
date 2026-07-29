using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.App.Infrastructure;

internal interface IAppServerProcessPlatform
{
    void EnsureCurrentProcessInKillOnCloseJob();
    IAppServerChildProcess Start(ProcessStartInfo startInfo);
    bool IsInKillOnCloseJob(IAppServerChildProcess process);
}

internal interface IAppServerChildProcess : IDisposable
{
    TextWriter StandardInput { get; }
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    bool HasExited { get; }

    void Kill();
    bool WaitForExit(TimeSpan timeout);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public sealed class AppServerProcess : IAppServerProcess, IDisposable
{
    private static readonly TimeSpan StartupCleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly IAppServerChildProcess _process;
    private bool _disposed;

    private AppServerProcess(IAppServerChildProcess process)
    {
        _process = process;
    }

    public TextWriter StandardInput => _process.StandardInput;
    public TextReader StandardOutput => _process.StandardOutput;
    public TextReader StandardError => _process.StandardError;
    public bool HasExited => _process.HasExited;

    public static AppServerProcess Start(string absoluteCodexPath)
    {
        return Start(
            absoluteCodexPath,
            WindowsAppServerProcessPlatform.Instance,
            File.Exists);
    }

    internal static AppServerProcess Start(
        string absoluteCodexPath,
        IAppServerProcessPlatform platform,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(fileExists);

        var startInfo = CreateStartInfo(absoluteCodexPath);
        if (!fileExists(startInfo.FileName))
        {
            throw new FileNotFoundException("The Codex executable was not found.", startInfo.FileName);
        }

        platform.EnsureCurrentProcessInKillOnCloseJob();
        var process = platform.Start(startInfo);

        try
        {
            if (!platform.IsInKillOnCloseJob(process))
            {
                throw new InvalidOperationException(
                    "The Codex app-server process did not inherit the HUD ownership job.");
            }

            return new AppServerProcess(process);
        }
        catch (Exception startupFailure)
        {
            ThrowAfterStartupCleanup(process, startupFailure);
            throw new UnreachableException();
        }
    }

    public async Task KillAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_process.HasExited)
        {
            _process.Kill();
            await _process.WaitForExitAsync(CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAndDispose(_process);
        GC.SuppressFinalize(this);
    }

    internal static ProcessStartInfo CreateStartInfo(string absoluteCodexPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteCodexPath);
        if (!Path.IsPathFullyQualified(absoluteCodexPath))
        {
            throw new ArgumentException(
                "The Codex executable path must be absolute.",
                nameof(absoluteCodexPath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(absoluteCodexPath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");
        return startInfo;
    }

    [DoesNotReturn]
    private static void ThrowAfterStartupCleanup(
        IAppServerChildProcess process,
        Exception startupFailure)
    {
        var failures = new List<Exception> { startupFailure };
        var shouldKill = true;

        try
        {
            shouldKill = !process.HasExited;
        }
        catch (Exception inspectionFailure)
        {
            failures.Add(inspectionFailure);
        }

        if (shouldKill)
        {
            try
            {
                process.Kill();
            }
            catch (Exception killFailure)
            {
                failures.Add(killFailure);
            }
        }

        try
        {
            if (!process.WaitForExit(StartupCleanupTimeout))
            {
                failures.Add(new TimeoutException(
                    $"The Codex app-server process did not exit within {StartupCleanupTimeout}."));
            }
        }
        catch (Exception waitFailure)
        {
            failures.Add(waitFailure);
        }

        try
        {
            process.Dispose();
        }
        catch (Exception disposeFailure)
        {
            failures.Add(disposeFailure);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
        }

        throw new AggregateException(
            "Codex app-server startup failed and cleanup was not fully successful.",
            failures);
    }

    private static void StopAndDispose(IAppServerChildProcess process)
    {
        List<Exception>? failures = null;
        var shouldStop = true;

        try
        {
            shouldStop = !process.HasExited;
        }
        catch (Exception inspectionFailure)
        {
            (failures ??= []).Add(inspectionFailure);
        }

        if (shouldStop)
        {
            try
            {
                process.Kill();
            }
            catch (Exception killFailure)
            {
                (failures ??= []).Add(killFailure);
            }

            try
            {
                if (!process.WaitForExit(StartupCleanupTimeout))
                {
                    (failures ??= []).Add(new TimeoutException(
                        $"The Codex app-server process did not exit within {StartupCleanupTimeout}."));
                }
            }
            catch (Exception waitFailure)
            {
                (failures ??= []).Add(waitFailure);
            }
        }

        try
        {
            process.Dispose();
        }
        catch (Exception disposeFailure)
        {
            (failures ??= []).Add(disposeFailure);
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "Failed to stop the Codex app-server process.",
                failures);
        }
    }

    private sealed class WindowsAppServerProcessPlatform : IAppServerProcessPlatform
    {
        private static readonly Lazy<SafeJobHandle> LifetimeJob = new(
            CreateAndOwnCurrentProcess,
            LazyThreadSafetyMode.ExecutionAndPublication);

        public static WindowsAppServerProcessPlatform Instance { get; } = new();

        private WindowsAppServerProcessPlatform()
        {
        }

        public void EnsureCurrentProcessInKillOnCloseJob()
        {
            _ = LifetimeJob.Value;
        }

        public IAppServerChildProcess Start(ProcessStartInfo startInfo)
        {
            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Failed to start the Codex app-server process.");
            return new SystemChildProcess(process);
        }

        public bool IsInKillOnCloseJob(IAppServerChildProcess process)
        {
            var systemProcess = (SystemChildProcess)process;
            if (!NativeMethods.IsProcessInJob(
                    systemProcess.Handle,
                    LifetimeJob.Value,
                    out var isInJob))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to verify the Codex app-server ownership job.");
            }

            return isInJob;
        }

        private static SafeJobHandle CreateAndOwnCurrentProcess()
        {
            var job = CreateKillOnCloseJob();

            using var currentProcess = Process.GetCurrentProcess();
            // Windows 8+ supports nested jobs. If host policy rejects nesting,
            // fail before spawning a child rather than weakening ownership.
            if (!NativeMethods.AssignProcessToJobObject(job, currentProcess.Handle))
            {
                var error = new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to assign the HUD to its app-server ownership job.");
                job.Dispose();
                throw error;
            }

            return job;
        }

        private static SafeJobHandle CreateKillOnCloseJob()
        {
            var job = NativeMethods.CreateJobObject(nint.Zero, null);
            if (job.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to create the Codex app-server ownership job.");
            }

            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitFlags.KillOnJobClose
                }
            };

            if (!NativeMethods.SetInformationJobObject(
                    job,
                    JobObjectInformationClass.ExtendedLimitInformation,
                    ref limits,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                var error = new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to configure the Codex app-server ownership job.");
                job.Dispose();
                throw error;
            }

            return job;
        }
    }

    private sealed class SystemChildProcess(Process process) : IAppServerChildProcess
    {
        public TextWriter StandardInput => process.StandardInput;
        public TextReader StandardOutput => process.StandardOutput;
        public TextReader StandardError => process.StandardError;
        public bool HasExited => process.HasExited;
        public nint Handle => process.Handle;

        public void Kill() => process.Kill(entireProcessTree: true);

        public bool WaitForExit(TimeSpan timeout)
        {
            var milliseconds = checked((int)timeout.TotalMilliseconds);
            return process.WaitForExit(milliseconds);
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public void Dispose() => process.Dispose();
    }

    [Flags]
    private enum JobObjectLimitFlags : uint
    {
        KillOnJobClose = 0x00002000
    }

    private enum JobObjectInformationClass
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JobObjectLimitFlags LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeJobHandle CreateJobObject(
            nint jobAttributes,
            string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            SafeJobHandle job,
            JobObjectInformationClass informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(
            SafeJobHandle job,
            nint process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsProcessInJob(
            nint process,
            SafeJobHandle job,
            [MarshalAs(UnmanagedType.Bool)] out bool result);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(nint handle);
    }
}
