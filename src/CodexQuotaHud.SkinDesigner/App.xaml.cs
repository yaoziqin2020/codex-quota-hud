using System.Windows;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Infrastructure;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.SkinDesigner.UI.Dialogs;
using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner;

public partial class App : System.Windows.Application
{
    private DesignerStartupComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var paths = new SkinStoragePaths(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData));
        var requests = new WindowsDesignerDocumentRequestSource(paths);
        var hudVersion = DesignerHudVersion.Current();
        var outputWindowOwner = new DesignerWindowOwner();
        var designerDialogs = new DesignerDialogService();
        var dialog = new WindowsUnsavedChangesDialog(
            designerDialogs,
            () => outputWindowOwner.Current);

        _composition = DesignerStartupComposition.TryCreate(
            new DesignerStartupFactories(
                () => DesignerSingleInstanceGuard.TryAcquire(),
                () =>
                {
                    var store = new DraftStore(paths);
                    var reader = new SkinPackageReader();
                    var writer = new SkinPackageWriter();
                    var catalog = new InstalledSkinCatalog(paths, hudVersion);
                    var documents = new DesignerDocumentService(
                        paths,
                        store,
                        catalog,
                        reader);
                    var outputDialogs = new WindowsSkinOutputDialogs(
                        () => outputWindowOwner.Current,
                        designerDialogs);
                    var builder = new DraftPackageBuilder(hudVersion);
                    var installer = new SkinPackageInstaller(paths, hudVersion);
                    var apply = new SkinApplyService(
                        paths,
                        hudVersion,
                        builder,
                        writer,
                        reader,
                        installer,
                        catalog,
                        new HudActivationRequester(),
                        outputDialogs);
                    var initial = documents.CreateNew(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow);
                    return new DesignerDocumentWorkspace(
                        initial,
                        documents,
                        new DesignerOutputServices(
                            apply,
                            new SkinExportService(builder, writer),
                            outputDialogs));
                },
                workspace => new MainWindow(
                    workspace.Initial.Draft!,
                    workspace.Initial.Assets,
                    paths,
                    dialog,
                    workspace.Documents,
                    requests,
                    outputServices: workspace.OutputServices,
                    outputWindowOwner: outputWindowOwner)));
        if (_composition is null)
        {
            Shutdown();
            return;
        }

        _composition.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _composition?.Dispose();
        base.OnExit(e);
    }
}

internal static class DesignerHudVersion
{
    private static readonly SemanticVersion RuntimeBaseline =
        SemanticVersion.Parse("1.3.0");

    internal static SemanticVersion Current()
    {
        var version = typeof(App).Assembly.GetName().Version;
        var detected = version is null
            ? RuntimeBaseline
            : new SemanticVersion(
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, version.Build));
        return detected.CompareTo(RuntimeBaseline) >= 0
            ? detected
            : RuntimeBaseline;
    }
}

internal sealed record DesignerStartupFactories(
    Func<IDisposable?> TryAcquireDesignerGuard,
    Func<DesignerDocumentWorkspace> CreateDocumentWorkspace,
    Func<DesignerDocumentWorkspace, IDesignerWindow> CreateWindow);

internal sealed record DesignerDocumentWorkspace(
    DesignerDocumentResult Initial,
    DesignerDocumentService Documents,
    DesignerOutputServices? OutputServices = null);

internal interface IDesignerWindow : IDisposable
{
    void Show();
}

internal sealed class DesignerStartupComposition : IDisposable
{
    private IDisposable? _lease;
    private readonly IDesignerWindow _window;

    private DesignerStartupComposition(
        IDisposable lease,
        IDesignerWindow window)
    {
        _lease = lease;
        _window = window;
    }

    internal static DesignerStartupComposition? TryCreate(
        DesignerStartupFactories factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        var lease = factories.TryAcquireDesignerGuard();
        if (lease is null)
        {
            return null;
        }

        try
        {
            var workspace = factories.CreateDocumentWorkspace();
            if (workspace.Initial.Draft is null || workspace.Initial.Errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "The initial Designer document workspace is invalid.");
            }

            var window = factories.CreateWindow(workspace);
            return new DesignerStartupComposition(lease, window);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal void Show() => _window.Show();

    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null)
        {
            return;
        }

        try
        {
            _window.Dispose();
        }
        finally
        {
            lease.Dispose();
        }
    }
}
