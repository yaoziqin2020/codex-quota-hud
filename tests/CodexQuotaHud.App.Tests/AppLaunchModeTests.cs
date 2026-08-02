using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.Infrastructure.LocalControl;

namespace CodexQuotaHud.App.Tests;

public sealed class AppLaunchModeTests
{
    private const string CustomSelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";
    private const string InstalledExecutablePath =
        @"C:\Users\Test\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe";

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
    public void ActivationLaunch_ParsesAsDedicatedNonStartupMode()
    {
        var parsed = AppLaunchRequest.TryParse(
            ["--activate-skin", CustomSelectionKey],
            out var request,
            out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(
            new AppLaunchRequest(false, false, CustomSelectionKey),
            request);
        Assert.False(App.ShouldRegisterStartup(
            ["--activate-skin", CustomSelectionKey]));
    }

    [Theory]
    [MemberData(nameof(InvalidActivationArguments))]
    public void ActivationLaunch_RejectsMissingMalformedDuplicateOrMixedArguments(
        string[] arguments)
    {
        var parsed = AppLaunchRequest.TryParse(
            arguments,
            out var request,
            out var error);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal("Invalid launch arguments.", error);
    }

    public static TheoryData<string[]> InvalidActivationArguments => new()
    {
        new string[] { "--activate-skin" },
        new string[] { "--activate-skin", "builtin:HudDial" },
        new string[]
        {
            "--activate-skin",
            "custom:AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"
        },
        new string[] { "--activate-skin", new('x', 65) },
        new string[]
        {
            "--activate-skin",
            CustomSelectionKey,
            "--activate-skin",
            CustomSelectionKey
        },
        new string[] { "--preview", "--activate-skin", CustomSelectionKey },
        new string[] { "--background", "--activate-skin", CustomSelectionKey }
    };

    [Fact]
    public void InstalledExecutablePath_StartsShutdownListener()
    {
        Assert.True(App.ShouldStartInstalledShutdownListener(
            InstalledExecutablePath,
            InstalledExecutablePath));
    }

    [Fact]
    public void InstalledExecutablePathComparison_IsCaseInsensitive()
    {
        Assert.True(App.ShouldStartInstalledShutdownListener(
            InstalledExecutablePath.ToUpperInvariant(),
            InstalledExecutablePath));
    }

    [Fact]
    public void DevelopmentExecutablePath_DoesNotStartShutdownListener()
    {
        Assert.False(App.ShouldStartInstalledShutdownListener(
            @"C:\src\CodexQuotaHud\bin\CodexQuotaHud.App.exe",
            InstalledExecutablePath));
    }

    [Fact]
    public void UnavailableExecutablePath_DoesNotStartShutdownListener()
    {
        Assert.False(App.ShouldStartInstalledShutdownListener(
            currentExecutablePath: null,
            InstalledExecutablePath));
    }

    [Fact]
    public void InvalidExecutablePath_DoesNotStartShutdownListener()
    {
        Assert.False(App.ShouldStartInstalledShutdownListener(
            "\0",
            InstalledExecutablePath));
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
    public void PreviewLaunch_ThrowingReplacementAcquisitionReportsStableError()
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
            acquirePreview: () =>
                throw new ArgumentException("invalid installed path"),
            showError: messages.Add,
            out var lease);

        Assert.False(result);
        Assert.Null(lease);
        Assert.Equal(0, normalCalls);
        Assert.Equal(
            ["开发预览启动失败，无法安全检查或替换已安装正式版。"],
            messages);
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
    public void OccupiedActivationLaunch_ForwardsExactlyOnceAndExitsWithoutLease()
    {
        var forwarded = new List<string>();
        var messages = new List<string>();

        var acquired = App.TryAcquireNormalLaunch(
            CustomSelectionKey,
            acquireNormal: () => null,
            forwardActivation: key =>
            {
                forwarded.Add(key);
                return new LocalControlResponse(true, null, null);
            },
            showError: messages.Add,
            out var lease);

        Assert.False(acquired);
        Assert.Null(lease);
        Assert.Equal([CustomSelectionKey], forwarded);
        Assert.Empty(messages);
    }

    [Fact]
    public void FreeActivationLaunch_RetainsLeaseAndDefersActivationUntilComposition()
    {
        var expectedLease = new FakeLease();
        var forwards = 0;

        var acquired = App.TryAcquireNormalLaunch(
            CustomSelectionKey,
            acquireNormal: () => expectedLease,
            forwardActivation: _ =>
            {
                forwards++;
                return new LocalControlResponse(true, null, null);
            },
            showError: _ => { },
            out var lease);

        Assert.True(acquired);
        Assert.Same(expectedLease, lease);
        Assert.Equal(0, forwards);
    }

    [Fact]
    public void OccupiedActivationFailure_ShowsOneBoundedErrorWithoutRemoteText()
    {
        const string remoteText = "package-controlled-remote-text";
        var messages = new List<string>();

        var acquired = App.TryAcquireNormalLaunch(
            CustomSelectionKey,
            acquireNormal: () => null,
            forwardActivation: _ => new LocalControlResponse(
                false,
                "skin.activation.failed",
                remoteText),
            showError: messages.Add,
            out var lease);

        Assert.False(acquired);
        Assert.Null(lease);
        var message = Assert.Single(messages);
        Assert.Equal("皮肤激活失败（skin.activation.failed）。请从 HUD 菜单重试。", message);
        Assert.DoesNotContain(remoteText, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, null, true)]
    [InlineData(false, true, null, true)]
    [InlineData(false, false, CustomSelectionKey, true)]
    [InlineData(true, false, null, false)]
    public void LocalControlServer_StartsOnlyForNormalHud(
        bool preview,
        bool background,
        string? activationSelectionKey,
        bool expected)
    {
        Assert.Equal(
            expected,
            App.ShouldStartLocalControlServer(new AppLaunchRequest(
                preview,
                background,
                activationSelectionKey)));
    }

    [Fact]
    public void StartupActivation_InvokesBoundaryOnceAndReportsBoundedFailure()
    {
        var calls = 0;
        var messages = new List<string>();

        var activated = App.TryApplyLaunchActivation(
            CustomSelectionKey,
            key =>
            {
                Assert.Equal(CustomSelectionKey, key);
                calls++;
                return false;
            },
            messages.Add);

        Assert.False(activated);
        Assert.Equal(1, calls);
        Assert.Equal(
            ["皮肤激活失败（skin.activation.failed）。请从 HUD 菜单重试。"],
            messages);
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
    public async Task GracefulShutdown_DrainsLocalControlBeforeUiAndLeaseCleanup()
    {
        var events = new List<string>();

        await App.RunLocalControlFirstCleanupAsync(
            async () =>
            {
                events.Add("server-cancelled");
                await Task.Yield();
                events.Add("server-drained");
            },
            () => RecordCleanup("tray", events),
            () => RecordCleanup("window", events),
            () => RecordCleanup("view-model", events),
            () => RecordCleanup("single-instance", events));

        Assert.Equal(
            [
                "server-cancelled",
                "server-drained",
                "tray",
                "window",
                "view-model",
                "single-instance"
            ],
            events);
    }

    [Fact]
    public void EmergencyShutdown_StopsLocalControlBeforeUiAndLeaseCleanup()
    {
        var events = new List<string>();

        App.RunLocalControlFirstEmergencyCleanup(
            () => events.Add("server"),
            () => events.Add("tray"),
            () => events.Add("window"),
            () => events.Add("view-model"),
            () => events.Add("single-instance"));

        Assert.Equal(
            ["server", "tray", "window", "view-model", "single-instance"],
            events);
    }

    private static ValueTask RecordCleanup(string value, List<string> events)
    {
        events.Add(value);
        return ValueTask.CompletedTask;
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
