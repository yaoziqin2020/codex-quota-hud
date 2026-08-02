using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Tests.Fixtures;
using System.IO.Compression;

namespace CodexQuotaHud.Skins.Tests.Packaging;

public sealed class SkinPackageReaderTests
{
    private static readonly SemanticVersion InstalledVersion =
        SemanticVersion.Parse("1.1.1");

    [Fact]
    public void ValidateFile_AcceptsPackageWithAllThreeRealImages()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage(
            SkinAssetSlot.Background,
            SkinAssetSlot.Center,
            SkinAssetSlot.Decoration);

        var result = new SkinPackageReader().ValidateFile(
            packagePath,
            InstalledVersion,
            CancellationToken.None);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            result.Value!.Manifest.SkinId);
        Assert.Equal(3, result.Value.Assets.Count);
        Assert.All(result.Value.Assets.Values, asset =>
        {
            Assert.Equal(1, asset.PixelWidth);
            Assert.Equal(1, asset.PixelHeight);
        });
        fixture.AssertNoEscape();

        File.Delete(packagePath);
        Assert.False(File.Exists(packagePath));
    }

    [Theory]
    [MemberData(nameof(OptionalAssetPackages))]
    public void ValidateFile_AcceptsZeroOneAndTwoIndependentlyOptionalSlots(
        SkinAssetSlot[] slots)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage(slots);

        var result = new SkinPackageReader().ValidateFile(
            packagePath,
            InstalledVersion,
            CancellationToken.None);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Equal(slots.Length, result.Value!.Assets.Count);
        Assert.Equal(
            slots.OrderBy(slot => slot),
            result.Value.Assets.Keys.OrderBy(slot => slot));
        fixture.AssertNoEscape();
    }

    [Fact]
    public void ValidateFile_RejectsDuplicateManifestSlotBeforeReturningDocument()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateDuplicateSlotPackage();

        var result = new SkinPackageReader().ValidateFile(
            packagePath,
            InstalledVersion,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Code == "asset.duplicate-slot");
        Assert.DoesNotContain(
            result.Errors,
            error => error.Message.Contains(fixture.RootDirectory, StringComparison.OrdinalIgnoreCase));
        fixture.AssertNoEscape();
    }

    [Fact]
    public void Decode_ReturnsFrozenOnLoadBitmapAndAlphaCapability()
    {
        var png = SkinImageDecoder.Decode(
            SkinAssetSlot.Background,
            "assets/background.png",
            SkinPackageFixture.OneByOnePng);
        var jpeg = SkinImageDecoder.Decode(
            SkinAssetSlot.Center,
            "assets/center.jpg",
            SkinPackageFixture.OneByOneJpeg);

        Assert.Equal(1, png.PixelWidth);
        Assert.Equal(1, png.PixelHeight);
        Assert.True(png.HasAlpha);
        Assert.True(png.Bitmap.IsFrozen);
        Assert.Equal(1, jpeg.PixelWidth);
        Assert.Equal(1, jpeg.PixelHeight);
        Assert.False(jpeg.HasAlpha);
        Assert.True(jpeg.Bitmap.IsFrozen);
    }

    [Fact]
    public void ValidateFile_AcceptsExactDimensionAndCombinedPixelLimit()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    SkinPackageFixture.MaximumPixelPng)
            ]);

        var result = new SkinPackageReader().ValidateFile(
            packagePath,
            InstalledVersion,
            CancellationToken.None);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        var asset = Assert.Single(result.Value!.Assets).Value;
        Assert.Equal(SkinPackageLimits.MaximumImageDimension, asset.PixelWidth);
        Assert.Equal(SkinPackageLimits.MaximumImageDimension, asset.PixelHeight);
        fixture.AssertNoEscape();
    }

    [Fact]
    public void ValidateStream_ObservesCancellationDuringBoundedEntryCopy()
    {
        using var fixture = new SkinPackageFixture();
        var paddedPng = new byte[512 * 1024];
        SkinPackageFixture.OneByOnePng.CopyTo(paddedPng, 0);
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    paddedPng,
                    CompressionLevel.NoCompression)
            ]);
        using var package = File.OpenRead(packagePath);
        using var cancellation = new CancellationTokenSource();
        using var cancelingStream = new CancelAfterReadStream(
            package,
            cancellation,
            cancelAfterBytes: 200 * 1024);

        Assert.Throws<OperationCanceledException>(() =>
            new SkinPackageReader().ValidateStream(
                cancelingStream,
                package.Length,
                InstalledVersion,
                cancellation.Token));
        fixture.AssertNoEscape();
    }

    public static IEnumerable<object[]> OptionalAssetPackages()
    {
        yield return [Array.Empty<SkinAssetSlot>()];
        yield return [new[] { SkinAssetSlot.Center }];
        yield return
        [
            new[]
            {
                SkinAssetSlot.Background,
                SkinAssetSlot.Decoration
            }
        ];
    }

    private sealed class CancelAfterReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationTokenSource _cancellation;
        private readonly long _cancelAfterBytes;
        private long _bytesRead;

        public CancelAfterReadStream(
            Stream inner,
            CancellationTokenSource cancellation,
            long cancelAfterBytes)
        {
            _inner = inner;
            _cancellation = cancellation;
            _cancelAfterBytes = cancelAfterBytes;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            Track(_inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) =>
            Track(_inner.Read(buffer));

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private int Track(int count)
        {
            _bytesRead += count;
            if (_bytesRead >= _cancelAfterBytes)
            {
                _cancellation.Cancel();
            }

            return count;
        }
    }
}
