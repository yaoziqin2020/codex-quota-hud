using System.Windows;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Infrastructure;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner;

public partial class App : System.Windows.Application
{
    private DesignerStartupComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        _composition = DesignerStartupComposition.TryCreate(
            new DesignerStartupFactories(
                () => DesignerSingleInstanceGuard.TryAcquire(),
                () => SkinDraftFactory.CreateNew(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    SemanticVersion.Parse("1.1.1")),
                draft => new MainWindow(draft)));
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
    Func<SkinDraftDocument> CreateDraft,
    Func<SkinDraftDocument, IDesignerWindow> CreateWindow);

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
            var draft = factories.CreateDraft();
            var window = factories.CreateWindow(draft);
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
