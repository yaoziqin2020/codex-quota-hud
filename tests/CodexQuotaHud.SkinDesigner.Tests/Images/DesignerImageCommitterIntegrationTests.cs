using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Images;

public sealed class DesignerImageCommitterIntegrationTests
{
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
        var ownedPath = Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot,
            "background.png");
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

        await slot.RemoveCommand.ExecuteAsync();

        Assert.True(slot.LastMutation?.Succeeded);
        Assert.False(slot.HasAsset);
        Assert.False(designer.Assets.ContainsKey(SkinAssetSlot.Background));
        Assert.False(session.Current.Assets.ContainsKey(SkinAssetSlot.Background));
        Assert.Equal(2, session.Current.Revision);
        Assert.Equal(2, previewCount);
    }

    [Fact]
    public async Task CrossExtensionImport_WhenRealCommitterRejectsKeepsOldCanonicalAssetOnly()
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
        var assetsRoot = new DraftProjectPaths(
            paths.DraftsRoot,
            draftId).AssetsRoot;
        Assert.Equal(AlphaPng,
            await File.ReadAllBytesAsync(Path.Combine(assetsRoot, "background.png")));
        Assert.False(File.Exists(Path.Combine(assetsRoot, "background.jpg")));
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
        var owned = Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot,
            "background.png");
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
    public async Task Remove_WhenOwnedFileIsLockedDoesNotCommitOrLoseCurrentState()
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
        var owned = Path.Combine(
            new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot,
            "background.png");
        var revisionBefore = session.Current.Revision;
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var assetBefore = designer.Assets[SkinAssetSlot.Background];
        var previewBefore = Assert.Single(previews);
        var bytesBefore = await File.ReadAllBytesAsync(owned);

        await using var locked = new FileStream(
            owned,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var rejected = await service.RemoveAsync(
            draftId,
            SkinAssetSlot.Background);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors, error => error.Code == "image.prepare-failed");
        Assert.Equal(revisionBefore, session.Current.Revision);
        Assert.Same(referenceBefore,
            session.Current.Assets[SkinAssetSlot.Background]);
        Assert.Same(assetBefore, designer.Assets[SkinAssetSlot.Background]);
        Assert.Same(previewBefore, Assert.Single(previews));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(owned));
    }

    [Fact]
    public async Task CrossExtensionImport_WhenOldCanonicalFileIsLockedRollsBackBeforeCommit()
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
        var assetsRoot = new DraftProjectPaths(paths.DraftsRoot, draftId).AssetsRoot;
        var pngOwned = Path.Combine(assetsRoot, "background.png");
        var jpgOwned = Path.Combine(assetsRoot, "background.jpg");
        var revisionBefore = session.Current.Revision;
        var referenceBefore = session.Current.Assets[SkinAssetSlot.Background];
        var assetBefore = designer.Assets[SkinAssetSlot.Background];
        var previewBefore = Assert.Single(previews);
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
        var rejected = await service.ImportAsync(
            draftId,
            SkinAssetSlot.Background,
            jpegSource);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors, error => error.Code == "image.promote-failed");
        Assert.Equal(revisionBefore, session.Current.Revision);
        Assert.Same(referenceBefore,
            session.Current.Assets[SkinAssetSlot.Background]);
        Assert.Same(assetBefore, designer.Assets[SkinAssetSlot.Background]);
        Assert.Same(previewBefore, Assert.Single(previews));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(pngOwned));
        Assert.False(File.Exists(jpgOwned));
    }

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
