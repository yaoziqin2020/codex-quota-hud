using System.Windows.Threading;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.About;
using CodexQuotaHud.App.UI.Skins;

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
        InstalledAppLauncher installedAppLauncher,
        AboutWindowCoordinator? aboutWindowCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _requestExit = requestExit ?? throw new ArgumentNullException(
            nameof(requestExit));

        Synthetic = new SyntheticPreviewComposition(
            dispatcher,
            requestExit,
            templates: null,
            usePhysicalWorkArea: true,
            aboutWindowCoordinator);
        Tray = new TrayController(
            ViewModel,
            Catalog,
            SkinController,
            HudWindow.TryActivateSkinKey,
            skinManagement: null,
            Synthetic.About.Show);
        WindowStateStore = new PreviewWindowStateStore();
        InstalledAppLauncher = installedAppLauncher ??
            throw new ArgumentNullException(nameof(installedAppLauncher));
        ControlWindow = new PreviewControlWindow(
            Session,
            InstalledAppLauncher.IsAvailable,
            WindowStateStore);
        ControlWindow.ExitRequested += OnExitRequested;
        ControlWindow.OpenInstalledRequested += OnOpenInstalledRequested;
    }

    internal SyntheticPreviewComposition Synthetic { get; }
    internal InMemorySettingsStore SettingsStore => Synthetic.SettingsStore;
    internal HudSkinCatalog Catalog => Synthetic.Catalog;
    internal SkinController SkinController => Synthetic.SkinController;
    internal PreviewQuotaRefreshController RefreshController =>
        Synthetic.RefreshController;
    internal QuotaOrbViewModel ViewModel => Synthetic.ViewModel;
    internal QuotaOrbWindow HudWindow => Synthetic.HudWindow;
    internal TrayController Tray { get; }
    internal PreviewSession Session => Synthetic.Session;
    internal PreviewControlWindow ControlWindow { get; }
    internal InstalledAppLauncher InstalledAppLauncher { get; }
    internal PreviewWindowStateStore WindowStateStore { get; }
    public event EventHandler? OpenInstalledRequested;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        Synthetic.ShowHud();
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
        ControlWindow.Close();
        Tray.Dispose();
        Synthetic.Dispose();
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

}
