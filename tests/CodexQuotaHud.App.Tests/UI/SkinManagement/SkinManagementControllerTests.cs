using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.Tests.UI.SkinManagement;

[Collection(WpfUiCollection.Name)]
public sealed class SkinManagementControllerTests
{
    private static readonly SemanticVersion HudVersion =
        SemanticVersion.Parse("1.1.1");

    [Fact]
    public async Task ImportAsync_NewPackageAddsMenuEntryWithoutChangingFormalSelection()
    {
        using var fixture = new ManagementFixture();
        var packagePath = fixture.WritePackage(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var result = await fixture.Controller.ImportAsync(packagePath);

        Assert.True(result.Succeeded, Format(result.Errors));
        Assert.False(result.Cancelled);
        Assert.NotNull(result.Installed);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.Settings.Load().SelectedSkinKey);
        Assert.Contains(
            fixture.Controller.Entries,
            entry => entry.SelectionKey == result.Installed.SelectionKey && entry.CanRemove);
        Assert.Equal(1, catalogEvents);
        Assert.Equal(1, fixture.Dialogs.PreviewCount);
    }

    [Fact]
    public async Task ImportAsync_CommittedCleanupFailureRefreshesAndShowsStructuredError()
    {
        using var fixture = new ManagementFixture(failOperationCleanup: true);
        var packagePath = fixture.WritePackage(
            Guid.Parse("10101010-1111-1111-1111-111111111111"));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var result = await fixture.Controller.ImportAsync(packagePath);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Installed);
        var error = Assert.Single(
            result.Errors,
            item => item.Code == "install.cleanup-failed");
        Assert.Contains(
            fixture.Controller.Entries,
            entry => entry.SelectionKey == result.Installed.SelectionKey);
        Assert.Equal(1, catalogEvents);
        Assert.Contains(error.Code, fixture.Dialogs.LastErrorMessage);
        Assert.Contains(error.Location, fixture.Dialogs.LastErrorMessage);
        Assert.Contains(error.Message, fixture.Dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task ImportAsync_PreviewCancelDoesNotInstallOrRaiseCatalogEvent()
    {
        using var fixture = new ManagementFixture
        {
            DialogDecision = SkinCollisionDecision.Cancel
        };
        var packagePath = fixture.WritePackage(
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var result = await fixture.Controller.ImportAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.True(result.Cancelled);
        Assert.Null(result.Installed);
        Assert.Empty(result.Errors);
        Assert.Equal(0, catalogEvents);
        Assert.Equal(1, fixture.Dialogs.PreviewCount);
        Assert.Equal(5, fixture.Controller.Entries.Count);
        Assert.Empty(Directory.Exists(fixture.Paths.InstalledSkinsRoot)
            ? Directory.GetDirectories(fixture.Paths.InstalledSkinsRoot)
            : []);
    }

    [Fact]
    public async Task ImportAsync_InvalidPackageMapsInspectionErrorsWithoutCatalogMutation()
    {
        using var fixture = new ManagementFixture();
        var invalidPath = Path.Combine(fixture.Root, "invalid.cqskin");
        File.WriteAllText(invalidPath, "not a skin archive");
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var result = await fixture.Controller.ImportAsync(invalidPath);

        Assert.False(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.Null(result.Installed);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(fixture.Dialogs.LastErrorMessage);
        Assert.All(result.Errors, error =>
        {
            Assert.Contains(error.Code, fixture.Dialogs.LastErrorMessage);
            Assert.Contains(error.Location, fixture.Dialogs.LastErrorMessage);
            Assert.Contains(error.Message, fixture.Dialogs.LastErrorMessage);
        });
        Assert.Equal(0, fixture.Dialogs.PreviewCount);
        Assert.Equal(0, catalogEvents);
        Assert.Equal(5, fixture.Controller.Entries.Count);
    }

    [Fact]
    public async Task ImportAsync_IncompatiblePackageReportsFieldReasonWithoutPreviewOrInstall()
    {
        using var fixture = new ManagementFixture();
        var packagePath = fixture.WritePackage(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            minimumHudVersion: SemanticVersion.Parse("9.0.0"));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var result = await fixture.Controller.ImportAsync(packagePath);

        var error = Assert.Single(result.Errors);
        Assert.Equal("version.incompatible", error.Code);
        Assert.Equal("$.minimumHudVersion", error.Location);
        Assert.Contains(error.Code, fixture.Dialogs.LastErrorMessage);
        Assert.Contains(error.Location, fixture.Dialogs.LastErrorMessage);
        Assert.Contains(error.Message, fixture.Dialogs.LastErrorMessage);
        Assert.False(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.Equal(0, fixture.Dialogs.PreviewCount);
        Assert.Equal(0, catalogEvents);
        Assert.Equal(5, fixture.Controller.Entries.Count);
    }

    [Fact]
    public async Task ImportAsync_SameIdReplaceRefreshesOneEntryWithoutSelectingIt()
    {
        using var fixture = new ManagementFixture();
        var skinId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        await fixture.Controller.ImportAsync(fixture.WritePackage(skinId));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;
        fixture.DialogDecision = SkinCollisionDecision.Replace;

        var result = await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("2.0.0"),
            fileName: "replacement.cqskin"));

        Assert.True(result.Succeeded, Format(result.Errors));
        Assert.Equal(SemanticVersion.Parse("2.0.0"), result.Installed!.PackageVersion);
        Assert.Single(fixture.Controller.Entries, entry => entry.CanRemove);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(1, catalogEvents);
    }

    [Fact]
    public async Task ImportAsync_SameIdKeepCopyAddsNewIdentityWithoutSelectingEither()
    {
        using var fixture = new ManagementFixture();
        var skinId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        await fixture.Controller.ImportAsync(fixture.WritePackage(skinId));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;
        fixture.DialogDecision = SkinCollisionDecision.KeepCopy;

        var result = await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("2.0.0"),
            fileName: "keep-copy.cqskin"));

        Assert.True(result.Succeeded, Format(result.Errors));
        Assert.NotEqual(skinId, result.Installed!.SkinId);
        Assert.Equal(skinId, result.Installed.Package.Manifest.OriginSkinId);
        Assert.Equal(2, fixture.Controller.Entries.Count(entry => entry.CanRemove));
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(1, catalogEvents);
    }

    [Fact]
    public async Task ImportAsync_SameIdCollisionCancelPreservesOriginalAndRaisesNoEvent()
    {
        using var fixture = new ManagementFixture();
        var skinId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var original = await fixture.Controller.ImportAsync(
            fixture.WritePackage(skinId));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;
        fixture.DialogDecision = SkinCollisionDecision.Cancel;

        var result = await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("2.0.0"),
            fileName: "cancelled-collision.cqskin"));

        Assert.False(result.Succeeded);
        Assert.True(result.Cancelled);
        Assert.Equal(0, catalogEvents);
        Assert.Single(fixture.Controller.Entries, entry => entry.CanRemove);
        Assert.Equal(
            original.Installed!.PackageVersion,
            fixture.Catalog.Load().Healthy.Single(entry => entry.CanRemove)
                .Installed!.PackageVersion);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
    }

    [Fact]
    public async Task ImportAsync_DowngradeCannotBeForcedThroughPreviewDecision()
    {
        using var fixture = new ManagementFixture();
        var skinId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("2.0.0")));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;
        fixture.DialogDecision = SkinCollisionDecision.Replace;

        var result = await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("1.0.0"),
            fileName: "downgrade.cqskin"));

        var error = Assert.Single(result.Errors);
        Assert.Equal("install.downgrade", error.Code);
        Assert.False(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.Equal(0, catalogEvents);
        Assert.Equal(SemanticVersion.Parse("2.0.0"),
            fixture.Catalog.Load().Healthy.Single(entry => entry.CanRemove)
                .Installed!.PackageVersion);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
    }

    [Fact]
    public async Task RemoveAsync_BuiltInAndUnknownKeysAreRejectedWithoutConfirmation()
    {
        using var fixture = new ManagementFixture();
        var corruptId = Guid.Parse("88888888-1111-1111-1111-111111111111");
        var corruptDirectory = Path.Combine(
            fixture.Paths.InstalledSkinsRoot,
            corruptId.ToString("D"));
        Directory.CreateDirectory(corruptDirectory);
        File.WriteAllText(Path.Combine(corruptDirectory, "manifest.json"), "corrupt");
        fixture.Catalog.Refresh();

        var builtIn = await fixture.Controller.RemoveAsync(SkinSelectionKey.HudDial);
        var corrupt = await fixture.Controller.RemoveAsync(
            $"custom:{corruptId:D}");

        Assert.False(builtIn);
        Assert.False(corrupt);
        Assert.Equal(0, fixture.Dialogs.ConfirmCount);
        Assert.True(Directory.Exists(corruptDirectory));
        Assert.Contains("remove.unknown", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("$selectionKey", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("not a healthy installed custom skin", fixture.Dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task RemoveAsync_UnselectedCustomRemovesOnlyTargetAndRaisesOneEvent()
    {
        using var fixture = new ManagementFixture { ConfirmRemoval = true };
        var target = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("88888888-2222-2222-2222-222222222222")))).Installed!;
        var sibling = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("88888888-3333-3333-3333-333333333333")))).Installed!;
        var settingsSentinel = fixture.WriteSentinel(
            fixture.Paths.SettingsRoot,
            "settings-sentinel.txt");
        var draftsSentinel = fixture.WriteSentinel(
            fixture.Paths.DraftsRoot,
            "draft-sentinel.txt");
        var importsSentinel = fixture.WriteSentinel(
            fixture.Paths.ImportsRoot,
            "import-sentinel.txt");
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var removed = await fixture.Controller.RemoveAsync(target.SelectionKey);

        Assert.True(removed);
        Assert.False(Directory.Exists(target.DirectoryPath));
        Assert.True(Directory.Exists(sibling.DirectoryPath));
        Assert.True(File.Exists(settingsSentinel));
        Assert.True(File.Exists(draftsSentinel));
        Assert.True(File.Exists(importsSentinel));
        Assert.Equal(1, fixture.Dialogs.ConfirmCount);
        Assert.Equal(1, catalogEvents);
        Assert.Equal(
            new[]
            {
                SkinSelectionKey.HudDial,
                SkinSelectionKey.EnergyRing,
                SkinSelectionKey.LiquidGlass,
                SkinSelectionKey.Aurora,
                SkinSelectionKey.LiquidTank
            },
            fixture.Controller.Entries
                .Where(entry => !entry.CanRemove)
                .Select(entry => entry.SelectionKey));
        Assert.Single(fixture.Controller.Entries, entry => entry.CanRemove);
    }

    [Fact]
    public async Task RemoveAsync_CancelPreservesPackageAndCatalogGeneration()
    {
        using var fixture = new ManagementFixture { ConfirmRemoval = false };
        var installed = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("88888888-4444-4444-4444-444444444444")))).Installed!;
        Assert.True(fixture.SkinController.TryPrepare(
            installed.SelectionKey,
            out var candidate,
            out _));
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var removed = await fixture.Controller.RemoveAsync(installed.SelectionKey);

        Assert.False(removed);
        Assert.True(Directory.Exists(installed.DirectoryPath));
        Assert.Equal(0, catalogEvents);
        fixture.SkinController.Activate(candidate!);
        Assert.Equal(installed.SelectionKey, fixture.SkinController.CurrentDescriptor.SelectionKey);
    }

    [Fact]
    public async Task RemoveAsync_SelectedCustomPersistsAndActivatesHudDialBeforeDeletion()
    {
        using var fixture = new ManagementFixture { ConfirmRemoval = true };
        var installed = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("88888888-5555-5555-5555-555555555555")))).Installed!;
        fixture.ActivateCustom(installed.SelectionKey);
        var customInstance = fixture.SkinController.CurrentSkin;
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var removed = await fixture.Controller.RemoveAsync(installed.SelectionKey);

        Assert.True(removed);
        Assert.False(Directory.Exists(installed.DirectoryPath));
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.Settings.Load().SelectedSkinKey);
        Assert.Equal(
            SkinSelectionKey.HudDial,
            fixture.SkinController.CurrentDescriptor.SelectionKey);
        Assert.NotSame(customInstance, fixture.SkinController.CurrentSkin);
        Assert.Equal(1, catalogEvents);
        Assert.DoesNotContain(
            fixture.Controller.Entries,
            entry => entry.SelectionKey == installed.SelectionKey);
    }

    [Fact]
    public async Task RemoveAsync_SelectedCustomSaveFailureAbortsBeforeDeletion()
    {
        using var fixture = new ManagementFixture { ConfirmRemoval = true };
        var installed = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("88888888-6666-6666-6666-666666666666")))).Installed!;
        fixture.ActivateCustom(installed.SelectionKey);
        var customInstance = fixture.SkinController.CurrentSkin;
        fixture.BlockSettingsWrites();

        var removed = await fixture.Controller.RemoveAsync(installed.SelectionKey);

        Assert.False(removed);
        Assert.True(Directory.Exists(installed.DirectoryPath));
        Assert.Equal(installed.SelectionKey, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(installed.SelectionKey, fixture.SkinController.CurrentDescriptor.SelectionKey);
        Assert.Same(customInstance, fixture.SkinController.CurrentSkin);
        Assert.Contains("remove.fallback-save", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("$settings.selectedSkinKey", fixture.Dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task RemoveAsync_RemoveFailureLeavesDurableHudDialAndCustomEntry()
    {
        using var fixture = new ManagementFixture { ConfirmRemoval = true };
        var installed = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("88888888-7777-7777-7777-777777777777")))).Installed!;
        fixture.ActivateCustom(installed.SelectionKey);
        var installedFile = Directory.EnumerateFiles(installed.DirectoryPath).First();
        using var deleteBlocker = File.Open(
            installedFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var removed = await fixture.Controller.RemoveAsync(installed.SelectionKey);

        Assert.False(removed);
        Assert.True(Directory.Exists(installed.DirectoryPath));
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.Settings.Load().SelectedSkinKey);
        Assert.Equal(
            SkinSelectionKey.HudDial,
            fixture.SkinController.CurrentDescriptor.SelectionKey);
        Assert.Contains(
            fixture.Controller.Entries,
            entry => entry.SelectionKey == installed.SelectionKey && entry.CanRemove);
        Assert.Contains("remove.io", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("$remove", fixture.Dialogs.LastErrorMessage);
        Assert.Equal(0, catalogEvents);
    }

    [Fact]
    public async Task RemoveAsync_CommittedCleanupFailureRefreshesAndReturnsRemoved()
    {
        using var fixture = new ManagementFixture(
            failOperationCleanup: true)
        {
            ConfirmRemoval = true
        };
        var installed = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("10101010-2222-2222-2222-222222222222")))).Installed!;
        fixture.Dialogs.ClearError();
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var removed = await fixture.Controller.RemoveAsync(installed.SelectionKey);

        Assert.True(removed);
        Assert.False(Directory.Exists(installed.DirectoryPath));
        Assert.DoesNotContain(
            fixture.Controller.Entries,
            entry => entry.SelectionKey == installed.SelectionKey);
        Assert.Equal(1, catalogEvents);
        Assert.Contains("remove.cleanup-failed", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("$remove", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("Recovery operation:", fixture.Dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task RemoveAsync_CommittedRemovalPublishesSnapshotWhenMissingActiveFallbackSaveFails()
    {
        using var fixture = new ManagementFixture { ConfirmRemoval = true };
        var active = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("10101010-3333-3333-3333-333333333333")))).Installed!;
        var target = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("10101010-4444-4444-4444-444444444444")))).Installed!;
        fixture.ActivateCustom(active.SelectionKey);
        var activeInstance = fixture.SkinController.CurrentSkin;
        Directory.Delete(active.DirectoryPath, recursive: true);
        fixture.BlockSettingsWrites();
        var catalogEvents = 0;
        fixture.Controller.CatalogChanged += (_, _) => catalogEvents++;

        var removed = await fixture.Controller.RemoveAsync(target.SelectionKey);

        Assert.True(removed);
        Assert.False(Directory.Exists(target.DirectoryPath));
        Assert.DoesNotContain(fixture.Controller.Entries, entry => entry.CanRemove);
        Assert.Equal(1, catalogEvents);
        Assert.Equal(active.SelectionKey, fixture.ViewModel.SelectedSkinKey);
        Assert.Same(activeInstance, fixture.SkinController.CurrentSkin);
        Assert.Contains("remove.fallback-save", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("$settings.selectedSkinKey", fixture.Dialogs.LastErrorMessage);
    }

    [Fact]
    public async Task RefreshAndReplaceCatalog_RejectStaleCandidateAndPreserveMissingActiveInstance()
    {
        using var fixture = new ManagementFixture();
        var installed = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("99999999-1111-1111-1111-111111111111")))).Installed!;
        fixture.ActivateCustom(installed.SelectionKey);
        var active = fixture.SkinController.CurrentSkin;
        Assert.True(fixture.SkinController.TryPrepare(
            SkinSelectionKey.HudDial,
            out var stale,
            out _));
        Directory.Delete(installed.DirectoryPath, recursive: true);

        var snapshot = fixture.Catalog.Refresh();
        var replaced = fixture.SkinController.ReplaceCatalog(snapshot, out var failure);

        Assert.False(replaced);
        Assert.Equal("skin.selection.missing", failure!.ErrorCode);
        Assert.Same(active, fixture.SkinController.CurrentSkin);
        Assert.Equal(installed.SelectionKey, fixture.SkinController.CurrentDescriptor.SelectionKey);
        Assert.Throws<InvalidOperationException>(() =>
            fixture.SkinController.Activate(stale!));
    }

    [Fact]
    public async Task ReplaceCatalog_KeepsLiveSkinButNextPrepareUsesReplacedDescriptor()
    {
        using var fixture = new ManagementFixture();
        var skinId = Guid.Parse("99999999-2222-2222-2222-222222222222");
        var first = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("1.0.0")))).Installed!;
        fixture.ActivateCustom(first.SelectionKey);
        var liveSkin = fixture.SkinController.CurrentSkin;
        var livePresentation = fixture.SkinController.CurrentPresentation;
        fixture.DialogDecision = SkinCollisionDecision.Replace;

        var replacement = await fixture.Controller.ImportAsync(fixture.WritePackage(
            skinId,
            SemanticVersion.Parse("2.0.0"),
            fileName: "same-id-v2.cqskin"));

        Assert.True(replacement.Succeeded, Format(replacement.Errors));
        Assert.Same(liveSkin, fixture.SkinController.CurrentSkin);
        Assert.Equal(livePresentation, fixture.SkinController.CurrentPresentation);
        fixture.ActivateBuiltIn(SkinSelectionKey.HudDial);
        Assert.True(fixture.SkinController.TryPrepare(
            first.SelectionKey,
            out var refreshed,
            out var failure),
            failure?.ErrorCode);
        Assert.NotSame(liveSkin, refreshed!.Skin);
        Assert.Equal(
            SemanticVersion.Parse("2.0.0"),
            refreshed.Descriptor.Installed!.PackageVersion);
    }

    [Fact]
    public async Task ImportRefresh_ExternalMissingActiveCustomCompletesDurableHudDialFallback()
    {
        using var fixture = new ManagementFixture();
        var active = (await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("99999999-4444-4444-4444-444444444444")))).Installed!;
        fixture.ActivateCustom(active.SelectionKey);
        var activeInstance = fixture.SkinController.CurrentSkin;
        Directory.Delete(active.DirectoryPath, recursive: true);

        var imported = await fixture.Controller.ImportAsync(fixture.WritePackage(
            Guid.Parse("99999999-5555-5555-5555-555555555555")));

        Assert.True(imported.Succeeded, Format(imported.Errors));
        Assert.Equal(SkinSelectionKey.HudDial, fixture.ViewModel.SelectedSkinKey);
        Assert.Equal(SkinSelectionKey.HudDial, fixture.Settings.Load().SelectedSkinKey);
        Assert.Equal(
            SkinSelectionKey.HudDial,
            fixture.SkinController.CurrentDescriptor.SelectionKey);
        Assert.NotSame(activeInstance, fixture.SkinController.CurrentSkin);
        Assert.DoesNotContain(
            fixture.Controller.Entries,
            entry => entry.SelectionKey == active.SelectionKey);
    }

    [Fact]
    public async Task ChooseAndImportAsync_ChoosesExactlyOnceAndNullHasNoSideEffects()
    {
        using var cancelled = new ManagementFixture();
        var cancelledEvents = 0;
        cancelled.Controller.CatalogChanged += (_, _) => cancelledEvents++;

        var noSelection = await cancelled.Controller.ChooseAndImportAsync();

        Assert.Null(noSelection);
        Assert.Equal(1, cancelled.Dialogs.ChooseCount);
        Assert.Equal(0, cancelled.Dialogs.PreviewCount);
        Assert.Equal(0, cancelledEvents);
        Assert.Null(cancelled.Dialogs.LastErrorMessage);

        using var selected = new ManagementFixture();
        var packagePath = selected.WritePackage(
            Guid.Parse("99999999-3333-3333-3333-333333333333"));
        selected.Dialogs.ChosenPackagePath = packagePath;

        var imported = await selected.Controller.ChooseAndImportAsync();

        Assert.True(imported!.Succeeded, Format(imported.Errors));
        Assert.Equal(1, selected.Dialogs.ChooseCount);
        Assert.Equal(packagePath, selected.Dialogs.LastChosenPackagePath);
    }

    [Fact]
    public void DesignerAvailabilityAndFailureFlowThroughOneActionableDialog()
    {
        using var fixture = new ManagementFixture(
            designerAvailable: true,
            designerStarts: false);

        Assert.True(fixture.Controller.DesignerAvailable);
        Assert.False(fixture.Controller.OpenDesigner());
        Assert.Equal(1, fixture.DesignerStartCount);
        Assert.Contains("designer.launch", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("$designer.executable", fixture.Dialogs.LastErrorMessage);
        Assert.Contains("did not start", fixture.Dialogs.LastErrorMessage);
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join(Environment.NewLine, errors.Select(error =>
            $"{error.Code}: {error.Message}"));

    private sealed class ManagementFixture : IDisposable
    {
        private readonly RecordingDialogs _dialogs = new();
        private readonly string _settingsPath;

        public ManagementFixture(
            bool designerAvailable = false,
            bool designerStarts = false,
            bool failOperationCleanup = false)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CodexQuotaHud.Task9.Import",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Paths = new SkinStoragePaths(Root);
            var installedCatalog = new InstalledSkinCatalog(Paths, HudVersion);
            Catalog = new HudSkinCatalog(installedCatalog);
            _settingsPath = Path.Combine(Root, "persist", "settings.json");
            Settings = new SettingsStore(
                _settingsPath,
                selectionKey => Catalog.TryGet(selectionKey, out _));
            ViewModel = new QuotaOrbViewModel(
                new SilentRefreshController(),
                Settings,
                new AppSettings(SelectedSkinKey: SkinSelectionKey.HudDial),
                new ImmediateDispatcher(),
                static () => { },
                selectionKey => Catalog.TryGet(selectionKey, out _));
            SkinController = new SkinController(
                Catalog,
                descriptor => new ManagementSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            SkinController.Render(new QuotaSkinState(
                68,
                34,
                "5 hours",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: false));
            var designerLauncher = new DesignerLauncher(
                Path.Combine(Root, "app"),
                _ => designerAvailable,
                _ =>
                {
                    DesignerStartCount++;
                    return designerStarts;
                });
            Controller = new SkinManagementController(
                failOperationCleanup
                    ? new SkinPackageInstaller(
                        Paths,
                        HudVersion,
                        PhysicalSkinFileSystem.Instance,
                        directoryDeleteProvider:
                            ThrowingDirectoryDeleteProvider.Instance)
                    : new SkinPackageInstaller(Paths, HudVersion),
                Catalog,
                ViewModel,
                SkinController,
                designerLauncher,
                _dialogs,
                HudVersion,
                new ImmediateDispatcher());
        }

        public string Root { get; }

        public SkinStoragePaths Paths { get; }

        public HudSkinCatalog Catalog { get; }

        public SettingsStore Settings { get; }

        public QuotaOrbViewModel ViewModel { get; }

        public SkinController SkinController { get; }

        public SkinManagementController Controller { get; }

        public int DesignerStartCount { get; private set; }

        public RecordingDialogs Dialogs => _dialogs;

        public SkinCollisionDecision DialogDecision
        {
            set => _dialogs.Decision = value;
        }

        public bool ConfirmRemoval
        {
            set => _dialogs.ConfirmRemovalResult = value;
        }

        public void ActivateCustom(string selectionKey)
        {
            Assert.True(SkinController.TryPrepare(
                selectionKey,
                out var candidate,
                out var failure),
                failure?.ErrorCode);
            Assert.True(ViewModel.TrySelectSkinKey(selectionKey));
            SkinController.Activate(candidate!);
        }

        public void ActivateBuiltIn(string selectionKey)
        {
            Assert.True(SkinController.TryPrepare(
                selectionKey,
                out var candidate,
                out var failure),
                failure?.ErrorCode);
            Assert.True(ViewModel.TrySelectSkinKey(selectionKey));
            SkinController.Activate(candidate!);
        }

        public void BlockSettingsWrites()
        {
            var settingsDirectory = Path.GetDirectoryName(_settingsPath)!;
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, recursive: true);
            }

            File.WriteAllText(settingsDirectory, "blocks settings directory creation");
        }

        public string WriteSentinel(string directory, string fileName)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, fileName);
            return path;
        }

        public string WritePackage(
            Guid skinId,
            SemanticVersion? packageVersion = null,
            SemanticVersion? minimumHudVersion = null,
            string? fileName = null)
        {
            var destination = Path.Combine(
                Root,
                fileName ?? $"{skinId:D}-{Guid.NewGuid():N}.cqskin");
            var result = new SkinPackageWriter().WriteFile(
                destination,
                Package(
                    skinId,
                    packageVersion ?? SemanticVersion.Parse("1.0.0"),
                    minimumHudVersion ?? HudVersion),
                overwrite: false,
                CancellationToken.None);
            Assert.True(result.IsValid, Format(result.Errors));
            return destination;
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static SkinPackageBuildRequest Package(
            Guid skinId,
            SemanticVersion packageVersion,
            SemanticVersion minimumHudVersion)
        {
            var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
            return new SkinPackageBuildRequest(
                new SkinManifest(
                    1,
                    skinId,
                    "Imported skin",
                    "Unverified author",
                    packageVersion,
                    "Task 9 import fixture",
                    SkinPackageLimits.FreeDecorationRingTemplateId,
                    minimumHudVersion,
                    null,
                    []),
                new SkinTheme(
                    1,
                    SkinPackageLimits.FreeDecorationRingTemplateId,
                    identity,
                    identity,
                    identity,
                    "#FF53DCF8",
                    "#FF9A68FF",
                    "#FF0A1622",
                    0.9,
                    96,
                    8,
                    6,
                    270,
                    "#FF24CFF2",
                    0.5,
                    28,
                    12,
                    SkinTextWeight.SemiBold,
                    SkinTextPlacement.NumberAboveLabel,
                    new SkinAnimationSettings(0.25, 0.5, 0.75, 1)),
                new Dictionary<SkinAssetSlot, SkinAsset>());
        }
    }

    private sealed class RecordingDialogs : ISkinManagementDialogs
    {
        public SkinCollisionDecision Decision { get; set; } =
            SkinCollisionDecision.Replace;

        public int PreviewCount { get; private set; }

        public int ConfirmCount { get; private set; }

        public int ChooseCount { get; private set; }

        public bool ConfirmRemovalResult { get; set; }

        public string? ChosenPackagePath { get; set; }

        public string? LastChosenPackagePath { get; private set; }

        public string? LastErrorMessage { get; private set; }

        public string? ChoosePackagePath()
        {
            ChooseCount++;
            LastChosenPackagePath = ChosenPackagePath;
            return ChosenPackagePath;
        }

        public SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview)
        {
            PreviewCount++;
            return Decision;
        }

        public bool ConfirmRemoval(SkinMenuEntry entry)
        {
            ConfirmCount++;
            return ConfirmRemovalResult;
        }

        public void ShowError(string message) => LastErrorMessage = message;

        public void ClearError() => LastErrorMessage = null;
    }

    private sealed class ThrowingDirectoryDeleteProvider :
        ISafeDirectoryDeleteProvider
    {
        public static ThrowingDirectoryDeleteProvider Instance { get; } = new();

        public void DeleteOwnedTree(string rootPath, int maximumEntries) =>
            throw new IOException("Injected Task 9 cleanup failure.");
    }

    private sealed class ManagementSkin(string selectionKey) : IQuotaSkin
    {
        public string SelectionKey { get; } = selectionKey;

        public System.Windows.FrameworkElement View => null!;

        public QuotaSkinState? LastState { get; private set; }

        public void Render(QuotaSkinState state) => LastState = state;
    }

    private sealed class SilentRefreshController : IQuotaRefreshController
    {
        public event Action<CodexQuotaHud.Core.Refresh.QuotaRefreshState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task RefreshNowAsync(bool onlyIfStale, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }
}
