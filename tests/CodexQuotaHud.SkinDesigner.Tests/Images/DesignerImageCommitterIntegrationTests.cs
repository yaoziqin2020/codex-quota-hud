using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Images;

public sealed class DesignerImageCommitterIntegrationTests
{
    [Fact]
    public async Task Import_AfterUndoStartsNewHistoryBoundaryAndRetainsCurrentAsset()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => { });
        var service = new DesignerImageService(
            new SkinStoragePaths(temporary.Path),
            designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);

        Assert.True(designer.Text.SetTextOffsetY(12).Succeeded);
        Assert.True(session.TryUndo());
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        var imported = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source);

        Assert.True(imported.Succeeded, Format(imported.Errors));
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal("assets/background.png",
            session.Current.Assets[SkinAssetSlot.Background].RelativePath);
        Assert.Equal(AlphaPng, designer.Assets[SkinAssetSlot.Background].Content);
    }

    [Fact]
    public async Task Import_WhenCommitDelegateThrowsRollsBackBytesAssetsAndHistory()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => { },
            _ => throw new InvalidOperationException("Rejected by test seam."));
        var paths = new SkinStoragePaths(temporary.Path);
        var service = new DesignerImageService(paths, designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);

        Assert.True(designer.Text.SetTextOffsetY(12).Succeeded);
        Assert.True(session.TryUndo());
        var canUndoBefore = session.CanUndo;
        var canRedoBefore = session.CanRedo;

        var rejected = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors,
            error => error.Code == "image.session-rejected");
        Assert.Equal(canUndoBefore, session.CanUndo);
        Assert.Equal(canRedoBefore, session.CanRedo);
        Assert.Empty(session.Current.Assets);
        Assert.Empty(designer.Assets);
        var unreferenced = DraftAssetStorage.CreateContentRelativePath(
            "assets/background.png",
            AlphaPng);
        Assert.True(File.Exists(OwnedPath(paths, draftId, unreferenced)));
    }

    [Fact]
    public async Task Import_WhenCancelledBeforeCommitRetainsHistory()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => { });
        var service = new DesignerImageService(
            new SkinStoragePaths(temporary.Path),
            designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);

        Assert.True(designer.Text.SetTextOffsetY(12).Succeeded);
        Assert.True(session.TryUndo());
        var canUndoBefore = session.CanUndo;
        var canRedoBefore = session.CanRedo;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ImportAsync(
                draftId,
                SkinAssetSlot.Background,
                source,
                new CancellationToken(canceled: true)));

        Assert.Equal(canUndoBefore, session.CanUndo);
        Assert.Equal(canRedoBefore, session.CanRedo);
        Assert.Empty(session.Current.Assets);
        Assert.Empty(designer.Assets);
    }

    [Fact]
    public async Task Import_ThroughDesignerCommitterUpdatesReferenceAssetAndPreviewOnce()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var previews = new List<PreviewSnapshot>();
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (draft, assets) => previews.Add(new PreviewSnapshot(
                draft,
                assets.ToDictionary(pair => pair.Key, pair => pair.Value))));
        var service = new DesignerImageService(
            new SkinStoragePaths(temporary.Path),
            designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);

        var imported = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source);

        Assert.True(imported.Succeeded, Format(imported.Errors));
        Assert.Equal(1, session.Current.Revision);
        var reference = session.Current.Assets[SkinAssetSlot.Background];
        Assert.Equal("assets/background.png", reference.RelativePath);
        Assert.Equal(
            DraftAssetStorage.CreateContentRelativePath(
                "assets/background.png",
                AlphaPng),
            reference.StorageRelativePath);
        var asset = designer.Assets[SkinAssetSlot.Background];
        Assert.Equal(AlphaPng, asset.Content);
        var preview = Assert.Single(previews);
        Assert.Equal(reference, preview.Draft.Assets[SkinAssetSlot.Background]);
        Assert.Equal(asset.Content,
            preview.Assets[SkinAssetSlot.Background].Content);
    }

    [Fact]
    public async Task Import_WhenRealCommitterSessionRejectsRestoresAssetSnapshotAndOwnedBytes()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var accept = true;
        var previewCount = 0;
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => previewCount++,
            edit => accept && session.ApplyMeaningful(edit));
        var paths = new SkinStoragePaths(temporary.Path);
        var service = new DesignerImageService(paths, designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);
        var first = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source);
        Assert.True(first.Succeeded, Format(first.Errors));
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var assetBefore = designer.Assets[SkinAssetSlot.Background];
        var oldStoragePath = Assert.IsType<string>(
            referenceBefore.StorageRelativePath);
        var ownedPath = OwnedPath(paths, draftId, oldStoragePath);
        var bytesBefore = await File.ReadAllBytesAsync(ownedPath);
        var revisionBefore = session.Current.Revision;
        accept = false;
        await File.WriteAllBytesAsync(
            source,
            DesignerImageServiceTests.CreateGrayscalePngForIntegration(1, 1));

        var rejected = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors,
            error => error.Code == "image.session-rejected");
        Assert.Equal(revisionBefore, session.Current.Revision);
        Assert.Same(referenceBefore,
            session.Current.Assets[SkinAssetSlot.Background]);
        Assert.Same(assetBefore,
            designer.Assets[SkinAssetSlot.Background]);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(ownedPath));
        var rejectedStoragePath = DraftAssetStorage.CreateContentRelativePath(
            "assets/background.png",
            DesignerImageServiceTests.CreateGrayscalePngForIntegration(1, 1));
        Assert.True(File.Exists(OwnedPath(paths, draftId, rejectedStoragePath)));
        Assert.Equal(1, previewCount);
    }

    [Fact]
    public async Task SlotCommands_PickReplaceAndRemoveThroughConfiguredWorkflow()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var previewCount = 0;
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => previewCount++);
        var source = Path.Combine(temporary.SourceRoot, "picked.png");
        await File.WriteAllBytesAsync(source, AlphaPng);
        var picker = new RecordingPicker(source);
        var service = new DesignerImageService(
            new SkinStoragePaths(temporary.Path),
            designer);
        designer.ConfigureImageWorkflow(picker, service);
        var slot = designer.Images.Background;

        Assert.True(slot.ReplaceCommand.CanExecute(null));
        Assert.False(slot.RemoveCommand.CanExecute(null));
        await slot.ReplaceCommand.ExecuteAsync();

        Assert.Equal([SkinAssetSlot.Background], picker.Slots);
        Assert.True(slot.LastMutation?.Succeeded);
        Assert.True(slot.HasAsset);
        Assert.True(slot.RemoveCommand.CanExecute(null));
        Assert.Equal(1, session.Current.Revision);
        var storagePath = Assert.IsType<string>(
            slot.LastMutation?.Reference?.StorageRelativePath);

        await slot.RemoveCommand.ExecuteAsync();

        Assert.True(slot.LastMutation?.Succeeded);
        Assert.False(slot.HasAsset);
        Assert.False(designer.Assets.ContainsKey(SkinAssetSlot.Background));
        Assert.False(session.Current.Assets.ContainsKey(SkinAssetSlot.Background));
        Assert.Equal(2, session.Current.Revision);
        Assert.Equal(2, previewCount);
        Assert.True(File.Exists(OwnedPath(
            new SkinStoragePaths(temporary.Path),
            draftId,
            storagePath)));
    }

    [Fact]
    public async Task CrossExtensionImport_WhenRealCommitterRejectsKeepsOldReferenceAndBothBlobs()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var accept = true;
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => { },
            edit => accept && session.ApplyMeaningful(edit));
        var paths = new SkinStoragePaths(temporary.Path);
        var service = new DesignerImageService(paths, designer);
        var pngSource = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(pngSource, AlphaPng);
        Assert.True((await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            pngSource)).Succeeded);
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var assetBefore = designer.Assets[SkinAssetSlot.Background];
        accept = false;
        var jpegSource = Path.Combine(temporary.SourceRoot, "background.jpg");
        await File.WriteAllBytesAsync(
            jpegSource,
            DesignerImageServiceTests.OneByOneJpeg);

        var rejected = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            jpegSource);

        Assert.False(rejected.Succeeded);
        Assert.Same(referenceBefore,
            session.Current.Assets[SkinAssetSlot.Background]);
        Assert.Same(assetBefore,
            designer.Assets[SkinAssetSlot.Background]);
        var oldStoragePath = Assert.IsType<string>(
            referenceBefore.StorageRelativePath);
        Assert.Equal(AlphaPng,
            await File.ReadAllBytesAsync(OwnedPath(paths, draftId, oldStoragePath)));
        var rejectedStoragePath = DraftAssetStorage.CreateContentRelativePath(
            "assets/background.jpg",
            DesignerImageServiceTests.OneByOneJpeg);
        Assert.Equal(
            DesignerImageServiceTests.OneByOneJpeg,
            await File.ReadAllBytesAsync(OwnedPath(
                paths,
                draftId,
                rejectedStoragePath)));
    }

    [Fact]
    public async Task Remove_WhenRealCommitterRejectsKeepsReferenceAssetPreviewAndBytes()
    {
        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var accept = true;
        var previewCount = 0;
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (_, _) => previewCount++,
            edit => accept && session.ApplyMeaningful(edit));
        var paths = new SkinStoragePaths(temporary.Path);
        var service = new DesignerImageService(paths, designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);
        Assert.True((await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source)).Succeeded);
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var assetBefore = designer.Assets[SkinAssetSlot.Background];
        var revisionBefore = session.Current.Revision;
        var storagePath = Assert.IsType<string>(
            referenceBefore.StorageRelativePath);
        var owned = OwnedPath(paths, draftId, storagePath);
        accept = false;

        var rejected = await service.RemoveAsync(
            draftId,
            SkinAssetSlot.Background);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors,
            error => error.Code == "image.session-rejected");
        Assert.Equal(revisionBefore, session.Current.Revision);
        Assert.Same(referenceBefore,
            session.Current.Assets[SkinAssetSlot.Background]);
        Assert.Same(assetBefore,
            designer.Assets[SkinAssetSlot.Background]);
        Assert.Equal(AlphaPng, await File.ReadAllBytesAsync(owned));
        Assert.Equal(1, previewCount);
    }

    [Fact]
    public async Task Remove_AfterUndoClearsBothHistoryDirectionsAndRetainsLockedBlob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var previews = new List<PreviewSnapshot>();
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (draft, assets) => previews.Add(new PreviewSnapshot(
                draft,
                assets.ToDictionary(pair => pair.Key, pair => pair.Value))));
        var paths = new SkinStoragePaths(temporary.Path);
        var service = new DesignerImageService(paths, designer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);
        Assert.True((await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            source)).Succeeded);
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var storagePath = Assert.IsType<string>(
            referenceBefore.StorageRelativePath);
        var owned = OwnedPath(paths, draftId, storagePath);
        var bytesBefore = await File.ReadAllBytesAsync(owned);
        Assert.True(designer.Text.SetTextOffsetY(12).Succeeded);
        Assert.True(session.TryUndo());
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        await using var locked = new FileStream(
            owned,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var removed = await service.RemoveAsync(
            draftId,
            SkinAssetSlot.Background);

        Assert.True(removed.Succeeded, Format(removed.Errors));
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.False(session.Current.Assets.ContainsKey(SkinAssetSlot.Background));
        Assert.False(designer.Assets.ContainsKey(SkinAssetSlot.Background));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(owned));
    }

    [Fact]
    public async Task CrossExtensionImport_WhenOldBlobIsLockedAppendsNewBlobAndCommits()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var draftId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var session = CreateSession(draftId);
        var previews = new List<PreviewSnapshot>();
        using var designer = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            (draft, assets) => previews.Add(new PreviewSnapshot(
                draft,
                assets.ToDictionary(pair => pair.Key, pair => pair.Value))));
        var paths = new SkinStoragePaths(temporary.Path);
        var service = new DesignerImageService(paths, designer);
        var pngSource = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(pngSource, AlphaPng);
        Assert.True((await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            pngSource)).Succeeded);
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var oldStoragePath = Assert.IsType<string>(
            referenceBefore.StorageRelativePath);
        var pngOwned = OwnedPath(paths, draftId, oldStoragePath);
        var bytesBefore = await File.ReadAllBytesAsync(pngOwned);
        var jpegSource = Path.Combine(temporary.SourceRoot, "background.jpg");
        await File.WriteAllBytesAsync(
            jpegSource,
            DesignerImageServiceTests.OneByOneJpeg);

        await using var locked = new FileStream(
            pngOwned,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var imported = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            jpegSource);

        Assert.True(imported.Succeeded, Format(imported.Errors));
        var current = session.Current.Assets[SkinAssetSlot.Background];
        Assert.Equal("assets/background.jpg", current.RelativePath);
        var jpgStoragePath = Assert.IsType<string>(current.StorageRelativePath);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(pngOwned));
        Assert.Equal(
            DesignerImageServiceTests.OneByOneJpeg,
            await File.ReadAllBytesAsync(OwnedPath(paths, draftId, jpgStoragePath)));
    }

    private static string OwnedPath(
        SkinStoragePaths paths,
        Guid draftId,
        string relativePath) => Path.Combine(
        new DraftProjectPaths(paths.DraftsRoot, draftId).ProjectRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static SkinDraftSession CreateSession(Guid draftId)
    {
        var now = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        return new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                draftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                now,
                SemanticVersion.Parse("1.1.1")),
            () => now = now.AddSeconds(1));
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}: {error.Message}"));

    private sealed record PreviewSnapshot(
        SkinDraftDocument Draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets);

    private sealed class RecordingPicker(string? selectedPath) : IImagePicker
    {
        public List<SkinAssetSlot> Slots { get; } = [];

        public string? ChooseImage(SkinAssetSlot slot)
        {
            Slots.Add(slot);
            return selectedPath;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task14-image-committer-" +
                Guid.NewGuid().ToString("N"));
            SourceRoot = System.IO.Path.Combine(Path, "source");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Path { get; }

        public string SourceRoot { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static readonly byte[] AlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");
}
