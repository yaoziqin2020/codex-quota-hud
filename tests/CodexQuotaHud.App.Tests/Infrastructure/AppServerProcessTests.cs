using System.Diagnostics;
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
}
