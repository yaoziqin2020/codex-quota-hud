using System.Globalization;
using System.Text;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Validation;

public static class SkinContractValidator
{
    private static readonly HashSet<Guid> ReservedIds =
    [
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("10000000-0000-0000-0000-000000000002"),
        Guid.Parse("10000000-0000-0000-0000-000000000003"),
        Guid.Parse("10000000-0000-0000-0000-000000000004"),
        Guid.Parse("10000000-0000-0000-0000-000000000005")
    ];

    public static SkinValidationResult<(
        SkinManifest Manifest,
        SkinTheme Theme)> Validate(
        SkinManifest manifest,
        SkinTheme theme,
        SemanticVersion installedHudVersion,
        bool allowLocalProvenance = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(theme);

        var errors = new List<SkinValidationError>();
        ValidateManifest(
            manifest,
            installedHudVersion,
            allowLocalProvenance,
            errors);
        ValidateTheme(theme, errors);

        return errors.Count == 0
            ? new SkinValidationResult<(SkinManifest Manifest, SkinTheme Theme)>(
                (manifest, theme),
                [])
            : new SkinValidationResult<(SkinManifest Manifest, SkinTheme Theme)>(
                default,
                errors);
    }

    private static void ValidateManifest(
        SkinManifest manifest,
        SemanticVersion installedHudVersion,
        bool allowLocalProvenance,
        ICollection<SkinValidationError> errors)
    {
        ValidateSchema(manifest.SchemaVersion, errors);
        ValidateTemplate(manifest.TemplateId, errors);

        if (manifest.SkinId == Guid.Empty)
        {
            Add(errors, "skin.invalid-id", "$.skinId", "The skin ID must not be empty.");
        }
        else if (ReservedIds.Contains(manifest.SkinId))
        {
            Add(errors, "skin.reserved-id", "$.skinId", "Built-in skin IDs are reserved.");
        }

        ValidateMetadata(
            manifest.DisplayName,
            SkinPackageLimits.MaximumDisplayNameScalars,
            "$.displayName",
            errors);
        ValidateMetadata(
            manifest.Author,
            SkinPackageLimits.MaximumAuthorScalars,
            "$.author",
            errors);
        ValidateMetadata(
            manifest.Description,
            SkinPackageLimits.MaximumDescriptionScalars,
            "$.description",
            errors);

        if (manifest.MinimumHudVersion.CompareTo(installedHudVersion) > 0)
        {
            Add(
                errors,
                "version.incompatible",
                "$.minimumHudVersion",
                "The skin requires a newer HUD version.");
        }

        ValidateProvenance(manifest, allowLocalProvenance, errors);
        ValidateAssets(manifest.Assets, errors);
    }

    private static void ValidateTheme(
        SkinTheme theme,
        ICollection<SkinValidationError> errors)
    {
        ValidateSchema(theme.SchemaVersion, errors);
        ValidateTemplate(theme.TemplateId, errors);

        ValidateTransform(theme.Background, "$.background", errors);
        ValidateTransform(theme.Center, "$.center", errors);
        ValidateTransform(theme.Decoration, "$.decoration", errors);

        ValidateColor(theme.PrimaryRingColor, "$.primaryRingColor", errors);
        ValidateColor(theme.SecondaryRingColor, "$.secondaryRingColor", errors);
        ValidateColor(theme.BaseBackgroundColor, "$.baseBackgroundColor", errors);
        ValidateColor(theme.GlowColor, "$.glowColor", errors);

        ValidateNumber(
            theme.BaseBackgroundOpacity,
            SkinPackageLimits.MinimumOpacity,
            SkinPackageLimits.MaximumOpacity,
            "$.baseBackgroundOpacity",
            errors);
        ValidateNumber(
            theme.RingDiameter,
            SkinPackageLimits.MinimumRingDiameterDip,
            SkinPackageLimits.MaximumRingDiameterDip,
            "$.ringDiameter",
            errors);
        ValidateNumber(
            theme.RingThickness,
            SkinPackageLimits.MinimumRingThicknessDip,
            SkinPackageLimits.MaximumRingThicknessDip,
            "$.ringThickness",
            errors);
        ValidateNumber(
            theme.RingGap,
            SkinPackageLimits.MinimumRingGapDip,
            SkinPackageLimits.MaximumRingGapDip,
            "$.ringGap",
            errors);
        ValidateNumber(
            theme.StartAngle,
            SkinPackageLimits.MinimumStartAngleDegrees,
            SkinPackageLimits.MaximumStartAngleDegrees,
            "$.startAngle",
            errors);
        ValidateNumber(
            theme.GlowIntensity,
            SkinPackageLimits.MinimumEffectIntensity,
            SkinPackageLimits.MaximumEffectIntensity,
            "$.glowIntensity",
            errors);
        ValidateNumber(
            theme.NumberTextSize,
            SkinPackageLimits.MinimumTextSizeDip,
            SkinPackageLimits.MaximumTextSizeDip,
            "$.numberTextSize",
            errors);
        ValidateNumber(
            theme.LabelTextSize,
            SkinPackageLimits.MinimumTextSizeDip,
            SkinPackageLimits.MaximumTextSizeDip,
            "$.labelTextSize",
            errors);

        if (!Enum.IsDefined(theme.TextWeight))
        {
            Add(
                errors,
                "enum.invalid",
                "$.textWeight",
                "The text weight is not defined by schema version 1.");
        }

        if (!Enum.IsDefined(theme.TextPlacement))
        {
            Add(
                errors,
                "enum.invalid",
                "$.textPlacement",
                "The text placement is not defined by schema version 1.");
        }

        ValidateAnimation(theme.Animation, errors);
    }

    private static void ValidateSchema(
        int schemaVersion,
        ICollection<SkinValidationError> errors)
    {
        if (schemaVersion != SkinPackageLimits.SchemaVersion)
        {
            Add(
                errors,
                "schema.unsupported",
                "$.schemaVersion",
                "Only skin schema version 1 is supported.");
        }
    }

    private static void ValidateTemplate(
        string? templateId,
        ICollection<SkinValidationError> errors)
    {
        if (!string.Equals(
                templateId,
                SkinPackageLimits.FreeDecorationRingTemplateId,
                StringComparison.Ordinal))
        {
            Add(
                errors,
                "template.unsupported",
                "$.templateId",
                "Only the free-decoration-ring template is supported.");
        }
    }

    private static void ValidateMetadata(
        string? value,
        int maximumScalars,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            HasControlScalar(value) ||
            value.EnumerateRunes().Count() > maximumScalars)
        {
            Add(
                errors,
                "metadata.invalid",
                location,
                "Metadata must be non-empty, control-free, and within its scalar limit.");
        }
    }

    private static bool HasControlScalar(string value) =>
        value.EnumerateRunes().Any(
            rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control);

    private static void ValidateProvenance(
        SkinManifest manifest,
        bool allowLocalProvenance,
        ICollection<SkinValidationError> errors)
    {
        if (manifest.OriginSkinId is not { } originSkinId)
        {
            return;
        }

        if (!allowLocalProvenance)
        {
            Add(
                errors,
                "provenance.not-allowed",
                "$.originSkinId",
                "Package manifests cannot carry local provenance.");
        }
        else if (originSkinId == manifest.SkinId)
        {
            Add(
                errors,
                "provenance.self-reference",
                "$.originSkinId",
                "A local copy must reference a different origin skin ID.");
        }
        else if (originSkinId == Guid.Empty)
        {
            Add(
                errors,
                "provenance.invalid-id",
                "$.originSkinId",
                "The origin skin ID must not be empty.");
        }
    }

    private static void ValidateAssets(
        IReadOnlyList<SkinAssetReference>? assets,
        ICollection<SkinValidationError> errors)
    {
        if (assets is null)
        {
            Add(
                errors,
                "asset.missing-slot",
                "$.assets",
                "Each schema-v1 asset slot must be declared exactly once.");
            return;
        }

        var seenSlots = new HashSet<SkinAssetSlot>();
        for (var index = 0; index < assets.Count; index++)
        {
            var asset = assets[index];
            var location = $"$.assets[{index}]";
            if (asset is null)
            {
                Add(
                    errors,
                    "asset.invalid",
                    location,
                    "Asset declarations must not be null.");
                continue;
            }

            if (!Enum.IsDefined(asset.Slot))
            {
                Add(
                    errors,
                    "asset.slot.invalid",
                    $"{location}.slot",
                    "The asset slot is not defined by schema version 1.");
            }
            else if (!seenSlots.Add(asset.Slot))
            {
                Add(
                    errors,
                    "asset.duplicate-slot",
                    $"{location}.slot",
                    "Each asset slot may be declared only once.");
            }

            if (!IsValidAssetPath(asset.Slot, asset.Path))
            {
                Add(
                    errors,
                    "asset.path.invalid",
                    $"{location}.path",
                    "The asset path is not the fixed relative name for its slot.");
            }

            if (!IsLowercaseSha256(asset.Sha256))
            {
                Add(
                    errors,
                    "asset.hash.invalid",
                    $"{location}.sha256",
                    "SHA-256 values must contain 64 lowercase hexadecimal characters.");
            }
        }

        if (seenSlots.Count != Enum.GetValues<SkinAssetSlot>().Length)
        {
            Add(
                errors,
                "asset.missing-slot",
                "$.assets",
                "Each schema-v1 asset slot must be declared exactly once.");
        }
    }

    private static bool IsValidAssetPath(
        SkinAssetSlot slot,
        string? path) => slot switch
        {
            SkinAssetSlot.Background =>
                path is "assets/background.png" or
                    "assets/background.jpg" or
                    "assets/background.jpeg",
            SkinAssetSlot.Center =>
                path is "assets/center.png" or
                    "assets/center.jpg" or
                    "assets/center.jpeg",
            SkinAssetSlot.Decoration => path is "assets/decoration.png",
            _ => false
        };

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateColor(
        string? value,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (value is not { Length: 9 } ||
            value[0] != '#' ||
            !value.AsSpan(1).ToString().All(Uri.IsHexDigit))
        {
            Add(
                errors,
                "color.invalid",
                location,
                "Colors must use exact #AARRGGBB form.");
        }
    }

    private static void ValidateTransform(
        SkinImageTransform? transform,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (transform is null)
        {
            Add(
                errors,
                "transform.invalid",
                location,
                "An image transform is required.");
            return;
        }

        ValidateNumber(
            transform.OffsetX,
            SkinPackageLimits.MinimumImageOffsetDip,
            SkinPackageLimits.MaximumImageOffsetDip,
            $"{location}.offsetX",
            errors);
        ValidateNumber(
            transform.OffsetY,
            SkinPackageLimits.MinimumImageOffsetDip,
            SkinPackageLimits.MaximumImageOffsetDip,
            $"{location}.offsetY",
            errors);
        ValidateNumber(
            transform.Scale,
            SkinPackageLimits.MinimumImageScale,
            SkinPackageLimits.MaximumImageScale,
            $"{location}.scale",
            errors);
        ValidateNumber(
            transform.Rotation,
            SkinPackageLimits.MinimumImageRotationDegrees,
            SkinPackageLimits.MaximumImageRotationDegrees,
            $"{location}.rotation",
            errors);
        ValidateNumber(
            transform.Opacity,
            SkinPackageLimits.MinimumOpacity,
            SkinPackageLimits.MaximumOpacity,
            $"{location}.opacity",
            errors);
        ValidateNumber(
            transform.CropFocusX,
            SkinPackageLimits.MinimumCropFocus,
            SkinPackageLimits.MaximumCropFocus,
            $"{location}.cropFocusX",
            errors);
        ValidateNumber(
            transform.CropFocusY,
            SkinPackageLimits.MinimumCropFocus,
            SkinPackageLimits.MaximumCropFocus,
            $"{location}.cropFocusY",
            errors);
    }

    private static void ValidateAnimation(
        SkinAnimationSettings? animation,
        ICollection<SkinValidationError> errors)
    {
        if (animation is null)
        {
            Add(
                errors,
                "animation.invalid",
                "$.animation",
                "Animation settings are required.");
            return;
        }

        ValidateNumber(
            animation.RotationIntensity,
            SkinPackageLimits.MinimumEffectIntensity,
            SkinPackageLimits.MaximumEffectIntensity,
            "$.animation.rotationIntensity",
            errors);
        ValidateNumber(
            animation.BreathingIntensity,
            SkinPackageLimits.MinimumEffectIntensity,
            SkinPackageLimits.MaximumEffectIntensity,
            "$.animation.breathingIntensity",
            errors);
        ValidateNumber(
            animation.GlowIntensity,
            SkinPackageLimits.MinimumEffectIntensity,
            SkinPackageLimits.MaximumEffectIntensity,
            "$.animation.glowIntensity",
            errors);
        ValidateNumber(
            animation.FloatingIntensity,
            SkinPackageLimits.MinimumEffectIntensity,
            SkinPackageLimits.MaximumEffectIntensity,
            "$.animation.floatingIntensity",
            errors);
    }

    private static void ValidateNumber(
        double value,
        double minimum,
        double maximum,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            Add(
                errors,
                "number.out-of-range",
                location,
                "The numeric value is outside the schema-v1 range.");
        }
    }

    private static void Add(
        ICollection<SkinValidationError> errors,
        string code,
        string location,
        string message) =>
        errors.Add(new SkinValidationError(code, location, message));
}
