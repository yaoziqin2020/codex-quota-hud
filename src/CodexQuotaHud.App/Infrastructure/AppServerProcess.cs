using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class AppServerProcess : IAppServerProcess, IDisposable
{
    private readonly Process _process;
    private readonly SafeJobHandle _job;
    private bool _disposed;

    private AppServerProcess(Process process, SafeJobHandle job)
    {
        _process = process;
        _job = job;
    }

    public TextWriter StandardInput => _process.StandardInput;
    public TextReader StandardOutput => _process.StandardOutput;
    public TextReader StandardError => _process.StandardError;
    public bool HasExited => _process.HasExited;

    public static AppServerProcess Start(string absoluteCodexPath)
    {
        var startInfo = CreateStartInfo(absoluteCodexPath);
        if (!File.Exists(startInfo.FileName))
        {
            throw new FileNotFoundException("The Codex executable was not found.", startInfo.FileName);
        }

        var job = CreateKillOnCloseJob();
        Process? process = null;

        try
        {
            process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Failed to start the Codex app-server process.");

            if (!NativeMethods.AssignProcessToJobObject(job, process.Handle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to assign the Codex app-server process to its owning job.");
            }

            return new AppServerProcess(process, job);
        }
        catch
        {
            if (process is not null)
            {
                TryKill(process);
                process.Dispose();
            }

            job.Dispose();
            throw;
        }
    }

    public async Task KillAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _job.Dispose();
        _process.Dispose();
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
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
        internal static extern bool CloseHandle(nint handle);
    }
}
