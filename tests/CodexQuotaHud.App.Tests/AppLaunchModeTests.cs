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
    public void PreviewLaunch_UsesReplacementAcquisitionAndReportsFailure()
    {
        var normalCalls = 0;
        var messages = new List<string>();

        var result = App.TryAcquireForLaunch(
            preview: true,
            acquireNormal: () =>
            {
                normalCalls++;
                return new FakeLease();
            },
            acquirePreview: () => (false, null, "无法关闭正式版"),
            showError: messages.Add,
            out var lease);

        Assert.False(result);
        Assert.Null(lease);
        Assert.Equal(0, normalCalls);
        Assert.Equal(["无法关闭正式版"], messages);
    }

    [Fact]
    public void PreviewLaunch_PreservesReplacementLeaseOnSuccess()
    {
        var expectedLease = new FakeLease();
        var normalCalls = 0;
        var messages = new List<string>();

        var result = App.TryAcquireForLaunch(
            preview: true,
            acquireNormal: () =>
            {
                normalCalls++;
                return new FakeLease();
            },
            acquirePreview: () => (true, expectedLease, null),
            showError: messages.Add,
            out var lease);

        Assert.True(result);
        Assert.Same(expectedLease, lease);
        Assert.Equal(0, normalCalls);
        Assert.Empty(messages);
    }

    [Fact]
    public void NormalLaunch_DoesNotInvokeReplacementOrShowError()
    {
        var replacementCalls = 0;
        var messages = new List<string>();

        var result = App.TryAcquireForLaunch(
            preview: false,
            acquireNormal: () => null,
            acquirePreview: () =>
            {
                replacementCalls++;
                return (true, new FakeLease(), null);
            },
            showError: messages.Add,
            out var lease);

        Assert.False(result);
        Assert.Null(lease);
        Assert.Equal(0, replacementCalls);
        Assert.Empty(messages);
    }

    [Fact]
    public void NormalLaunch_PreservesSingleInstanceLeaseOnSuccess()
    {
        var expectedLease = new FakeLease();
        var replacementCalls = 0;
        var messages = new List<string>();

        var result = App.TryAcquireForLaunch(
            preview: false,
            acquireNormal: () => expectedLease,
            acquirePreview: () =>
            {
                replacementCalls++;
                return (true, new FakeLease(), null);
            },
            showError: messages.Add,
            out var lease);

        Assert.True(result);
        Assert.Same(expectedLease, lease);
        Assert.Equal(0, replacementCalls);
        Assert.Empty(messages);
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

    private sealed class FakeLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
