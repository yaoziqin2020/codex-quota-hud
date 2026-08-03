using CodexQuotaHud.SkinDesigner;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests;

public sealed class AppCompositionTests
{
    [Fact]
    public void StartupCompositionCarriesTheRealDocumentWorkspaceNotABareDraft()
    {
        var factoryProperties = typeof(DesignerStartupFactories)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("CreateDocumentWorkspace", factoryProperties);
        Assert.DoesNotContain("CreateDraft", factoryProperties);
    }

    private static readonly string[] ForbiddenServiceNames =
    [
        "RestartableQuotaClient",
        "CodexProcessMonitor",
        "StartupRegistration",
        "InstalledAppShutdownListener",
        "LocalControlServer"
    ];

    [Fact]
    public void TryCreate_UsesOnlyDesignerGuardDraftAndWindowFactories()
    {
        var calls = new List<string>();
        using var lease = new RecordingLease(calls);
        var window = new RecordingWindow(calls);
        var workspace = CreateWorkspace();
        var factories = new DesignerStartupFactories(
            () =>
            {
                calls.Add("DesignerSingleInstanceGuard");
                return lease;
            },
            () =>
            {
                calls.Add("DesignerDocumentWorkspace");
                return workspace;
            },
            actual =>
            {
                Assert.Same(workspace, actual);
                Assert.NotNull(actual.Initial.Draft);
                Assert.NotNull(actual.Documents);
                calls.Add("MainWindow");
                return window;
            });

        using var composition =
            DesignerStartupComposition.TryCreate(factories);
        Assert.NotNull(composition);
        composition.Show();

        Assert.Equal(
            [
                "DesignerSingleInstanceGuard",
                "DesignerDocumentWorkspace",
                "MainWindow",
                "MainWindow.Show"
            ],
            calls);
        Assert.DoesNotContain(
            calls,
            call => ForbiddenServiceNames.Contains(
                call,
                StringComparer.Ordinal));
    }

    [Fact]
    public void TryCreate_WhenDesignerLeaseIsUnavailableCreatesNothingElse()
    {
        var workspaceCreated = false;
        var windowCreated = false;
        var factories = new DesignerStartupFactories(
            () => null,
            () =>
            {
                workspaceCreated = true;
                return CreateWorkspace();
            },
            _ =>
            {
                windowCreated = true;
                return new RecordingWindow([]);
            });

        using var composition =
            DesignerStartupComposition.TryCreate(factories);

        Assert.Null(composition);
        Assert.False(workspaceCreated);
        Assert.False(windowCreated);
    }

    private sealed class RecordingLease(List<string> calls) : IDisposable
    {
        public void Dispose() => calls.Add("DesignerSingleInstanceGuard.Dispose");
    }

    private sealed class RecordingWindow(List<string> calls) : IDesignerWindow
    {
        public void Show() => calls.Add("MainWindow.Show");

        public void Dispose() => calls.Add("MainWindow.Dispose");
    }

    private static SkinDraftDocument CreateDraft() =>
        SkinDraftFactory.CreateNew(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1"));

    private static DesignerDocumentWorkspace CreateWorkspace()
    {
        var paths = new SkinStoragePaths(Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud-Task14-composition-" + Guid.NewGuid().ToString("N")));
        var documents = new DesignerDocumentService(
            paths,
            new DraftStore(paths),
            new InstalledSkinCatalog(
                paths,
                SemanticVersion.Parse("1.1.1")),
            new SkinPackageReader());
        return new DesignerDocumentWorkspace(
            new DesignerDocumentResult(
                CreateDraft(),
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                []),
            documents);
    }
}
