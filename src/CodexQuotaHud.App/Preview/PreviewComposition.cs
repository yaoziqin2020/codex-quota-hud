using System.ComponentModel;
using System.Windows.Threading;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Templates;

namespace CodexQuotaHud.App.Preview;

internal sealed class PreviewComposition : IDisposable
{
    private readonly Action _requestExit;
    private int _disposed;

    public PreviewComposition(
        Dispatcher dispatcher,
        Action requestExit)
        : this(dispatcher, requestExit, new InstalledAppLauncher())
    {
    }

    internal PreviewComposition(
        Dispatcher dispatcher,
        Action requestExit,
        InstalledAppLauncher installedAppLauncher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _requestExit = requestExit ?? throw new ArgumentNullException(
            nameof(requestExit));

        Catalog = HudSkinCatalog.CreateBuiltInOnly();
        SkinController = new SkinController(
            Catalog,
            SkinTemplateRegistry.CreateDefault());
        SettingsStore = new InMemorySettingsStore(new AppSettings());
        RefreshController = new PreviewQuotaRefreshController();
        ViewModel = new QuotaOrbViewModel(
            RefreshController,
            SettingsStore,
            SettingsStore.Load(),
            new WpfUiDispatcher(dispatcher),
            requestExit,
            key => Catalog.TryGet(key, out _));
        HudWindow = new QuotaOrbWindow(ViewModel, SkinController);
        Tray = new TrayController(
            ViewModel,
            Catalog,
            SkinController,
            HudWindow.TryActivateSkinKey);
        Session = new PreviewSession(
            RefreshController,
            ViewModel,
            HudWindow);
        WindowStateStore = new PreviewWindowStateStore();
        InstalledAppLauncher = installedAppLauncher ??
            throw new ArgumentNullException(nameof(installedAppLauncher));
        ControlWindow = new PreviewControlWindow(
            Session,
            InstalledAppLauncher.IsAvailable,
            WindowStateStore);
        ControlWindow.ExitRequested += OnExitRequested;
        ControlWindow.OpenInstalledRequested += OnOpenInstalledRequested;
        HudWindow.Closing += OnHudClosing;
    }

    internal InMemorySettingsStore SettingsStore { get; }
    internal HudSkinCatalog Catalog { get; }
    internal SkinController SkinController { get; }
    internal PreviewQuotaRefreshController RefreshController { get; }
    internal QuotaOrbViewModel ViewModel { get; }
    internal QuotaOrbWindow HudWindow { get; }
    internal TrayController Tray { get; }
    internal PreviewSession Session { get; }
    internal PreviewControlWindow ControlWindow { get; }
    internal InstalledAppLauncher InstalledAppLauncher { get; }
    internal PreviewWindowStateStore WindowStateStore { get; }
    public event EventHandler? OpenInstalledRequested;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (!HudWindow.IsVisible && ViewModel.IsVisible)
        {
            HudWindow.Show();
        }
        ControlWindow.Show();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ControlWindow.ExitRequested -= OnExitRequested;
        ControlWindow.OpenInstalledRequested -= OnOpenInstalledRequested;
        HudWindow.Closing -= OnHudClosing;
        ControlWindow.Close();
        Tray.Dispose();
        HudWindow.CloseForExit();
        ViewModel.Dispose();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _requestExit();
        }
    }

    private void OnOpenInstalledRequested(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            OpenInstalledRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnHudClosing(object? sender, CancelEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _requestExit();
        }
    }
}
