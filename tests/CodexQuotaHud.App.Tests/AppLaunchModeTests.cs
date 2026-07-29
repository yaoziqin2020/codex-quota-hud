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
}
