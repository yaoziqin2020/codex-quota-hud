using CodexQuotaHud.SkinDesigner;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests;

public sealed class AppCompositionTests
{
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
        var factories = new DesignerStartupFactories(
            () =>
            {
                calls.Add("DesignerSingleInstanceGuard");
                return lease;
            },
            () =>
            {
                calls.Add("SkinDraftDocument");
                return CreateDraft();
            },
            draft =>
            {
                Assert.NotNull(draft);
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
                "SkinDraftDocument",
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
        var draftCreated = false;
        var windowCreated = false;
        var factories = new DesignerStartupFactories(
            () => null,
            () =>
            {
                draftCreated = true;
                return CreateDraft();
            },
            _ =>
            {
                windowCreated = true;
                return new RecordingWindow([]);
            });

        using var composition =
            DesignerStartupComposition.TryCreate(factories);

        Assert.Null(composition);
        Assert.False(draftCreated);
        Assert.False(windowCreated);
    }

    private sealed class RecordingLease(List<string> calls) : IDisposable
    {
        public void Dispose() => calls.Add("DesignerSingleInstanceGuard.Dispose");
    }

    private sealed class RecordingWindow(List<string> calls) : IDesignerWindow
    {
        public void Show() => calls.Add("MainWindow.Show");
    }

    private static SkinDraftDocument CreateDraft() =>
        SkinDraftFactory.CreateNew(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1"));
}
