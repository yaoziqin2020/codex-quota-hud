namespace CodexQuotaHud.App.Tests;

public sealed class AppLaunchModeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(true, "--other")]
    [InlineData(false, "--background")]
    [InlineData(false, "--BACKGROUND")]
    public void InteractiveLaunch_ExcludesBackgroundStartup(
        bool expected,
        params string[] arguments)
    {
        Assert.Equal(expected, App.IsInteractiveLaunch(arguments));
    }

    [Theory]
    [InlineData(true, "--preview")]
    [InlineData(true, "--PREVIEW")]
    [InlineData(false)]
    [InlineData(false, "--background")]
    public void PreviewLaunch_RequiresPreviewArgument(
        bool expected,
        params string[] arguments)
    {
        Assert.Equal(expected, App.IsPreviewLaunch(arguments));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(true, "--other")]
    [InlineData(false, "--background")]
    [InlineData(false, "--preview")]
    [InlineData(false, "--preview", "--other")]
    public void StartupRegistration_OnlyRunsForNormalInteractiveLaunch(
        bool expected,
        params string[] arguments)
    {
        Assert.Equal(expected, App.ShouldRegisterStartup(arguments));
    }
}
