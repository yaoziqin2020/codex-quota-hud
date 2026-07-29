using System.Windows;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App;

public partial class App : System.Windows.Application
{
    private readonly SemaphoreSlim _runningTransition = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private SingleInstanceGuard? _singleInstance;
    private CodexProcessMonitor? _processMonitor;
    private RestartableQuotaClient? _quotaClient;
    private QuotaRefreshService? _refreshService;
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

            if (IsInteractiveLaunch(e.Args))
            {
                new StartupRegistration().Enable();
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
        _lifetime.Cancel();
        if (_processMonitor is not null)
        {
            _processMonitor.RunningChanged -= OnCodexRunningChanged;
        }

        _tray?.Dispose();
        _window?.CloseForExit();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        _lifetime.Dispose();
        _runningTransition.Dispose();
        base.OnExit(e);
    }

    internal static bool IsInteractiveLaunch(IReadOnlyList<string> arguments) =>
        !arguments.Any(
            argument => string.Equals(
                argument,
                "--background",
                StringComparison.OrdinalIgnoreCase));

    private async void OnCodexRunningChanged(bool isRunning)
    {
        try
        {
            await ApplyCodexRunningAsync(isRunning);
        }
        catch (OperationCanceledException)
            when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    private async Task ApplyCodexRunningAsync(bool isRunning)
    {
        await _runningTransition.WaitAsync(_lifetime.Token);
        try
        {
            if (_refreshService is null || _quotaClient is null)
            {
                return;
            }

            await _refreshService.SetCodexRunningAsync(
                isRunning,
                _lifetime.Token);
            if (!isRunning)
            {
                await _quotaClient.ResetAsync();
            }
        }
        finally
        {
            _runningTransition.Release();
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
        finally
        {
            Shutdown();
        }
    }

    private async Task ShutdownResourcesAsync()
    {
        _lifetime.Cancel();
        if (_processMonitor is not null)
        {
            _processMonitor.RunningChanged -= OnCodexRunningChanged;
        }

        await _runningTransition.WaitAsync();
        try
        {
            if (_refreshService is not null)
            {
                await _refreshService.DisposeAsync();
                _refreshService = null;
            }

            if (_quotaClient is not null)
            {
                await _quotaClient.DisposeAsync();
                _quotaClient = null;
            }
        }
        finally
        {
            _runningTransition.Release();
        }

        if (_processMonitor is not null)
        {
            await _processMonitor.DisposeAsync();
            _processMonitor = null;
        }

        _tray?.Dispose();
        _tray = null;
        _window?.CloseForExit();
        _window = null;
        _viewModel?.Dispose();
        _viewModel = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
    }
}
