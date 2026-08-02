using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Serialization;
using CodexQuotaHud.Skins.Tests.Fixtures;

namespace CodexQuotaHud.Skins.Tests.Packaging;

public sealed class SkinPackageWriterTests
{
    private static readonly DateTimeOffset DosEpoch =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ProducesCanonicalDeterministicPackageWithoutMutatingInputs()
    {
        var background = SkinPackageFixture.OneByOnePng;
        var center = SkinPackageFixture.OneByOneJpeg;
        var decoration = SkinPackageFixture.OneByOnePng;
        var originalBackground = background.ToArray();
        var originalCenter = center.ToArray();
        var originalDecoration = decoration.ToArray();
        var request = CreateRequest(background, center, decoration);
        var writer = new SkinPackageWriter();
        using var first = new MemoryStream();
        using var second = new MemoryStream();

        var firstManifest = writer.Write(
            first,
            request,
            CancellationToken.None);
        var secondManifest = writer.Write(
            second,
            request,
            CancellationToken.None);

        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal(
            [
                SkinAssetSlot.Background,
                SkinAssetSlot.Center,
                SkinAssetSlot.Decoration
            ],
            firstManifest.Assets.Select(asset => asset.Slot).ToArray());
        Assert.Equal(
            firstManifest.Assets.Select(asset => asset.Path),
            secondManifest.Assets.Select(asset => asset.Path));

        first.Position = 0;
        using (var archive = new ZipArchive(
                   first,
                   ZipArchiveMode.Read,
                   leaveOpen: true))
        {
            Assert.Equal(
                [
                    "assets/background.png",
                    "assets/center.jpg",
                    "assets/decoration.png",
                    "manifest.json",
                    "theme.json"
                ],
                archive.Entries.Select(entry => entry.FullName).ToArray());
            Assert.All(
                archive.Entries,
                entry => Assert.Equal(
                    DosEpoch.DateTime,
                    entry.LastWriteTime.DateTime));

            var manifestBytes = ReadEntry(
                archive,
                SkinPackageLimits.ManifestFileName);
            var manifestResult = SkinJsonCodec.ParseManifest(manifestBytes);
            var parsedManifest = AssertValid(manifestResult);
            Assert.Equal(
                manifestBytes,
                SkinJsonCodec.WriteManifest(parsedManifest));
            Assert.Equal(
                ReadEntry(archive, SkinPackageLimits.ThemeFileName),
                SkinJsonCodec.WriteTheme(request.Theme));

            var expectedContent = new Dictionary<SkinAssetSlot, byte[]>
            {
                [SkinAssetSlot.Background] = originalBackground,
                [SkinAssetSlot.Center] = originalCenter,
                [SkinAssetSlot.Decoration] = originalDecoration
            };
            foreach (var assetReference in parsedManifest.Assets)
            {
                var expectedHash = Convert.ToHexString(
                        SHA256.HashData(expectedContent[assetReference.Slot]))
                    .ToLowerInvariant();
                Assert.Equal(expectedHash, assetReference.Sha256);
                Assert.Matches("^[0-9a-f]{64}$", assetReference.Sha256);
                Assert.Equal(
                    expectedContent[assetReference.Slot],
                    ReadEntry(archive, assetReference.Path));
            }
        }

        Assert.Empty(request.Manifest.Assets);
        Assert.Equal(originalBackground, background);
        Assert.Equal(originalCenter, center);
        Assert.Equal(originalDecoration, decoration);
        Assert.Equal(
            "sources/background.png",
            request.Assets[SkinAssetSlot.Background].RelativePath);
        Assert.Equal(
            "sources/center.jpg",
            request.Assets[SkinAssetSlot.Center].RelativePath);
        Assert.Equal(
            "sources/decoration.png",
            request.Assets[SkinAssetSlot.Decoration].RelativePath);

        first.Position = 0;
        var reopened = new SkinPackageReader().ValidateStream(
            first,
            first.Length,
            SemanticVersion.Parse("1.1.1"),
            CancellationToken.None);
        Assert.True(
            reopened.IsValid,
            string.Join(
                Environment.NewLine,
                reopened.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.NotNull(reopened.Value);
    }

    [Fact]
    public void WriteFile_WithoutOverwritePreservesExistingDestination()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "existing.cqskin");
        var original = "existing package"u8.ToArray();
        File.WriteAllBytes(destination, original);
        var request = CreateRequest(
            SkinPackageFixture.OneByOnePng,
            SkinPackageFixture.OneByOneJpeg,
            SkinPackageFixture.OneByOnePng);

        var result = new SkinPackageWriter().WriteFile(
            destination,
            request,
            overwrite: false,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == "export.destination-exists");
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(directory.EnumerateTemporaryFiles(destination));
    }

    [Fact]
    public void WriteFile_WhenFinalMoveFailsPreservesDestinationAndDeletesTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "existing.cqskin");
        var original = "previous package"u8.ToArray();
        File.WriteAllBytes(destination, original);
        string? observedTemporaryPath = null;
        var request = CreateRequest(
            SkinPackageFixture.OneByOnePng,
            SkinPackageFixture.OneByOneJpeg,
            SkinPackageFixture.OneByOnePng);
        var writer = new SkinPackageWriter((source, target, overwrite) =>
        {
            observedTemporaryPath = source;
            Assert.Equal(Path.GetFullPath(destination), target);
            Assert.True(overwrite);
            Assert.True(File.Exists(source));
            var validation = new SkinPackageReader().ValidateFile(
                source,
                request.Manifest.MinimumHudVersion,
                CancellationToken.None);
            Assert.True(
                validation.IsValid,
                string.Join(
                    Environment.NewLine,
                    validation.Errors.Select(error =>
                        $"{error.Code}: {error.Message}")));
            throw new IOException("Injected final-move failure.");
        });

        var exception = Assert.Throws<IOException>(() => writer.WriteFile(
            destination,
            request,
            overwrite: true,
            CancellationToken.None));

        Assert.Equal("Injected final-move failure.", exception.Message);
        Assert.NotNull(observedTemporaryPath);
        Assert.False(File.Exists(observedTemporaryPath));
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(directory.EnumerateTemporaryFiles(destination));
    }

    [Fact]
    public void WriteFile_WhenCancelledAtFinalMoveDeletesTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "cancelled.cqskin");
        using var cancellation = new CancellationTokenSource();
        string? observedTemporaryPath = null;
        var request = CreateRequest(
            SkinPackageFixture.OneByOnePng,
            SkinPackageFixture.OneByOneJpeg,
            SkinPackageFixture.OneByOnePng);
        var writer = new SkinPackageWriter((source, _, _) =>
        {
            observedTemporaryPath = source;
            Assert.True(File.Exists(source));
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });

        Assert.Throws<OperationCanceledException>(() => writer.WriteFile(
            destination,
            request,
            overwrite: false,
            cancellation.Token));

        Assert.NotNull(observedTemporaryPath);
        Assert.False(File.Exists(observedTemporaryPath));
        Assert.False(File.Exists(destination));
        Assert.Empty(directory.EnumerateTemporaryFiles(destination));
    }

    [Fact]
    public void Write_DoesNotSerializeAbsoluteSourceImagePaths()
    {
        using var directory = new TemporaryDirectory();
        var request = WithAbsoluteSourcePaths(
            CreateRequest(
                SkinPackageFixture.OneByOnePng,
                SkinPackageFixture.OneByOneJpeg,
                SkinPackageFixture.OneByOnePng),
            directory.Path);
        using var package = new MemoryStream();

        _ = new SkinPackageWriter().Write(
            package,
            request,
            CancellationToken.None);

        package.Position = 0;
        using var archive = new ZipArchive(
            package,
            ZipArchiveMode.Read,
            leaveOpen: true);
        var sourcePathBytes = request.Assets.Values
            .Select(asset => Encoding.UTF8.GetBytes(asset.RelativePath))
            .ToArray();
        foreach (var entry in archive.Entries)
        {
            var content = ReadEntry(archive, entry.FullName);
            Assert.All(
                sourcePathBytes,
                sourcePath => Assert.False(
                    ContainsSequence(content, sourcePath),
                    $"{entry.FullName} leaked an absolute source path."));
        }
    }

    [Fact]
    public void WriteFile_RejectsDestinationWithoutCqskinExtension()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "skin.zip");
        var request = CreateRequest(
            SkinPackageFixture.OneByOnePng,
            SkinPackageFixture.OneByOneJpeg,
            SkinPackageFixture.OneByOnePng);

        Assert.Throws<ArgumentException>(() => new SkinPackageWriter().WriteFile(
            destination,
            request,
            overwrite: false,
            CancellationToken.None));
        Assert.False(File.Exists(destination));
        Assert.Empty(directory.EnumerateTemporaryFiles(destination));
    }

    private static SkinPackageBuildRequest CreateRequest(
        byte[] background,
        byte[] center,
        byte[] decoration)
    {
        var manifest = new SkinManifest(
            SchemaVersion: 1,
            SkinId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DisplayName: "Deterministic skin",
            Author: "Alice",
            PackageVersion: SemanticVersion.Parse("1.2.3"),
            Description: "Writer fixture",
            TemplateId: SkinPackageLimits.FreeDecorationRingTemplateId,
            MinimumHudVersion: SemanticVersion.Parse("1.1.1"),
            OriginSkinId: null,
            Assets: []);
        var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
        var theme = new SkinTheme(
            SchemaVersion: 1,
            TemplateId: SkinPackageLimits.FreeDecorationRingTemplateId,
            Background: identity,
            Center: identity,
            Decoration: identity,
            PrimaryRingColor: "#FF53DCF8",
            SecondaryRingColor: "#FF9A68FF",
            BaseBackgroundColor: "#FF0A1622",
            BaseBackgroundOpacity: 0.9,
            RingDiameter: 96,
            RingThickness: 8,
            RingGap: 6,
            StartAngle: 270,
            GlowColor: "#FF24CFF2",
            GlowIntensity: 0.5,
            NumberTextSize: 28,
            LabelTextSize: 12,
            TextWeight: SkinTextWeight.SemiBold,
            TextPlacement: SkinTextPlacement.NumberAboveLabel,
            Animation: new SkinAnimationSettings(0.25, 0.5, 0.75, 1));
        var assets = new Dictionary<SkinAssetSlot, SkinAsset>
        {
            [SkinAssetSlot.Decoration] = new(
                SkinAssetSlot.Decoration,
                "sources/decoration.png",
                decoration,
                1,
                1,
                true),
            [SkinAssetSlot.Background] = new(
                SkinAssetSlot.Background,
                "sources/background.png",
                background,
                1,
                1,
                true),
            [SkinAssetSlot.Center] = new(
                SkinAssetSlot.Center,
                "sources/center.jpg",
                center,
                1,
                1,
                false)
        };

        return new SkinPackageBuildRequest(manifest, theme, assets);
    }

    private static SkinPackageBuildRequest WithAbsoluteSourcePaths(
        SkinPackageBuildRequest request,
        string sourceRoot)
    {
        var assets = request.Assets.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                RelativePath = Path.Combine(
                    sourceRoot,
                    "private",
                    Path.GetFileName(pair.Value.RelativePath))
            });
        return new SkinPackageBuildRequest(
            request.Manifest,
            request.Theme,
            assets);
    }

    private static byte[] ReadEntry(ZipArchive archive, string path)
    {
        var entry = Assert.Single(
            archive.Entries,
            candidate => candidate.FullName == path);
        using var source = entry.Open();
        using var destination = new MemoryStream();
        source.CopyTo(destination);
        return destination.ToArray();
    }

    private static T AssertValid<T>(SkinValidationResult<T> result)
    {
        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        return Assert.IsType<T>(result.Value);
    }

    private static bool ContainsSequence(byte[] content, byte[] sequence) =>
        content.AsSpan().IndexOf(sequence) >= 0;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud.SkinPackageWriterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string[] EnumerateTemporaryFiles(string destination) =>
            Directory.GetFiles(
                Path,
                $"{System.IO.Path.GetFileName(destination)}.*.tmp",
                SearchOption.TopDirectoryOnly);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
