using System.Diagnostics;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Templates;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CodexQuotaHud.App.Tests.Infrastructure.LocalControl;

[Collection(global::CodexQuotaHud.App.Tests.UI.WpfUiCollection.Name)]
public sealed class LocalControlActivationHandlerTests
{
    private static readonly TimeSpan WpfDeadlockGuard = TimeSpan.FromSeconds(15);

    private const string SelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task HealthyCanonicalSkin_ActivatesExactlyOnceAndSucceedsAfterCompletion()
    {
        var activationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var handler = new LocalControlActivationHandler(
            key => key == SelectionKey,
            async (key, cancellationToken) =>
            {
                Assert.Equal(SelectionKey, key);
                Assert.True(Interlocked.Increment(ref calls) == 1);
                activationStarted.SetResult();
                return await allowCompletion.Task.WaitAsync(cancellationToken);
            });

        var handling = handler.HandleAsync(Request(SelectionKey), CancellationToken.None);
        await activationStarted.Task;
        Assert.False(handling.IsCompleted);

        allowCompletion.SetResult(true);
        var response = await handling;

        Assert.True(response.Succeeded);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task MissingOrCorruptSkin_IsRejectedWithoutActivation()
    {
        var calls = 0;
        var formalSelection = "builtin:EnergyRing";
        var handler = new LocalControlActivationHandler(
            _ => false,
            (_, _) =>
            {
                calls++;
                formalSelection = SelectionKey;
                return Task.FromResult(true);
            });

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("skin.selection.missing", response.ErrorCode);
        Assert.Equal(0, calls);
        Assert.Equal("builtin:EnergyRing", formalSelection);
    }

    [Fact]
    public async Task ProductionAppPath_ReloadsCatalogPreparesSavesAndActivatesRealWindow()
    {
        using var root = new TemporaryRoot();
        var ready = new TaskCompletionSource<ProductionFixture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var installed = new InstalledSkinCatalog(root.Paths, HudVersion);
                var catalog = new HudSkinCatalog(installed);
                var settings = new RecordingSettingsStore(new AppSettings(
                    SelectedSkinKey: SkinSelectionKey.EnergyRing));
                var viewModel = new QuotaOrbViewModel(
                    new InertRefreshController(),
                    settings,
                    settings.Load(),
                    new InlineDispatcher(),
                    () => { },
                    key => catalog.TryGet(key, out _));
                var controller = new SkinController(
                    catalog,
                    SkinTemplateRegistry.CreateDefault());
                var management = new SkinManagementController(
                    new SkinPackageInstaller(root.Paths, HudVersion),
                    catalog,
                    viewModel,
                    controller,
                    new DesignerLauncher(
                        root.Path,
                        _ => false,
                        _ => throw new InvalidOperationException()),
                    new SilentSkinManagementDialogs(),
                    HudVersion,
                    new InlineDispatcher());
                var window = new QuotaOrbWindow(viewModel, controller, management);
                var handler = new LocalControlActivationHandler(
                    key => catalog.Refresh().Healthy.Any(descriptor =>
                        string.Equals(
                            descriptor.SelectionKey,
                            key,
                            StringComparison.Ordinal)),
                    (key, cancellationToken) =>
                        LocalControlActivationHandler.InvokeOnDispatcherAsync(
                            dispatcher,
                            token => global::CodexQuotaHud.App.App
                                .TryActivateInstalledSkin(
                                    catalog,
                                    controller,
                                    window,
                                    key,
                                    token),
                            cancellationToken));
                ready.SetResult(new ProductionFixture(
                    dispatcher,
                    handler,
                    () => new ProductionSnapshot(
                        settings.SaveCount,
                        settings.Load().SelectedSkinKey,
                        viewModel.SelectedSkinKey,
                        controller.CurrentDescriptor.SelectionKey,
                        management.Entries.Any(entry => string.Equals(
                            entry.SelectionKey,
                            SelectionKey,
                            StringComparison.Ordinal)),
                        ReferenceEquals(
                            controller.CurrentView,
                            Assert.IsType<ContentControl>(
                                window.FindName("SkinHost")).Content)),
                    () =>
                    {
                        window.CloseForExit();
                        viewModel.Dispose();
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    }));
                Dispatcher.Run();
                stopped.TrySetResult(null);
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
                stopped.TrySetResult(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ProductionFixture? fixture = null;
        try
        {
            fixture = await ready.Task.WaitAsync(WpfDeadlockGuard);
            InstallCustomSkin(root.Paths);

            var response = await fixture.Handler.HandleAsync(
                Request(SelectionKey),
                CancellationToken.None);

            Assert.True(response.Succeeded);
            var snapshot = await fixture.Dispatcher.InvokeAsync(
                fixture.Snapshot,
                DispatcherPriority.Send).Task;
            Assert.Equal(1, snapshot.SaveCount);
            Assert.Equal(SelectionKey, snapshot.PersistedSelectionKey);
            Assert.Equal(SelectionKey, snapshot.ViewModelSelectionKey);
            Assert.Equal(SelectionKey, snapshot.ControllerSelectionKey);
            Assert.True(snapshot.MenuContainsSelection);
            Assert.True(snapshot.ViewMatchesController);
        }
        finally
        {
            if (fixture is not null && !fixture.Dispatcher.HasShutdownStarted)
            {
                await fixture.Dispatcher.InvokeAsync(
                    fixture.Dispose,
                    DispatcherPriority.Send).Task;
            }

            var failure = await stopped.Task.WaitAsync(WpfDeadlockGuard);
            Assert.Null(failure);
            Assert.True(thread.Join(WpfDeadlockGuard));
        }
    }

    [Fact]
    public async Task OfflineActivationFallbackUsesExactInstalledPathAndTwoArgumentListValues()
    {
        ProcessStartInfo? captured = null;
        var launcher = new InstalledAppLauncher(
            localAppData: Path.Combine(
                Path.GetTempPath(),
                "CodexQuotaHud-Task15-offline-" + Guid.NewGuid().ToString("N")),
            fileExists: _ => true,
            startProcess: info =>
            {
                captured = info;
                return true;
            });
        var requester = new HudActivationRequester(
            (_, _) => Task.FromResult(new LocalControlResponse(
                false,
                "control.unavailable",
                "No running HUD.")),
            key =>
            {
                var succeeded = launcher.TryLaunchActivation(key, out var error);
                return (succeeded, error);
            });

        var result = await requester.ActivateAsync(SelectionKey);

        Assert.Equal(HudActivationDisposition.StartedHud, result.Disposition);
        Assert.NotNull(captured);
        Assert.Equal(launcher.ExecutablePath, captured.FileName);
        Assert.True(Path.IsPathFullyQualified(captured.FileName));
        Assert.True(captured.UseShellExecute);
        Assert.Equal(string.Empty, captured.Arguments);
        Assert.Equal(["--activate-skin", SelectionKey], captured.ArgumentList);
    }

    [Fact]
    public async Task FailedActivation_ReturnsStableRejection()
    {
        var handler = new LocalControlActivationHandler(
            _ => true,
            (_, _) => Task.FromResult(false));

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("skin.activation.failed", response.ErrorCode);
    }

    [Fact]
    public async Task CommittedActivation_WinsCancellationBeforeHandlerConfirmation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new LocalControlActivationHandler(
            _ => true,
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(true);
            });

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            cancellation.Token);

        Assert.True(response.Succeeded);
    }

    [Fact]
    public async Task HandlerException_DoesNotLeakDetails()
    {
        const string packageControlled = "package-controlled-exception-detail";
        var handler = new LocalControlActivationHandler(
            _ => true,
            (_, _) => throw new InvalidOperationException(packageControlled));

        var response = await handler.HandleAsync(
            Request(SelectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("control.handler.failed", response.ErrorCode);
        Assert.DoesNotContain(
            packageControlled,
            response.Message ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builtin:HudDial")]
    [InlineData("custom:AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("custom:11111111111111111111111111111111")]
    public async Task BuiltinOrMalformedSelection_IsRejectedBeforeCatalogLookup(
        string selectionKey)
    {
        var lookups = 0;
        var handler = new LocalControlActivationHandler(
            _ =>
            {
                lookups++;
                return true;
            },
            (_, _) => Task.FromResult(true));

        var response = await handler.HandleAsync(
            Request(selectionKey),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("control.request.invalid", response.ErrorCode);
        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task ProductionDispatcherBoundary_CancelledWhileQueuedNeverExecutes()
    {
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseBlocker = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var dispatcher = await ready.Task;

        try
        {
            _ = dispatcher.BeginInvoke(
                () =>
                {
                    blockerStarted.SetResult();
                    releaseBlocker.Wait();
                },
                DispatcherPriority.Send);
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var cancellation = new CancellationTokenSource();
            var activations = 0;

            var queued = LocalControlActivationHandler.InvokeOnDispatcherAsync(
                dispatcher,
                _ =>
                {
                    Interlocked.Increment(ref activations);
                    return true;
                },
                cancellation.Token);
            cancellation.Cancel();
            releaseBlocker.Set();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(0, Volatile.Read(ref activations));
        }
        finally
        {
            releaseBlocker.Set();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        }
    }

    private static LocalControlRequest Request(string selectionKey) => new(
        LocalControlProtocol.ProtocolVersion,
        LocalControlCommandKind.ActivateSkin,
        selectionKey);

    private static readonly SemanticVersion HudVersion = SemanticVersion.Parse("1.1.1");

    private static void InstallCustomSkin(SkinStoragePaths paths)
    {
        var packagePath = Path.Combine(
            Path.GetDirectoryName(paths.SettingsRoot)!,
            "handler.cqskin");
        var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
        var written = new SkinPackageWriter().WriteFile(
            packagePath,
            new SkinPackageBuildRequest(
                new SkinManifest(
                    1,
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Handler skin",
                    "Test author",
                    SemanticVersion.Parse("1.0.0"),
                    "Production activation handler fixture",
                    SkinPackageLimits.FreeDecorationRingTemplateId,
                    HudVersion,
                    null,
                    []),
                new SkinTheme(
                    1,
                    SkinPackageLimits.FreeDecorationRingTemplateId,
                    identity,
                    identity,
                    identity,
                    "#FF53DCF8",
                    "#FF9A68FF",
                    "#FF0A1622",
                    0.9,
                    96,
                    8,
                    6,
                    270,
                    "#FF24CFF2",
                    0.5,
                    28,
                    12,
                    SkinTextWeight.SemiBold,
                    SkinTextPlacement.NumberAboveLabel,
                    new SkinAnimationSettings(0.25, 0.5, 0.75, 1)),
                new Dictionary<SkinAssetSlot, SkinAsset>()),
            overwrite: false,
            CancellationToken.None);
        Assert.True(written.IsValid);
        var installer = new SkinPackageInstaller(paths, HudVersion);
        var inspected = installer.Inspect(
            packagePath,
            HudVersion,
            CancellationToken.None);
        Assert.True(inspected.IsValid);
        var installed = installer.Install(
            inspected.Value!,
            SkinCollisionDecision.Replace,
            CancellationToken.None);
        Assert.NotNull(installed.Installed);
        Assert.Empty(installed.Errors);
        File.Delete(packagePath);
    }

    private sealed record ProductionFixture(
        Dispatcher Dispatcher,
        LocalControlActivationHandler Handler,
        Func<ProductionSnapshot> Snapshot,
        Action Dispose);

    private sealed record ProductionSnapshot(
        int SaveCount,
        string PersistedSelectionKey,
        string ViewModelSelectionKey,
        string ControllerSelectionKey,
        bool MenuContainsSelection,
        bool ViewMatchesController);

    private sealed class SilentSkinManagementDialogs : ISkinManagementDialogs
    {
        public string? ChoosePackagePath() => null;

        public SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview) =>
            SkinCollisionDecision.Cancel;

        public bool ConfirmRemoval(SkinMenuEntry entry) => false;

        public void ShowError(string message)
        {
        }
    }

    private sealed class RecordingSettingsStore(AppSettings initial) : ISettingsStore
    {
        private AppSettings _settings = initial;

        public int SaveCount { get; private set; }

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings)
        {
            SaveCount++;
            _settings = settings;
        }
    }

    private sealed class InertRefreshController : IQuotaRefreshController
    {
        public event Action<QuotaRefreshState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task RefreshNowAsync(
            bool onlyIfStale,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-handler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new SkinStoragePaths(Path);
        }

        public string Path { get; }

        public SkinStoragePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
