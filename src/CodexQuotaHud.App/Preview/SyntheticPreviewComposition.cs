using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.About;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates;

namespace CodexQuotaHud.App.Preview;

public sealed class SyntheticPreviewComposition : IDisposable
{
    private readonly Action _requestExit;
    private readonly SkinTemplateRegistry _templates;
    private int _disposed;

    public SyntheticPreviewComposition(
        Dispatcher dispatcher,
        Action requestExit,
        SkinTemplateRegistry? templates = null)
        : this(
            dispatcher,
            requestExit,
            templates,
            usePhysicalWorkArea: false,
            aboutWindowCoordinator: null)
    {
    }

    internal SyntheticPreviewComposition(
        Dispatcher dispatcher,
        Action requestExit,
        SkinTemplateRegistry? templates,
        bool usePhysicalWorkArea,
        AboutWindowCoordinator? aboutWindowCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _requestExit = requestExit ?? throw new ArgumentNullException(
            nameof(requestExit));
        _templates = templates ?? SkinTemplateRegistry.CreateDefault();

        Catalog = HudSkinCatalog.CreateBuiltInOnly();
        SkinController = new SkinController(Catalog, _templates);
        SettingsStore = new InMemorySettingsStore(new AppSettings());
        RefreshController = new PreviewQuotaRefreshController();
        About = aboutWindowCoordinator ?? new AboutWindowCoordinator();
        ViewModel = new QuotaOrbViewModel(
            RefreshController,
            SettingsStore,
            SettingsStore.Load(),
            new WpfUiDispatcher(dispatcher),
            requestExit,
            key => Catalog.TryGet(key, out _));
        HudWindow = new QuotaOrbWindow(
            ViewModel,
            SkinController,
            About.Show,
            suppressAutomaticShow: true);
        if (!usePhysicalWorkArea)
        {
            HudWindow.SetPreviewWorkArea(new Rect(0, 0, 520, 420));
        }

        Session = new PreviewSession(
            RefreshController,
            ViewModel,
            HudWindow);
        HudWindow.Closing += OnHudClosing;
    }

    public PreviewSession Session { get; }

    public QuotaOrbWindow HudWindow { get; }

    public AppSettings CurrentInMemorySettings => SettingsStore.Current;

    internal InMemorySettingsStore SettingsStore { get; }

    internal HudSkinCatalog Catalog { get; }

    internal SkinController SkinController { get; }

    internal PreviewQuotaRefreshController RefreshController { get; }

    internal QuotaOrbViewModel ViewModel { get; }

    internal AboutWindowCoordinator About { get; }

    public SkinValidationResult<SkinPackageDocument> SetCustomPackage(
        SkinPackageDocument package)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(package);

        var candidate = TransientCustomSkinFactory.Create(package, _templates);
        if (!candidate.IsValid)
        {
            return new SkinValidationResult<SkinPackageDocument>(
                null,
                candidate.Errors);
        }

        HudWindow.ActivateSyntheticSkin(candidate.Value!);
        return new SkinValidationResult<SkinPackageDocument>(package, []);
    }

    public void SetPreviewWorkArea(Rect workArea)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (!double.IsFinite(workArea.Left) ||
            !double.IsFinite(workArea.Top) ||
            !double.IsFinite(workArea.Width) ||
            !double.IsFinite(workArea.Height) ||
            workArea.Width <= 0 ||
            workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        HudWindow.SetPreviewWorkArea(workArea);
    }

    public void RecenterHudInPreviewWorkArea(Rect workArea)
    {
        SetPreviewWorkArea(workArea);
        HudWindow.CenterInPreviewWorkArea();
    }

    public void ShowHud()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        HudWindow.ShowSyntheticHud();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        About.Dispose();
        HudWindow.Closing -= OnHudClosing;
        HudWindow.CloseForExit();
        ViewModel.Dispose();
    }

    private void OnHudClosing(object? sender, CancelEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _requestExit();
        }
    }
}
