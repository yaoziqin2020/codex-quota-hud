using System.Diagnostics;
using System.ComponentModel;
using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class AppServerProcessTests
{
    [Fact]
    public void CreatesHiddenRedirectedStdioStartInfo()
    {
        const string executablePath = @"C:\Codex\codex.exe";

        var startInfo = AppServerProcess.CreateStartInfo(executablePath);

        Assert.Equal(executablePath, startInfo.FileName);
        Assert.Equal(["app-server", "--listen", "stdio://"], startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
    }

    [Fact]
    public void RejectsRelativeExecutablePath()
    {
        Assert.Throws<ArgumentException>(() =>
            AppServerProcess.CreateStartInfo(@"relative\codex.exe"));
    }

    [Fact]
    public void OwnsHudBeforeStartingChildAndVerifiesInheritedJob()
    {
        var child = new FakeChildProcess { HasExitedValue = true };
        var platform = new FakeAppServerProcessPlatform(child);

        using var process = AppServerProcess.Start(
            @"C:\Codex\codex.exe",
            platform,
            _ => true);

        Assert.Equal(["own-hud", "start-child", "verify-child"], platform.Calls);
    }

    [Fact]
    public void HudJobAssignmentFailurePreventsChildStart()
    {
        var platform = new FakeAppServerProcessPlatform(new FakeChildProcess())
        {
            OwnHudException = new Win32Exception(5)
        };

        Assert.Throws<Win32Exception>(() =>
            AppServerProcess.Start(@"C:\Codex\codex.exe", platform, _ => true));

        Assert.Equal(["own-hud"], platform.Calls);
    }

    [Fact]
    public void FailedInheritanceVerificationKillsWaitsAndDisposesChild()
    {
        var child = new FakeChildProcess
        {
            HasExitedValue = false,
            WaitForExitResult = true
        };
        var platform = new FakeAppServerProcessPlatform(child)
        {
            IsChildInJob = false
        };

        Assert.Throws<InvalidOperationException>(() =>
            AppServerProcess.Start(@"C:\Codex\codex.exe", platform, _ => true));

        Assert.Equal(["has-exited", "kill", "wait", "dispose"], child.Calls);
    }

    [Fact]
    public void CleanupFailureIsSurfacedWithStartupFailure()
    {
        var child = new FakeChildProcess
        {
            HasExitedValue = false,
            KillException = new Win32Exception(5),
            WaitForExitResult = false
        };
        var platform = new FakeAppServerProcessPlatform(child)
        {
            IsChildInJob = false
        };

        var error = Assert.Throws<AggregateException>(() =>
            AppServerProcess.Start(@"C:\Codex\codex.exe", platform, _ => true));

        Assert.Collection(
            error.InnerExceptions,
            exception => Assert.IsType<InvalidOperationException>(exception),
            exception => Assert.IsType<Win32Exception>(exception),
            exception => Assert.IsType<TimeoutException>(exception));
        Assert.Equal(["has-exited", "kill", "wait", "dispose"], child.Calls);
    }

    [Fact]
    public void DisposeWaitsAndSurfacesKillFailure()
    {
        var child = new FakeChildProcess
        {
            HasExitedValue = false,
            KillException = new Win32Exception(5),
            WaitForExitResult = true
        };
        var platform = new FakeAppServerProcessPlatform(child);
        var process = AppServerProcess.Start(
            @"C:\Codex\codex.exe",
            platform,
            _ => true);

        var error = Assert.Throws<AggregateException>(process.Dispose);

        Assert.Collection(
            error.InnerExceptions,
            exception => Assert.IsType<Win32Exception>(exception));
        Assert.Equal(["has-exited", "kill", "wait", "dispose"], child.Calls);
    }

    private sealed class FakeAppServerProcessPlatform(FakeChildProcess child)
        : IAppServerProcessPlatform
    {
        public List<string> Calls { get; } = [];
        public Exception? OwnHudException { get; init; }
        public bool IsChildInJob { get; init; } = true;

        public void EnsureCurrentProcessInKillOnCloseJob()
        {
            Calls.Add("own-hud");
            if (OwnHudException is not null)
            {
                throw OwnHudException;
            }
        }

        public IAppServerChildProcess Start(ProcessStartInfo startInfo)
        {
            Calls.Add("start-child");
            return child;
        }

        public bool IsInKillOnCloseJob(IAppServerChildProcess process)
        {
            Calls.Add("verify-child");
            return IsChildInJob;
        }
    }

    private sealed class FakeChildProcess : IAppServerChildProcess
    {
        public List<string> Calls { get; } = [];
        public bool HasExitedValue { get; set; }
        public Exception? KillException { get; init; }
        public bool WaitForExitResult { get; init; } = true;

        public TextWriter StandardInput => new StringWriter();
        public TextReader StandardOutput => new StringReader(string.Empty);
        public TextReader StandardError => new StringReader(string.Empty);

        public bool HasExited
        {
            get
            {
                Calls.Add("has-exited");
                return HasExitedValue;
            }
        }

        public void Kill()
        {
            Calls.Add("kill");
            if (KillException is not null)
            {
                throw KillException;
            }
        }

        public bool WaitForExit(TimeSpan timeout)
        {
            Calls.Add("wait");
            return WaitForExitResult;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            Calls.Add("wait-async");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Calls.Add("dispose");
        }
    }
}
