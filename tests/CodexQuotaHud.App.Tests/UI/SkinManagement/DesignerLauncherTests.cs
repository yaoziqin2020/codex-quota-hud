using System.ComponentModel;
using System.Diagnostics;
using CodexQuotaHud.App.UI.SkinManagement;

namespace CodexQuotaHud.App.Tests.UI.SkinManagement;

public sealed class DesignerLauncherTests
{
    [Fact]
    public void ExpectedPathAndAvailabilityUseOnlyExactInstalledChildPath()
    {
        var appDirectory = Path.Combine("C:\\", "Program Files", "CodexQuotaHud");
        string? checkedPath = null;
        var starts = 0;
        var launcher = new DesignerLauncher(
            appDirectory,
            path =>
            {
                checkedPath = path;
                return false;
            },
            _ =>
            {
                starts++;
                return true;
            });

        Assert.Equal(
            Path.Combine(
                appDirectory,
                "designer",
                "CodexQuotaHud.SkinDesigner.exe"),
            launcher.ExpectedExecutablePath);
        Assert.False(launcher.IsAvailable);
        Assert.Equal(launcher.ExpectedExecutablePath, checkedPath);
        Assert.False(launcher.TryLaunch(out var error));
        Assert.Contains("not installed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void TryLaunch_StartsExactFullPathWithShellAndNoArgumentsOrSearch()
    {
        var appDirectory = Path.Combine("C:\\", "Apps", "CodexQuotaHud");
        ProcessStartInfo? started = null;
        var launcher = new DesignerLauncher(
            appDirectory,
            _ => true,
            info =>
            {
                started = info;
                return true;
            });

        var launched = launcher.TryLaunch(out var error);

        Assert.True(launched, error);
        Assert.Null(error);
        Assert.NotNull(started);
        Assert.Equal(launcher.ExpectedExecutablePath, started.FileName);
        Assert.True(Path.IsPathFullyQualified(started.FileName));
        Assert.True(started.UseShellExecute);
        Assert.Equal(string.Empty, started.Arguments);
        Assert.Equal(string.Empty, started.WorkingDirectory);
    }

    [Fact]
    public void TryLaunch_ContainsFalseStartAndWin32FailureWithoutFallback()
    {
        var calls = 0;
        var falseLauncher = new DesignerLauncher(
            Path.Combine("C:\\", "Apps", "CodexQuotaHud"),
            _ => true,
            _ =>
            {
                calls++;
                return false;
            });

        Assert.False(falseLauncher.TryLaunch(out var falseError));
        Assert.Contains("did not start", falseError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, calls);

        var failingLauncher = new DesignerLauncher(
            Path.Combine("C:\\", "Apps", "CodexQuotaHud"),
            _ => true,
            _ =>
            {
                calls++;
                throw new Win32Exception("file disappeared");
            });

        Assert.False(failingLauncher.TryLaunch(out var failureError));
        Assert.Contains("file disappeared", failureError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, calls);
    }
}
