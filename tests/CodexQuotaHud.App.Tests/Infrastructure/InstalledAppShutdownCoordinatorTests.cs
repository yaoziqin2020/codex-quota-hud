using System.ComponentModel;
using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class InstalledAppShutdownCoordinatorTests
{
    private const string InstalledPath =
        @"C:\Users\Test\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe";

    [Fact]
    public void FreeMutex_ReturnsLeaseWithoutSignalOrProcessCapture()
    {
        var lease = new FakeLease();
        var platform = new FakePlatform();
        var coordinator = CreateCoordinator(platform, () => lease);

        Assert.True(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Same(lease, acquired);
        Assert.Null(error);
        Assert.Equal(0, platform.SignalCalls);
        Assert.Equal(0, platform.CaptureCalls);
    }

    [Fact]
    public void OccupiedMutex_SignalThenThirdRetryReturnsLease_WithoutKilling()
    {
        var lease = new FakeLease();
        var platform = new FakePlatform { SignalResult = true };
        var attempts = 0;
        var coordinator = CreateCoordinator(
            platform,
            () => ++attempts == 4 ? lease : null);

        Assert.True(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Same(lease, acquired);
        Assert.Null(error);
        Assert.Equal(4, attempts);
        Assert.Equal(1, platform.SignalCalls);
        Assert.Equal(0, platform.CaptureCalls);
    }

    [Fact]
    public void SignalAbsent_ExactPathIgnoringCase_KillsAndReturnsPostExitLease()
    {
        var lease = new FakeLease();
        var process = new FakeProcess(InstalledPath.ToUpperInvariant());
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(
            platform,
            () => process.WaitForExitCalls == 1 ? lease : null);

        Assert.True(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Same(lease, acquired);
        Assert.Null(error);
        Assert.Equal(1, platform.SignalCalls);
        Assert.Equal(1, platform.CaptureCalls);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(1, process.WaitForExitCalls);
        Assert.Equal(TimeSpan.FromSeconds(2), process.WaitTimeout);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void SameNameDevelopmentExecutableAtAnotherPath_IsNotKilled()
    {
        var process = new FakeProcess(
            @"C:\src\CodexQuotaHud\bin\CodexQuotaHud.App.exe");
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Null(acquired);
        Assert.Equal(
            "未找到正在运行的已安装正式版，预览无法取得单实例锁。",
            error);
        Assert.Equal(0, process.KillCalls);
        Assert.Equal(0, process.WaitForExitCalls);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void NullExecutablePath_IsNotKilled()
    {
        var process = new FakeProcess(executablePath: null);
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out _));
        Assert.Null(acquired);
        Assert.Equal(0, process.KillCalls);
        Assert.Equal(0, process.WaitForExitCalls);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void InaccessibleExecutablePath_IsContainedAndNotKilled()
    {
        var process = new FakeProcess(
            pathError: new Win32Exception("access denied"));
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out _));
        Assert.Null(acquired);
        Assert.Equal(0, process.KillCalls);
        Assert.Equal(0, process.WaitForExitCalls);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void ExactProcessKillThrows_ReturnsFailureAndChineseError()
    {
        var process = new FakeProcess(
            InstalledPath,
            killError: new Win32Exception("access denied"));
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Null(acquired);
        Assert.Equal(
            "无法关闭正在运行的正式版：access denied",
            error);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(0, process.WaitForExitCalls);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void ExactProcessDoesNotExit_ReturnsFailureWithoutAnotherKill()
    {
        var process = new FakeProcess(
            InstalledPath,
            waitForExitResult: false);
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Null(acquired);
        Assert.NotNull(error);
        Assert.StartsWith("无法关闭正在运行的正式版：", error);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(1, process.WaitForExitCalls);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void MutexRemainsOccupiedAfterProcessExit_ReturnsFailure()
    {
        var process = new FakeProcess(InstalledPath);
        var platform = new FakePlatform(process);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Null(acquired);
        Assert.Equal(
            "正式版已关闭，但单实例锁仍未释放。",
            error);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(1, process.WaitForExitCalls);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public void MultipleExactPathProcesses_AreRejectedWithoutMassKill()
    {
        var first = new FakeProcess(InstalledPath);
        var second = new FakeProcess(InstalledPath.ToUpperInvariant());
        var platform = new FakePlatform(first, second);
        var coordinator = CreateCoordinator(platform, () => null);

        Assert.False(coordinator.TryAcquireForPreview(
            out var acquired,
            out var error));
        Assert.Null(acquired);
        Assert.Equal(
            "检测到多个正式版进程，为避免误关，预览未启动。",
            error);
        Assert.Equal(0, first.KillCalls);
        Assert.Equal(0, second.KillCalls);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
    }

    private static InstalledAppShutdownCoordinator CreateCoordinator(
        FakePlatform platform,
        Func<IDisposable?> tryAcquire)
    {
        return new InstalledAppShutdownCoordinator(
            tryAcquire,
            InstalledPath,
            platform);
    }

    private sealed class FakeLease : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakePlatform(
        params FakeProcess[] processes) : IInstalledAppShutdownPlatform
    {
        public long Timestamp { get; private set; }
        public long TimestampFrequency => 1_000;
        public bool SignalResult { get; init; }
        public int SignalCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public List<TimeSpan> Waits { get; } = [];

        public bool TrySignalShutdown()
        {
            SignalCalls++;
            return SignalResult;
        }

        public IReadOnlyList<IInstalledAppProcess> CaptureProcesses()
        {
            CaptureCalls++;
            return processes;
        }

        public void Wait(TimeSpan duration)
        {
            Waits.Add(duration);
            Timestamp += (long)Math.Round(
                duration.TotalSeconds * TimestampFrequency);
        }
    }

    private sealed class FakeProcess : IInstalledAppProcess
    {
        private readonly string? _executablePath;
        private readonly Exception? _pathError;
        private readonly Exception? _killError;
        private readonly bool _waitForExitResult;

        public FakeProcess(
            string? executablePath = null,
            Exception? pathError = null,
            Exception? killError = null,
            bool waitForExitResult = true)
        {
            _executablePath = executablePath;
            _pathError = pathError;
            _killError = killError;
            _waitForExitResult = waitForExitResult;
        }

        public string? ExecutablePath
        {
            get
            {
                if (_pathError is not null)
                {
                    throw _pathError;
                }

                return _executablePath;
            }
        }

        public int KillCalls { get; private set; }
        public int WaitForExitCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public TimeSpan? WaitTimeout { get; private set; }

        public void Kill()
        {
            KillCalls++;
            if (_killError is not null)
            {
                throw _killError;
            }
        }

        public bool WaitForExit(TimeSpan timeout)
        {
            WaitForExitCalls++;
            WaitTimeout = timeout;
            return _waitForExitResult;
        }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }
}
