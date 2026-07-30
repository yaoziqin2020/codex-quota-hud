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

    [Fact]
    public void ExitHandoff_LaunchesOnlyAfterCleanup()
    {
        var events = new List<string>();

        App.CompleteExit(
            openInstalled: true,
            cleanup: () => events.Add("cleanup"),
            launch: () =>
            {
                events.Add("launch");
                return true;
            },
            traceError: _ => events.Add("error"));

        Assert.Equal(["cleanup", "launch"], events);
    }

    [Fact]
    public void ExitHandoff_NormalExitDoesNotLaunchAndFailureIsContained()
    {
        var normalLaunches = 0;
        App.CompleteExit(
            openInstalled: false,
            cleanup: () => { },
            launch: () => { normalLaunches++; return true; },
            traceError: _ => { });

        var events = new List<string>();
        var exception = Record.Exception(() => App.CompleteExit(
            openInstalled: true,
            cleanup: () => events.Add("cleanup"),
            launch: () => false,
            traceError: message => events.Add(message)));

        Assert.Equal(0, normalLaunches);
        Assert.Null(exception);
        Assert.Equal(["cleanup", "正式版启动失败"], events);
    }
}
