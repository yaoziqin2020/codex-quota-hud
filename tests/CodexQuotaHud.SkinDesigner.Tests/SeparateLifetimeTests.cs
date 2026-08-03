using System.Reflection;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Infrastructure;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests;

[Collection("Designer singleton")]
public sealed class SeparateLifetimeTests
{
    [Fact]
    public void ClosingDesignerCompositionCleansItsRecoveryAndHistoryBeforeOnlyItsMutex()
    {
        var events = new List<string>();
        var suffix = Guid.NewGuid().ToString("N");
        var normalName = $@"Local\CodexQuotaHud.Task15.Normal.{suffix}";
        var designerName = $@"Local\CodexQuotaHud.Task15.Designer.{suffix}";
        using var hud = RecordingNormalHudRoot.Start(normalName, events);
        using var designer = RecordingDesignerRoot.Start(designerName, events);

        designer.Close();

        Assert.True(hud.ControlServer.IsAlive);
        Assert.True(hud.Settings.IsAlive);
        using var normalRejected = AcquireNormal(normalName);
        using var designerReacquired =
            DesignerSingleInstanceGuard.TryAcquire(designerName);
        Assert.Null(normalRejected);
        Assert.NotNull(designerReacquired);
        Assert.Equal(
            [
                "designer.recovery.dispose",
                "designer.history.dispose",
                "designer.guard.dispose"
            ],
            events.Where(value => value.StartsWith("designer.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ClosingHudRootLeavesDesignerGuardRecoveryAndHistoryAlive()
    {
        var events = new List<string>();
        var suffix = Guid.NewGuid().ToString("N");
        var normalName = $@"Local\CodexQuotaHud.Task15.Normal.{suffix}";
        var designerName = $@"Local\CodexQuotaHud.Task15.Designer.{suffix}";
        using var hud = RecordingNormalHudRoot.Start(normalName, events);
        using var designer = RecordingDesignerRoot.Start(designerName, events);

        await hud.CloseAsync();

        Assert.False(hud.ControlServer.IsAlive);
        Assert.False(hud.Settings.IsAlive);
        Assert.True(designer.Recovery.IsAlive);
        Assert.True(designer.History.IsAlive);
        using var normalReacquired = AcquireNormal(normalName);
        using var designerRejected =
            DesignerSingleInstanceGuard.TryAcquire(designerName);
        Assert.NotNull(normalReacquired);
        Assert.Null(designerRejected);
        Assert.DoesNotContain(
            events,
            value => value.StartsWith("designer.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAndExportLeaveBothCompositionLifetimesOwnedAndExportAvoidsHudBoundaries()
    {
        var events = new List<string>();
        var suffix = Guid.NewGuid().ToString("N");
        var normalName = $@"Local\CodexQuotaHud.Task15.Normal.{suffix}";
        var designerName = $@"Local\CodexQuotaHud.Task15.Designer.{suffix}";
        using var hud = RecordingNormalHudRoot.Start(normalName, events);
        using var designer = RecordingDesignerRoot.Start(designerName, events);
        var dialogs = new RecordingDialogs(Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud-Task15-lifetime-" + Guid.NewGuid().ToString("N") +
            ".cqskin"));
        var applyCalls = 0;
        var exportCalls = 0;
        using var output = new DesignerOutputCoordinator(
            CreateDraft,
            () => new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _, _) =>
            {
                applyCalls++;
                AssertRootsRemainOwned(hud, designer);
                return Task.FromResult(Result(DesignerOutputDisposition.AppliedLive));
            },
            (_, _, path, overwrite, _) =>
            {
                exportCalls++;
                Assert.Equal(dialogs.ExportPath, path);
                Assert.False(overwrite);
                AssertRootsRemainOwned(hud, designer);
                return Task.FromResult(Result(
                    DesignerOutputDisposition.Exported,
                    exportPath: path));
            },
            dialogs);

        await output.ApplyCommand.ExecuteAsync();
        await output.ExportCommand.ExecuteAsync();

        Assert.Equal(1, applyCalls);
        Assert.Equal(1, exportCalls);
        Assert.Equal(1, hud.GuardAcquireCount);
        Assert.Equal(1, designer.GuardAcquireCount);
        Assert.Equal(0, hud.ControlInteractionCount);
        Assert.Equal(0, hud.ProcessInteractionCount);
        AssertRootsRemainOwned(hud, designer);
    }

    [Fact]
    public void ClosingDesignerReleasesOnlyDesignerMutexAndLeavesHudOwned()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var normalName = $@"Local\CodexQuotaHud.Task15.Normal.{suffix}";
        var designerName = $@"Local\CodexQuotaHud.Task15.Designer.{suffix}";
        using var normal = AcquireNormal(normalName);
        var designer = DesignerSingleInstanceGuard.TryAcquire(designerName);
        Assert.NotNull(normal);
        Assert.NotNull(designer);

        designer.Dispose();

        using var normalRejected = AcquireNormal(normalName);
        using var designerReacquired =
            DesignerSingleInstanceGuard.TryAcquire(designerName);
        Assert.Null(normalRejected);
        Assert.NotNull(designerReacquired);
    }

    [Fact]
    public void ClosingHudReleasesOnlyHudMutexAndLeavesDesignerOwned()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var normalName = $@"Local\CodexQuotaHud.Task15.Normal.{suffix}";
        var designerName = $@"Local\CodexQuotaHud.Task15.Designer.{suffix}";
        var normal = AcquireNormal(normalName);
        using var designer = DesignerSingleInstanceGuard.TryAcquire(designerName);
        Assert.NotNull(normal);
        Assert.NotNull(designer);

        normal.Dispose();

        using var normalReacquired = AcquireNormal(normalName);
        using var designerRejected =
            DesignerSingleInstanceGuard.TryAcquire(designerName);
        Assert.NotNull(normalReacquired);
        Assert.Null(designerRejected);
    }

    [Fact]
    public void SecondDesignerIsRejectedWithoutChangingHudOwnership()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var normalName = $@"Local\CodexQuotaHud.Task15.Normal.{suffix}";
        var designerName = $@"Local\CodexQuotaHud.Task15.Designer.{suffix}";
        using var normal = AcquireNormal(normalName);
        using var designer = DesignerSingleInstanceGuard.TryAcquire(designerName);

        using var secondDesigner =
            DesignerSingleInstanceGuard.TryAcquire(designerName);
        using var secondNormal = AcquireNormal(normalName);

        Assert.Null(secondDesigner);
        Assert.Null(secondNormal);
    }

    private static IDisposable? AcquireNormal(string mutexName)
    {
        var overload = typeof(SingleInstanceGuard).GetMethod(
            nameof(SingleInstanceGuard.TryAcquire),
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null);
        Assert.NotNull(overload);
        return overload.Invoke(null, [mutexName]) as IDisposable;
    }

    private static void AssertRootsRemainOwned(
        RecordingNormalHudRoot hud,
        RecordingDesignerRoot designer)
    {
        Assert.True(hud.ControlServer.IsAlive);
        Assert.True(hud.Settings.IsAlive);
        Assert.True(designer.Recovery.IsAlive);
        Assert.True(designer.History.IsAlive);
        Assert.False(hud.Guard.IsDisposed);
        Assert.False(designer.Guard.IsDisposed);
    }

    private static DesignerOutputResult Result(
        DesignerOutputDisposition disposition,
        string? exportPath = null) =>
        new(disposition, null, exportPath, [], null);

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
            "CodexQuotaHud-Task15-lifetime-root-" + Guid.NewGuid().ToString("N")));
        var documents = new DesignerDocumentService(
            paths,
            new DraftStore(paths),
            new InstalledSkinCatalog(paths, SemanticVersion.Parse("1.1.1")),
            new SkinPackageReader());
        return new DesignerDocumentWorkspace(
            new DesignerDocumentResult(
                CreateDraft(),
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                []),
            documents);
    }

    private sealed class RecordingNormalHudRoot : IDisposable
    {
        private readonly List<string> _events;

        private RecordingNormalHudRoot(
            RecordingGuard guard,
            List<string> events)
        {
            Guard = guard;
            _events = events;
            ControlServer = new RecordingLifetime("hud.control", events);
            Settings = new RecordingLifetime("hud.settings", events);
        }

        public RecordingGuard Guard { get; }

        public RecordingLifetime ControlServer { get; }

        public RecordingLifetime Settings { get; }

        public int GuardAcquireCount { get; private set; }

        public int ControlInteractionCount { get; private set; }

        public int ProcessInteractionCount { get; private set; }

        public static RecordingNormalHudRoot Start(
            string mutexName,
            List<string> events)
        {
            var lease = AcquireNormal(mutexName);
            Assert.NotNull(lease);
            var root = new RecordingNormalHudRoot(
                new RecordingGuard("hud.guard", lease, events),
                events)
            {
                GuardAcquireCount = 1
            };
            return root;
        }

        public async Task CloseAsync()
        {
            if (Guard.IsDisposed)
            {
                return;
            }

            await RunNormalHudCleanupAsync(
                () =>
                {
                    ControlServer.Dispose();
                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    Settings.Dispose();
                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    Guard.Dispose();
                    return ValueTask.CompletedTask;
                });
        }

        public void Dispose() => CloseAsync().GetAwaiter().GetResult();

        private static Task RunNormalHudCleanupAsync(
            Func<ValueTask> stopControl,
            params Func<ValueTask>[] remaining)
        {
            var method = typeof(global::CodexQuotaHud.App.App).GetMethod(
                "RunLocalControlFirstCleanupAsync",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                [typeof(Func<ValueTask>), typeof(Func<ValueTask>[])],
                modifiers: null);
            Assert.NotNull(method);
            var task = method.Invoke(null, [stopControl, remaining]) as Task;
            Assert.NotNull(task);
            return task;
        }
    }

    private sealed class RecordingDesignerRoot : IDisposable
    {
        private readonly DesignerStartupComposition _composition;
        private bool _closed;

        private RecordingDesignerRoot(
            DesignerStartupComposition composition,
            RecordingDesignerWindow window,
            RecordingGuard guard,
            int guardAcquireCount)
        {
            _composition = composition;
            Window = window;
            Guard = guard;
            GuardAcquireCount = guardAcquireCount;
        }

        public RecordingDesignerWindow Window { get; }

        public RecordingLifetime Recovery => Window.Recovery;

        public RecordingLifetime History => Window.History;

        public RecordingGuard Guard { get; }

        public int GuardAcquireCount { get; }

        public static RecordingDesignerRoot Start(
            string mutexName,
            List<string> events)
        {
            RecordingGuard? guard = null;
            var acquireCount = 0;
            var window = new RecordingDesignerWindow(events);
            var composition = DesignerStartupComposition.TryCreate(
                new DesignerStartupFactories(
                    () =>
                    {
                        acquireCount++;
                        var lease = DesignerSingleInstanceGuard.TryAcquire(mutexName);
                        guard = lease is null
                            ? null
                            : new RecordingGuard("designer.guard", lease, events);
                        return guard;
                    },
                    CreateWorkspace,
                    _ => window));
            Assert.NotNull(composition);
            Assert.NotNull(guard);
            return new RecordingDesignerRoot(
                composition,
                window,
                guard,
                acquireCount);
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _composition.Dispose();
        }

        public void Dispose() => Close();
    }

    private sealed class RecordingDesignerWindow(List<string> events) :
        IDesignerWindow
    {
        public RecordingLifetime Recovery { get; } =
            new("designer.recovery", events);

        public RecordingLifetime History { get; } =
            new("designer.history", events);

        public void Show()
        {
        }

        public void Dispose()
        {
            Recovery.Dispose();
            History.Dispose();
        }
    }

    private sealed class RecordingGuard(
        string name,
        IDisposable inner,
        List<string> events) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            inner.Dispose();
            events.Add(name + ".dispose");
        }
    }

    private sealed class RecordingLifetime(
        string name,
        List<string> events) : IDisposable
    {
        public bool IsAlive { get; private set; } = true;

        public void Dispose()
        {
            if (!IsAlive)
            {
                return;
            }

            IsAlive = false;
            events.Add(name + ".dispose");
        }
    }

    private sealed class RecordingDialogs(string exportPath) : ISkinOutputDialogs
    {
        public string ExportPath { get; } = exportPath;

        public string? ChooseExportPath(string suggestedFileName) => ExportPath;

        public bool ConfirmExportReplace(string destinationPath) => false;

        public SkinCollisionDecision ChooseApplyCollision(
            SkinInstallPreview preview) => SkinCollisionDecision.Cancel;

        public void ShowResult(DesignerOutputResult result)
        {
        }
    }
}
