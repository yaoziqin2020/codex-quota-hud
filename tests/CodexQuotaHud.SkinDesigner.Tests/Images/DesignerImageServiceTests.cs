using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Images;

public sealed class DesignerImageServiceTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData(SkinAssetSlot.Background, "source.png", "assets/background.png")]
    [InlineData(SkinAssetSlot.Background, "source.jpeg", "assets/background.jpg")]
    [InlineData(SkinAssetSlot.Center, "source.jpg", "assets/center.jpg")]
    [InlineData(SkinAssetSlot.Center, "source.png", "assets/center.png")]
    [InlineData(SkinAssetSlot.Decoration, "source.png", "assets/decoration.png")]
    public async Task ImportAsync_DecodesAndOwnsCanonicalAssetIndependentOfSource(
        SkinAssetSlot slot,
        string sourceLeaf,
        string expectedRelativePath)
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var sourcePath = Path.Combine(temporary.SourceRoot, sourceLeaf);
        var bytes = sourceLeaf.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? AlphaPng
            : OneByOneJpeg;
        await File.WriteAllBytesAsync(sourcePath, bytes);
        var sut = new DesignerImageService(storage);

        var result = await sut.ImportAsync(DraftId, slot, sourcePath);

        Assert.True(result.Succeeded, Format(result.Errors));
        Assert.Equal(expectedRelativePath, result.Reference?.RelativePath);
        Assert.Equal(sourceLeaf, result.Reference?.OriginalFileName);
        var asset = Assert.IsType<SkinAsset>(result.Asset);
        Assert.Equal(slot, asset.Slot);
        Assert.Equal(expectedRelativePath, asset.RelativePath);
        Assert.Equal(bytes, asset.Content);
        Assert.Equal(1, asset.PixelWidth);
        Assert.Equal(1, asset.PixelHeight);
        if (slot == SkinAssetSlot.Decoration)
        {
            Assert.True(asset.HasAlpha);
        }

        File.Delete(sourcePath);
        var owned = OwnedPath(storage, DraftId, expectedRelativePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(owned));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(owned)!),
            path => Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_WhenAtomicReplaceFailsPreservesExactOldBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var sourcePath = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(sourcePath, AlphaPng);
        var sut = new DesignerImageService(storage);
        var first = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            sourcePath);
        Assert.True(first.Succeeded, Format(first.Errors));
        var ownedPath = OwnedPath(
            storage,
            DraftId,
            "assets/background.png");
        var oldBytes = await File.ReadAllBytesAsync(ownedPath);
        var replacement = CreateGrayscalePngForIntegration(1, 1);
        await File.WriteAllBytesAsync(sourcePath, replacement);

        await using var lockStream = new FileStream(
            ownedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var failed = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            sourcePath);

        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Errors, error => error.Code == "image.promote-failed");
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(ownedPath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(ownedPath)!),
            path => Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_WhenSessionCommitRejectsRollsBackBytesReferenceAndPreview()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var committer = new RecordingCommitter();
        var sut = new DesignerImageService(storage, committer);
        var sourcePath = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(sourcePath, AlphaPng);
        var first = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            sourcePath);
        Assert.True(first.Succeeded, Format(first.Errors));
        var ownedPath = OwnedPath(
            storage,
            DraftId,
            "assets/background.png");
        var oldBytes = await File.ReadAllBytesAsync(ownedPath);
        var oldReference = committer.Reference;
        var oldPreview = committer.PreviewAsset;
        committer.Accept = false;
        await File.WriteAllBytesAsync(
            sourcePath,
            CreateGrayscalePngForIntegration(1, 1));

        var rejected = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            sourcePath);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors,
            error => error.Code == "image.session-rejected");
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(ownedPath));
        Assert.Same(oldReference, committer.Reference);
        Assert.Same(oldPreview, committer.PreviewAsset);
        Assert.Equal(1, committer.AcceptedCount);
    }

    [Fact]
    public async Task ImportAsync_WhenCancellationRollbackFailsReturnsExplicitIntegrityError()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var sourcePath = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(sourcePath, AlphaPng);
        var first = await new DesignerImageService(storage).ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            sourcePath);
        Assert.True(first.Succeeded, Format(first.Errors));
        await File.WriteAllBytesAsync(
            sourcePath,
            CreateGrayscalePngForIntegration(1, 1));
        using var cancellation = new CancellationTokenSource();
        var faultingStorage = new RollbackFailingStorage(cancellation);
        var sut = new DesignerImageService(
            storage,
            new RecordingCommitter(),
            storage: faultingStorage);

        var failed = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            sourcePath,
            cancellation.Token);

        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Errors,
            error => error.Code == "image.rollback-failed");
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyTheSelectedCanonicalSlotFiles()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var sut = new DesignerImageService(storage);
        foreach (var (slot, leaf, bytes) in new[]
                 {
                     (SkinAssetSlot.Background, "background.png", AlphaPng),
                     (SkinAssetSlot.Center, "center.jpg", OneByOneJpeg),
                     (SkinAssetSlot.Decoration, "decoration.png", AlphaPng)
                 })
        {
            var source = Path.Combine(temporary.SourceRoot, leaf);
            await File.WriteAllBytesAsync(source, bytes);
            Assert.True((await sut.ImportAsync(DraftId, slot, source)).Succeeded);
        }

        var result = await sut.RemoveAsync(DraftId, SkinAssetSlot.Center);

        Assert.True(result.Succeeded, Format(result.Errors));
        Assert.Null(result.Asset);
        Assert.Null(result.Reference);
        Assert.True(File.Exists(OwnedPath(storage, DraftId, "assets/background.png")));
        Assert.False(File.Exists(OwnedPath(storage, DraftId, "assets/center.jpg")));
        Assert.True(File.Exists(OwnedPath(storage, DraftId, "assets/decoration.png")));
    }

    [Fact]
    public async Task RemoveAsync_WhenSessionRejectsPreservesSelectedOwnedBytes()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var committer = new RecordingCommitter();
        var sut = new DesignerImageService(storage, committer);
        var source = Path.Combine(temporary.SourceRoot, "background.png");
        await File.WriteAllBytesAsync(source, AlphaPng);
        Assert.True((await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            source)).Succeeded);
        var owned = OwnedPath(storage, DraftId, "assets/background.png");
        var bytesBefore = await File.ReadAllBytesAsync(owned);
        committer.Accept = false;

        var rejected = await sut.RemoveAsync(
            DraftId,
            SkinAssetSlot.Background);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors,
            error => error.Code == "image.session-rejected");
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(owned));
    }

    [Fact]
    public async Task ImportAsync_RejectsSpoofNonAlphaOversizeDimensionAndUnsafeSourcesWithoutMutation()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var sut = new DesignerImageService(storage);
        var cases = new List<(SkinAssetSlot Slot, string Leaf, byte[]? Bytes, string Code)>
        {
            (SkinAssetSlot.Background, "spoof.png", OneByOneJpeg, "image.signature"),
            (SkinAssetSlot.Decoration, "opaque.png", NonAlphaPng, "image.decoration-alpha"),
            (SkinAssetSlot.Background, "unsupported.gif", AlphaPng, "image.extension"),
            (SkinAssetSlot.Background, "too-large.png",
                new byte[SkinPackageLimits.MaximumImageBytes + 1], "image.too-large"),
            (SkinAssetSlot.Background, "too-wide.png",
                CreateGrayscalePngForIntegration(8193, 1), "image.dimension"),
            (SkinAssetSlot.Background, "missing.png", null, "image.source-unavailable")
        };

        foreach (var item in cases)
        {
            var source = Path.Combine(temporary.SourceRoot, item.Leaf);
            if (item.Bytes is not null)
            {
                await File.WriteAllBytesAsync(source, item.Bytes);
            }

            var result = await sut.ImportAsync(DraftId, item.Slot, source);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.Code == item.Code);
            Assert.False(Directory.Exists(new DraftProjectPaths(
                storage.DraftsRoot,
                DraftId).AssetsRoot));
        }

        var relative = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Background,
            "..\\escape.png");
        Assert.False(relative.Succeeded);
        Assert.Contains(relative.Errors, error => error.Code == "image.source-path");
    }

    [Fact]
    public async Task ImportAsync_RejectsCompleteThreeSlotDecodedPixelBudgetBeforeOwnedMutation()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var committer = new PixelBudgetCommitter(
            new SkinAsset(
                SkinAssetSlot.Background,
                "assets/background.png",
                AlphaPng,
                8192,
                4096,
                true),
            new SkinAsset(
                SkinAssetSlot.Center,
                "assets/center.jpg",
                OneByOneJpeg,
                8192,
                4096,
                false));
        var source = Path.Combine(temporary.SourceRoot, "decoration.png");
        await File.WriteAllBytesAsync(source, AlphaPng);
        var sut = new DesignerImageService(storage, committer);

        var rejected = await sut.ImportAsync(
            DraftId,
            SkinAssetSlot.Decoration,
            source);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors,
            error => error.Code == "image.total-pixels");
        Assert.Equal(0, committer.CommitCount);
        Assert.False(Directory.Exists(new DraftProjectPaths(
            storage.DraftsRoot,
            DraftId).ProjectRoot));
    }

    [Fact]
    public async Task SourceLease_PreventsReplacementUntilExactBytesAreRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var source = Path.Combine(temporary.SourceRoot, "selected.png");
        var moved = Path.Combine(temporary.SourceRoot, "moved.png");
        await File.WriteAllBytesAsync(source, AlphaPng);

        using (var lease = PhysicalDraftFileOperations.Instance.OpenDesignerSource(source))
        {
            Assert.ThrowsAny<IOException>(() => File.Move(source, moved));
            Assert.Equal(AlphaPng, lease.ReadAllBytes(CancellationToken.None));
        }

        File.Move(source, moved);
        Assert.Equal(AlphaPng, await File.ReadAllBytesAsync(moved));
    }

    [Theory]
    [MemberData(nameof(TransformBoundaries))]
    public void ImageSlotTransforms_UseSharedBoundsAndChangeOnlySelectedSlot(
        string field,
        double minimum,
        double maximum,
        double below,
        double above)
    {
        var now = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        var session = new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                DraftId,
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                now,
                SemanticVersion.Parse("1.1.1")),
            () => now = now.AddSeconds(1));
        var previewed = new List<SkinDraftDocument>();
        using var designer = new DesignerViewModel(session, previewed.Add);
        var background = designer.Images.Background;
        var centerBefore = designer.Images.Center.Transform;

        Assert.True(ApplyTransform(background, field, minimum).Succeeded);
        Assert.True(ApplyTransform(background, field, maximum).Succeeded);
        var revision = session.Current.Revision;
        var transform = background.Transform;

        foreach (var invalid in new[]
                 {
                     below,
                     above,
                     double.NaN,
                     double.PositiveInfinity
                 })
        {
            Assert.False(ApplyTransform(background, field, invalid).Succeeded);
            Assert.Equal(revision, session.Current.Revision);
            Assert.Equal(transform, background.Transform);
            Assert.Equal(centerBefore, designer.Images.Center.Transform);
            Assert.Equal(revision, previewed.Count);
        }
    }

    public static IEnumerable<object[]> TransformBoundaries()
    {
        yield return ["offsetX", -50d, 50d, -50.001d, 50.001d];
        yield return ["offsetY", -50d, 50d, -50.001d, 50.001d];
        yield return ["scale", 0.25d, 3d, 0.249d, 3.001d];
        yield return ["rotation", -180d, 180d, -180.001d, 180.001d];
        yield return ["opacity", 0d, 1d, -0.001d, 1.001d];
        yield return ["cropFocusX", 0d, 1d, -0.001d, 1.001d];
        yield return ["cropFocusY", 0d, 1d, -0.001d, 1.001d];
    }

    private static EditorMutationResult ApplyTransform(
        ImageSlotViewModel slot,
        string field,
        double value) => field switch
    {
        "offsetX" => slot.SetOffsetX(value),
        "offsetY" => slot.SetOffsetY(value),
        "scale" => slot.SetScale(value),
        "rotation" => slot.SetRotation(value),
        "opacity" => slot.SetOpacity(value),
        "cropFocusX" => slot.SetCropFocusX(value),
        "cropFocusY" => slot.SetCropFocusY(value),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static string OwnedPath(
        SkinStoragePaths paths,
        Guid draftId,
        string relativePath) => Path.Combine(
        new DraftProjectPaths(paths.DraftsRoot, draftId).ProjectRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}: {error.Message}"));

    internal static byte[] CreateGrayscalePngForIntegration(
        int width,
        int height)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 0;
        WritePngChunk(png, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(
                   compressed,
                   CompressionLevel.Fastest,
                   leaveOpen: true))
        {
            var scanline = new byte[checked(width + 1)];
            for (var row = 0; row < height; row++)
            {
                zlib.Write(scanline);
            }
        }

        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WritePngChunk(
        Stream destination,
        string chunkType,
        byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);
        var type = Encoding.ASCII.GetBytes(chunkType);
        destination.Write(type);
        destination.Write(data);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ComputePngCrc(type, data));
        destination.Write(checksum);
    }

    private static uint ComputePngCrc(byte[] type, byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? 0xEDB88320u ^ (crc >> 1)
                    : crc >> 1;
            }
        }

        return ~crc;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task14-images-" + Guid.NewGuid().ToString("N"));
            SourceRoot = System.IO.Path.Combine(Path, "source");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Path { get; }

        public string SourceRoot { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class RecordingCommitter : IDesignerImageMutationCommitter
    {
        public bool Accept { get; set; } = true;

        public int AcceptedCount { get; private set; }

        public DraftAssetReference? Reference { get; private set; }

        public SkinAsset? PreviewAsset { get; private set; }

        public IReadOnlyDictionary<SkinAssetSlot, SkinAsset> SnapshotAssets() =>
            PreviewAsset is null
                ? new Dictionary<SkinAssetSlot, SkinAsset>()
                : new Dictionary<SkinAssetSlot, SkinAsset>
                {
                    [PreviewAsset.Slot] = PreviewAsset
                };

        public bool TryCommit(
            SkinAsset asset,
            DraftAssetReference reference)
        {
            if (!Accept)
            {
                return false;
            }

            Reference = reference;
            PreviewAsset = asset;
            AcceptedCount++;
            return true;
        }

        public bool TryRemove(SkinAssetSlot slot) => Accept;
    }

    private sealed class RollbackFailingStorage(CancellationTokenSource cancellation) :
        IDesignerDraftStorageLeaseProvider
    {
        public IDesignerDraftProjectLease? OpenDesignerProject(
            string draftsRoot,
            Guid draftId,
            DesignerDraftProjectOpenMode mode)
        {
            var inner = PhysicalDraftFileOperations.Instance.OpenDesignerProject(
                draftsRoot,
                draftId,
                mode);
            return inner is null ? null : new Project(inner, cancellation);
        }

        public IDesignerSourceFileLease OpenDesignerSource(string absolutePath) =>
            PhysicalDraftFileOperations.Instance.OpenDesignerSource(absolutePath);

        private sealed class Project(
            IDesignerDraftProjectLease inner,
            CancellationTokenSource cancellation) : IDesignerDraftProjectLease
        {
            public bool WasCreated => inner.WasCreated;

            public IDesignerDraftAssetsLease OpenAssets(bool create) =>
                new Assets(inner.OpenAssets(create), cancellation);

            public void DeleteOwnedProjectIfEmpty() => inner.DeleteOwnedProjectIfEmpty();

            public void Dispose() => inner.Dispose();
        }

        private sealed class Assets(
            IDesignerDraftAssetsLease inner,
            CancellationTokenSource cancellation) : IDesignerDraftAssetsLease
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
                if (operationLeafName.Contains(".rollback-", StringComparison.Ordinal))
                {
                    throw new IOException("Injected rollback write failure.");
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
                string canonicalLeafName)
            {
                inner.MoveOperationToCanonical(operationLeafName, canonicalLeafName);
                cancellation.Cancel();
            }

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

    private sealed class PixelBudgetCommitter(params SkinAsset[] assets) :
        IDesignerImageMutationCommitter
    {
        private readonly IReadOnlyDictionary<SkinAssetSlot, SkinAsset> _assets =
            assets.ToDictionary(asset => asset.Slot, asset => asset);

        public int CommitCount { get; private set; }

        public IReadOnlyDictionary<SkinAssetSlot, SkinAsset> SnapshotAssets() =>
            _assets;

        public bool TryCommit(SkinAsset asset, DraftAssetReference reference)
        {
            CommitCount++;
            return true;
        }

        public bool TryRemove(SkinAssetSlot slot) => true;
    }

    private static readonly byte[] AlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");

    private static readonly byte[] NonAlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    internal static readonly byte[] OneByOneJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");
}
