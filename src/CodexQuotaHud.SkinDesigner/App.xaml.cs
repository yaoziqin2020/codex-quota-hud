using System.Windows;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Infrastructure;
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
        var store = new DraftStore(paths);
        var documents = new DesignerDocumentService(
            paths,
            store,
            new InstalledSkinCatalog(
                paths,
                SemanticVersion.Parse("1.1.1")),
            new SkinPackageReader());
        var dialog = new WindowsUnsavedChangesDialog();
        var requests = new WindowsDesignerDocumentRequestSource(paths);

        _composition = DesignerStartupComposition.TryCreate(
            new DesignerStartupFactories(
                () => DesignerSingleInstanceGuard.TryAcquire(),
                () =>
                {
                    var initial = documents.CreateNew(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow,
                        SemanticVersion.Parse("1.1.1"));
                    return new DesignerDocumentWorkspace(initial, documents);
                },
                workspace => new MainWindow(
                    workspace.Initial.Draft!,
                    workspace.Initial.Assets,
                    paths,
                    dialog,
                    workspace.Documents,
                    requests)));
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

internal sealed record DesignerStartupFactories(
    Func<IDisposable?> TryAcquireDesignerGuard,
    Func<DesignerDocumentWorkspace> CreateDocumentWorkspace,
    Func<DesignerDocumentWorkspace, IDesignerWindow> CreateWindow);

internal sealed record DesignerDocumentWorkspace(
    DesignerDocumentResult Initial,
    DesignerDocumentService Documents);

internal interface IDesignerWindow
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

    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}
