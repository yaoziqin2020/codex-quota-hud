using System.ComponentModel;
using System.Diagnostics;
using CodexQuotaHud.App.UI.About;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class AboutLinkLauncherTests
{
    [Fact]
    public void TryOpen_UsesExactHttpsUrlAndShellExecution()
    {
        ProcessStartInfo? observed = null;

        var opened = AboutLinkLauncher.TryOpen(
            "https://github.com/yaoziqin2020/codex-quota-hud",
            info =>
            {
                observed = info;
                return true;
            },
            out var error);

        Assert.True(opened);
        Assert.Null(error);
        Assert.NotNull(observed);
        Assert.Equal(
            "https://github.com/yaoziqin2020/codex-quota-hud",
            observed.FileName);
        Assert.True(observed.UseShellExecute);
    }

    [Fact]
    public void TryOpen_WhenShellLaunchFailsReturnsStableMessage()
    {
        var opened = AboutLinkLauncher.TryOpen(
            "https://github.com/yaoziqin2020/codex-quota-hud",
            _ => throw new Win32Exception("browser unavailable"),
            out var error);

        Assert.False(opened);
        Assert.Equal("无法打开项目主页。", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://github.com/yaoziqin2020/codex-quota-hud")]
    [InlineData("not a url")]
    public void TryOpen_RejectsAnythingOtherThanAbsoluteHttps(string url)
    {
        var starts = 0;

        var opened = AboutLinkLauncher.TryOpen(
            url,
            _ =>
            {
                starts++;
                return true;
            },
            out var error);

        Assert.False(opened);
        Assert.Equal(0, starts);
        Assert.Equal("无法打开项目主页。", error);
    }
}
