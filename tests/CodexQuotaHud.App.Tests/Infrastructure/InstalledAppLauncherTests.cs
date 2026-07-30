using System.Diagnostics;
using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class InstalledAppLauncherTests
{
    [Fact]
    public void ResolvesCurrentUserInstalledExecutable()
    {
        var launcher = new InstalledAppLauncher(
            localAppData: @"C:\Users\Test\AppData\Local",
            fileExists: _ => true,
            startProcess: _ => true);

        Assert.Equal(
            @"C:\Users\Test\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe",
            launcher.ExecutablePath);
        Assert.True(launcher.IsAvailable);
    }

    [Fact]
    public void TryLaunch_StartsExactAbsolutePathWithoutArguments()
    {
        ProcessStartInfo? captured = null;
        var launcher = new InstalledAppLauncher(
            localAppData: @"C:\Users\Test\AppData\Local",
            fileExists: _ => true,
            startProcess: info =>
            {
                captured = info;
                return true;
            });

        Assert.True(launcher.TryLaunch(out var error));
        Assert.Null(error);
        Assert.Equal(launcher.ExecutablePath, captured!.FileName);
        Assert.Empty(captured.ArgumentList);
        Assert.True(captured.UseShellExecute);
    }

    [Fact]
    public void MissingOrFailedLaunch_ReturnsErrorWithoutStarting()
    {
        var starts = 0;
        var missing = new InstalledAppLauncher(
            @"C:\Missing",
            _ => false,
            _ => { starts++; return true; });
        var failing = new InstalledAppLauncher(
            @"C:\Present",
            _ => true,
            _ => throw new InvalidOperationException("start failed"));

        Assert.False(missing.TryLaunch(out var missingError));
        Assert.Equal("未找到已安装正式版", missingError);
        Assert.Equal(0, starts);
        Assert.False(failing.TryLaunch(out var startError));
        Assert.Contains("start failed", startError);
    }
}
