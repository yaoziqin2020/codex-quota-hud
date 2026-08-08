using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Documents;

public sealed class DesignerDocumentServiceTests
{
    private static readonly SemanticVersion HudVersion =
        SemanticVersion.Parse("1.1.1");
    private static readonly SemanticVersion LegacyTemplateMinimumHudVersion =
        SemanticVersion.Parse("1.2.3");
    private static readonly SemanticVersion TemplateMinimumHudVersion =
        SemanticVersion.Parse("1.3.0");

    [Fact]
    public void CreateNew_UsesRefreshDefaultsAndTemplateCompatibility()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var skinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var now = DateTimeOffset.Parse("2026-08-02T01:02:03Z");
        var sut = CreateService(temporary, () => Guid.NewGuid(), () => now);

        var result = sut.CreateNew(draftId, skinId, now);

        Assert.Empty(result.Errors);
        var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(draftId, draft.DraftId);
        Assert.Equal(skinId, draft.SkinId);
        Assert.Equal(TemplateMinimumHudVersion, draft.MinimumHudVersion);
        Assert.Equal(2d, draft.Theme.Animation.RefreshSpeedMultiplier);
        Assert.Equal(1.5d, draft.Theme.Animation.RefreshHoldSeconds);
        Assert.Empty(result.Assets);
        Assert.False(Directory.Exists(new SkinStoragePaths(
            temporary.Path).DraftsRoot));
    }

    [Fact]
    public async Task OpenDraft_UsesNamedRecoveryPrecedenceAndLoadsOnlyOwnedDecodedAssets()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var named = WithBackground(
            SkinDraftFactory.CreateNew(
                draftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
                HudVersion) with { Revision = 2 },
            "named.png");
        var recovery = named with
        {
            Revision = 3,
            DisplayName = "Recovered document",
            Assets = new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [SkinAssetSlot.Background] = named.Assets[SkinAssetSlot.Background]
                    with { OriginalFileName = "recovery-source.png" }
            }
        };
        var store = new DraftStore(paths);
        await store.SaveNamedAsync(named);
        await store.SaveRecoveryAsync(recovery);
        var project = new DraftProjectPaths(paths.DraftsRoot, draftId);
        var legacyRecovery = RemoveRefreshAnimationFields(
            await File.ReadAllBytesAsync(project.RecoveryPath));
        await File.WriteAllBytesAsync(project.RecoveryPath, legacyRecovery);
        var owned = Path.Combine(
            project.AssetsRoot,
            "background.png");
        await File.WriteAllBytesAsync(owned, AlphaPng);
        var sut = CreateService(temporary, Guid.NewGuid, () => recovery.UpdatedAtUtc);

        var result = sut.OpenDraft(draftId);

        Assert.Empty(result.Errors);
        var opened = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(recovery.DraftId, opened.DraftId);
        Assert.Equal(recovery.Revision, opened.Revision);
        Assert.Equal(recovery.DisplayName, opened.DisplayName);
        Assert.Equal(
            recovery.Assets[SkinAssetSlot.Background],
            opened.Assets[SkinAssetSlot.Background]);
        Assert.Equal(TemplateMinimumHudVersion, opened.MinimumHudVersion);
        Assert.Equal(2d, opened.Theme.Animation.RefreshSpeedMultiplier);
        Assert.Equal(1.5d, opened.Theme.Animation.RefreshHoldSeconds);
        Assert.Equal(legacyRecovery, await File.ReadAllBytesAsync(project.RecoveryPath));
        var asset = Assert.Single(result.Assets).Value;
        Assert.Null(opened.Assets[SkinAssetSlot.Background].StorageRelativePath);
        Assert.Equal("assets/background.png", asset.RelativePath);
        Assert.Equal(AlphaPng, asset.Content);
        Assert.Equal(1, asset.PixelWidth);
        Assert.Equal(1, asset.PixelHeight);

        await store.SaveRecoveryAsync(opened);
        AssertCanonicalRefreshAnimation(
            await File.ReadAllBytesAsync(project.RecoveryPath));
    }

    [Fact]
    public async Task OpenDraft_NamedAndRecoveryResolveTheirIndependentAddressedBlobs()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var named = WithAddressedBackground(
            SkinDraftFactory.CreateNew(
                draftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
                HudVersion) with { Revision = 1 },
            AlphaPng,
            "named.png");
        var recovery = WithAddressedBackground(
            named with { Revision = 2, DisplayName = "Recovery" },
            OpaquePng,
            "recovery.png");
        var store = new DraftStore(paths);
        await store.SaveNamedAsync(named);
        await store.SaveRecoveryAsync(recovery);
        WriteAddressedAsset(paths, named);
        WriteAddressedAsset(paths, recovery);
        var sut = CreateService(temporary, Guid.NewGuid, () => recovery.UpdatedAtUtc);

        var recovered = sut.OpenDraft(draftId);

        Assert.Empty(recovered.Errors);
        Assert.Equal(recovery.Assets[SkinAssetSlot.Background],
            recovered.Draft?.Assets[SkinAssetSlot.Background]);
        var recoveredAsset = Assert.Single(recovered.Assets).Value;
        Assert.Equal("assets/background.png", recoveredAsset.RelativePath);
        Assert.Equal(OpaquePng, recoveredAsset.Content);

        Assert.True(await store.DiscardWorkingCopyAsync(draftId, recovery.Revision));
        var reopenedNamed = sut.OpenDraft(draftId);

        Assert.Empty(reopenedNamed.Errors);
        Assert.Equal(named.Assets[SkinAssetSlot.Background],
            reopenedNamed.Draft?.Assets[SkinAssetSlot.Background]);
        var namedAsset = Assert.Single(reopenedNamed.Assets).Value;
        Assert.Equal("assets/background.png", namedAsset.RelativePath);
        Assert.Equal(AlphaPng, namedAsset.Content);
    }

    [Theory]
    [InlineData(false, "document.asset-missing")]
    [InlineData(true, "document.asset-hash-mismatch")]
    public async Task OpenDraft_AddressedBlobMissingOrHashMismatchFailsAtAssetLocation(
        bool writeMismatchedBlob,
        string expectedCode)
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var draft = WithAddressedBackground(
            SkinDraftFactory.CreateNew(
                draftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
                HudVersion),
            AlphaPng,
            "source.png");
        await new DraftStore(paths).SaveNamedAsync(draft);
        if (writeMismatchedBlob)
        {
            var reference = draft.Assets[SkinAssetSlot.Background];
            var leaf = DraftAssetStorage.ResolveOwnedLeaf(reference);
            var path = Path.Combine(
                new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot,
                leaf);
            await File.WriteAllBytesAsync(path, OpaquePng);
        }
        var sut = CreateService(temporary, Guid.NewGuid, () => draft.UpdatedAtUtc);

        var result = sut.OpenDraft(draftId);

        Assert.Null(result.Draft);
        var error = Assert.Single(result.Errors);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("$.assets[0]", error.Location);
    }

    [Fact]
    public async Task OpenDraft_NormalizesV123MinimumWithoutChangingSkinContent()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var legacy = WithBackground(
            SkinDraftFactory.CreateNew(
                draftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                DateTimeOffset.Parse("2026-08-02T01:02:03Z"),
                LegacyTemplateMinimumHudVersion) with
            {
                PackageVersion = SemanticVersion.Parse("2.3.4")
            },
            "legacy.png");
        var store = new DraftStore(paths);
        await store.SaveNamedAsync(legacy);
        var project = new DraftProjectPaths(paths.DraftsRoot, draftId);
        await File.WriteAllBytesAsync(
            project.NamedDraftPath,
            RemoveTextLayoutFields(
                await File.ReadAllBytesAsync(project.NamedDraftPath)));
        Directory.CreateDirectory(project.AssetsRoot);
        await File.WriteAllBytesAsync(
            Path.Combine(project.AssetsRoot, "background.png"),
            AlphaPng);
        var sut = CreateService(
            temporary,
            Guid.NewGuid,
            () => DateTimeOffset.Parse("2026-08-02T02:02:03Z"));

        var result = sut.OpenDraft(draftId);

        Assert.Empty(result.Errors);
        var opened = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(TemplateMinimumHudVersion, opened.MinimumHudVersion);
        Assert.Equal(legacy.SkinId, opened.SkinId);
        Assert.Equal(legacy.PackageVersion, opened.PackageVersion);
        Assert.Equal(legacy.Theme, opened.Theme);
        Assert.Equal(
            legacy.Assets.Keys.OrderBy(slot => slot),
            opened.Assets.Keys.OrderBy(slot => slot));
        foreach (var asset in legacy.Assets)
        {
            Assert.Equal(asset.Value, opened.Assets[asset.Key]);
        }

        await store.SaveNamedAsync(opened);
        AssertCanonicalTextLayout(
            await File.ReadAllBytesAsync(project.NamedDraftPath));
    }

    [Fact]
    public void EditInstalled_ClonesHealthyCustomMetadataAndAssetsWithoutChangingInstalledBytes()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var installedSkinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var newDraftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(temporary, installedSkinId, "Installed Ocean");
        ExtractInstalled(package, paths, installedSkinId);
        var installedAsset = Path.Combine(
            paths.InstalledSkinsRoot,
            installedSkinId.ToString("D"),
            "assets",
            "background.png");
        var installedBefore = File.ReadAllBytes(installedAsset);
        var sut = CreateService(
            temporary,
            () => newDraftId,
            () => DateTimeOffset.Parse("2026-08-02T02:00:00Z"));

        var result = sut.EditInstalled(
            $"custom:{installedSkinId:D}");

        Assert.Empty(result.Errors);
        var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(newDraftId, draft.DraftId);
        Assert.Equal(installedSkinId, draft.SkinId);
        Assert.Equal("Installed Ocean", draft.DisplayName);
        Assert.Equal("Fixture Author", draft.Author);
        Assert.Equal(SemanticVersion.Parse("2.3.4"), draft.PackageVersion);
        Assert.Equal("Fixture description", draft.Description);
        Assert.Equal(TemplateMinimumHudVersion, draft.MinimumHudVersion);
        Assert.Equal(installedBefore, File.ReadAllBytes(installedAsset));
        var reference = draft.Assets[SkinAssetSlot.Background];
        Assert.Equal("assets/background.png", reference.RelativePath);
        var storageRelativePath = Assert.IsType<string>(
            reference.StorageRelativePath);
        var owned = Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, newDraftId).AssetsRoot,
            Path.GetFileName(storageRelativePath));
        Assert.Equal(installedBefore, File.ReadAllBytes(owned));
        Assert.False(File.Exists(Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, newDraftId).AssetsRoot,
            "background.png")));
        Assert.Equal(installedBefore,
            result.Assets[SkinAssetSlot.Background].Content);
    }

    [Fact]
    public async Task ImportForEditing_ValidatesPackageAndCreatesIsolatedDraftWithoutInstalling()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var skinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(temporary, skinId, "Imported Ocean");
        RewriteThemeInPackage(
            package,
            document => RemoveRefreshAnimationFields(
                document,
                draftDocument: false));
        var packageBefore = await File.ReadAllBytesAsync(package);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T03:00:00Z"));

        var result = await sut.ImportForEditingAsync(package, HudVersion);

        Assert.Empty(result.Errors);
        var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(draftId, draft.DraftId);
        Assert.Equal(skinId, draft.SkinId);
        Assert.Equal("Imported Ocean", draft.DisplayName);
        Assert.Equal(TemplateMinimumHudVersion, draft.MinimumHudVersion);
        Assert.Equal(2d, draft.Theme.Animation.RefreshSpeedMultiplier);
        Assert.Equal(1.5d, draft.Theme.Animation.RefreshHoldSeconds);
        Assert.Equal(packageBefore, await File.ReadAllBytesAsync(package));
        Assert.False(Directory.Exists(paths.InstalledSkinsRoot));
        Assert.Single(result.Assets);
        var reference = draft.Assets[SkinAssetSlot.Background];
        var storageRelativePath = Assert.IsType<string>(
            reference.StorageRelativePath);
        Assert.Equal(
            DraftAssetStorage.CreateContentRelativePath(
                reference.RelativePath,
                result.Assets[SkinAssetSlot.Background].Content),
            storageRelativePath);
        Assert.True(File.Exists(Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot,
            Path.GetFileName(storageRelativePath))));
    }

    [Fact]
    public async Task ImportForEditing_TwoPngSlotsWithIdenticalBytesReuseOneVerifiedImmutableBlob()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(
            temporary,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Shared Blob",
            includeCenter: true);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T03:30:00Z"));

        var result = await sut.ImportForEditingAsync(package, HudVersion);

        Assert.Empty(result.Errors);
        var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
        var background = draft.Assets[SkinAssetSlot.Background];
        var center = draft.Assets[SkinAssetSlot.Center];
        Assert.Equal(background.StorageRelativePath, center.StorageRelativePath);
        Assert.Equal("assets/background.png", background.RelativePath);
        Assert.Equal("assets/center.png", center.RelativePath);
        Assert.Equal(AlphaPng, result.Assets[SkinAssetSlot.Background].Content);
        Assert.Equal(AlphaPng, result.Assets[SkinAssetSlot.Center].Content);
        var assetsRoot = new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot;
        var storageLeaf = Path.GetFileName(Assert.IsType<string>(
            background.StorageRelativePath));
        Assert.Equal(
            [storageLeaf],
            Directory.EnumerateFiles(assetsRoot)
                .Select(path => Path.GetFileName(path)!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(AlphaPng, await File.ReadAllBytesAsync(Path.Combine(
            assetsRoot,
            storageLeaf)));
    }

    [Theory]
    [InlineData(PromotionWinnerKind.SameContent, true)]
    [InlineData(PromotionWinnerKind.MismatchedContent, false)]
    public async Task ImportForEditing_NoReplaceWinnerRaceReusesOnlyExactContent(
        PromotionWinnerKind winnerKind,
        bool expectedSuccess)
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(
            temporary,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            $"{winnerKind} Race");
        var racingStorage = new ImmutablePromotionRaceStorage(winnerKind);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T03:45:00Z"),
            racingStorage);

        var result = await sut.ImportForEditingAsync(package, HudVersion);

        Assert.Equal(1, racingStorage.MoveCalls);
        Assert.Equal(1, racingStorage.DeleteCalls);
        var storageRelativePath = DraftAssetStorage.CreateContentRelativePath(
            "assets/background.png",
            AlphaPng);
        var blobPath = Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot,
            Path.GetFileName(storageRelativePath));
        if (expectedSuccess)
        {
            Assert.Equal(1, racingStorage.ReleaseCalls);
            Assert.Empty(result.Errors);
            var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
            Assert.Equal(
                storageRelativePath,
                draft.Assets[SkinAssetSlot.Background].StorageRelativePath);
            Assert.Equal(AlphaPng, await File.ReadAllBytesAsync(blobPath));
        }
        else
        {
            Assert.Equal(1, racingStorage.ReleaseCalls);
            Assert.Null(result.Draft);
            Assert.Contains(result.Errors,
                error => error.Code == "document.cleanup-failed");
            Assert.Equal(OpaquePng, await File.ReadAllBytesAsync(blobPath));
        }

        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(blobPath)!),
            path => Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportForEditing_NormalizesV123MinimumWithoutChangingSkinContent()
    {
        using var temporary = new TemporaryDirectory();
        var skinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(
            temporary,
            skinId,
            "Legacy Ocean",
            minimumHudVersion: LegacyTemplateMinimumHudVersion);
        RewriteThemeInPackage(
            package,
            document => RemoveTextLayoutFields(
                document,
                draftDocument: false));
        var original = new SkinPackageReader().ValidateFile(
            package,
            SemanticVersion.Parse("9.0.0"),
            CancellationToken.None);
        Assert.True(original.IsValid, string.Join("; ", original.Errors));
        var source = Assert.IsType<SkinPackageDocument>(original.Value);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T03:00:00Z"));

        var result = await sut.ImportForEditingAsync(
            package,
            SemanticVersion.Parse("9.0.0"));

        Assert.Empty(result.Errors);
        var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(TemplateMinimumHudVersion, draft.MinimumHudVersion);
        Assert.Equal(source.Manifest.SkinId, draft.SkinId);
        Assert.Equal(source.Manifest.PackageVersion, draft.PackageVersion);
        Assert.Equal(source.Theme, draft.Theme);
        Assert.Equal(
            source.Assets.Keys.OrderBy(slot => slot),
            result.Assets.Keys.OrderBy(slot => slot));
        foreach (var asset in source.Assets)
        {
            var copied = result.Assets[asset.Key];
            Assert.Equal(asset.Value.Slot, copied.Slot);
            Assert.Equal(asset.Value.RelativePath, copied.RelativePath);
            Assert.Equal(asset.Value.Content, copied.Content);
            Assert.Equal(asset.Value.PixelWidth, copied.PixelWidth);
            Assert.Equal(asset.Value.PixelHeight, copied.PixelHeight);
            Assert.Equal(asset.Value.HasAlpha, copied.HasAlpha);
        }
    }

    [Fact]
    public async Task ImportForEditing_PreservesHigherDeclaredMinimumHudVersion()
    {
        using var temporary = new TemporaryDirectory();
        var skinId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(
            temporary,
            skinId,
            "Future Ocean",
            minimumHudVersion: SemanticVersion.Parse("2.0.0"));
        var packageBefore = await File.ReadAllBytesAsync(package);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T03:00:00Z"));

        var result = await sut.ImportForEditingAsync(
            package,
            SemanticVersion.Parse("9.0.0"));

        Assert.Empty(result.Errors);
        var draft = Assert.IsType<SkinDraftDocument>(result.Draft);
        Assert.Equal(SemanticVersion.Parse("2.0.0"), draft.MinimumHudVersion);
        Assert.Equal(packageBefore, await File.ReadAllBytesAsync(package));
    }

    [Theory]
    [InlineData("builtin:default", "document.installed-not-editable")]
    [InlineData("custom:not-a-guid", "document.installed-not-editable")]
    public void EditInstalled_RejectsBuiltInAndInvalidSelectionWithoutDraftMutation(
        string selectionKey,
        string code)
    {
        using var temporary = new TemporaryDirectory();
        var sut = CreateService(
            temporary,
            Guid.NewGuid,
            () => DateTimeOffset.UtcNow);

        var result = sut.EditInstalled(selectionKey);

        Assert.Null(result.Draft);
        Assert.Empty(result.Assets);
        Assert.Contains(result.Errors, error => error.Code == code);
        Assert.False(Directory.Exists(new SkinStoragePaths(
            temporary.Path).DraftsRoot));
    }

    [Fact]
    public async Task FailedOpenImportAndDraftIdCollisionPreserveEvidenceAndCurrentStorage()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var collisionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var collisionRoot = new DraftProjectPaths(
            paths.DraftsRoot,
            collisionId).ProjectRoot;
        Directory.CreateDirectory(collisionRoot);
        var evidence = Path.Combine(collisionRoot, "evidence.bin");
        await File.WriteAllBytesAsync(evidence, [1, 2, 3, 4]);
        var corruptDraftId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var corruptRoot = new DraftProjectPaths(
            paths.DraftsRoot,
            corruptDraftId).ProjectRoot;
        Directory.CreateDirectory(corruptRoot);
        var corruptDraft = Path.Combine(corruptRoot, "draft.json");
        await File.WriteAllTextAsync(corruptDraft, "{broken");
        var invalidPackage = Path.Combine(temporary.SourceRoot, "invalid.cqskin");
        await File.WriteAllBytesAsync(invalidPackage, [5, 6, 7]);
        var validPackage = BuildPackage(
            temporary,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Collision Ocean");
        var sut = CreateService(
            temporary,
            () => collisionId,
            () => DateTimeOffset.UtcNow);

        var opened = sut.OpenDraft(corruptDraftId);
        var imported = await sut.ImportForEditingAsync(
            invalidPackage,
            HudVersion);
        var collision = await sut.ImportForEditingAsync(
            validPackage,
            HudVersion);

        Assert.Null(opened.Draft);
        Assert.Contains(opened.Errors, error => error.Code == "draft.corrupt");
        Assert.Null(imported.Draft);
        Assert.NotEmpty(imported.Errors);
        Assert.Null(collision.Draft);
        Assert.Contains(collision.Errors,
            error => error.Code == "document.draft-exists");
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(evidence));
        Assert.Equal("{broken", await File.ReadAllTextAsync(corruptDraft));
        Assert.Equal([5, 6, 7], await File.ReadAllBytesAsync(invalidPackage));
    }

    [Fact]
    public async Task ImportForEditing_CheckCreateCollisionNeverClaimsOrCleansRacedProject()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(
            temporary,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Collision Transition");
        var transition = new TransitionStorage(TransitionFault.ClaimCollision);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T04:00:00Z"),
            transition);

        var rejected = await sut.ImportForEditingAsync(package, HudVersion);

        Assert.Null(rejected.Draft);
        Assert.Contains(rejected.Errors,
            error => error.Code == "document.draft-exists");
        Assert.Equal(1, transition.ProjectOpenCount);
        var evidence = Path.Combine(
            new DraftProjectPaths(
                new SkinStoragePaths(temporary.Path).DraftsRoot,
                draftId).ProjectRoot,
            "foreign-evidence.bin");
        Assert.Equal([7, 6, 5, 4], await File.ReadAllBytesAsync(evidence));
    }

    [Fact]
    public async Task ImportForEditing_MidCopyFailureDeletesOnlyClaimedPartialProject()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var paths = new SkinStoragePaths(temporary.Path);
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = BuildPackage(
            temporary,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Partial Copy",
            includeCenter: true,
            centerUsesSameContent: false);
        var transition = new TransitionStorage(TransitionFault.SecondAssetWrite);
        var sut = CreateService(
            temporary,
            () => draftId,
            () => DateTimeOffset.Parse("2026-08-02T05:00:00Z"),
            transition);

        var rejected = await sut.ImportForEditingAsync(package, HudVersion);

        Assert.Null(rejected.Draft);
        Assert.Contains(rejected.Errors,
            error => error.Code == "document.asset-copy-failed");
        Assert.Equal(2, transition.AssetWriteCount);
        Assert.False(Directory.Exists(new DraftProjectPaths(
            paths.DraftsRoot,
            draftId).ProjectRoot));
    }

    private static DesignerDocumentService CreateService(
        TemporaryDirectory temporary,
        Func<Guid> newId,
        Func<DateTimeOffset> utcNow,
        IDesignerDraftStorageLeaseProvider? storage = null)
    {
        var paths = new SkinStoragePaths(temporary.Path);
        return storage is null
            ? new DesignerDocumentService(
                paths,
                new DraftStore(paths),
                new InstalledSkinCatalog(paths, HudVersion),
                new SkinPackageReader(),
                newId,
                utcNow)
            : new DesignerDocumentService(
                paths,
                new DraftStore(paths),
                new InstalledSkinCatalog(paths, HudVersion),
                new SkinPackageReader(),
                newId,
                utcNow,
                storage);
    }

    private static SkinDraftDocument WithBackground(
        SkinDraftDocument draft,
        string originalFileName) => draft with
        {
            Assets = new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [SkinAssetSlot.Background] = new(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    originalFileName)
            }
        };

    private static SkinDraftDocument WithAddressedBackground(
        SkinDraftDocument draft,
        byte[] content,
        string originalFileName)
    {
        const string relativePath = "assets/background.png";
        return draft with
        {
            Assets = new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [SkinAssetSlot.Background] = new(
                    SkinAssetSlot.Background,
                    relativePath,
                    originalFileName,
                    DraftAssetStorage.CreateContentRelativePath(
                        relativePath,
                        content))
            }
        };
    }

    private static void WriteAddressedAsset(
        SkinStoragePaths paths,
        SkinDraftDocument draft)
    {
        var reference = draft.Assets[SkinAssetSlot.Background];
        var leaf = DraftAssetStorage.ResolveOwnedLeaf(reference);
        var path = Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, draft.DraftId).AssetsRoot,
            leaf);
        var content = reference.OriginalFileName == "recovery.png"
            ? OpaquePng
            : AlphaPng;
        File.WriteAllBytes(path, content);
    }

    private static string BuildPackage(
        TemporaryDirectory temporary,
        Guid skinId,
        string displayName,
        bool includeCenter = false,
        bool centerUsesSameContent = true,
        SemanticVersion? minimumHudVersion = null)
    {
        var packagePath = Path.Combine(
            temporary.SourceRoot,
            displayName.Replace(' ', '-') + ".cqskin");
        var defaults = SkinDraftFactory.CreateNew(
            Guid.NewGuid(),
            skinId,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            HudVersion);
        var manifest = new SkinManifest(
            SkinPackageLimits.SchemaVersion,
            skinId,
            displayName,
            "Fixture Author",
            SemanticVersion.Parse("2.3.4"),
            "Fixture description",
            defaults.Theme.TemplateId,
            minimumHudVersion ?? HudVersion,
            OriginSkinId: null,
            Assets: []);
        var assets = new Dictionary<SkinAssetSlot, SkinAsset>
        {
            [SkinAssetSlot.Background] = new(
                SkinAssetSlot.Background,
                "source.png",
                AlphaPng,
                1,
                1,
                HasAlpha: true)
        };
        if (includeCenter)
        {
            var centerContent = centerUsesSameContent ? AlphaPng : OpaquePng;
            assets.Add(
                SkinAssetSlot.Center,
                new SkinAsset(
                    SkinAssetSlot.Center,
                    "center.png",
                    centerContent,
                    1,
                    1,
                    HasAlpha: centerUsesSameContent));
        }

        var request = new SkinPackageBuildRequest(
            manifest,
            defaults.Theme,
            assets);
        var written = new SkinPackageWriter().WriteFile(
            packagePath,
            request,
            overwrite: false,
            CancellationToken.None);
        Assert.True(written.IsValid,
            string.Join("; ", written.Errors.Select(error => error.Code)));
        return packagePath;
    }

    private static byte[] RemoveRefreshAnimationFields(
        byte[] document,
        bool draftDocument = true)
    {
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(document));
        var theme = draftDocument
            ? Assert.IsType<JsonObject>(root["theme"])
            : root;
        var animation = Assert.IsType<JsonObject>(theme["animation"]);

        Assert.True(animation.Remove("refreshSpeedMultiplier"));
        Assert.True(animation.Remove("refreshHoldSeconds"));

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static byte[] RemoveTextLayoutFields(
        byte[] document,
        bool draftDocument = true)
    {
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(document));
        var theme = draftDocument
            ? Assert.IsType<JsonObject>(root["theme"])
            : root;

        Assert.True(theme.Remove("textOffsetY"));
        Assert.True(theme.Remove("textLineGap"));

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static void AssertCanonicalRefreshAnimation(byte[] document)
    {
        using var parsed = JsonDocument.Parse(document);
        Assert.Equal(
            "1.3.0",
            parsed.RootElement.GetProperty("minimumHudVersion").GetString());
        var animation = parsed.RootElement
            .GetProperty("theme")
            .GetProperty("animation");

        Assert.Equal(2d, animation.GetProperty("refreshSpeedMultiplier").GetDouble());
        Assert.Equal(1.5d, animation.GetProperty("refreshHoldSeconds").GetDouble());
    }

    private static void AssertCanonicalTextLayout(byte[] document)
    {
        using var parsed = JsonDocument.Parse(document);
        var theme = parsed.RootElement.GetProperty("theme");

        Assert.Equal(0d, theme.GetProperty("textOffsetY").GetDouble());
        Assert.Equal(0d, theme.GetProperty("textLineGap").GetDouble());
    }

    private static void RewriteThemeInPackage(
        string packagePath,
        Func<byte[], byte[]> rewrite)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var entry = Assert.Single(
            archive.Entries,
            candidate => string.Equals(
                candidate.FullName,
                "theme.json",
                StringComparison.Ordinal));
        byte[] theme;
        using (var source = entry.Open())
        using (var buffer = new MemoryStream())
        {
            source.CopyTo(buffer);
            theme = buffer.ToArray();
        }

        entry.Delete();
        var replacement = archive.CreateEntry(
            "theme.json",
            CompressionLevel.NoCompression);
        using var destination = replacement.Open();
        destination.Write(rewrite(theme));
    }

    private static void ExtractInstalled(
        string packagePath,
        SkinStoragePaths paths,
        Guid skinId)
    {
        var destination = Path.Combine(
            paths.InstalledSkinsRoot,
            skinId.ToString("D"));
        Directory.CreateDirectory(destination);
        using var package = ZipFile.OpenRead(packagePath);
        foreach (var entry in package.Entries)
        {
            var target = Path.Combine(
                destination,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task14-documents-" + Guid.NewGuid().ToString("N"));
            SourceRoot = System.IO.Path.Combine(Path, "source");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Path { get; }

        public string SourceRoot { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    public enum PromotionWinnerKind
    {
        SameContent,
        MismatchedContent
    }

    private sealed class ImmutablePromotionRaceStorage(
        PromotionWinnerKind winnerKind) : IDesignerDraftStorageLeaseProvider
    {
        public int MoveCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        public IDesignerDraftProjectLease? OpenDesignerProject(
            string draftsRoot,
            Guid draftId,
            DesignerDraftProjectOpenMode mode)
        {
            var inner = PhysicalDraftFileOperations.Instance.OpenDesignerProject(
                draftsRoot,
                draftId,
                mode);
            return inner is null
                ? null
                : new Project(
                    inner,
                    new DraftProjectPaths(draftsRoot, draftId).AssetsRoot,
                    this,
                    winnerKind);
        }

        public IDesignerSourceFileLease OpenDesignerSource(string absolutePath) =>
            PhysicalDraftFileOperations.Instance.OpenDesignerSource(absolutePath);

        private sealed class Project(
            IDesignerDraftProjectLease inner,
            string assetsRoot,
            ImmutablePromotionRaceStorage owner,
            PromotionWinnerKind winnerKind) : IDesignerDraftProjectLease
        {
            public bool WasCreated => inner.WasCreated;

            public IDesignerDraftAssetsLease OpenAssets(bool create) =>
                new Assets(
                    inner.OpenAssets(create),
                    assetsRoot,
                    owner,
                    winnerKind);

            public void DeleteOwnedProjectIfEmpty() =>
                inner.DeleteOwnedProjectIfEmpty();

            public void Dispose() => inner.Dispose();
        }

        private sealed class Assets(
            IDesignerDraftAssetsLease inner,
            string assetsRoot,
            ImmutablePromotionRaceStorage owner,
            PromotionWinnerKind winnerKind) : IDesignerDraftAssetsLease
        {
            public bool FileExists(string leafName) => inner.FileExists(leafName);

            public byte[] ReadAllBytes(string leafName) =>
                inner.ReadAllBytes(leafName);

            public void WriteAndFlushNew(
                string operationLeafName,
                ReadOnlySpan<byte> bytes,
                CancellationToken cancellationToken) =>
                inner.WriteAndFlushNew(
                    operationLeafName,
                    bytes,
                    cancellationToken);

            public byte[] ReadOperationBytes(string operationLeafName) =>
                inner.ReadOperationBytes(operationLeafName);

            public bool MoveCanonicalToOperation(
                string canonicalLeafName,
                string operationLeafName) =>
                inner.MoveCanonicalToOperation(
                    canonicalLeafName,
                    operationLeafName);

            public void MoveOperationToCanonical(
                string operationLeafName,
                string canonicalLeafName) =>
                inner.MoveOperationToCanonical(
                    operationLeafName,
                    canonicalLeafName);

            public void MoveOperationToImmutable(
                string operationLeafName,
                string contentAddressedLeafName)
            {
                owner.MoveCalls++;
                var winner = winnerKind == PromotionWinnerKind.SameContent
                    ? inner.ReadOperationBytes(operationLeafName)
                    : OpaquePng;
                File.WriteAllBytes(
                    Path.Combine(assetsRoot, contentAddressedLeafName),
                    winner);
                inner.MoveOperationToImmutable(
                    operationLeafName,
                    contentAddressedLeafName);
            }

            public void DeleteCanonical(string canonicalLeafName) =>
                inner.DeleteCanonical(canonicalLeafName);

            public void DeleteOperation(string operationLeafName)
            {
                owner.DeleteCalls++;
                inner.DeleteOperation(operationLeafName);
            }

            public void ReleaseOperation(string operationLeafName)
            {
                owner.ReleaseCalls++;
                inner.ReleaseOperation(operationLeafName);
            }

            public void DeleteDirectoryIfEmpty() =>
                inner.DeleteDirectoryIfEmpty();

            public void Dispose() => inner.Dispose();
        }
    }

    private enum TransitionFault
    {
        ClaimCollision,
        SecondAssetWrite
    }

    private sealed class TransitionStorage(TransitionFault fault) :
        IDesignerDraftStorageLeaseProvider
    {
        private TransitionFault Fault { get; } = fault;

        public int ProjectOpenCount { get; private set; }

        public int AssetWriteCount { get; private set; }

        public IDesignerDraftProjectLease? OpenDesignerProject(
            string draftsRoot,
            Guid draftId,
            DesignerDraftProjectOpenMode mode)
        {
            ProjectOpenCount++;
            if (Fault == TransitionFault.ClaimCollision &&
                mode == DesignerDraftProjectOpenMode.CreateExclusive)
            {
                var project = new DraftProjectPaths(draftsRoot, draftId);
                Directory.CreateDirectory(project.ProjectRoot);
                File.WriteAllBytes(
                    Path.Combine(project.ProjectRoot, "foreign-evidence.bin"),
                    [7, 6, 5, 4]);
                return null;
            }

            var inner = PhysicalDraftFileOperations.Instance.OpenDesignerProject(
                draftsRoot,
                draftId,
                mode);
            return inner is null ? null : new Project(inner, this);
        }

        public IDesignerSourceFileLease OpenDesignerSource(string absolutePath) =>
            PhysicalDraftFileOperations.Instance.OpenDesignerSource(absolutePath);

        private sealed class Project(
            IDesignerDraftProjectLease inner,
            TransitionStorage owner) : IDesignerDraftProjectLease
        {
            public bool WasCreated => inner.WasCreated;

            public IDesignerDraftAssetsLease OpenAssets(bool create) =>
                new Assets(inner.OpenAssets(create), owner);

            public void DeleteOwnedProjectIfEmpty() => inner.DeleteOwnedProjectIfEmpty();

            public void Dispose() => inner.Dispose();
        }

        private sealed class Assets(
            IDesignerDraftAssetsLease inner,
            TransitionStorage owner) : IDesignerDraftAssetsLease
        {
            public bool FileExists(string canonicalLeafName) =>
                inner.FileExists(canonicalLeafName);

            public byte[] ReadAllBytes(string canonicalLeafName) =>
                inner.ReadAllBytes(canonicalLeafName);

            public void WriteAndFlushNew(
                string operationLeafName,
                ReadOnlySpan<byte> bytes,
                CancellationToken cancellationToken)
            {
                owner.AssetWriteCount++;
                if (owner.Fault == TransitionFault.SecondAssetWrite &&
                    owner.AssetWriteCount == 2)
                {
                    throw new IOException("Injected second asset copy failure.");
                }

                inner.WriteAndFlushNew(operationLeafName, bytes, cancellationToken);
            }

            public byte[] ReadOperationBytes(string operationLeafName) =>
                inner.ReadOperationBytes(operationLeafName);

            public bool MoveCanonicalToOperation(
                string canonicalLeafName,
                string operationLeafName) =>
                inner.MoveCanonicalToOperation(canonicalLeafName, operationLeafName);

            public void MoveOperationToCanonical(
                string operationLeafName,
                string canonicalLeafName) =>
                inner.MoveOperationToCanonical(operationLeafName, canonicalLeafName);

            public void MoveOperationToImmutable(
                string operationLeafName,
                string contentAddressedLeafName) =>
                inner.MoveOperationToImmutable(
                    operationLeafName,
                    contentAddressedLeafName);

            public void DeleteCanonical(string canonicalLeafName) =>
                inner.DeleteCanonical(canonicalLeafName);

            public void DeleteOperation(string operationLeafName) =>
                inner.DeleteOperation(operationLeafName);

            public void ReleaseOperation(string operationLeafName) =>
                inner.ReleaseOperation(operationLeafName);

            public void DeleteDirectoryIfEmpty() => inner.DeleteDirectoryIfEmpty();

            public void Dispose() => inner.Dispose();
        }
    }

    private static readonly byte[] AlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");

    private static readonly byte[] OpaquePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
