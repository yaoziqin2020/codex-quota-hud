using System.Security.Cryptography;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.SkinDesigner.Tests.Images;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Documents;

public sealed class DraftAssetPersistenceIntegrationTests
{
    [Fact]
    public async Task NamedReplacementThenDiscardReopensExactNamedBytes()
    {
        using var harness = await Harness.CreateAsync();
        var namedHash = Hash(AlphaPng);

        await harness.ReplaceAsync(ReplacementPng);
        await harness.Store.SaveRecoveryAsync(harness.Session.Current);
        Assert.True(await harness.Store.DiscardWorkingCopyAsync(
            harness.DraftId,
            harness.Session.Current.Revision));

        harness.AssertOpened(AlphaPng, namedHash);
    }

    [Fact]
    public async Task NamedRemovalThenDiscardReopensExactNamedBytes()
    {
        using var harness = await Harness.CreateAsync();
        var namedHash = Hash(AlphaPng);

        var removed = await harness.Images.RemoveAsync(
            harness.DraftId,
            SkinAssetSlot.Background);
        Assert.True(removed.Succeeded, Format(removed.Errors));
        await harness.Store.SaveRecoveryAsync(harness.Session.Current);
        Assert.True(await harness.Store.DiscardWorkingCopyAsync(
            harness.DraftId,
            harness.Session.Current.Revision));

        harness.AssertOpened(AlphaPng, namedHash);
    }

    [Fact]
    public async Task ReplacementThenNamedSaveReopensExactReplacementBytes()
    {
        using var harness = await Harness.CreateAsync();

        await harness.ReplaceAsync(ReplacementPng);
        await harness.Store.SaveNamedAsync(harness.Session.Current);
        harness.Session.MarkNamedSaved();

        harness.AssertOpened(ReplacementPng, Hash(ReplacementPng));
    }

    [Fact]
    public async Task ReplacementRecoveryReopensItsOwnExactImmutableBytes()
    {
        using var harness = await Harness.CreateAsync();

        await harness.ReplaceAsync(ReplacementPng);
        await harness.Store.SaveRecoveryAsync(harness.Session.Current);

        harness.AssertOpened(ReplacementPng, Hash(ReplacementPng));
    }

    [Fact]
    public async Task FailedNamedSaveReopensExactPriorNamedBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = await Harness.CreateAsync();
        await harness.ReplaceAsync(ReplacementPng);
        var namedPath = new DraftProjectPaths(
            harness.Paths.DraftsRoot,
            harness.DraftId).NamedDraftPath;

        await using (var locked = new FileStream(
                         namedPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() =>
                harness.Store.SaveNamedAsync(harness.Session.Current));
        }

        harness.AssertOpened(AlphaPng, Hash(AlphaPng));
        Assert.True(File.Exists(harness.OwnedPath(
            harness.Session.Current.Assets[SkinAssetSlot.Background])));
    }

    [Fact]
    public async Task CrashBeforeRecoveryFlushReopensExactPriorNamedBytes()
    {
        using var harness = await Harness.CreateAsync();
        await harness.ReplaceAsync(ReplacementPng);

        harness.AssertOpened(AlphaPng, Hash(AlphaPng));
        Assert.True(File.Exists(harness.OwnedPath(
            harness.Session.Current.Assets[SkinAssetSlot.Background])));
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}: {error.Message}"));

    private sealed class Harness : IDisposable
    {
        private readonly TemporaryDirectory _temporary;
        private readonly DesignerViewModel _designer;
        private DateTimeOffset _now =
            DateTimeOffset.Parse("2026-08-08T00:00:00Z");

        private Harness(TemporaryDirectory temporary)
        {
            _temporary = temporary;
            Paths = new SkinStoragePaths(temporary.Path);
            Store = new DraftStore(Paths);
            var draft = SkinDraftFactory.CreateNew(
                DraftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                _now,
                SemanticVersion.Parse("1.3.0"));
            Session = new SkinDraftSession(
                draft,
                () => _now = _now.AddSeconds(1));
            _designer = new DesignerViewModel(
                Session,
                new Dictionary<SkinAssetSlot, SkinAsset>(),
                (_, _) => { });
            Images = new DesignerImageService(Paths, _designer);
            Documents = new DesignerDocumentService(
                Paths,
                Store,
                new InstalledSkinCatalog(
                    Paths,
                    SemanticVersion.Parse("1.3.0")),
                new SkinPackageReader(),
                Guid.NewGuid,
                () => _now);
        }

        public Guid DraftId { get; } =
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public SkinStoragePaths Paths { get; }

        public DraftStore Store { get; }

        public SkinDraftSession Session { get; }

        public DesignerImageService Images { get; }

        public DesignerDocumentService Documents { get; }

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness(new TemporaryDirectory());
            var source = Path.Combine(harness._temporary.SourceRoot, "named.png");
            await File.WriteAllBytesAsync(source, AlphaPng);
            var imported = await harness.Images.ImportAsync(
                harness.DraftId,
                SkinAssetSlot.Background,
                source);
            Assert.True(imported.Succeeded, Format(imported.Errors));
            await harness.Store.SaveNamedAsync(harness.Session.Current);
            harness.Session.MarkNamedSaved();
            return harness;
        }

        public async Task ReplaceAsync(byte[] content)
        {
            var source = Path.Combine(_temporary.SourceRoot, "replacement.png");
            await File.WriteAllBytesAsync(source, content);
            var replaced = await Images.ImportAsync(
                DraftId,
                SkinAssetSlot.Background,
                source);
            Assert.True(replaced.Succeeded, Format(replaced.Errors));
            Assert.True(Session.HasUnsavedChanges);
        }

        public void AssertOpened(byte[] expectedContent, string expectedHash)
        {
            var opened = Documents.OpenDraft(DraftId);
            Assert.Empty(opened.Errors);
            var draft = Assert.IsType<SkinDraftDocument>(opened.Draft);
            var reference = draft.Assets[SkinAssetSlot.Background];
            Assert.Equal("assets/background.png", reference.RelativePath);
            Assert.EndsWith(
                $"sha256-{expectedHash}.png",
                reference.StorageRelativePath,
                StringComparison.Ordinal);
            var asset = Assert.Single(opened.Assets).Value;
            Assert.Equal("assets/background.png", asset.RelativePath);
            Assert.Equal(expectedContent, asset.Content);
            Assert.Equal(expectedHash, Hash(asset.Content));
        }

        public string OwnedPath(DraftAssetReference reference) => Path.Combine(
            new DraftProjectPaths(Paths.DraftsRoot, DraftId).AssetsRoot,
            DraftAssetStorage.ResolveOwnedLeaf(reference));

        public void Dispose()
        {
            _designer.Dispose();
            _temporary.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task7-persistence-" + Guid.NewGuid().ToString("N"));
            SourceRoot = System.IO.Path.Combine(Path, "source");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Path { get; }

        public string SourceRoot { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static readonly byte[] AlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");

    private static readonly byte[] ReplacementPng =
        DesignerImageServiceTests.CreateGrayscalePngForIntegration(1, 1);
}
