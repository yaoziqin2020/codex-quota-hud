using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class CodexProcessMonitorTests
{
    [Fact]
    public void DetectsCodexDesktopCaseInsensitively()
    {
        using var monitor = CreateMonitor(
            currentProcessId: 100,
            new FakeProcessSnapshot(200, "cOdEx", new nint(1), null));

        Assert.True(monitor.IsRunning);
    }

    [Fact]
    public void IgnoresCurrentHudProcess()
    {
        using var monitor = CreateMonitor(
            currentProcessId: 100,
            new FakeProcessSnapshot(
                100,
                "Codex",
                new nint(1),
                @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0.0_x64__test\app\Codex.exe"));

        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public void EmitsOnlyWhenRunningStateChanges()
    {
        IReadOnlyList<IProcessSnapshot> snapshots = [];
        using var monitor = new CodexProcessMonitor(
            () => snapshots,
            currentProcessId: 100,
            startPolling: false);
        var changes = new List<bool>();
        monitor.RunningChanged += changes.Add;

        monitor.Poll();
        snapshots =
        [
            new FakeProcessSnapshot(
                200,
                "Codex",
                nint.Zero,
                @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0.0_x64__test\app\Codex.exe")
        ];
        monitor.Poll();
        monitor.Poll();
        snapshots = [];
        monitor.Poll();

        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void ContinuesAfterAccessDeniedForOneProcess()
    {
        IProcessSnapshot inaccessible = new ThrowingProcessSnapshot();
        IProcessSnapshot desktop = new FakeProcessSnapshot(200, "Codex", new nint(1), null);
        using var monitor = CreateMonitor(100, inaccessible, desktop);

        Assert.True(monitor.IsRunning);
    }

    [Fact]
    public void UsesPackagePathWhenWindowHandleAccessIsDenied()
    {
        using var monitor = CreateMonitor(100, new WindowDeniedProcessSnapshot());

        Assert.True(monitor.IsRunning);
    }

    [Fact]
    public void IgnoresHeadlessCodexOutsideDesktopPackage()
    {
        using var monitor = CreateMonitor(
            currentProcessId: 100,
            new FakeProcessSnapshot(200, "codex", nint.Zero, @"C:\Tools\codex.exe"));

        Assert.False(monitor.IsRunning);
    }

    private static CodexProcessMonitor CreateMonitor(
        int currentProcessId,
        params IProcessSnapshot[] snapshots)
    {
        return new CodexProcessMonitor(
            () => snapshots,
            currentProcessId,
            startPolling: false);
    }

    private sealed class FakeProcessSnapshot(
        int id,
        string processName,
        nint mainWindowHandle,
        string? executablePath) : IProcessSnapshot
    {
        public int Id => id;
        public string ProcessName => processName;
        public nint MainWindowHandle => mainWindowHandle;
        public string? ExecutablePath => executablePath;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingProcessSnapshot : IProcessSnapshot
    {
        public int Id => throw new UnauthorizedAccessException();
        public string ProcessName => throw new UnauthorizedAccessException();
        public nint MainWindowHandle => throw new UnauthorizedAccessException();
        public string? ExecutablePath => throw new UnauthorizedAccessException();

        public void Dispose()
        {
        }
    }

    private sealed class WindowDeniedProcessSnapshot : IProcessSnapshot
    {
        public int Id => 200;
        public string ProcessName => "Codex";
        public nint MainWindowHandle => throw new UnauthorizedAccessException();
        public string? ExecutablePath =>
            @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0.0_x64__test\app\Codex.exe";

        public void Dispose()
        {
        }
    }
}
