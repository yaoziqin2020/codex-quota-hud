using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Security;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.About;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Templates;

namespace CodexQuotaHud.App;

public partial class App : System.Windows.Application
{
    internal const string PreviewStartupFailureMessage =
        "开发预览启动失败，无法安全检查或替换已安装正式版。";

    private IDisposable? _singleInstance;
    private LocalControlServer? _localControlServer;
    private InstalledAppShutdownListener? _shutdownListener;
    private CodexProcessMonitor? _processMonitor;
    private RestartableQuotaClient? _quotaClient;
    private QuotaRefreshService? _refreshService;
    private CodexRunningCoordinator? _runningCoordinator;
    private QuotaOrbViewModel? _viewModel;
    private QuotaOrbWindow? _window;
    private TrayController? _tray;
    private AboutWindowCoordinator? _about;
    private PreviewComposition? _previewComposition;
    private InstalledAppLauncher? _installedAppLauncher;
    private int _openInstalledAfterExit;
    private int _shutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!AppLaunchRequest.TryParse(
                e.Args,
                out var launchRequest,
                out _))
        {
            System.Windows.MessageBox.Show(
                "启动参数无效。",
                "Codex Quota HUD",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var preview = launchRequest!.IsPreview;
        InstalledAppLauncher? installedAppLauncher = null;
        var acquired = preview
            ? TryAcquireForLaunch(
                preview: true,
                () => SingleInstanceGuard.TryAcquire(),
                () =>
                {
                    installedAppLauncher = new InstalledAppLauncher();
                    var coordinator = new InstalledAppShutdownCoordinator(
                        () => SingleInstanceGuard.TryAcquire(),
                        installedAppLauncher.ExecutablePath,
                        new InstalledAppShutdownPlatform());
                    var success = coordinator.TryAcquireForPreview(
                        out var lease,
                        out var error);
                    return (success, lease, error);
                },
                message => System.Windows.MessageBox.Show(
                    message,
                    "Codex Quota HUD — 开发预览",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning),
                out _singleInstance)
            : TryAcquireNormalLaunch(
                launchRequest.ActivationSelectionKey,
                () => SingleInstanceGuard.TryAcquire(),
                selectionKey => new LocalControlClient(
                        LocalControlProtocol.PipeName)
                    .SendAsync(new LocalControlRequest(
                        LocalControlProtocol.ProtocolVersion,
                        LocalControlCommandKind.ActivateSkin,
                        selectionKey))
                    .GetAwaiter()
                    .GetResult(),
                message => System.Windows.MessageBox.Show(
                    message,
                    "Codex Quota HUD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning),
                out _singleInstance);
        if (!acquired)
        {
            Shutdown();
            return;
        }

        try
        {
            if (preview)
            {
                _previewComposition = new PreviewComposition(
                    Dispatcher,
                    RequestExit,
                    installedAppLauncher!);
                _installedAppLauncher =
                    _previewComposition.InstalledAppLauncher;
                _previewComposition.OpenInstalledRequested +=
                    OnOpenInstalledRequested;
                _previewComposition.Show();
                return;
            }

            var installedExecutablePath =
                TryResolveInstalledExecutablePath();
            if (ShouldStartInstalledShutdownListener(
                    Environment.ProcessPath,
                    installedExecutablePath))
            {
                _shutdownListener = new InstalledAppShutdownListener(
                    () => Dispatcher.BeginInvoke(RequestExit));
            }
            var storagePaths = new SkinStoragePaths(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData));
            var installedCatalog = new InstalledSkinCatalog(
                storagePaths,
                CurrentHudVersion());
            var hudCatalog = new HudSkinCatalog(installedCatalog);
            var templates = SkinTemplateRegistry.CreateDefault();
            var startupSettings = LoadNormalSkinSettings(
                Path.Combine(storagePaths.SettingsRoot, "settings.json"),
                hudCatalog);
            var settingsStore = startupSettings.Store;
            var requestedSelectionKey = startupSettings.RequestedSelectionKey;
            var settings = startupSettings.Settings;

            _processMonitor = new CodexProcessMonitor();
            _quotaClient = new RestartableQuotaClient();
            _refreshService = new QuotaRefreshService(
                _quotaClient,
                new SystemClock());
            _runningCoordinator = new CodexRunningCoordinator(
                _refreshService.SetCodexRunningAsync,
                _quotaClient.ResetAsync);
            _viewModel = new QuotaOrbViewModel(
                new QuotaRefreshController(_refreshService),
                settingsStore,
                settings,
                new WpfUiDispatcher(Dispatcher),
                RequestExit,
                key => hudCatalog.TryGet(key, out _));
            var skinController = new SkinController(hudCatalog, templates);
            _ = TryApplyStartupSkinSelection(
                requestedSelectionKey,
                _viewModel,
                skinController,
                message => System.Windows.MessageBox.Show(
                    message,
                    "Codex Quota HUD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            var skinManagement = new SkinManagementController(
                new SkinPackageInstaller(storagePaths, CurrentHudVersion()),
                hudCatalog,
                _viewModel,
                skinController,
                new DesignerLauncher(AppContext.BaseDirectory),
                new SkinManagementDialogs(() => _window),
                CurrentHudVersion(),
                new WpfUiDispatcher(Dispatcher));
            _about = new AboutWindowCoordinator();
            _window = new QuotaOrbWindow(
                _viewModel,
                skinController,
                skinManagement,
                _about.Show);
            _tray = new TrayController(
                _viewModel,
                hudCatalog,
                skinController,
                _window.TryActivateSkinKey,
                skinManagement,
                _about.Show);

            bool TryActivateInstalledSkin(
                string selectionKey,
                CancellationToken cancellationToken = default) =>
                global::CodexQuotaHud.App.App.TryActivateInstalledSkin(
                    hudCatalog,
                    skinController,
                    _window,
                    selectionKey,
                    cancellationToken);

            if (ShouldStartLocalControlServer(launchRequest))
            {
                var activationHandler = new LocalControlActivationHandler(
                    key => hudCatalog.Refresh().Healthy.Any(descriptor =>
                        string.Equals(
                            descriptor.SelectionKey,
                            key,
                            StringComparison.Ordinal)),
                    (key, cancellationToken) =>
                        LocalControlActivationHandler.InvokeOnDispatcherAsync(
                            Dispatcher,
                            token => TryActivateInstalledSkin(key, token),
                            cancellationToken));
                _localControlServer = new LocalControlServer(
                    LocalControlProtocol.PipeName,
                    activationHandler.HandleAsync);
                _localControlServer.Start();

                _ = TryApplyLaunchActivation(
                    launchRequest.ActivationSelectionKey,
                    key => TryActivateInstalledSkin(key),
                    message => System.Windows.MessageBox.Show(
                        message,
                        "Codex Quota HUD",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
            }

            _processMonitor.RunningChanged += OnCodexRunningChanged;
            OnCodexRunningChanged(_processMonitor.IsRunning);

            if (ShouldRegisterStartup(e.Args))
            {
                if (!new StartupRegistration().TryEnable(out var error))
                {
                    Trace.TraceWarning(
                        "Could not register CodexQuotaHud startup: {0}",
                        error);
                }
            }
        }
        catch
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
            {
                _ = ShutdownAfterStartupFailureAsync();
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_processMonitor is not null)
        {
            _processMonitor.RunningChanged -= OnCodexRunningChanged;
        }

        var openInstalled =
            Interlocked.Exchange(ref _openInstalledAfterExit, 0) != 0;
        string? launchError = null;
        CompleteExit(
            openInstalled,
            () => RunLocalControlFirstEmergencyCleanup(
                () => _localControlServer?.DisposeAsync().AsTask()
                    .GetAwaiter().GetResult(),
                () => _runningCoordinator?.DisposeAsync().AsTask()
                    .GetAwaiter().GetResult(),
                () => _refreshService?.DisposeAsync().AsTask()
                    .GetAwaiter().GetResult(),
                () => _quotaClient?.DisposeAsync().AsTask()
                    .GetAwaiter().GetResult(),
                () => _processMonitor?.DisposeAsync().AsTask()
                    .GetAwaiter().GetResult(),
                () => _tray?.Dispose(),
                () => _previewComposition?.Dispose(),
                () => _about?.Dispose(),
                () => _window?.CloseForExit(),
                () => _viewModel?.Dispose(),
                () => _shutdownListener?.Dispose(),
                () => _singleInstance?.Dispose()),
            () => _installedAppLauncher?.TryLaunch(out launchError) == true,
            message => Trace.TraceWarning(
                "{0}{1}",
                message,
                launchError is null ? string.Empty : $": {launchError}"));
        base.OnExit(e);
    }

    internal static bool TryActivateInstalledSkin(
        HudSkinCatalog hudCatalog,
        SkinController skinController,
        QuotaOrbWindow window,
        string selectionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hudCatalog);
        ArgumentNullException.ThrowIfNull(skinController);
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = hudCatalog.Refresh();
        cancellationToken.ThrowIfCancellationRequested();
        if (!snapshot.Healthy.Any(descriptor => string.Equals(
                descriptor.SelectionKey,
                selectionKey,
                StringComparison.Ordinal)) ||
            !ReferenceEquals(window.SkinController, skinController) ||
            !window.SynchronizeSkinCatalog(snapshot))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return window.TryActivateSkinKey(selectionKey, cancellationToken);
    }

    internal static bool IsInteractiveLaunch(IReadOnlyList<string> arguments) =>
        !arguments.Any(
            argument => string.Equals(
                argument,
                "--background",
                StringComparison.OrdinalIgnoreCase));

    internal static bool IsPreviewLaunch(IReadOnlyList<string> arguments) =>
        arguments.Any(
            argument => string.Equals(
                argument,
                "--preview",
                StringComparison.OrdinalIgnoreCase));

    internal static bool ShouldRegisterStartup(IReadOnlyList<string> arguments) =>
        AppLaunchRequest.TryParse(arguments, out var request, out _) &&
        request!.ActivationSelectionKey is null &&
        IsInteractiveLaunch(arguments) &&
        !IsPreviewLaunch(arguments);

    internal static bool TryApplyStartupSkinSelection(
        string requestedSelectionKey,
        QuotaOrbViewModel viewModel,
        SkinController controller,
        Action<string> showMessage)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(showMessage);

        controller.Render(viewModel.SkinState);

        if (controller.TryPrepare(
                requestedSelectionKey,
                out var requested,
                out var failure))
        {
            controller.Activate(requested!);
            return true;
        }

        var safePrepared = controller.TryPrepare(
            SkinSelectionKey.HudDial,
            out var safe,
            out _);
        var persisted = safePrepared &&
            viewModel.TrySelectSkinKey(SkinSelectionKey.HudDial);
        if (persisted)
        {
            controller.Activate(safe!);
        }

        var identity = failure?.DisplayNameOrId ?? requestedSelectionKey;
        const string customPrefix = "custom:";
        if (identity.StartsWith(customPrefix, StringComparison.Ordinal))
        {
            identity = identity[customPrefix.Length..];
        }

        showMessage(persisted
            ? $"自定义皮肤“{identity}”无法加载，已切换到 HUD 科技仪表。请重新导入或删除该皮肤。"
            : $"自定义皮肤“{identity}”无法加载。当前仅临时使用 HUD 科技仪表，设置未能保存；请检查设置文件权限后重新选择。");
        return false;
    }

    internal static NormalSkinStartupSettings LoadNormalSkinSettings(
        string settingsPath,
        HudSkinCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(catalog);

        string? validatedSelectionKey = null;
        var store = new SettingsStore(
            settingsPath,
            key =>
            {
                validatedSelectionKey = key;
                return catalog.TryGet(key, out _);
            });
        var loadResult = store.LoadWithMigration();
        var requestedSelectionKey = loadResult.SelectionErrorCode is not null &&
            validatedSelectionKey is not null
                ? validatedSelectionKey
                : loadResult.Settings.SelectedSkinKey;
        var settings = loadResult.Settings with
        {
            SelectedSkinKey = requestedSelectionKey
        };
        if (loadResult.RequiresWriteBack &&
            loadResult.SelectionErrorCode is null)
        {
            TryPersistStartupMigration(store, loadResult.Settings);
        }

        return new NormalSkinStartupSettings(
            store,
            settings,
            requestedSelectionKey);
    }

    internal static AppSettings LoadSettingsForStartup(SettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        var loadResult = settingsStore.LoadWithMigration();
        if (loadResult.RequiresWriteBack)
        {
            try
            {
                settingsStore.Save(loadResult.Settings);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                Trace.TraceWarning(
                    "Could not persist settings migration: {0}",
                    exception);
            }
        }

        return loadResult.Settings;
    }

    private static void TryPersistStartupMigration(
        SettingsStore settingsStore,
        AppSettings settings)
    {
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning(
                "Could not persist settings migration: {0}",
                exception);
        }
    }

    private static SemanticVersion CurrentHudVersion()
    {
          var version = typeof(App).Assembly.GetName().Version;
          var detected = version is null
              ? SemanticVersion.Parse("1.2.0")
            : new SemanticVersion(
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, version.Build));
          var runtimeBaseline = SemanticVersion.Parse("1.2.0");
        return detected.CompareTo(runtimeBaseline) >= 0
            ? detected
            : runtimeBaseline;
    }

    internal static bool ShouldStartInstalledShutdownListener(
        string? currentExecutablePath,
        string? installedExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(currentExecutablePath) ||
            string.IsNullOrWhiteSpace(installedExecutablePath))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(currentExecutablePath) ||
                !Path.IsPathFullyQualified(installedExecutablePath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(currentExecutablePath),
                Path.GetFullPath(installedExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryAcquireForLaunch(
        bool preview,
        Func<IDisposable?> acquireNormal,
        Func<(bool Success, IDisposable? Lease, string? Error)> acquirePreview,
        Action<string> showError,
        out IDisposable? lease)
    {
        if (!preview)
        {
            lease = acquireNormal();
            return lease is not null;
        }

        (bool Success, IDisposable? Lease, string? Error) result;
        try
        {
            result = acquirePreview();
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(
                "Could not prepare developer-preview startup: {0}",
                exception);
            lease = null;
            showError(PreviewStartupFailureMessage);
            return false;
        }

        lease = result.Lease;
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            showError(result.Error);
        }

        return result.Success;
    }

    internal static bool TryAcquireNormalLaunch(
        string? activationSelectionKey,
        Func<IDisposable?> acquireNormal,
        Func<string, LocalControlResponse> forwardActivation,
        Action<string> showError,
        out IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(acquireNormal);
        ArgumentNullException.ThrowIfNull(forwardActivation);
        ArgumentNullException.ThrowIfNull(showError);

        lease = acquireNormal();
        if (lease is not null || activationSelectionKey is null)
        {
            return lease is not null;
        }

        LocalControlResponse response;
        try
        {
            response = forwardActivation(activationSelectionKey);
        }
        catch (Exception)
        {
            response = new LocalControlResponse(
                false,
                "control.failed",
                null);
        }

        if (!response.Succeeded)
        {
            showError(ActivationFailureMessage(response.ErrorCode));
        }

        return false;
    }

    internal static bool ShouldStartLocalControlServer(AppLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return !request.IsPreview;
    }

    internal static bool TryApplyLaunchActivation(
        string? activationSelectionKey,
        Func<string, bool> activate,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(showError);
        if (activationSelectionKey is null)
        {
            return true;
        }

        try
        {
            if (activate(activationSelectionKey))
            {
                return true;
            }
        }
        catch (Exception)
        {
        }

        showError(ActivationFailureMessage("skin.activation.failed"));
        return false;
    }

    private static string ActivationFailureMessage(string? errorCode)
    {
        var stableCode = errorCode switch
        {
            "control.unavailable" => errorCode,
            "control.timeout" => errorCode,
            "control.protocol.invalid" => errorCode,
            "control.request.invalid" => errorCode,
            "control.handler.failed" => errorCode,
            "control.failed" => errorCode,
            "skin.selection.missing" => errorCode,
            "skin.activation.failed" => errorCode,
            _ => "control.failed"
        };
        return $"皮肤激活失败（{stableCode}）。请从 HUD 菜单重试。";
    }

    private static string? TryResolveInstalledExecutablePath()
    {
        try
        {
            return new InstalledAppLauncher().ExecutablePath;
        }
        catch (Exception exception)
        {
            Trace.TraceWarning(
                "Could not resolve installed HUD path for shutdown listener: {0}",
                exception);
            return null;
        }
    }

    internal static void CompleteExit(
        bool openInstalled,
        Action cleanup,
        Func<bool> launch,
        Action<string> traceError)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(traceError);
        cleanup();
        if (openInstalled && !launch())
        {
            traceError("正式版启动失败");
        }
    }

    private void OnOpenInstalledRequested(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _openInstalledAfterExit, 1) == 0)
        {
            RequestExit();
        }
    }

    private void OnCodexRunningChanged(bool isRunning)
    {
        try
        {
            _ = _runningCoordinator?.SetDesiredStateAsync(isRunning);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RequestExit()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _ = ShutdownFromUiAsync();
    }

    private async Task ShutdownFromUiAsync()
    {
        try
        {
            await ShutdownResourcesAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "CodexQuotaHud shutdown cleanup failed: {0}",
                exception);
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task ShutdownAfterStartupFailureAsync()
    {
        try
        {
            await ShutdownResourcesAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "CodexQuotaHud startup-failure cleanup failed: {0}",
                exception);
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task ShutdownResourcesAsync()
    {
        if (_processMonitor is not null)
        {
            _processMonitor.RunningChanged -= OnCodexRunningChanged;
        }

        await RunLocalControlFirstCleanupAsync(
            async () =>
            {
                var localControlServer = _localControlServer;
                _localControlServer = null;
                if (localControlServer is not null)
                {
                    await localControlServer.DisposeAsync();
                }
            },
            () =>
            {
                var previewComposition = _previewComposition;
                _previewComposition = null;
                previewComposition?.Dispose();
                return ValueTask.CompletedTask;
            },
            async () =>
            {
                var coordinator = _runningCoordinator;
                _runningCoordinator = null;
                if (coordinator is not null)
                {
                    await coordinator.DisposeAsync();
                }
            },
            async () =>
            {
                var refreshService = _refreshService;
                _refreshService = null;
                if (refreshService is not null)
                {
                    await refreshService.DisposeAsync();
                }
            },
            async () =>
            {
                var quotaClient = _quotaClient;
                _quotaClient = null;
                if (quotaClient is not null)
                {
                    await quotaClient.DisposeAsync();
                }
            },
            async () =>
            {
                var processMonitor = _processMonitor;
                _processMonitor = null;
                if (processMonitor is not null)
                {
                    await processMonitor.DisposeAsync();
                }
            },
            () =>
            {
                var tray = _tray;
                _tray = null;
                tray?.Dispose();
                return ValueTask.CompletedTask;
            },
            () =>
            {
                var window = _window;
                _window = null;
                window?.CloseForExit();
                return ValueTask.CompletedTask;
            },
            () =>
            {
                var viewModel = _viewModel;
                _viewModel = null;
                viewModel?.Dispose();
                return ValueTask.CompletedTask;
            },
            () =>
            {
                var shutdownListener = _shutdownListener;
                _shutdownListener = null;
                shutdownListener?.Dispose();
                return ValueTask.CompletedTask;
            },
            () =>
            {
                var singleInstance = _singleInstance;
                _singleInstance = null;
                singleInstance?.Dispose();
                return ValueTask.CompletedTask;
            });
    }

    internal static Task RunLocalControlFirstCleanupAsync(
        Func<ValueTask> stopLocalControl,
        params Func<ValueTask>[] remainingCleanup)
    {
        ArgumentNullException.ThrowIfNull(stopLocalControl);
        ArgumentNullException.ThrowIfNull(remainingCleanup);
        var cleanupActions = new Func<ValueTask>[remainingCleanup.Length + 1];
        cleanupActions[0] = stopLocalControl;
        Array.Copy(
            remainingCleanup,
            0,
            cleanupActions,
            1,
            remainingCleanup.Length);
        return BestEffortCleanup.RunAsync(cleanupActions);
    }

    internal static void RunLocalControlFirstEmergencyCleanup(
        Action stopLocalControl,
        params Action[] remainingCleanup)
    {
        ArgumentNullException.ThrowIfNull(stopLocalControl);
        ArgumentNullException.ThrowIfNull(remainingCleanup);
        var cleanupActions = new Action[remainingCleanup.Length + 1];
        cleanupActions[0] = stopLocalControl;
        Array.Copy(
            remainingCleanup,
            0,
            cleanupActions,
            1,
            remainingCleanup.Length);
        EmergencyCleanup(cleanupActions);
    }

    private static void EmergencyCleanup(params Action[] cleanupActions)
    {
        foreach (var cleanup in cleanupActions)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                Trace.TraceError(
                    "CodexQuotaHud emergency cleanup failed: {0}",
                    exception);
            }
        }
    }
}

internal sealed record NormalSkinStartupSettings(
    SettingsStore Store,
    AppSettings Settings,
    string RequestedSelectionKey);
