using CodexQuotaHud.App.UI.About;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class AboutInformationTests
{
    [Theory]
    [InlineData(1, 2, 0, 99, "1.2.0")]
    [InlineData(7, 4, 19, 3, "7.4.19")]
    public void FormatVersion_UsesThreeNumericComponents(
        int major,
        int minor,
        int build,
        int revision,
        string expected)
    {
        var version = new Version(major, minor, build, revision);

        Assert.Equal(expected, AboutInformation.FormatVersion(version));
    }

    [Fact]
    public void FormatVersion_UsesSafeFallbackForUnavailableBuild()
    {
        Assert.Equal("未知", AboutInformation.FormatVersion(null));
        Assert.Equal("未知", AboutInformation.FormatVersion(new Version(1, 2)));
    }

    [Fact]
    public void Current_UsesPublicProjectIdentity()
    {
        var current = AboutInformation.Current;

        Assert.Equal("Codex Quota HUD", current.ProductName);
        Assert.Equal("老姚", current.Author);
        Assert.Equal("yaoziqin2020/codex-quota-hud", current.RepositoryLabel);
        Assert.Equal(
            "https://github.com/yaoziqin2020/codex-quota-hud",
            current.RepositoryUrl);
        Assert.Equal("MIT License", current.LicenseName);
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+$", current.VersionText);
    }
}
