using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.Skins.Tests.Validation;

public sealed class SkinContractValidatorTests
{
    private static readonly SemanticVersion InstalledVersion =
        SemanticVersion.Parse("1.1.1");

    [Fact]
    public void Validate_AcceptsSchemaOneContractAndReturnsBothDocuments()
    {
        var manifest = ValidManifest();
        var theme = ValidTheme();

        var result = SkinContractValidator.Validate(
            manifest,
            theme,
            InstalledVersion);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(manifest, result.Value.Manifest);
        Assert.Equal(theme, result.Value.Theme);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Validate_RejectsUnsupportedManifestAndThemeSchemas(int schema)
    {
        AssertError(
            Validate(manifest: ValidManifest() with { SchemaVersion = schema }),
            "schema.unsupported",
            "$.schemaVersion");
        AssertError(
            Validate(theme: ValidTheme() with { SchemaVersion = schema }),
            "schema.unsupported",
            "$.schemaVersion");
    }

    [Fact]
    public void Validate_RejectsTemplateMismatchAndUnknownTemplate()
    {
        AssertError(
            Validate(theme: ValidTheme() with { TemplateId = "other" }),
            "template.unsupported",
            "$.templateId");
        AssertError(
            Validate(manifest: ValidManifest() with { TemplateId = "other" }),
            "template.unsupported",
            "$.templateId");
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    [InlineData("10000000-0000-0000-0000-000000000003")]
    [InlineData("10000000-0000-0000-0000-000000000004")]
    [InlineData("10000000-0000-0000-0000-000000000005")]
    public void Validate_RejectsReservedBuiltInSkinIds(string id) =>
        AssertError(
            Validate(manifest: ValidManifest() with { SkinId = Guid.Parse(id) }),
            "skin.reserved-id",
            "$.skinId");

    [Fact]
    public void Validate_RejectsEmptySkinId() =>
        AssertError(
            Validate(manifest: ValidManifest() with { SkinId = Guid.Empty }),
            "skin.invalid-id",
            "$.skinId");

    [Fact]
    public void Validate_RejectsMinimumHudVersionAboveInstalledVersion()
    {
        var result = SkinContractValidator.Validate(
            ValidManifest() with
            {
                MinimumHudVersion = SemanticVersion.Parse("9.0.0")
            },
            ValidTheme(),
            InstalledVersion);

        AssertError(result, "version.incompatible", "$.minimumHudVersion");
    }

    [Theory]
    [MemberData(nameof(MetadataBoundaryCases))]
    public void Validate_CountsMetadataLimitsByUnicodeScalar(
        string field,
        string atLimit,
        string overLimit)
    {
        Assert.True(Validate(manifest: WithMetadata(field, atLimit)).IsValid);
        AssertError(
            Validate(manifest: WithMetadata(field, overLimit)),
            "metadata.invalid",
            $"$.{field}");
    }

    [Theory]
    [InlineData("displayName")]
    [InlineData("author")]
    [InlineData("description")]
    public void Validate_RejectsEmptyWhitespaceAndControlMetadata(string field)
    {
        AssertError(
            Validate(manifest: WithMetadata(field, "")),
            "metadata.invalid",
            $"$.{field}");
        AssertError(
            Validate(manifest: WithMetadata(field, "   ")),
            "metadata.invalid",
            $"$.{field}");
        AssertError(
            Validate(manifest: WithMetadata(field, "safe\u0001text")),
            "metadata.invalid",
            $"$.{field}");
    }

    [Theory]
    [InlineData("primaryRingColor")]
    [InlineData("secondaryRingColor")]
    [InlineData("baseBackgroundColor")]
    [InlineData("glowColor")]
    public void Validate_RejectsMalformedArgbColors(string field)
    {
        foreach (var malformed in new[]
                 {
                     "#FFFFFF", "#FFFFFFFFF", "FFFFFFFF", "#GG000000", "# FF000000"
                 })
        {
            AssertError(
                Validate(theme: WithColor(field, malformed)),
                "color.invalid",
                $"$.{field}");
        }
    }

    [Fact]
    public void Validate_RejectsDuplicateAndMissingAssetSlots()
    {
        var duplicate = ValidManifest() with
        {
            Assets =
            [
                .. ValidManifest().Assets,
                new SkinAssetReference(
                    SkinAssetSlot.Background,
                    "assets/background.jpg",
                    new string('d', 64))
            ]
        };
        var missing = ValidManifest() with
        {
            Assets = ValidManifest().Assets
                .Where(asset => asset.Slot != SkinAssetSlot.Center)
                .ToArray()
        };

        AssertError(
            Validate(manifest: duplicate),
            "asset.duplicate-slot",
            "$.assets[3].slot");
        AssertError(
            Validate(manifest: missing),
            "asset.missing-slot",
            "$.assets");
    }

    [Theory]
    [InlineData(SkinAssetSlot.Background, "background.png")]
    [InlineData(SkinAssetSlot.Background, "assets/../background.png")]
    [InlineData(SkinAssetSlot.Background, "assets/sub/background.png")]
    [InlineData(SkinAssetSlot.Background, "assets\\background.png")]
    [InlineData(SkinAssetSlot.Background, "/assets/background.png")]
    [InlineData(SkinAssetSlot.Background, "assets/background.gif")]
    [InlineData(SkinAssetSlot.Background, "assets/center.png")]
    [InlineData(SkinAssetSlot.Center, "assets/center.gif")]
    [InlineData(SkinAssetSlot.Center, "assets/background.jpg")]
    [InlineData(SkinAssetSlot.Decoration, "assets/decoration.jpg")]
    [InlineData(SkinAssetSlot.Decoration, "assets/Decoration.png")]
    public void Validate_RejectsUndeclaredAssetPathForms(
        SkinAssetSlot slot,
        string path)
    {
        var assets = ValidManifest().Assets.ToArray();
        var index = Array.FindIndex(assets, asset => asset.Slot == slot);
        assets[index] = assets[index] with { Path = path };

        AssertError(
            Validate(manifest: ValidManifest() with { Assets = assets }),
            "asset.path.invalid",
            $"$.assets[{index}].path");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Validate_RejectsMalformedSha256(string hash)
    {
        var assets = ValidManifest().Assets.ToArray();
        assets[1] = assets[1] with { Sha256 = hash };

        AssertError(
            Validate(manifest: ValidManifest() with { Assets = assets }),
            "asset.hash.invalid",
            "$.assets[1].sha256");
    }

    [Fact]
    public void Validate_RejectsUnknownEnumValues()
    {
        var assets = ValidManifest().Assets.ToArray();
        assets[1] = assets[1] with { Slot = (SkinAssetSlot)99 };

        AssertError(
            Validate(manifest: ValidManifest() with { Assets = assets }),
            "asset.slot.invalid",
            "$.assets[1].slot");
        AssertError(
            Validate(theme: ValidTheme() with { TextWeight = (SkinTextWeight)99 }),
            "enum.invalid",
            "$.textWeight");
        AssertError(
            Validate(theme: ValidTheme() with { TextPlacement = (SkinTextPlacement)99 }),
            "enum.invalid",
            "$.textPlacement");
    }

    [Fact]
    public void Validate_EnforcesLocalProvenanceRules()
    {
        var origin = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var localCopy = ValidManifest() with { OriginSkinId = origin };

        AssertError(
            SkinContractValidator.Validate(localCopy, ValidTheme(), InstalledVersion),
            "provenance.not-allowed",
            "$.originSkinId");
        Assert.True(SkinContractValidator.Validate(
            localCopy,
            ValidTheme(),
            InstalledVersion,
            allowLocalProvenance: true).IsValid);
        AssertError(
            SkinContractValidator.Validate(
                ValidManifest() with
                {
                    OriginSkinId = ValidManifest().SkinId
                },
                ValidTheme(),
                InstalledVersion,
                allowLocalProvenance: true),
            "provenance.self-reference",
            "$.originSkinId");
    }

    [Theory]
    [MemberData(nameof(TransformBoundaryCases))]
    public void Validate_EnforcesEveryTransformBoundary(
        string layer,
        string field,
        double minimum,
        double maximum,
        double below,
        double above)
    {
        Assert.True(Validate(theme: WithTransform(layer, field, minimum)).IsValid);
        Assert.True(Validate(theme: WithTransform(layer, field, maximum)).IsValid);
        AssertError(
            Validate(theme: WithTransform(layer, field, below)),
            "number.out-of-range",
            $"$.{layer}.{field}");
        AssertError(
            Validate(theme: WithTransform(layer, field, above)),
            "number.out-of-range",
            $"$.{layer}.{field}");
    }

    [Theory]
    [MemberData(nameof(ThemeBoundaryCases))]
    public void Validate_EnforcesEveryThemeAndAnimationBoundary(
        string field,
        double minimum,
        double maximum,
        double below,
        double above)
    {
        Assert.True(Validate(theme: WithThemeNumber(field, minimum)).IsValid);
        Assert.True(Validate(theme: WithThemeNumber(field, maximum)).IsValid);
        AssertError(
            Validate(theme: WithThemeNumber(field, below)),
            "number.out-of-range",
            $"$.{field}");
        AssertError(
            Validate(theme: WithThemeNumber(field, above)),
            "number.out-of-range",
            $"$.{field}");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_RejectsProgrammaticNonFiniteNumbers(double value) =>
        AssertError(
            Validate(theme: ValidTheme() with { RingThickness = value }),
            "number.out-of-range",
            "$.ringThickness");

    public static IEnumerable<object[]> MetadataBoundaryCases()
    {
        yield return ["displayName", string.Concat(Enumerable.Repeat("😀", 80)), string.Concat(Enumerable.Repeat("😀", 81))];
        yield return ["author", string.Concat(Enumerable.Repeat("🎨", 80)), string.Concat(Enumerable.Repeat("🎨", 81))];
        yield return ["description", string.Concat(Enumerable.Repeat("🚀", 500)), string.Concat(Enumerable.Repeat("🚀", 501))];
    }

    public static IEnumerable<object[]> TransformBoundaryCases()
    {
        foreach (var layer in new[] { "background", "center", "decoration" })
        {
            yield return [layer, "offsetX", -50d, 50d, -50.001d, 50.001d];
            yield return [layer, "offsetY", -50d, 50d, -50.001d, 50.001d];
            yield return [layer, "scale", 0.25d, 3d, 0.249d, 3.001d];
            yield return [layer, "rotation", -180d, 180d, -180.001d, 180.001d];
            yield return [layer, "opacity", 0d, 1d, -0.001d, 1.001d];
            yield return [layer, "cropFocusX", 0d, 1d, -0.001d, 1.001d];
            yield return [layer, "cropFocusY", 0d, 1d, -0.001d, 1.001d];
        }
    }

    public static IEnumerable<object[]> ThemeBoundaryCases()
    {
        yield return ["baseBackgroundOpacity", 0d, 1d, -0.001d, 1.001d];
        yield return ["ringDiameter", 72d, 116d, 71.999d, 116.001d];
        yield return ["ringThickness", 2d, 16d, 1.999d, 16.001d];
        yield return ["ringGap", 2d, 24d, 1.999d, 24.001d];
        yield return ["startAngle", 0d, 359d, -0.001d, 359.001d];
        yield return ["glowIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["numberTextSize", 12d, 34d, 11.999d, 34.001d];
        yield return ["labelTextSize", 12d, 34d, 11.999d, 34.001d];
        yield return ["animation.rotationIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["animation.breathingIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["animation.glowIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["animation.floatingIntensity", 0d, 1d, -0.001d, 1.001d];
    }

    private static SkinValidationResult<(SkinManifest Manifest, SkinTheme Theme)> Validate(
        SkinManifest? manifest = null,
        SkinTheme? theme = null) =>
        SkinContractValidator.Validate(
            manifest ?? ValidManifest(),
            theme ?? ValidTheme(),
            InstalledVersion);

    private static SkinManifest ValidManifest() => new(
        SchemaVersion: 1,
        SkinId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DisplayName: "Ocean",
        Author: "Alice",
        PackageVersion: SemanticVersion.Parse("1.2.3"),
        Description: "Ocean ring",
        TemplateId: "free-decoration-ring",
        MinimumHudVersion: SemanticVersion.Parse("1.1.1"),
        OriginSkinId: null,
        Assets:
        [
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
        ]);

    private static SkinTheme ValidTheme()
    {
        var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
        return new SkinTheme(
            SchemaVersion: 1,
            TemplateId: "free-decoration-ring",
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
    }

    private static SkinManifest WithMetadata(string field, string value)
    {
        var manifest = ValidManifest();
        return field switch
        {
            "displayName" => manifest with { DisplayName = value },
            "author" => manifest with { Author = value },
            "description" => manifest with { Description = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static SkinTheme WithColor(string field, string value)
    {
        var theme = ValidTheme();
        return field switch
        {
            "primaryRingColor" => theme with { PrimaryRingColor = value },
            "secondaryRingColor" => theme with { SecondaryRingColor = value },
            "baseBackgroundColor" => theme with { BaseBackgroundColor = value },
            "glowColor" => theme with { GlowColor = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static SkinTheme WithTransform(
        string layer,
        string field,
        double value)
    {
        var theme = ValidTheme();
        var original = layer switch
        {
            "background" => theme.Background,
            "center" => theme.Center,
            "decoration" => theme.Decoration,
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };
        var transform = field switch
        {
            "offsetX" => original with { OffsetX = value },
            "offsetY" => original with { OffsetY = value },
            "scale" => original with { Scale = value },
            "rotation" => original with { Rotation = value },
            "opacity" => original with { Opacity = value },
            "cropFocusX" => original with { CropFocusX = value },
            "cropFocusY" => original with { CropFocusY = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        return layer switch
        {
            "background" => theme with { Background = transform },
            "center" => theme with { Center = transform },
            "decoration" => theme with { Decoration = transform },
            _ => throw new ArgumentOutOfRangeException(nameof(layer))
        };
    }

    private static SkinTheme WithThemeNumber(string field, double value)
    {
        var theme = ValidTheme();
        if (field.StartsWith("animation.", StringComparison.Ordinal))
        {
            var animationField = field["animation.".Length..];
            var animation = animationField switch
            {
                "rotationIntensity" => theme.Animation with { RotationIntensity = value },
                "breathingIntensity" => theme.Animation with { BreathingIntensity = value },
                "glowIntensity" => theme.Animation with { GlowIntensity = value },
                "floatingIntensity" => theme.Animation with { FloatingIntensity = value },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            return theme with { Animation = animation };
        }

        return field switch
        {
            "baseBackgroundOpacity" => theme with { BaseBackgroundOpacity = value },
            "ringDiameter" => theme with { RingDiameter = value },
            "ringThickness" => theme with { RingThickness = value },
            "ringGap" => theme with { RingGap = value },
            "startAngle" => theme with { StartAngle = value },
            "glowIntensity" => theme with { GlowIntensity = value },
            "numberTextSize" => theme with { NumberTextSize = value },
            "labelTextSize" => theme with { LabelTextSize = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static void AssertError(
        SkinValidationResult<(SkinManifest Manifest, SkinTheme Theme)> result,
        string code,
        string location)
    {
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == code && error.Location == location);
    }
}
