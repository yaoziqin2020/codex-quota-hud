using System.Windows.Controls;
using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class ProductionOutputCompositionTests
{
    [Fact]
    public void RealMainWindow_WiresEnabledOutputCommandsFromDesignerOnlyServices()
    {
        RunSta(() =>
        {
            using var root = new TemporaryRoot();
            var draft = OutputTestFixture.CompleteDraft();
            var store = new DraftStore(root.Paths);
            var documents = new DesignerDocumentService(
                root.Paths,
                store,
                new InstalledSkinCatalog(root.Paths, OutputTestFixture.HudVersion),
                new SkinPackageReader());
            var dialogs = new RecordingDialogs();
            var writer = new SkinPackageWriter();
            var reader = new SkinPackageReader();
            var installer = new SkinPackageInstaller(
                root.Paths,
                OutputTestFixture.HudVersion);
            var catalog = new InstalledSkinCatalog(
                root.Paths,
                OutputTestFixture.HudVersion);
            var builder = new DraftPackageBuilder(OutputTestFixture.HudVersion);
            var apply = new SkinApplyService(
                root.Paths,
                OutputTestFixture.HudVersion,
                builder,
                PhysicalApplyStagingLeaseProvider.Instance,
                writer.Write,
                reader.ValidateStream,
                installer.Inspect,
                installer.Install,
                catalog.TryLoadSelection,
                (_, _) => Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.Rejected,
                    "test.no-hud",
                    "No external HUD contacted.")),
                dialogs);
            var outputServices = new DesignerOutputServices(
                apply,
                new SkinExportService(builder, writer),
                dialogs);
            var window = new MainWindow(
                draft,
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                root.Paths,
                new NoUnsavedDialog(),
                documents,
                new NoDocumentRequests(),
                outputServices: outputServices);

            var coordinator = Assert.IsType<DesignerOutputCoordinator>(
                window.Editor.Output);
            var applyButton = Assert.IsType<Button>(
                window.FindName("ApplyToHudButton"));
            var exportButton = Assert.IsType<Button>(
                window.FindName("ExportPackageButton"));
            Assert.Same(coordinator.ApplyCommand, applyButton.Command);
            Assert.Same(coordinator.ExportCommand, exportButton.Command);
            Assert.True(applyButton.IsEnabled);
            Assert.True(exportButton.IsEnabled);
            window.DisposeWithoutShowingForTesting();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private sealed class NoUnsavedDialog : IUnsavedChangesDialog
    {
        public UnsavedCloseChoice Show(SkinDraftDocument draft) =>
            UnsavedCloseChoice.Discard;
    }

    private sealed class NoDocumentRequests : IDesignerDocumentRequestSource
    {
        public Guid? SelectDraftId(System.Windows.Window owner) => null;

        public string? SelectInstalledSelectionKey(System.Windows.Window owner) => null;

        public string? SelectPackagePath(System.Windows.Window owner) => null;
    }

    private sealed class RecordingDialogs : ISkinOutputDialogs
    {
        public string? ChooseExportPath(string suggestedFileName) => null;

        public bool ConfirmExportReplace(string destinationPath) => false;

        public SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview) =>
            SkinCollisionDecision.Cancel;

        public void ShowResult(DesignerOutputResult result)
        {
        }
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-composition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new SkinStoragePaths(Path);
        }

        public string Path { get; }

        public SkinStoragePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
