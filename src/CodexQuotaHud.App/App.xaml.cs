using System.Windows;
using System.Diagnostics;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private CodexProcessMonitor? _processMonitor;
    private RestartableQuotaClient? _quotaClient;
    private QuotaRefreshService? _refreshService;
    private CodexRunningCoordinator? _runningCoordinator;
    private QuotaOrbViewModel? _viewModel;
    private QuotaOrbWindow? _window;
    private TrayController? _tray;
    private int _shutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstance = SingleInstanceGuard.TryAcquire();
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        try
        {
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

        EmergencyCleanup(
            () => _runningCoordinator?.DisposeAsync().AsTask()
                .GetAwaiter().GetResult(),
            () => _refreshService?.DisposeAsync().AsTask()
                .GetAwaiter().GetResult(),
            () => _quotaClient?.DisposeAsync().AsTask()
                .GetAwaiter().GetResult(),
            () => _processMonitor?.DisposeAsync().AsTask()
                .GetAwaiter().GetResult(),
            () => _tray?.Dispose(),
            () => _window?.CloseForExit(),
            () => _viewModel?.Dispose(),
            () => _singleInstance?.Dispose());
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
