using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.Skins.Tests.Contracts;

public sealed class SkinContractTests
{
    [Theory]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("12.34.56", 12, 34, 56)]
    public void SemanticVersion_RoundTripsCanonicalTriplets(
        string text,
        int major,
        int minor,
        int patch)
    {
        var version = SemanticVersion.Parse(text);

        Assert.Equal(
            (major, minor, patch),
            (version.Major, version.Minor, version.Patch));
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-beta")]
    [InlineData("01.2.3")]
    [InlineData("-1.2.3")]
    public void SemanticVersion_RejectsAnythingOutsideUnsignedCanonicalTriplet(
        string text) =>
        Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void SemanticVersion_OrdersMajorThenMinorThenPatch()
    {
        Assert.True(
            SemanticVersion.Parse("2.0.0")
                .CompareTo(SemanticVersion.Parse("1.99.99")) > 0);
        Assert.True(
            SemanticVersion.Parse("1.3.0")
                .CompareTo(SemanticVersion.Parse("1.2.99")) > 0);
        Assert.True(
            SemanticVersion.Parse("1.2.4")
                .CompareTo(SemanticVersion.Parse("1.2.3")) > 0);
        Assert.Equal(
            0,
            SemanticVersion.Parse("1.2.3")
                .CompareTo(SemanticVersion.Parse("1.2.3")));
    }

    [Fact]
    public void StoragePaths_AreExactChildrenOfLocalAppData()
    {
        var paths = new SkinStoragePaths(@"C:\Users\Test\AppData\Local");

        Assert.Equal(
            @"C:\Users\Test\AppData\Local\CodexQuotaHud",
            paths.SettingsRoot);
        Assert.Equal(
            @"C:\Users\Test\AppData\Local\CodexQuotaHud\skins",
            paths.InstalledSkinsRoot);
        Assert.Equal(
            @"C:\Users\Test\AppData\Local\CodexQuotaHud\designer\drafts",
            paths.DraftsRoot);
        Assert.Equal(
            @"C:\Users\Test\AppData\Local\CodexQuotaHud\imports",
            paths.ImportsRoot);
    }

    [Fact]
    public void ContractConstants_MatchApprovedSchemaAndLimits()
    {
        Assert.Equal(1, SkinPackageLimits.SchemaVersion);
        Assert.Equal(
            "free-decoration-ring",
            SkinPackageLimits.FreeDecorationRingTemplateId);
        Assert.Equal(
            50L * 1024 * 1024,
            SkinPackageLimits.MaximumPackageBytes);
        Assert.Equal(
            64L * 1024 * 1024,
            SkinPackageLimits.MaximumExtractedBytes);
        Assert.Equal(64, SkinPackageLimits.MaximumEntries);
        Assert.Equal(
            16L * 1024 * 1024,
            SkinPackageLimits.MaximumImageBytes);
        Assert.Equal(8192, SkinPackageLimits.MaximumImageDimension);
        Assert.Equal(
            67_108_864L,
            SkinPackageLimits.MaximumDecodedPixels);
        Assert.Equal(80, SkinPackageLimits.MaximumDisplayNameScalars);
        Assert.Equal(80, SkinPackageLimits.MaximumAuthorScalars);
        Assert.Equal(500, SkinPackageLimits.MaximumDescriptionScalars);
        Assert.Equal("manifest.json", SkinPackageLimits.ManifestFileName);
        Assert.Equal("theme.json", SkinPackageLimits.ThemeFileName);
        Assert.Equal("assets/", SkinPackageLimits.AssetsDirectoryName);
    }

    [Fact]
    public void TemplateBounds_MatchApprovedNormalizedAndDipRanges()
    {
        Assert.Equal((-50d, 50d),
            (SkinPackageLimits.MinimumImageOffsetDip,
             SkinPackageLimits.MaximumImageOffsetDip));
        Assert.Equal((0.25d, 3d),
            (SkinPackageLimits.MinimumImageScale,
             SkinPackageLimits.MaximumImageScale));
        Assert.Equal((-180d, 180d),
            (SkinPackageLimits.MinimumImageRotationDegrees,
             SkinPackageLimits.MaximumImageRotationDegrees));
        Assert.Equal((0d, 1d),
            (SkinPackageLimits.MinimumOpacity,
             SkinPackageLimits.MaximumOpacity));
        Assert.Equal((0d, 1d),
            (SkinPackageLimits.MinimumCropFocus,
             SkinPackageLimits.MaximumCropFocus));
        Assert.Equal((72d, 116d),
            (SkinPackageLimits.MinimumRingDiameterDip,
             SkinPackageLimits.MaximumRingDiameterDip));
        Assert.Equal((2d, 16d),
            (SkinPackageLimits.MinimumRingThicknessDip,
             SkinPackageLimits.MaximumRingThicknessDip));
        Assert.Equal((2d, 24d),
            (SkinPackageLimits.MinimumRingGapDip,
             SkinPackageLimits.MaximumRingGapDip));
        Assert.Equal((0d, 359d),
            (SkinPackageLimits.MinimumStartAngleDegrees,
             SkinPackageLimits.MaximumStartAngleDegrees));
        Assert.Equal((12d, 34d),
            (SkinPackageLimits.MinimumTextSizeDip,
             SkinPackageLimits.MaximumTextSizeDip));
        Assert.Equal((0d, 1d),
            (SkinPackageLimits.MinimumEffectIntensity,
             SkinPackageLimits.MaximumEffectIntensity));
    }

    [Fact]
    public void ManifestAndTheme_AreImmutableValueContracts()
    {
        var assets = new SkinAssetReference[]
        {
            new(
                SkinAssetSlot.Background,
                "assets/background.png",
                new string('a', 64)),
            new(
                SkinAssetSlot.Center,
                "assets/center.jpg",
                new string('b', 64)),
            new(
                SkinAssetSlot.Decoration,
                "assets/decoration.png",
                new string('c', 64))
        };
        var manifest = new SkinManifest(
            SkinPackageLimits.SchemaVersion,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Ocean Ring",
            "老姚",
            SemanticVersion.Parse("1.2.3"),
            "Three-slot fixture",
            SkinPackageLimits.FreeDecorationRingTemplateId,
            SemanticVersion.Parse("1.1.1"),
            OriginSkinId: null,
            assets);
        var identity = new SkinImageTransform(
            OffsetX: 0,
            OffsetY: 0,
            Scale: 1,
            Rotation: 0,
            Opacity: 1,
            CropFocusX: 0.5,
            CropFocusY: 0.5);
        var theme = new SkinTheme(
            SkinPackageLimits.SchemaVersion,
            SkinPackageLimits.FreeDecorationRingTemplateId,
            identity,
            identity,
            identity,
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
            Animation: new SkinAnimationSettings(
                RotationIntensity: 0.25,
                BreathingIntensity: 0.5,
                GlowIntensity: 0.75,
                FloatingIntensity: 1));

        Assert.Equal(manifest, manifest with { });
        Assert.Equal(theme, theme with { });
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            manifest.SkinId);
        Assert.Equal(SemanticVersion.Parse("1.2.3"), manifest.PackageVersion);
        Assert.Equal(SemanticVersion.Parse("1.1.1"), manifest.MinimumHudVersion);
        Assert.Equal(assets, manifest.Assets);
        Assert.Equal(SkinTextWeight.SemiBold, theme.TextWeight);
        Assert.Equal(
            SkinTextPlacement.NumberAboveLabel,
            theme.TextPlacement);
    }

    [Fact]
    public void ValidationResult_IsValidOnlyWithValueAndNoErrors()
    {
        var valid = new SkinValidationResult<string>("skin", []);
        var missingValue = new SkinValidationResult<string>(null, []);
        var invalid = new SkinValidationResult<string>(
            "skin",
            [new SkinValidationError("schema.invalid", "$.schemaVersion", "Invalid schema")]);

        Assert.True(valid.IsValid);
        Assert.False(missingValue.IsValid);
        Assert.False(invalid.IsValid);
    }
}
