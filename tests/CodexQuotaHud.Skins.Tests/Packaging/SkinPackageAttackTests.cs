using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Tests.Fixtures;

namespace CodexQuotaHud.Skins.Tests.Packaging;

public sealed class SkinPackageAttackTests
{
    private static readonly SemanticVersion InstalledVersion =
        SemanticVersion.Parse("1.1.1");

    [Fact]
    public void ValidateFile_RejectsCompressedPackageOverFiftyMebibytesBeforeZipParsing()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateOversizedPackageFile();

        AssertRejected(fixture, packagePath, "package.too-large");
    }

    [Fact]
    public void ValidateFile_RejectsSixtyFifthEntry()
    {
        using var fixture = new SkinPackageFixture();
        var additionalEntries = Enumerable.Range(0, 63)
            .Select(index => new SkinPackageFixture.FixtureEntry(
                $"padding/{index:D2}.txt",
                [0x01]))
            .ToArray();
        var packagePath = fixture.CreatePackage(
            additionalEntries: additionalEntries);

        AssertRejected(fixture, packagePath, "archive.entry-count");
    }

    [Fact]
    public void ValidateFile_RejectsEocdCountBeforeCentralDirectoryMaterialization()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.SetEndOfCentralDirectoryEntryCount(packagePath, 65);

        AssertRejected(fixture, packagePath, "archive.entry-count");
    }

    [Fact]
    public void ValidateFile_RejectsAggregateUncompressedBytesOverSixtyFourMebibytes()
    {
        using var fixture = new SkinPackageFixture();
        var additionalEntries = Enumerable.Range(0, 5)
            .Select(index => new SkinPackageFixture.FixtureEntry(
                $"padding/{index}.txt",
                Content: [],
                RepeatedByteCount: 14L * 1024 * 1024))
            .ToArray();
        var packagePath = fixture.CreatePackage(
            additionalEntries: additionalEntries);

        AssertRejected(fixture, packagePath, "archive.extracted-size");
    }

    [Fact]
    public void ValidateFile_RejectsImageOverSixteenMebibytes()
    {
        using var fixture = new SkinPackageFixture();
        var oversizedImage = new byte[SkinPackageLimits.MaximumImageBytes + 1];
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    oversizedImage,
                    System.IO.Compression.CompressionLevel.NoCompression)
            ]);

        AssertRejected(fixture, packagePath, "archive.entry-size");
    }

    [Fact]
    public void ValidateFile_CountsActualStoredImageBytesWhenDeclaredSizeIsForgedSmall()
    {
        using var fixture = new SkinPackageFixture();
        var oversizedImage = new byte[SkinPackageLimits.MaximumImageBytes + 1];
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    oversizedImage,
                    System.IO.Compression.CompressionLevel.NoCompression)
            ]);
        fixture.SetEntryDeclaredUncompressedSize(
            packagePath,
            "assets/background.png",
            declaredSize: 1);

        AssertRejected(fixture, packagePath, "archive.entry-size");
    }

    [Fact]
    public void ValidateFile_CountsActualDeflatedAggregateWhenDeclaredSizeIsForgedSmall()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            manifestPaddingBytes:
                SkinPackageLimits.MaximumExtractedBytes + 1);
        fixture.SetEntryDeclaredUncompressedSize(
            packagePath,
            SkinPackageLimits.ManifestFileName,
            declaredSize: 1);

        AssertRejected(fixture, packagePath, "archive.extracted-size");
    }

    [Theory]
    [InlineData(SkinPackageFixture.ZipHeaderTarget.Central)]
    [InlineData(SkinPackageFixture.ZipHeaderTarget.Local)]
    public void ValidateFile_RejectsEncryptionFlagInEitherHeader(
        SkinPackageFixture.ZipHeaderTarget target)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.MarkEntryEncrypted(
            packagePath,
            SkinPackageLimits.ThemeFileName,
            target);

        AssertRejected(fixture, packagePath, "archive.entry.encrypted");
    }

    [Theory]
    [InlineData(SkinPackageFixture.ZipHeaderTarget.Central)]
    [InlineData(SkinPackageFixture.ZipHeaderTarget.Local)]
    public void ValidateFile_RejectsUnsupportedCompressionInEitherHeader(
        SkinPackageFixture.ZipHeaderTarget target)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.SetEntryCompressionMethod(
            packagePath,
            SkinPackageLimits.ThemeFileName,
            compressionMethod: 12,
            target);

        AssertRejected(
            fixture,
            packagePath,
            "archive.compression.unsupported");
    }

    [Fact]
    public void ValidateFile_RejectsLocalTraversalEvenWhenCentralNameIsSafe()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.SetLocalEntryName(
            packagePath,
            SkinPackageLimits.ManifestFileName,
            "../escape.png");

        AssertRejected(fixture, packagePath, "archive.path.traversal");
    }

    [Fact]
    public void ValidateFile_RejectsDifferentSafeCentralAndLocalNames()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.SetLocalEntryName(
            packagePath,
            SkinPackageLimits.ThemeFileName,
            "other.json");

        AssertRejected(fixture, packagePath, "archive.name.mismatch");
    }

    [Fact]
    public void ValidateFile_RejectsCentralLocalEncodingFlagMismatch()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.ToggleLocalUtf8NameFlag(
            packagePath,
            SkinPackageLimits.ThemeFileName);

        AssertRejected(fixture, packagePath, "archive.name.mismatch");
    }

    [Fact]
    public void ValidateFile_RejectsLocalVariableFieldsOutsideDataBoundary()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.SetLocalEntryNameLength(
            packagePath,
            SkinPackageLimits.ThemeFileName,
            ushort.MaxValue);

        AssertRejected(fixture, packagePath, "archive.invalid");
    }

    [Fact]
    public void ValidateFile_RejectsDataDescriptorEntriesExplicitly()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.MarkEntryDataDescriptor(
            packagePath,
            SkinPackageLimits.ThemeFileName);

        AssertRejected(
            fixture,
            packagePath,
            "archive.data-descriptor.unsupported");
    }

    [Fact]
    public void ValidateFile_RejectsEntryLevelZip64Explicitly()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.MarkEntryZip64(
            packagePath,
            SkinPackageLimits.ThemeFileName);

        AssertRejected(fixture, packagePath, "archive.zip64.unsupported");
    }

    [Fact]
    public void ValidateFile_RejectsZip64EocdSentinelBeforeMaterialization()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreateValidPackage();
        fixture.SetEndOfCentralDirectoryEntryCount(
            packagePath,
            ushort.MaxValue);

        AssertRejected(fixture, packagePath, "archive.zip64.unsupported");
    }

    [Theory]
    [MemberData(nameof(NonRegularEntries))]
    public void ValidateFile_RejectsDirectorySymlinkReparseAndDeviceEntries(
        string entryName,
        int externalAttributes)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            additionalEntries:
            [
                new SkinPackageFixture.FixtureEntry(
                    entryName,
                    Content: [],
                    ExternalAttributes: externalAttributes)
            ]);

        AssertRejected(fixture, packagePath, "archive.entry.not-regular");
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("payload.dll")]
    [InlineData("payload.xaml")]
    [InlineData("payload.js")]
    [InlineData("payload.ps1")]
    public void ValidateFile_RejectsForbiddenContentPaths(string entryName)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            additionalEntries:
            [
                new SkinPackageFixture.FixtureEntry(entryName, [0x01])
            ]);

        AssertRejected(fixture, packagePath, "archive.file.forbidden");
    }

    [Fact]
    public void ValidateFile_RejectsAssetWhoseSha256DoesNotMatchManifest()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    SkinPackageFixture.OneByOnePng)
            ],
            transformManifest: manifest => manifest with
            {
                Assets =
                [
                    manifest.Assets[0] with
                    {
                        Sha256 = new string('0', 64)
                    }
                ]
            });

        AssertRejected(fixture, packagePath, "asset.hash.mismatch");
    }

    [Theory]
    [MemberData(nameof(SignatureSpoofs))]
    public void ValidateFile_RejectsExtensionAndContentSignatureSpoof(
        SkinAssetSlot slot,
        string relativePath,
        byte[] content)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(slot, relativePath, content)
            ]);

        AssertRejected(fixture, packagePath, "image.signature");
    }

    [Theory]
    [MemberData(nameof(CorruptImages))]
    public void ValidateFile_RejectsTruncatedOrCorruptPngAndJpeg(
        SkinAssetSlot slot,
        string relativePath,
        byte[] content)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(slot, relativePath, content)
            ]);

        AssertRejected(fixture, packagePath, "image.decode");
    }

    [Fact]
    public void ValidateFile_RejectsImageDimensionOfEightThousandOneHundredNinetyThree()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    SkinPackageFixture.CreateGrayscalePng(8193, 1))
            ]);

        AssertRejected(fixture, packagePath, "image.dimension");
    }

    [Fact]
    public void ValidateFile_RejectsAggregateDecodedPixelLimitPlusOne()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    SkinPackageFixture.MaximumPixelPng),
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Center,
                    "assets/center.jpg",
                    SkinPackageFixture.OneByOneJpeg)
            ]);

        AssertRejected(fixture, packagePath, "image.pixel-budget");
    }

    [Fact]
    public void ValidateFile_RejectsSecondHighBitDepthImageByBudgetBeforePixelDecode()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    SkinPackageFixture.HalfMaximumPixelPng),
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Center,
                    "assets/center.png",
                    SkinPackageFixture.CreateCorruptHighBitDepthPng(
                        4097,
                        SkinPackageLimits.MaximumImageDimension))
            ]);

        AssertRejected(fixture, packagePath, "image.pixel-budget");
    }

    [Fact]
    public void ValidateFile_RejectsJpegContentInDecorationSlot()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            assets:
            [
                new SkinPackageFixture.FixtureAsset(
                    SkinAssetSlot.Decoration,
                    "assets/decoration.png",
                    SkinPackageFixture.OneByOneJpeg)
            ]);

        AssertRejected(fixture, packagePath, "image.decoration-format");
    }

    [Theory]
    [InlineData("C:/escape.png")]
    [InlineData("/escape.png")]
    public void ValidateFile_RejectsAbsoluteEntryPaths(string entryName) =>
        AssertRejectedEntry(entryName, "archive.path.absolute");

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("assets/../../escape.png")]
    public void ValidateFile_RejectsTraversalEntryPaths(string entryName) =>
        AssertRejectedEntry(entryName, "archive.path.traversal");

    [Fact]
    public void ValidateFile_RejectsBackslashEntryPaths() =>
        AssertRejectedEntry(
            "assets\\background.png",
            "archive.path.separator");

    [Theory]
    [MemberData(nameof(DuplicateEntryNames))]
    public void ValidateFile_RejectsOrdinalIgnoreCaseAndNfcDuplicateNames(
        string firstName,
        string secondName)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            additionalEntries:
            [
                new SkinPackageFixture.FixtureEntry(firstName, [0x01]),
                new SkinPackageFixture.FixtureEntry(secondName, [0x02])
            ]);

        AssertRejected(
            fixture,
            packagePath,
            "archive.path.duplicate");
    }

    [Fact]
    public void ValidateFile_RejectsFileNotDeclaredByManifest()
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            additionalEntries:
            [
                new SkinPackageFixture.FixtureEntry(
                    "notes.txt",
                    "not declared"u8.ToArray())
            ]);

        AssertRejected(fixture, packagePath, "archive.file.undeclared");
    }

    public static IEnumerable<object[]> DuplicateEntryNames()
    {
        yield return ["assets/center.png", "assets/center.png"];
        yield return ["assets/center.png", "ASSETS/CENTER.PNG"];
        yield return ["assets/caf\u00e9.png", "assets/cafe\u0301.png"];
    }

    public static IEnumerable<object[]> NonRegularEntries()
    {
        yield return ["directory/", 0x10];
        yield return ["assets/link.png", unchecked((int)0xA1FF0000)];
        yield return ["assets/reparse.png", 0x400];
        yield return ["assets/device.png", 0x40];
    }

    public static IEnumerable<object[]> SignatureSpoofs()
    {
        yield return
        [
            SkinAssetSlot.Background,
            "assets/background.png",
            SkinPackageFixture.OneByOneJpeg
        ];
        yield return
        [
            SkinAssetSlot.Center,
            "assets/center.jpg",
            SkinPackageFixture.OneByOnePng
        ];
    }

    public static IEnumerable<object[]> CorruptImages()
    {
        yield return
        [
            SkinAssetSlot.Background,
            "assets/background.png",
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }
        ];
        yield return
        [
            SkinAssetSlot.Center,
            "assets/center.jpg",
            new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }
        ];
    }

    private static void AssertRejectedEntry(
        string entryName,
        string expectedCode)
    {
        using var fixture = new SkinPackageFixture();
        var packagePath = fixture.CreatePackage(
            additionalEntries:
            [
                new SkinPackageFixture.FixtureEntry(entryName, [0x01])
            ]);

        AssertRejected(fixture, packagePath, expectedCode);
    }

    private static void AssertRejected(
        SkinPackageFixture fixture,
        string packagePath,
        string expectedCode)
    {
        var result = new SkinPackageReader().ValidateFile(
            packagePath,
            InstalledVersion,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
        Assert.DoesNotContain(result.Errors, error =>
            error.Message.Contains(packagePath, StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains(fixture.RootDirectory, StringComparison.OrdinalIgnoreCase) ||
            error.Location.Contains(packagePath, StringComparison.OrdinalIgnoreCase) ||
            error.Location.Contains(fixture.RootDirectory, StringComparison.OrdinalIgnoreCase));
        fixture.AssertNoEscape();
    }
}
