using System.Windows;
using System.Diagnostics;
using System.IO;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App;

public partial class App : System.Windows.Application
{
    internal const string PreviewStartupFailureMessage =
        "开发预览启动失败，无法安全检查或替换已安装正式版。";

    private IDisposable? _singleInstance;
    private InstalledAppShutdownListener? _shutdownListener;
    private CodexProcessMonitor? _processMonitor;
    private RestartableQuotaClient? _quotaClient;
    private QuotaRefreshService? _refreshService;
    private CodexRunningCoordinator? _runningCoordinator;
    private QuotaOrbViewModel? _viewModel;
    private QuotaOrbWindow? _window;
    private TrayController? _tray;
    private PreviewComposition? _previewComposition;
    private InstalledAppLauncher? _installedAppLauncher;
    private int _openInstalledAfterExit;
    private int _shutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var preview = IsPreviewLaunch(e.Args);
        InstalledAppLauncher? installedAppLauncher = null;
        var acquired = TryAcquireForLaunch(
            preview,
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
            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load();
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
                RequestExit);
            _window = new QuotaOrbWindow(_viewModel);
            _tray = new TrayController(_viewModel);

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
            () => EmergencyCleanup(
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
        IsInteractiveLaunch(arguments) && !IsPreviewLaunch(arguments);

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

        await BestEffortCleanup.RunAsync(
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
